using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;

namespace PhoneAccounting.App.Services.Sync;

/// <summary>
/// يحوّل سطر OperationLog إلى DeltaPacket: بيانات العملية + صفوف الكيانات (بأوطان الـ SyncId).
/// وحدة الدلتا = العملية (بهويتها (OriginDevice, OriginSeq)) وليست الصف المفرّد (البند 9: لا نقل ملف — دلتا العمليات فقط).
/// الصفوف تُلتقط بحالتها الحالية عند الالتقاط؛ المنطق الحي يعيد الإعمار، والحذف شاهد قبر (DeletedAt).
/// المرحلة 2/الانتشارية: التمديد يُبنى بعلامات ماء «لكل مصدر» (BuildFor) لا بعلامة واحدة للجهاز (15.2).
/// </summary>
public static class DeltaBuilder
{
    public static List<DeltaPacket> BuildNew(AppDbContext db, string deviceId, string? excludeOrigin = null)
    {
        // «ما لم أُرسل بعد» نسبةً لعملياتي أنا: علامة ماء أصل = آخر OriginSeq في إيصالات Out.
        var lastSent = db.SyncLogs.AsNoTracking()
            .Where(s => s.Direction == "Out" && s.OriginDevice == deviceId)
            .Select(s => (long?)s.OriginSeq)
            .Max() ?? 0;

        var ops = db.OperationLogs.AsNoTracking()
            .Where(o => o.OriginDevice == deviceId && o.OriginSeq > lastSent)
            .OrderBy(o => o.OriginSeq)
            .ToList();

        if (excludeOrigin is not null)
            ops = ops.Where(o => o.OriginDevice != excludeOrigin).ToList();

        return ops.Select(op => ToPacket(db, op)).ToList();
    }

    /// <summary>بناء دلتا لطالب معيّن في الانتشارية: كل عملية تتجاوز علامة ماء الطالب لِأصلها، أيًّا كان مصدرها
    /// (أصليّات + مُمرَّرات — قدرة التمرير تأتي من خرائط (15.2)). المصدر الحذفِ على الطالب يستثنى حفاظاً على هوية الأشرطة.</summary>
    public static List<DeltaPacket> BuildFor(AppDbContext db, IReadOnlyDictionary<string, long> watermarkByOrigin, string? excludeOrigin = null)
    {
        var ops = db.OperationLogs.AsNoTracking()
            .ToList()
            .Where(o => IsMissing(o, watermarkByOrigin) && (excludeOrigin is null || o.OriginDevice != excludeOrigin))
            .OrderBy(o => o.OriginDevice).ThenBy(o => o.OriginSeq)
            .ToList();

        return ops.Select(op => ToPacket(db, op)).ToList();
    }

    /// <summary>هل العملية تتجاوز علامة ماء الطالب لذلك المصدر؟ غياب مصدر في الخريطة = الطالب لا يملك شيئاً له (0).</summary>
    private static bool IsMissing(OperationLog op, IReadOnlyDictionary<string, long> watermarkByOrigin)
    {
        var origin = op.OriginDevice;
        if (string.IsNullOrWhiteSpace(origin) || op.OriginSeq <= 0) return false;
        var watermark = watermarkByOrigin.TryGetValue(origin, out var w) ? w : 0;
        return op.OriginSeq > watermark;
    }

    /// <summary>تسجيل ما أُرسل بنجاح (إيصال Out بهوية العملية) — يضبط علامة ماء إرسالي ويمنع إعادة الإرسال اللانهائية.</summary>
    public static void MarkSent(AppDbContext db, string deviceId, IEnumerable<DeltaPacket> packets)
    {
        foreach (var packet in packets)
        {
            var origin = packet.OriginDevice ?? deviceId;
            var originSeq = packet.OriginSeq > 0 ? packet.OriginSeq : packet.OpSeq;
            if (!db.SyncLogs.Any(s => s.Direction == "Out" && s.OriginDevice == origin && s.OriginSeq == originSeq))
            {
                db.SyncLogs.Add(new SyncLogEntry
                {
                    DeviceId = deviceId,
                    OriginDevice = origin,
                    OriginSeq = originSeq,
                    OpType = packet.OpType,
                    EntityName = packet.EntityName,
                    EntitySyncId = packet.Rows.FirstOrDefault()?.S,
                    Direction = "Out",
                    SentAtUtc = DateTime.UtcNow
                });
            }
        }
        db.SaveChanges();
    }

    /// <summary>تحديث علامة ماء هذا الجهاز لِأصل معيّن (ماذا يعرف «أنا» عن «ذلك المصدر») — أُحدّث بعد تطبيق الدلتا.</summary>
    public static void RememberSyncPoint(AppDbContext db, string ownerDeviceId, string originDevice, long lastOriginSeq)
    {
        var state = db.SyncPeerStates.FirstOrDefault(p => p.OwnerDeviceId == ownerDeviceId && p.OriginDevice == originDevice);
        if (state is null)
        {
            db.SyncPeerStates.Add(new SyncPeerState
            {
                OwnerDeviceId = ownerDeviceId,
                OriginDevice = originDevice,
                LastOriginSeq = lastOriginSeq,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else if (lastOriginSeq > state.LastOriginSeq)
        {
            state.LastOriginSeq = lastOriginSeq;
            state.UpdatedAt = DateTime.UtcNow;
        }
        db.SaveChanges();
    }

    /// <summary>خريطة علامات الماء {المصدر: آخر ما أعرفه} — تُرسل في delta_request (15.2).</summary>
    public static Dictionary<string, long> WatermarkMap(AppDbContext db, string ownerDeviceId)
        => db.SyncPeerStates.AsNoTracking()
            .Where(p => p.OwnerDeviceId == ownerDeviceId)
            .ToDictionary(p => p.OriginDevice, p => p.LastOriginSeq);

    /// <summary>لقطة صفوف الكيان بحالته المحلية الحالية (بنفس منطق AttachRows) — أساس «حارس المساواة» في كشف الصراع
    /// (P3.1): مقارنة دلتا الطرف الوارد ضد لقطة الحالة المحلية للكيان نفسه.</summary>
    internal static List<DeltaRow> SnapshotRows(AppDbContext db, string entityName, int entityId)
    {
        var packet = new DeltaPacket { EntityName = entityName, EntityId = entityId };
        AttachRows(db, packet);
        return packet.Rows;
    }

    private static DeltaPacket ToPacket(AppDbContext db, OperationLog op)
    {
        var packet = new DeltaPacket
        {
            OriginDevice = op.OriginDevice,
            OriginSeq = op.OriginSeq,
            OpSeq = (int)op.Seq,
            OpType = op.OpType,
            EntityName = op.EntityName,
            EntityId = op.EntityId,
            Document = op.DocumentNumber,
            Amount = op.Amount,
            UserId = op.UserId,
            UserName = db.Users.AsNoTracking()
                .Where(u => u.Id == op.UserId)
                .Select(u => u.DisplayName)
                .FirstOrDefault() ?? "مستخدم غير معروف",
            UserRole = (int)op.UserRole,
            DeviceRole = (int)op.DeviceRole,
            SummaryJson = op.SummaryJson,
            CommittedAtUtc = op.CommittedAt
        };

        AttachRows(db, packet);
        return packet;
    }

    private static void AttachRows(AppDbContext db, DeltaPacket packet)
    {
        switch (packet.EntityName)
        {
            case "Sales":
                AddSaleRows(db, packet);
                break;
            case "Purchases":
                AddPurchaseRows(db, packet);
                break;
            case "RepairJobs":
                AddRepairRows(db, packet);
                break;
            case "Vouchers":
                var voucher = db.Vouchers.FirstOrDefault(v => v.Id == packet.EntityId || v.VoucherNumber == packet.Document);
                if (voucher is not null)
                    packet.Rows.Add(RowOf("Vouchers", voucher, ("CustomerId", SyncOfCustomer(db, voucher.CustomerId))));
                break;
            case "Customers":
                var customer = db.Customers.FirstOrDefault(c => c.Id == packet.EntityId);
                if (customer is not null)
                    packet.Rows.Add(RowOf("Customers", customer));
                break;
            case "Products":
                var product = db.Products.Include(p => p.Category).FirstOrDefault(p => p.Id == packet.EntityId || (packet.EntityId == null && p.Name == packet.Document));
                if (product is not null)
                {
                    if (product.Category is not null)
                        packet.Rows.Add(RowOf("Categories", product.Category));
                    packet.Rows.Add(RowOf("Products", product, ("CategoryId", product.Category is not null ? product.Category.SyncId : null)));
                }
                break;
            default:
                // Ø¹Ù…Ù„ÙŠØ§Øª Ù…Ø¹Ù„ÙˆÙ…Ø§ØªÙŠØ© (Ø¥Ù‚ÙØ§Ù„ Ø§Ù„ÙŠÙˆÙ…...) â€” Ø§Ù„Ø­Ù…ÙˆÙ„Ø© ÙÙŠ SummaryJson ÙÙ‚Ø·.
                break;
        }
    }

    private static void AddSaleRows(AppDbContext db, DeltaPacket packet)
    {
        var sale = db.Sales.Include(s => s.Items).FirstOrDefault(s => s.Id == packet.EntityId || (packet.EntityId == null && s.InvoiceNumber == packet.Document));
        if (sale is null) return;

        packet.Rows.Add(RowOf("Sales", sale, ("CustomerId", sale.CustomerId is int cid ? SyncOfCustomer(db, cid) : null)));

        // Ø§Ù„Ù…Ù†ØªØ¬Ø§Øª Ø§Ù„Ù…ØªØ£Ø«Ø±Ø© (Ø­Ø§Ù„Ø© Ø§Ù„Ù…Ø®Ø²ÙˆÙ† Ø¨Ø¹Ø¯ Ø§Ù„Ø¹Ù…Ù„ÙŠØ©) ØªÙØ³Ø¨Ù‚ Ø¨Ù†ÙˆØ¯Ù‡Ø§ Ù„ÙŠÙØ¹Ø§Ø¯ Ø±Ø¨Ø·Ù‡Ø§ Ù‚Ø¨Ù„ Ø£ÙŠ Ref
        foreach (var productId in sale.Items.Select(i => i.ProductId).Distinct())
            AddProductAndCategoryRows(db, packet, productId);

        foreach (var item in sale.Items)
        {
            packet.Rows.Add(RowOf("SaleItems", item,
                ("SaleId", sale.SyncId ?? ""),
                ("ProductId", SyncOfProduct(db, item.ProductId))));
        }
    }

    private static void AddPurchaseRows(AppDbContext db, DeltaPacket packet)
    {
        var purchase = db.Purchases.Include(p => p.Items).FirstOrDefault(p => p.Id == packet.EntityId || (packet.EntityId == null && p.InvoiceNumber == packet.Document));
        if (purchase is null) return;

        packet.Rows.Add(RowOf("Purchases", purchase, ("SupplierId", purchase.SupplierId is int sid ? SyncOfSupplier(db, sid) : null)));

        foreach (var productId in purchase.Items.Select(i => i.ProductId).Distinct())
            AddProductAndCategoryRows(db, packet, productId);

        foreach (var item in purchase.Items)
        {
            packet.Rows.Add(RowOf("PurchaseItems", item,
                ("PurchaseId", purchase.SyncId ?? ""),
                ("ProductId", SyncOfProduct(db, item.ProductId))));
        }
    }

    private static void AddRepairRows(AppDbContext db, DeltaPacket packet)
    {
        var job = db.RepairJobs.Include(j => j.Parts).FirstOrDefault(j => j.Id == packet.EntityId || (packet.EntityId == null && j.JobNumber == packet.Document));
        if (job is null) return;

        packet.Rows.Add(RowOf("RepairJobs", job));

        foreach (var productId in job.Parts.Select(p => p.ProductId).Distinct())
            AddProductAndCategoryRows(db, packet, productId);

        foreach (var part in job.Parts)
        {
            packet.Rows.Add(RowOf("RepairParts", part,
                ("RepairJobId", job.SyncId ?? ""),
                ("ProductId", SyncOfProduct(db, part.ProductId))));
        }
    }

    /// <summary>ÙŠÙØ¯Ø±Ø¬ ØµÙ Ø§Ù„Ù…Ù†ØªØ¬ ÙˆØµÙ Ø§Ù„ØªØµÙ†ÙŠÙ (Ø¥Ù† ÙˆÙØ¬Ø¯) Ù…Ø¹Ø§Ù‹ â€” Ø§Ù„ØªØµÙ†ÙŠÙ Ø£Ø¨ØŒ ÙˆØ§Ù„Ù…Ù†ØªØ¬ Ø§Ø¨Ù†Ù‡.</summary>
    private static void AddProductAndCategoryRows(AppDbContext db, DeltaPacket packet, int productId)
    {
        var product = db.Products.Include(p => p.Category).FirstOrDefault(p => p.Id == productId);
        if (product is null) return;
        if (product.Category is not null && !packet.Rows.Any(r => r.T == "Categories" && r.S == product.Category.SyncId))
            packet.Rows.Add(RowOf("Categories", product.Category));
        if (!packet.Rows.Any(r => r.T == "Products" && r.S == product.SyncId))
            packet.Rows.Add(RowOf("Products", product, ("CategoryId", product.Category?.SyncId)));
    }

    private static string? SyncOfCustomer(AppDbContext db, int id) =>
        db.Customers.AsNoTracking().Where(x => x.Id == id).Select(x => x.SyncId).FirstOrDefault();

    private static string? SyncOfProduct(AppDbContext db, int id) =>
        db.Products.AsNoTracking().Where(x => x.Id == id).Select(x => x.SyncId).FirstOrDefault();

    private static string? SyncOfSupplier(AppDbContext db, int id) =>
        db.Suppliers.AsNoTracking().Where(x => x.Id == id).Select(x => x.SyncId).FirstOrDefault();

    private static readonly Dictionary<Type, System.Reflection.PropertyInfo[]> _propsCache = new();

    private static readonly Type[] ScalarTypes =
    {
        typeof(string), typeof(int), typeof(long), typeof(decimal), typeof(bool), typeof(DateTime),
        typeof(float), typeof(double), typeof(byte), typeof(short)
    };

    private static DeltaRow RowOf(string table, object entity, params (string RefName, object? RefSyncId)[] refs)
    {
        var syncId = (string)entity.GetType().GetProperty("SyncId")!.GetValue(entity)!;
        var origin = (string?)entity.GetType().GetProperty("OriginDevice")!.GetValue(entity);

        var entityType = entity.GetType();
        if (!_propsCache.TryGetValue(entityType, out var props))
        {
            props = entityType.GetProperties().Where(p => p.GetIndexParameters().Length == 0 && p.SetMethod is not null).ToArray();
            _propsCache[entityType] = props;
        }

        var fields = new Dictionary<string, JsonScalar>();
        foreach (var p in props)
        {
            if (p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute), false).Length > 0) continue;
            if (p.Name is "Id" or "SyncId" or "OriginDevice") continue;

            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (!ScalarTypes.Contains(t) && !t.IsEnum) continue;

            var raw = p.GetValue(entity);
            fields[p.Name] = ToScalar(raw);
        }

        var refsDict = new Dictionary<string, string?>();
        foreach (var (name, refSyncId) in refs)
        {
            refsDict[name] = refSyncId is null ? null : refSyncId.ToString();
        }

        return new DeltaRow { T = table, S = syncId, Origin = origin, F = fields, R = refsDict };
    }

    private static JsonScalar ToScalar(object? raw)
    {
        if (raw is null) return JsonScalar.OfNull();
        if (raw is string s) return JsonScalar.Of(s);
        if (raw is bool b) return JsonScalar.Of(b);
        if (raw is DateTime dt) return JsonScalar.Of(dt);
        if (raw is decimal d) return JsonScalar.Of(d);
        if (raw is long l) return JsonScalar.Of(l);
        if (raw is int i) return JsonScalar.Of(i);
        if (raw is short sh) return JsonScalar.Of(sh);
        if (raw is byte by) return JsonScalar.Of(by);
        if (raw is float f) return JsonScalar.Of((decimal)f);
        if (raw is double dbl) return JsonScalar.Of((decimal)dbl);
        if (raw is Enum e) return JsonScalar.Of(Convert.ToInt64(e));
        return JsonScalar.Of(raw.ToString() ?? "");
    }
}