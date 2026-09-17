using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;

namespace PhoneAccounting.App.Services.Sync;

public enum ProbeKind { Ok, Rejected, Unreachable }

/// <summary>نتيجة فحص الصحة — لا كتابة على أي جهة، ولا جلسة معلّقة (الخادم يُغلق فوراً بعد الرد).</summary>
    public sealed record ProbeResult(ProbeKind Kind, string? Reason, string? RemoteDeviceId);

    /// <summary>صف خلاف مفتوح عند قريب — يُنقل كنسخة عرض فقط (Survey قراءة-فقط، بلا كتابة على أي جهة).</summary>
    public sealed record SurveyConflictDto(
        string EntityTable, string ConflictSyncId, string? LocalJson, string? RemoteJson, DateTime? LastUpdatedAt);

    /// <summary>نتيجة استطلاع الخلافات المفتوحة — قراءة-فقط محضة.</summary>
    public sealed record SurveyResult(bool Accepted, string? Reason, List<SurveyConflictDto> Conflicts, string? RemoteDeviceId);

/// <summary>
/// قناة مزامنة نظير-لنظير عبر TCP (البند 15.4 من SYNC-DESIGN v1.8):
/// - مؤطّر بطول صريح (4 بايت) + سقف صارم 50MB — تجاوزه ⇒ إغلاق فورى (منع استنزاف الذاكرة).
/// - مصافحة أولية: هوية + سر مشترك + SchemaVersion — رفض مبكر عند اختلاف الإصدار أو السر.
/// - تبادل متزامن ثنائي الاتجاه فوق جلسة واحدة: عميل يرسل خريطة علامات ماء → خادم يبني ما ينقصه
///   (BuildFor — أصليّات ومُمرَّرات) → عميل يطبّق ويتذكّر نقاطاً، ثم يدفع دلتاه الخاصة بنفس خريطة الخادم.
/// - إعادة اتصال بتراجع أسّى (1s × 2^n) + Jitter عشوائي + سقف 5د + تسجيل كل محاولة فاشلة.
/// </summary>
public static class SyncPeer
{
    public const int MaxFrameBytes = 50 * 1024 * 1024;
    public const int DefaultPort = 45678;

    /// <summary>القيمة الاحتياطية المعروفة لسر المزامنة عند غياب المفتاح — منشورة في التوثيق، ولذا غير مقبولة تشغيلياً إطلاقاً.</summary>
    public const string DefaultSecret = "phone-accounting-m2.2";

    /// <summary>حارس السر (البند 1 من دراسة-تقييم-ملاحظات-كلاود) — رفضٌ صريح قبل أي اتصال إن لم يُضبط سرّ صريح غير المعروف.</summary>
    public const string SecretGuardReason =
        "لا مزامنة: اضبط «سرّ المزامنة» من الإعدادات أولاً — القيمة الاحتياطية غير مقبولة تشغيلياً";

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>رسالة مبادلة داخل الإطار — كل حقل يُملأ حسب Type.</summary>
    public sealed class SyncMessage
    {
        public string Type { get; set; } = "";          // Hello | Request | Survey | Delivery | Ack
        public string DeviceId { get; set; } = "";
        public string Role { get; set; } = "";          // ادّعاء الدور (من إعدادات الطالب) — حارس الاستطلاع فقط؛ السلطة الحقيقية في العملية الموقّعة
        public string Secret { get; set; } = "";        // المصافحة
        public int SchemaVersion { get; set; }
        public bool Accepted { get; set; }              // رد المصافحة
        public bool Probe { get; set; }                 // فحص صحة قراءة-فقط: الخادم يرد على Hello ويُغلق فوراً بلا Request
        public string? Reason { get; set; }             // سبب الرفض
        public Dictionary<string, long> Watermarks { get; set; } = new();
        public List<DeltaPacket> Packets { get; set; } = new();
        public List<SurveyConflictDto> Survey { get; set; } = new();   // رد الاستطلاع — نسخة عرض فقط
        public int Applied { get; set; }
    }

    public sealed class SyncResult
    {
        public bool Accepted { get; set; }
        public string? Reason { get; set; }
        public int Pulled { get; set; }
        public int Pushed { get; set; }
        public int Attempts { get; set; }
    }

    public static Action<string>? Log { get; set; }

    /// <summary>زمن مهلة قراءة الخادم (ملليثانية) — قابل للضبط حتى تُختبر «مهجور معلّق» بلا انتظار 15 ثانية في الاختبار، والافتراضي للإنتاج 15000.</summary>
    internal static int ServerReadTimeoutMs { get; set; } = 15000;

    /// <summary>مقالة جهاز: سرّ/نسخة/معرّف — تُقرأ من إعدادات قاعدته نفسها (لا حالٍ ثابت). آخر عنصر: هل السرّ صريح غير المعروف.</summary>
    public static (string Secret, int Schema, string DeviceId, bool SecretExplicit) PeerConfig(AppDbContext db)
    {
        var rawSecret = db.AppSettings.AsNoTracking().FirstOrDefault(s => s.Key == "SyncSecret")?.Value;
        var secret = string.IsNullOrWhiteSpace(rawSecret) ? DefaultSecret : rawSecret;
        var secretExplicit = !string.IsNullOrWhiteSpace(rawSecret) && rawSecret != DefaultSecret;
        var schema = db.AppSettings.AsNoTracking().FirstOrDefault(s => s.Key == "SchemaVersion")?.Value;
        int.TryParse(schema, out var schemaNum);
        if (schemaNum <= 0) schemaNum = Database.CurrentSchemaVersion;
        var deviceId = db.AppSettings.AsNoTracking().FirstOrDefault(s => s.Key == "DeviceId")?.Value ?? "";
        if (string.IsNullOrWhiteSpace(deviceId)) deviceId = "peer_device";
        return (secret, schemaNum, deviceId, secretExplicit);
    }

    /// <summary>هل يستحق سرّ مزامنةَ تشغيل؟ لا: غياب المفتاح أو قيمة معلّنة تساوي الاحتياطية المعروفة — النظام يرفض الاقتران بأمان صامت (البند 1).</summary>
    public static bool HasUsableSecret(AppDbContext db)
    {
        var rawSecret = db.AppSettings.AsNoTracking().FirstOrDefault(s => s.Key == "SyncSecret")?.Value;
        return !string.IsNullOrWhiteSpace(rawSecret) && rawSecret != DefaultSecret;
    }

    // ===================== الجانب الخادم =====================

    public static Task StartServer(int port, string dbPath, CancellationToken ct = default)
    {
        // الربط على كل الواجهات (IPAddress.Any) ليتصل بها أقرانٌ على الشبكة المحلية — معاملات الحماية تستبعد
        // أي متصلٍ بلا الشرط: المصافحة ترفض السر الخاطئ وتدمير SchemaVersion.
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        return Task.Run(async () =>
        {
            Log?.Invoke($"SyncPeer: server on 0.0.0.0:{port}");
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
                catch when (ct.IsCancellationRequested) { break; }
                _ = Task.Run(() => HandleConnection(client, dbPath));
            }
            listener.Stop();
        }, ct);
    }

    private static async Task HandleConnection(TcpClient client, string dbPath)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                stream.ReadTimeout = ServerReadTimeoutMs;
                stream.WriteTimeout = 30000;

                var hello = FromJson(ReadFrame(stream));
                if (hello.Type != "Hello")
                {
                    Send(stream, new SyncMessage { Type = "Hello", Accepted = false, Reason = "لا مصافحة أولى" });
                    return;
                }

                using var db = new AppDbContext(dbPath);
                var (secret, schema, deviceId, secretExplicit) = PeerConfig(db);
                if (!secretExplicit)
                {
                    Send(stream, new SyncMessage { Type = "Hello", Accepted = false, Reason = SecretGuardReason });
                    Log?.Invoke($"SyncPeer: رفض {hello.DeviceId} — {SecretGuardReason}");
                    return;
                }
                if (hello.SchemaVersion != schema)
                {
                    Send(stream, new SyncMessage { Type = "Hello", Accepted = false, Reason = "SchemaVersion غير متطابق" });
                    Log?.Invoke($"SyncPeer: رفض {hello.DeviceId} — إصدار مختلف ({hello.SchemaVersion} != {schema})");
                    return;
                }
                if (hello.Secret != secret)
                {
                    Send(stream, new SyncMessage { Type = "Hello", Accepted = false, Reason = "السر خاطئ" });
                    Log?.Invoke($"SyncPeer: رفض {hello.DeviceId} — سرّ مشترك غير صحيح");
                    return;
                }

                Send(stream, new SyncMessage { Type = "Hello", Accepted = true, DeviceId = deviceId });

                if (hello.Probe) return; // فحص صحة فقط — لا Request، صفر كتابة على الجانبين، ولا جلسة معلّقة

                var request = FromJson(ReadFrame(stream));

                if (request.Type == "Survey")
                {
                    // استطلاعٌ قراءة-فقط: نسخةُ عرضٍ لصراعاتِ القريب المفتوحة — صفر كتابة (لا سجلّ، لا علامات ماء، لا إيصالات).
                    // الحارس: ادّعاء دور الطالب (من إعداداته) بأنه MAIN — سلطة الحسم الحقيقية تبقى في العملية الموقّعة، لا في العرض.
                    if (hello.Role == "MainServer")
                    {
                        var open = db.SyncConflicts.AsNoTracking()
                            .Where(c => !c.IsResolved)
                            .OrderByDescending(c => c.LastUpdatedAt)
                            .ToList()
                            .Select(c => new SurveyConflictDto(c.EntityTable, c.ConflictSyncId, c.LocalJson, c.RemoteJson, c.LastUpdatedAt))
                            .ToList();
                        Send(stream, new SyncMessage { Type = "Ack", Accepted = true, Survey = open });
                    }
                    else
                    {
                        Send(stream, new SyncMessage { Type = "Ack", Accepted = false, Reason = "الاستطلاع حصراً من أجهزة MAIN" });
                    }
                    return;
                }

                if (request.Type != "Request")
                {
                    Send(stream, new SyncMessage { Type = "Ack", Accepted = false, Reason = "بروتوكول غير مفهوم" });
                    return;
                }

                // ما ينقص الطالب — أيًّا كان مصدره (أصليّات + مُمرَّرات)
                var packets = DeltaBuilder.BuildFor(db, request.Watermarks, request.DeviceId);
                Send(stream, new SyncMessage
                {
                    Type = "Delivery",
                    Packets = packets,
                    Watermarks = DeltaBuilder.WatermarkMap(db, deviceId)
                });

                // دلتا الطالب نفسه
                var delivery = FromJson(ReadFrame(stream));
                var applied = 0;
                if (delivery.Type == "Delivery" && delivery.Packets.Count > 0)
                {
                    var outcome = DeltaApplier.Apply(db, hello.DeviceId, delivery.Packets, deviceId, delivery.Watermarks);
                    applied = outcome.Applied;
                    // لا تُتقدَّم علامة ماءٌ لأصلٍ التُقطَ صراعُه — فلا يُجزم أن الطالب «طبّق» ما لم يقبل
                    foreach (var g in delivery.Packets.GroupBy(p => p.OriginDevice ?? ""))
                        if (g.Key.Length > 0 && !outcome.ConflictedOrigins.Contains(g.Key))
                            DeltaBuilder.RememberSyncPoint(db, deviceId, g.Key, g.Max(p => p.OriginSeq));
                }
                Send(stream, new SyncMessage { Type = "Ack", Applied = applied, Accepted = true });
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"SyncPeer: جلسة فشلت — {ex.Message}");
        }
    }

    // ===================== الجانب العميل =====================

    /// <summary>مزامنة كاملة ثنائية الاتجاه مع قريب، مع تراجع أسّى بجتر عند فشل الشبكة (1s×2^n، سقف 5د).</summary>
    public static SyncResult Synchronize(string host, int port, string dbPath,
        string? secretOverride = null, int? schemaOverride = null, int maxAttempts = 1)
    {
        var result = new SyncResult();
        for (var attempt = 1; ; attempt++)
        {
            result.Attempts = attempt;
            try
            {
                return TrySynchronizeOnce(host, port, dbPath, secretOverride, schemaOverride, result);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"SyncPeer: محاولة {attempt} فشلت — {ex.Message}");
                if (maxAttempts > 0 && attempt >= maxAttempts) throw;
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                if (backoff > TimeSpan.FromMinutes(5)) backoff = TimeSpan.FromMinutes(5);
                var jitter = TimeSpan.FromMilliseconds(new Random().Next(1000));
                Thread.Sleep(backoff + jitter);
            }
        }
    }

    private static SyncResult TrySynchronizeOnce(string host, int port, string dbPath,
        string? secretOverride, int? schemaOverride, SyncResult result)
    {
        using var db = new AppDbContext(dbPath);
        var (secret, schema, deviceId, secretExplicit) = PeerConfig(db);
        if (!secretExplicit && secretOverride is null)
        {
            result.Accepted = false;
            result.Reason = SecretGuardReason;
            Log?.Invoke($"SyncPeer: {host}:{port} — {SecretGuardReason}");
            return result;
        }
        var myId = DeviceIdFromSessionOrDefault(deviceId);

        using var client = new TcpClient();
        client.Connect(host, port);
        client.ReceiveTimeout = 30000;
        client.SendTimeout = 30000;
        using var stream = client.GetStream();

        Send(stream, new SyncMessage
        {
            Type = "Hello",
            DeviceId = myId,
            Secret = secretOverride ?? secret,
            SchemaVersion = schemaOverride ?? schema
        });

        var hello = FromJson(ReadFrame(stream));
        if (hello.Type != "Hello" || !hello.Accepted)
        {
            result.Accepted = false;
            result.Reason = hello.Reason ?? "رفض غير مفسَّر";
            Log?.Invoke($"SyncPeer: مصافحة رُفضت — {result.Reason}");
            return result;
        }
        result.Accepted = true;

        Send(stream, new SyncMessage { Type = "Request", DeviceId = myId, Watermarks = DeltaBuilder.WatermarkMap(db, myId) });

        var delivery = FromJson(ReadFrame(stream));
        int pulled = 0;
        if (delivery.Type == "Delivery" && delivery.Packets.Count > 0)
        {
            var outcome = DeltaApplier.Apply(db, hello.DeviceId, delivery.Packets, myId, delivery.Watermarks);
            pulled = outcome.Applied;
            foreach (var g in delivery.Packets.GroupBy(p => p.OriginDevice ?? ""))
                if (g.Key.Length > 0 && !outcome.ConflictedOrigins.Contains(g.Key))
                    DeltaBuilder.RememberSyncPoint(db, myId, g.Key, g.Max(p => p.OriginSeq));
        }

        var myPackets = DeltaBuilder.BuildFor(db, delivery.Watermarks, hello.DeviceId);
        Send(stream, new SyncMessage
        {
            Type = "Delivery",
            Packets = myPackets,
            Watermarks = DeltaBuilder.WatermarkMap(db, myId)
        });

        var ack = FromJson(ReadFrame(stream));
        result.Pulled = pulled;
        result.Pushed = ack.Type == "Ack" ? ack.Applied : 0;
        return result;
    }

    /// <summary>فحص «نظير حي + سر صحيح + إصدار متطابق» بمصافحة وحيدة (Hello + Probe) — قراءة فقط، تُستخدم للاستطلاع الدوري.</summary>
    public static ProbeResult Probe(string host, int port, string dbPath, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(host, port);
            if (!connect.Wait(timeout))
            {
                _ = connect.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                return new ProbeResult(ProbeKind.Unreachable, "انتهت مهلة الاتصال", null);
            }

            client.ReceiveTimeout = (int)timeout.TotalMilliseconds;
            client.SendTimeout = (int)timeout.TotalMilliseconds;
            using var stream = client.GetStream();

            using var db = new AppDbContext(dbPath);
            var (secret, schema, deviceId, secretExplicit) = PeerConfig(db);
            if (!secretExplicit)
                return new ProbeResult(ProbeKind.Rejected, SecretGuardReason, null);
            var myId = DeviceIdFromSessionOrDefault(deviceId);

            Send(stream, new SyncMessage
            {
                Type = "Hello",
                DeviceId = myId,
                Secret = secret,
                SchemaVersion = schema,
                Probe = true
            });

            var hello = FromJson(ReadFrame(stream));
            if (hello.Type != "Hello" || !hello.Accepted)
                return new ProbeResult(ProbeKind.Rejected, hello.Reason ?? "رفض غير مفسَّر", null);

            return new ProbeResult(ProbeKind.Ok, null, hello.DeviceId);
        }
        catch (Exception ex)
        {
            return new ProbeResult(ProbeKind.Unreachable, ex.Message, null);
        }
    }

    private static string DeviceIdFromSessionOrDefault(string fromSettings)
    {
        var overrideId = PhoneAccounting.App.Services.Session.DeviceIdOverride;
        return string.IsNullOrWhiteSpace(overrideId) ? (string.IsNullOrWhiteSpace(fromSettings) ? "peer_client" : fromSettings) : overrideId;
    }

    /// <summary>استطلاع قراءة-فقط لصراعاتِ قريبٍ المفتوحة (P3.2B) — بلا أي كتابة على أي جهة.</summary>
    public static SurveyResult QueryOpenConflicts(string host, int port, string dbPath,
        string? secretOverride = null, int? schemaOverride = null)
    {
        try
        {
            using var client = new TcpClient();
            client.Connect(host, port);
            client.ReceiveTimeout = 30000;
            client.SendTimeout = 30000;
            using var stream = client.GetStream();

            using var db = new AppDbContext(dbPath);
            var (secret, schema, deviceId, secretExplicit) = PeerConfig(db);
            if (!secretExplicit && secretOverride is null)
                return new SurveyResult(false, SecretGuardReason, [], null);
            var myId = DeviceIdFromSessionOrDefault(deviceId);
            var myRole = PhoneAccounting.App.Services.Session.Device.ToString(); // ادّعاء من إعدادات القاعدة — يفحصه الخادم

            Send(stream, new SyncMessage
            {
                Type = "Hello",
                DeviceId = myId,
                Role = myRole,
                Secret = secretOverride ?? secret,
                SchemaVersion = schemaOverride ?? schema
            });

            var hello = FromJson(ReadFrame(stream));
            if (hello.Type != "Hello" || !hello.Accepted)
                return new SurveyResult(false, hello.Reason ?? "رفض غير مفسَّر", [], null);

            Send(stream, new SyncMessage { Type = "Survey", DeviceId = myId });

            var ack = FromJson(ReadFrame(stream));
            if (ack.Type != "Ack" || !ack.Accepted)
                return new SurveyResult(false, ack.Reason ?? "رفض غير مفسَّر", [], null);
            return new SurveyResult(true, null, ack.Survey, hello.DeviceId);
        }
        catch (Exception ex)
        {
            return new SurveyResult(false, ex.Message, [], null);
        }
    }

    // ===================== المؤطّر (4 بايت طول + حمولة) =====================

    private static void Send(Stream stream, SyncMessage message)
        => WriteFrame(stream, ToJson(message));

    private static string ToJson(SyncMessage m) => JsonSerializer.Serialize(m, Json);

    private static SyncMessage FromJson(string json) => JsonSerializer.Deserialize<SyncMessage>(json, Json) ?? new SyncMessage();

    internal static void WriteFrame(Stream stream, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        if (payload.Length > MaxFrameBytes)
            throw new InvalidOperationException("رسالة تتجاوز سقف 50MB — إغلاق");
        var len = BitConverter.GetBytes(payload.Length);
        if (BitConverter.IsLittleEndian) Array.Reverse(len);
        stream.Write(len, 0, 4);
        stream.Write(payload, 0, payload.Length);
    }

    internal static string ReadFrame(Stream stream)
    {
        var lenBuf = new byte[4];
        stream.ReadExactly(lenBuf);
        if (BitConverter.IsLittleEndian) Array.Reverse(lenBuf);
        int len = BitConverter.ToInt32(lenBuf);
        if (len < 0 || len > MaxFrameBytes)
            throw new InvalidOperationException($"طول إطار {len} يتجاوز السقف — إغلاق");
        var buf = new byte[len];
        stream.ReadExactly(buf);
        return Encoding.UTF8.GetString(buf);
    }
}