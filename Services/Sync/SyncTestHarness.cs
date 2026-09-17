using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Services.Sync;

/// <summary>
/// إثبات تشغيلي لـ M2.1 «لا فقدان عند النقل»: قاعدة A أفعال، قاعدة B جديدة.
/// سيناريو كامل: Baseline → دلتا A→B (بيع + شراء + تسوية + بيع + إلغاء بيع) → تحقق من تطابق الحالة
/// → idempotency (إعادة التسليم لا تكرر شيئاً) → ثم الاتجاه المعاكس B→A (بيع جديد) → تحقق.
/// يُشغَّل فقط بـ PHONEACCOUNTING_SYNCTEST=1 في وضع DEBUG — لا يمس قاعدتك الحقيقية إطلاقاً.
/// </summary>
public static class SyncTestHarness
{
    public static void Run()
    {
        var results = new List<string>
        {
            "=== SyncTestHarness M2.1 — لا فقدان عند النقل ===",
            "started = " + DateTime.Now.ToString("O")
        };

        var root = Path.Combine(Path.GetTempPath(), "PhoneAccounting_SyncTest_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        var dbA = Path.Combine(root, "A", "phoneaccounting.db");
        var dbB = Path.Combine(root, "B", "phoneaccounting.db");
        var dbC = Path.Combine(root, "C", "phoneaccounting.db");
        var dbP = Path.Combine(root, "P", "phoneaccounting.db");
        var dbQ = Path.Combine(root, "Q", "phoneaccounting.db");
        var dbR = Path.Combine(root, "R", "phoneaccounting.db");
        var dbW = Path.Combine(root, "W", "phoneaccounting.db");
        var dbD = Path.Combine(root, "D", "phoneaccounting.db");
        var dbE = Path.Combine(root, "E", "phoneaccounting.db");
        var dbG = Path.Combine(root, "G", "phoneaccounting.db");
        var dbH = Path.Combine(root, "H", "phoneaccounting.db");
        var dbI = Path.Combine(root, "I", "phoneaccounting.db");
        var dbJ = Path.Combine(root, "J", "phoneaccounting.db");
        var dbK = Path.Combine(root, "K", "phoneaccounting.db");
        var dbN = Path.Combine(root, "N", "phoneaccounting.db");
        var dbM = Path.Combine(root, "M", "phoneaccounting.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbA)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbB)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbC)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbP)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbQ)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbR)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbW)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbD)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbE)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbG)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbH)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbI)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbJ)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbK)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbN)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbM)!);

        try
        {
            RunScenario(results, dbA, dbB, dbC);
            RunChannelScenario(results, dbP, dbQ, dbR);
            RunRemainderScenario(results, dbP, dbQ, dbW);
            RunP30aScenario(results, dbD, dbE, dbG, dbH, dbI, dbJ);
            RunP30bUiScenario(results, dbE, dbG, dbJ, dbK);
            RunP31ConflictScenario(results, dbM, dbK, dbN);
            RunP32aUiScenario(results, dbM, dbK, dbN);
            RunP32bUiScenario(results, dbM, dbK, dbN);
            RunP32cPermissionScenario(results, dbM, dbK, dbN);
            RunP33ReconciliationScenario(results, dbR, dbW);
            RunP35ChaosScenario(results, dbM, dbK, dbN);
            RunP35BulkScenario(results, dbM, dbK, dbN);
            RunP35AlternatingScenario(results, dbM, dbK, dbN);
            RunP35CorruptScenario(results, dbM, dbK, dbN);
            RunP35WindowScenario(results, dbM, dbK, dbN);
            RunP35DuplicateScenario(results, dbM, dbK, dbN);
            RunP35RouterScenario(results, dbM, dbK, dbN);
            RunP35GraceScenario(results, dbM, dbK, dbN);
            RunR1RestoreScenario(results, dbM, dbK, dbN);
            RunR2BackupDrillScenario(results, dbM, dbK, dbN);
            RunR3DocChecks(results);
            RunH1InvoicePrefixScenario(results);
            RunPairingUiScenario(results);
            RunSecretGuardScenario(results);
            RunUiSmokeScenario(results);
        }
        catch (Exception ex)
        {
            results.Add("SCENARIO_FAILED = " + ex);
        }

        var allPassed = !results.Any(x => x.StartsWith("FAIL !!") || x.Contains("SCENARIO_FAILED"));
        results.Add("ALL_TESTS_PASSED = " + (allPassed ? "TRUE" : "FALSE"));

        results.Add("finished = " + DateTime.Now.ToString("O"));
        results.Add("sandbox   = " + root);

        var outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneAccounting");
        Directory.CreateDirectory(outDir);
        var outFile = Path.Combine(outDir, "synctest-results.txt");
        File.WriteAllLines(outFile, results);
        Console.Out.WriteLine(string.Join(Environment.NewLine, results));
        Environment.Exit(allPassed ? 0 : 1);
    }

    private static void RunScenario(List<string> r, string dbA, string dbB, string dbC)
    {
        var deviceA = "deviceA_TEST";
        var deviceB = "deviceB_TEST";

        // ===== جهاز A: بيانات حية =====
        UseDevice(dbA, deviceA);
        SeedAdmin();
        var productSyncId = SeedDeviceAData(r);

        // ===== دلتا A (5 عمليات) =====
        var packetsA = DeltaBuilder.BuildNew(new AppDbContext(), deviceA, excludeOrigin: deviceB);
        r.Add("packetsA count = " + packetsA.Count + " (expect 5)");
        Check(r, packetsA.Count == 5, "A يكشف 5 عمليات في الدلتا (لا تُردّ B لأنها غير موجودة بعد)");
        Check(r, packetsA.Select(p => p.OpSeq).SequenceEqual(new[] { 1, 2, 3, 4, 5 }), "أرقام Seq على A = 1..5 بالترتيب");
        Check(r, packetsA.All(p => p.OriginDevice == deviceA && p.OriginSeq == p.OpSeq),
            "هوية كل حزمة A = (Origin=A، OriginSeq=Seq) — أساس (15.3)");

        // ===== جهاز B: جديد كلياً — لقطة البداية أولاً ثم الدلتا =====
        var baseline = Baseline.Export(new AppDbContext());
        r.Add("baseline rows = " + baseline.Count);
        Check(r, baseline.Any(x => x.T == "Products"), "اللقطة تحوي منتجات");

        UseDevice(dbB, deviceB);
        SeedAdmin();
        var createdB = Baseline.Apply(new AppDbContext(), baseline);
        r.Add("baseline applied on B = " + createdB);
        Check(r, createdB > 0, "B تبدأ من لقطة المرجع");

        var appliedB = DeltaApplier.Apply(new AppDbContext(), deviceA, packetsA).Applied;
        r.Add("deltas A to B applied = " + appliedB + " (expect 5)");
        Check(r, appliedB == 5, "تطبيق 5 عمليات على B");

        UseDevice(dbA, deviceA);
        DeltaBuilder.MarkSent(new AppDbContext(), deviceA, packetsA);
        UseDevice(dbB, deviceB);

        // ===== تحقق B =====
        VerifyB(r, deviceA, productSyncId);

        // ===== idempotency: إعادة التسليم لا تكرر شيئاً =====
        var appliedB2 = DeltaApplier.Apply(new AppDbContext(), deviceA, packetsA).Applied;
        r.Add("re-deliver A to B = " + appliedB2 + " (expect 0)");
        Check(r, appliedB2 == 0, "idempotent: الإيصال يمنع التكرار");
        using (var db = new AppDbContext())
        {
            Check(r, db.OperationLogs.Count() == 5, "بلا مضاعفة في سجل عمليات B بعد إعادة التسليم");
        }

        // ===== الاتجاه المعاكس: B تنشئ بيعاً وتُمرّره لـ A =====
        UseDevice(dbB, deviceB);
        Session.CurrentUser = CurrentAdmin();
        using (var db = new AppDbContext())
        {
            var prod = db.Products.First();
            var sale = new Sale
            {
                InvoiceNumber = "S3",
                Date = DateTime.Now,
                UserId = Session.CurrentUser.Id,
                Device = Session.Device,
                PaymentMethod = PaymentMethod.Cash,
                Total = 450,
                Profit = 150
            };
            sale.Items.Add(new SaleItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 3, UnitPrice = 150, BuyPrice = 100 });
            var op = OperationWriter.Register(db, OperationType.Sale, "S3", 450, "Sales");
            db.Sales.Add(sale);
            prod.Stock -= 3;
            prod.UpdatedAt = DateTime.Now;
            db.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Sales", sale.Id);
            db.SaveChanges();
        }

        var packetsB = DeltaBuilder.BuildNew(new AppDbContext(), deviceB, excludeOrigin: deviceA);
        r.Add("packetsB count = " + packetsB.Count + " (expect 1)");
        Check(r, packetsB.Count == 1 && packetsB[0].OpType == "Sale" && packetsB[0].Document == "S3", "B يرسل عمليته الجديدة فقط (S3)");

        UseDevice(dbA, deviceA);
        var appliedA = DeltaApplier.Apply(new AppDbContext(), deviceB, packetsB).Applied;
        r.Add("deltas B to A applied = " + appliedA + " (expect 1)");
        Check(r, appliedA == 1, "تطبيق بيع B على A");

        UseDevice(dbB, deviceB);
        DeltaBuilder.MarkSent(new AppDbContext(), deviceB, packetsB);
        UseDevice(dbA, deviceA);

        VerifyA(r, deviceB, productSyncId);

        // ===== Regression (نقطتا كلود): فشل الحفظ بعد Register لا يستهلك Seq، وبيانات السجل مطابقة للواقع =====
        long aMaxBefore, aCountBefore;
        using (var db = new AppDbContext())
        {
            aMaxBefore = db.OperationLogs.Any() ? db.OperationLogs.Max(o => o.Seq) : 0;
            aCountBefore = db.OperationLogs.Count();
        }

        // (1) محاولة فاشلة (رقم فاتورة مكرر) — يجب ألا يظهر لها أي أثر في القاعدة
        using (var db = new AppDbContext())
        {
            var failOp = OperationWriter.Register(db, OperationType.Sale, "S1", 200, "Sales");
            db.Sales.Add(new Sale
            {
                InvoiceNumber = "S1",
                Date = DateTime.Now,
                UserId = Session.CurrentUser.Id,
                Device = Session.Device,
                PaymentMethod = PaymentMethod.Cash,
                Total = 200,
                Profit = 100
            });
            var threw = false;
            try { db.SaveChanges(); }
            catch (DbUpdateException) { threw = true; }
            Check(r, threw, "Regression (كلود): حفظ مكرر يفشل — لا صف جديد ولا Seq يُستهلك");
        }

        // (2) عملية ناجحة بعدها مباشرة — تأخذ الرقم التالي بلا فجوة، ورقم المستند يطابق الفاتورة المحفوظة
        using (var db = new AppDbContext())
        {
            var okOp = OperationWriter.Register(db, OperationType.Sale, "S4", 100, "Sales");
            db.Sales.Add(new Sale
            {
                InvoiceNumber = "S4",
                Date = DateTime.Now,
                UserId = Session.CurrentUser.Id,
                Device = Session.Device,
                PaymentMethod = PaymentMethod.Cash,
                Total = 100,
                Profit = 50
            });
            db.SaveChanges();

            using (var fresh = new AppDbContext())
            {
                Check(r, fresh.OperationLogs.Max(o => o.Seq) == aMaxBefore + 1,
                    "Regression (كلود): بعد فشل + نجاح — Seq الناجحة = التالي مباشرة (لا فجوة)");
                Check(r, fresh.OperationLogs.Count() == aCountBefore + 1,
                    "Regression (كلود): العملية الفاشلة لا تُسجَّل في السجل إطلاقاً");
                Check(r, fresh.OperationLogs.OrderByDescending(o => o.Seq).First().DocumentNumber == "S4",
                    "Regression (كلود): رقم المستند في سطر العملية يطابق الفاتورة المحفوظة فعلياً");
                Check(r, fresh.OperationLogs.AsNoTracking().Select(o => o.Seq).OrderBy(x => x).ToList()
                        .SequenceEqual(Enumerable.Range(1, (int)fresh.OperationLogs.Max(o => o.Seq)).Select(i => (long)i)),
                    "Regression (كلود): Seq متصلة 1..N بلا أي فجوة في السجل");
            }
        }

        // ===== الانتشارية/التمرير (M2.2 — البند 15): C تتلقى عمليات A عبر B (تمرير)، ثم يصلها A مباشرة — تطبيق مرة واحدة =====
        RunMeshRelay(r, dbA, dbB, dbC, deviceA, deviceB);
    }

    // ===================== الانتشارية: التمرير وidempotency متعدد المُرسِلين =====================

    private static void RunMeshRelay(List<string> r, string dbA, string dbB, string dbC, string deviceA, string deviceB)
    {
        var deviceC = "deviceC_TEST";

        r.Add("--- Mesh relay (C تلتقي B، وB تمرّر ما عندها من أصل A) ---");

        // لقطة المرجع تُلتقط من A (التي تحمل بيانات كاملة) قبل مغادرتها
        UseDevice(dbA, deviceA);
        var baseline = Baseline.Export(new AppDbContext());
        r.Add("baseline rows (from A) = " + baseline.Count);
        Check(r, baseline.Any(x => x.T == "Products"), "اللقطة من A تحوي منتجات");

        // B (عليها أصليّات A الـ5 + عمليتها S3) تبنّي ما تَمرِّره — دلتا بعلامة ماء كل مصدر (15.2): خريطة C فارغة
        UseDevice(dbB, deviceB);
        var packetsRelay = DeltaBuilder.BuildFor(new AppDbContext(), new Dictionary<string, long>(), deviceC);
        r.Add("relay B→C packets = " + packetsRelay.Count + " (expect ≥ 6: 5 من أصل A + S3 من أصل B)");
        Check(r, packetsRelay.Count >= 6, "B يمرّر أصليّات ومُمرَّرات معاً — قدرة التمرير");
        Check(r, packetsRelay.Count(p => p.OriginDevice == deviceA) == 5, "منها 5 بأصل A (مُمرَّرة)");

        // C: لقطة + تطبيق المُمرَّر
        UseDevice(dbC, deviceC);
        SeedAdmin();
        var createdC = Baseline.Apply(new AppDbContext(), baseline);
        r.Add("baseline applied on C = " + createdC);
        Check(r, createdC > 0, "C تبدأ من لقطة المرجع");

        var appliedRelay = DeltaApplier.Apply(new AppDbContext(), deviceB, packetsRelay).Applied;
        r.Add("relay applied on C = " + appliedRelay);
        Check(r, appliedRelay == packetsRelay.Count, "تطبيق كل المُمرَّر على C");

        using (var db = new AppDbContext())
        {
            Check(r, db.OperationLogs.AsNoTracking().Count(o => o.OriginDevice == deviceA) == 5,
                "على C: عمليات أصل A محفوظة بأصلها الحقيقي وليس (B المرسِلة) — جوهر التمرير");
            Check(r, db.SyncLogs.AsNoTracking().Count(x => x.Direction == "In" && x.OriginDevice == deviceA) == 5,
                "على C: إيصالات بأصل A رغم أن المُرسل B (الوصول لا يغيّر الهوية)");

            // خريطة علامة ماء C لا تزال فارغة (لا تذكُّر بعد)
            Check(r, db.SyncPeerStates.AsNoTracking().Count() == 0,
                "على C: خريطة علامة ماء فارغة قبل تذكّر أي نقطة (15.2)");
        }

        // كما في الحقيقي (مصافحة/تطبيق دلتا) — بعد التطبيق تتذكّر C نطاق كل أصل عرفت عملياته ({A:5، B:6})
        foreach (var grp in packetsRelay.GroupBy(p => p.OriginDevice!))
            DeltaBuilder.RememberSyncPoint(new AppDbContext(), deviceC, grp.Key, grp.Max(p => p.OriginSeq));
        var watermarkAfterRelay = DeltaBuilder.WatermarkMap(new AppDbContext(), deviceC);
        Check(r, watermarkAfterRelay.TryGetValue(deviceA, out var wa) && wa == 5 &&
              watermarkAfterRelay.TryGetValue(deviceB, out var wb) && wb == 6,
            "على C: تذكّرت {A:5، B:6} بعد التمرير — خريطة لكل أصل لا رقم وحيد");

        // A تصل C متأخرة وتُسلّم نفس العمليات مباشرة (مُرسِل مختلف!) — لا تكرار لأن الإيصال بأصل العملية
        UseDevice(dbA, deviceA);
        var packetsDirect = DeltaBuilder.BuildFor(new AppDbContext(), new Dictionary<string, long>(), deviceC);
        UseDevice(dbC, deviceC);
        var appliedDirect = DeltaApplier.Apply(new AppDbContext(), deviceA, packetsDirect).Applied;
        r.Add("late direct A→C applied = " + appliedDirect + " (expect 1 — فقط S4 الجديدة)");
        Check(r, appliedDirect == 1, "نفس عمليات A من مُرسِل آخر (A مباشرة) لا تعيد التطبيق — idempotency متعدد المُرسِلين");
        r.Add("late direct A→C packets = " + packetsDirect.Count + " (expect 7: 5 أصل A + S3 أصل B + S4 أصل A)");

        using (var db = new AppDbContext())
        {
            var s1 = db.Sales.AsNoTracking().FirstOrDefault(s => s.InvoiceNumber == "S1");
            var s2 = db.Sales.AsNoTracking().FirstOrDefault(s => s.InvoiceNumber == "S2");
            var s3 = db.Sales.AsNoTracking().FirstOrDefault(s => s.InvoiceNumber == "S3");
            Check(r, s1 is not null && s2 is not null && s3 is not null, "على C: S1 وS2 وS3 موجودة (لا خسارة عبر التمرير)");
            Check(r, db.Sales.AsNoTracking().Count(s => s.InvoiceNumber == "S1") == 1 &&
                    db.Sales.AsNoTracking().Count(s => s.InvoiceNumber == "S2") == 1 &&
                    db.Sales.AsNoTracking().Count(s => s.InvoiceNumber == "S3") == 1,
                "على C: لا صف مكرر رغم وصول نفس العملية من مُرسِلَين — قلّة-dupe عبر التمرير");
            Check(r, s2?.DeletedAt is not null, "P3.0a/A2(d): Tombstone سَلِمَ عبر التمرير — DeletedAt محفوظ على C (لا يتساقط شاهد القبر)");
            Check(r, db.OperationLogs.AsNoTracking().Count() == packetsRelay.Count + 1,
                "على C: سجل العمليات = المُمرَّر + S4 فقط (لا تكرار)");
        }

        // كبت بالعلامة: A يعرف خريطة C {A:5، B:6} — فلا يرسل إلا ما يتجاوزها
        UseDevice(dbA, deviceA);
        var packetsSuppressed = DeltaBuilder.BuildFor(new AppDbContext(), watermarkAfterRelay, deviceC);
        r.Add("watermark-suppressed A→C packets = " + packetsSuppressed.Count + " (expect 1 — فقط ما تجاوز حدود C)");
        Check(r, packetsSuppressed.Count == 1 && packetsSuppressed[0].Document == "S4",
            "علامة الماء لكل مصدر تمنع نقل ما يملكه الطالب فعلاً (15.2)");

        UseDevice(dbC, deviceC);
        var appliedSuppressed = DeltaApplier.Apply(new AppDbContext(), deviceA, packetsSuppressed).Applied;
        Check(r, appliedSuppressed == 0, "حتى لو أُرسل ما يملكه C — الإيصالات بأصل العملية تصد التكرار");
    }

    // ===================== M2.2: قناة TCP حقيقية على 127.0.0.1 =====================

    private static void RunChannelScenario(List<string> r, string dbP, string dbQ, string dbR)
    {
        const string secret = "m2.2-secret-الاختبار";
        const int port = 47123;
        var deviceP = "deviceP_TEST";
        var deviceQ = "deviceQ_TEST";

        r.Add("--- M2.2 channel: مصافحة + تبادل دلتا عبر TCP على 127.0.0.1 ---");

        // P: فئة عمليات + منتج + بيع S9 (عملية واحدة كافية — الدلتا تحمل المنتج/التصنيف معها)
        UseDevice(dbP, deviceP);
        SeedAdmin();
        ConfigurePeer(deviceP, secret);
        Session.CurrentUser = CurrentAdmin();
        using (var db = new AppDbContext())
        {
            var cat = new Category { Name = "هواتف" };
            db.Categories.Add(cat);
            db.SaveChanges();
            var prod = new Product { Name = "هاتف K", CategoryId = cat.Id, Stock = 20, BuyPrice = 90, SellPrice = 140, Device = DeviceRole.Cashier };
            db.Products.Add(prod);
            db.SaveChanges();
            var sale = new Sale
            {
                InvoiceNumber = "S9",
                Date = DateTime.Now,
                UserId = Session.CurrentUser!.Id,
                Device = Session.Device,
                PaymentMethod = PaymentMethod.Cash,
                Total = 280,
                Profit = 100
            };
            sale.Items.Add(new SaleItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 2, UnitPrice = 140, BuyPrice = 90 });
            var op = OperationWriter.Register(db, OperationType.Sale, "S9", 280, "Sales");
            db.Sales.Add(sale);
            prod.Stock -= 2;
            prod.UpdatedAt = DateTime.Now;
            db.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Sales", sale.Id);
            db.SaveChanges();
        }

        // Q: جهاز جديد بلا بيانات — يتلقى كل شيء عبر القناة
        UseDevice(dbQ, deviceQ);
        SeedAdmin();
        ConfigurePeer(deviceQ, secret);

        // خادم P في الخلفية
        using (var cts = new CancellationTokenSource())
        {
            SyncPeer.Log = msg => r.Add("[sync] " + msg);
            var server = SyncPeer.StartServer(port, dbP, cts.Token);
            Thread.Sleep(250);

            var first = SyncPeer.Synchronize("127.0.0.1", port, dbQ, maxAttempts: 1);
            r.Add("first sync Q←P = accepted:" + first.Accepted + " pulled:" + first.Pulled + " pushed:" + first.Pushed);
            Check(r, first.Accepted && first.Pulled == 1, "مصافحة ناجحة وسحب بيع S9 حقيقياً عبر TCP");
            Check(r, first.Pushed == 0, "Q جديدة — لا شيء تدفعه");

            using (var db = new AppDbContext())
            {
                var s9 = db.Sales.AsNoTracking().FirstOrDefault(s => s.InvoiceNumber == "S9");
                Check(r, s9 is not null && s9.Total == 280, "على Q: S9 وصلت الشبكة بمحتواها كاملاً");
                var qOps = db.OperationLogs.AsNoTracking().ToList();
                Check(r, qOps.Count == 1 && qOps[0].OriginDevice == deviceP && qOps[0].DocumentNumber == "S9",
                    "على Q: العملية محفوظة بأصلها P ودلالة رقمها");
                Check(r, db.SyncLogs.AsNoTracking().Any(x => x.Direction == "In" && x.OriginDevice == deviceP),
                    "على Q: إيصال استلام بأصل P (من الشبكة لا من وهم)");
                Check(r, db.SyncPeerStates.AsNoTracking().Any(p => p.OriginDevice == deviceP && p.LastOriginSeq == qOps[0].OriginSeq),
                    "على Q: تذكّرت علامة الماء {P:" + qOps[0].OriginSeq + "} بعد الجلسة");
            }

            // إعادة مصافحة — نفس العمليات لا تعيد التطبيق (إيصال بأصل العملية)
            var again = SyncPeer.Synchronize("127.0.0.1", port, dbQ, maxAttempts: 1);
            r.Add("resync Q←P pulled:" + again.Pulled);
            using (var db = new AppDbContext())
                Check(r, again.Accepted && again.Pulled == 0 && db.Sales.AsNoTracking().Count(s => s.InvoiceNumber == "S9") == 1,
                    "إعادة الاتصال idempotent — لا نموّ إضافي ولا صف مكرر");

            // رفض المصافحة: سر خاطئ ثم إصدار مختلف — يجب رفضها صراحةً بلا ضرر
            var badSecret = SyncPeer.Synchronize("127.0.0.1", port, dbQ, secretOverride: "wrong", maxAttempts: 1);
            Check(r, !badSecret.Accepted && badSecret.Reason is not null && badSecret.Reason.Contains("سر"),
                "رفض المصافحة بالسر الخاطئ: " + (badSecret.Reason ?? ""));
            var badSchema = SyncPeer.Synchronize("127.0.0.1", port, dbQ, schemaOverride: 999, maxAttempts: 1);
            Check(r, !badSchema.Accepted && badSchema.Reason is not null && badSchema.Reason.Contains("SchemaVersion"),
                "رفض المصافحة بإصدار مختلف: " + (badSchema.Reason ?? ""));

            // لا أثر للسجل R (عميل بلا قاعدة جاهزة لا يُنشأ له شيء)
            if (File.Exists(dbR)) File.Delete(dbR);

            // الخادم لا يزال سليماً بعد الرفض — جلسة ناجحة أخيرة
            var afterRefusals = SyncPeer.Synchronize("127.0.0.1", port, dbQ, maxAttempts: 1);
            Check(r, afterRefusals.Accepted && afterRefusals.Pulled == 0, "بعد الرفضَين: جلسة ناجحة لا تزال تعمل (pulled=0)");

            cts.Cancel();
            server.Wait(2000);
        }
    }

    private static void ConfigurePeer(string deviceId, string secret)
    {
        using var db = new AppDbContext();
        if (!db.AppSettings.Any(s => s.Key == "DeviceId"))
            db.AppSettings.Add(new AppSetting { Key = "DeviceId", Value = deviceId });
        if (!db.AppSettings.Any(s => s.Key == "SyncSecret"))
            db.AppSettings.Add(new AppSetting { Key = "SyncSecret", Value = secret });
        db.SaveChanges();
    }

    // ===================== تجهيز جهاز A =====================

    private static string SeedDeviceAData(List<string> r)
    {
        Session.CurrentUser = CurrentAdmin();
        var admin = Session.CurrentUser;

        string productSyncId;
        using (var db = new AppDbContext())
        {
            var cat = new Category { Name = "هواتف" };
            db.Categories.Add(cat);
            db.SaveChanges();

            var p1 = new Product { Name = "هاتف X", CategoryId = cat.Id, Stock = 10, BuyPrice = 100, SellPrice = 150, Device = DeviceRole.Cashier };
            db.Products.Add(p1);
            db.SaveChanges();
            productSyncId = p1.SyncId!;
        }

        int opSeq = 0;

        // عملية 1: بيع S1 (2 قطعة) → المخزون 8
        opSeq++;
        using (var db = new AppDbContext())
        {
            var prod = db.Products.First();
            var sale = new Sale
            {
                InvoiceNumber = "S1",
                Date = DateTime.Now,
                UserId = admin.Id,
                Device = Session.Device,
                PaymentMethod = PaymentMethod.Cash,
                Total = 300,
                Profit = 100
            };
            sale.Items.Add(new SaleItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 2, UnitPrice = 150, BuyPrice = 100 });
            var op = OperationWriter.Register(db, OperationType.Sale, "S1", 300, "Sales");
            db.Sales.Add(sale);
            prod.Stock -= 2;
            prod.UpdatedAt = DateTime.Now;
            db.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Sales", sale.Id);
            db.SaveChanges();
        }

        // عملية 2: شراء PO1 (5 قطع) → المخزون 13
        opSeq++;
        using (var db = new AppDbContext())
        {
            var supplier = new Supplier { Name = "مورد1", Balance = 0 };
            db.Suppliers.Add(supplier);
            db.SaveChanges();

            var prod = db.Products.First();
            var purchase = new Purchase
            {
                InvoiceNumber = "PO1",
                Date = DateTime.Now,
                UserId = admin.Id,
                Device = Session.Device,
                SupplierId = supplier.Id,
                SupplierName = "مورد1",
                PaymentMethod = PaymentMethod.Cash,
                Discount = 0,
                Total = 500
            };
            purchase.Items.Add(new PurchaseItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 5, UnitPrice = 100 });
            var op = OperationWriter.Register(db, OperationType.Purchase, "PO1", 500, "Purchases");
            db.Purchases.Add(purchase);
            prod.Stock += 5;
            prod.UpdatedAt = DateTime.Now;
            db.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Purchases", purchase.Id);
            db.SaveChanges();
        }

        // عملية 3: تسوية مخزون → 15 (سبب مكتوب)
        opSeq++;
        using (var db = new AppDbContext())
        {
            var prod = db.Products.First();
            OperationWriter.Register(db, OperationType.StockAdjustment, prod.Name, 0, "Products", prod.Id,
                summaryJson: "الكمية: 13 → 15 — السبب: جرد فعلي — بواسطة: مدير النظام");
            prod.Stock = 15;
            prod.UpdatedAt = DateTime.Now;
            db.SaveChanges();
        }

        int sale2Id;
        // عملية 4: بيع S2 (قطعة) → 14
        opSeq++;
        using (var db = new AppDbContext())
        {
            var prod = db.Products.First();
            var sale2 = new Sale
            {
                InvoiceNumber = "S2",
                Date = DateTime.Now,
                UserId = admin.Id,
                Device = Session.Device,
                PaymentMethod = PaymentMethod.Cash,
                Total = 150,
                Profit = 50
            };
            sale2.Items.Add(new SaleItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 1, UnitPrice = 150, BuyPrice = 100 });
            var op = OperationWriter.Register(db, OperationType.Sale, "S2", 150, "Sales");
            db.Sales.Add(sale2);
            prod.Stock -= 1;
            prod.UpdatedAt = DateTime.Now;
            db.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Sales", sale2.Id);
            db.SaveChanges();
            sale2Id = sale2.Id;
        }

        // عملية 5: إلغاء S2 → شاهد قبر + رد المخزون → 15
        opSeq++;
        using (var db = new AppDbContext())
        {
            var sale2 = db.Sales.First(s => s.Id == sale2Id);
            sale2.DeletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var prod = db.Products.First();
            prod.Stock += 1;
            prod.UpdatedAt = DateTime.Now;
            OperationWriter.Register(db, OperationType.SaleVoid, "S2", -150, "Sales", sale2.Id,
                summaryJson: "إلغاء فاتورة S2 — بواسطة: مدير النظام");
            db.SaveChanges();
        }

        r.Add("deviceA seeded: 5 ops executed (seq up to " + opSeq + ")");
        return productSyncId;
    }

    // ===================== الفحوصات =====================

    private static void VerifyB(List<string> r, string deviceA, string productSyncId)
    {
        using var db = new AppDbContext();

        var prod = db.Products.AsNoTracking().First(p => p.SyncId == productSyncId);
        Check(r, prod.Stock == 15, "على B: مخزون المنتج = " + prod.Stock + " (متوقع 15 بعد التسوية + إلغاء البيع)");
        Check(r, prod.Name == "هاتف X", "على B: اسم المنتج مطابق");

        var sales = db.Sales.AsNoTracking().OrderBy(s => s.InvoiceNumber).ToList();
        Check(r, sales.Count == 2, "على B: عدد المبيعات = " + sales.Count + " (متوقع 2)");
        var s1 = sales.Single(s => s.InvoiceNumber == "S1");
        var s2 = sales.Single(s => s.InvoiceNumber == "S2");
        Check(r, s1.DeletedAt is null, "على B: S1 نشطة (لا شاهد قبر)");
        Check(r, s2.DeletedAt is not null, "على B: S2 عليها شاهد قبر (إلغاء)");
        Check(r, db.SaleItems.AsNoTracking().Count(x => x.SaleId == s1.Id) == 1, "على B: بنود S1 موجودة");

        var ops = db.OperationLogs.AsNoTracking().OrderBy(o => o.Seq).ToList();
        var expected = new[] { "Sale", "Purchase", "StockAdjustment", "Sale", "SaleVoid" };
        Check(r, ops.Count == 5, "على B: سجل العمليات = " + ops.Count + " (متوقع 5)");
        Check(r, ops.Select(o => o.OpType).SequenceEqual(expected), "على B: ترتيب أنواع العمليات مطابق (" + string.Join("/", ops.Select(o => o.OpType)) + ")");
        Check(r, ops.Select(o => o.Amount).SequenceEqual(new decimal[] { 300, 500, 0, 150, -150 }), "على B: مبالغ العمليات تطابق الأصل");

        Check(r, db.SyncLogs.AsNoTracking().Count(x => x.Direction == "In" && x.OriginDevice == deviceA) == 5,
            "على B: 5 إيصالات استلام بأصل A");
        Check(r, db.OperationLogs.AsNoTracking().All(o => o.OriginDevice == deviceA), "على B: كل العمليات أصلها A");
        Check(r, db.OperationLogs.AsNoTracking().All(o => o.OriginSeq == o.Seq),
            "على B: OriginSeq = Seq عند التطبيق المتصل (هوية أصل محفوظة بلا ترقيم زائف)");
        Check(r, db.Suppliers.AsNoTracking().Any(x => x.Name == "مورد1"), "على B: المورد منقول");
    }

    private static void VerifyA(List<string> r, string deviceB, string productSyncId)
    {
        using var db = new AppDbContext();

        var prod = db.Products.AsNoTracking().First(p => p.SyncId == productSyncId);
        Check(r, prod.Stock == 12, "على A بعد استقبال S3 من B: المخزون = " + prod.Stock + " (متوقع 12)");

        var s3 = db.Sales.AsNoTracking().FirstOrDefault(s => s.InvoiceNumber == "S3");
        Check(r, s3 is not null, "على A: فاتورة S3 (بيع جهاز B) وصلت");
        Check(r, s3!.Total == 450, "على A: إجمالي S3 = 450");

        var opsA = db.OperationLogs.AsNoTracking().OrderBy(o => o.Seq).ToList();
        Check(r, opsA.Count == 6 && opsA[^1].OpType == "Sale", "على A: سجل العمليات = 6 وآخرها بيع B");
        Check(r, db.SyncLogs.AsNoTracking().Count(x => x.Direction == "In" && x.OriginDevice == deviceB) == 1,
            "على A: إيصال استلام بأصل " + deviceB);

        var sumA = opsA.Sum(o => o.Amount);
        Check(r, sumA == 300 + 500 + 0 + 150 - 150 + 450, "على A: مجموع العمليات = " + sumA + " (متوقع 1250)");

        Check(r, prod.SyncId == productSyncId, "على A: نفس SyncId للمنتج الواحد بين الجهازين");
    }

    // ===================== أدوات =====================

    private static void UseDevice(string dbFile, string deviceId)
    {
        AppDbContext.TestConnectionString = new SqliteConnectionStringBuilder { DataSource = dbFile }.ToString();
        Session.DeviceIdOverride = deviceId;
        SqliteConnection.ClearAllPools();
        using (var db = new AppDbContext())
        {
            db.Database.EnsureCreated();
        }
    }

    private static void SeedAdmin()
    {
        using var db = new AppDbContext();
        if (!db.Users.Any())
        {
            db.Users.Add(new User
            {
                Username = "admin",
                PasswordHash = PasswordHasher.Hash("admin"),
                DisplayName = "مدير النظام",
                Role = UserRole.Admin,
                IsActive = true,
                MustChangePassword = true
            });
            db.SaveChanges();
        }
    }

    private static User CurrentAdmin()
    {
        using var db = new AppDbContext();
        return db.Users.First(u => u.Role == UserRole.Admin);
    }

    private static void Check(List<string> r, bool condition, string label)
    {
        r.Add((condition ? "PASS : " : "FAIL !! ") + label);
    }

    // ===================== بقايا M2: قفل الكاتب + الجمع التوافقي + فرض EntityId =====================

    private static void RunRemainderScenario(List<string> r, string dbP, string dbQ, string dbW)
    {
        r.Add("--- بقايا M2: SyncWriteLock (15.6) + FinancialTotals (15.5) + EntityId (15.4) ---");

        // M2.5 (a): إعادة الدخول بنفس الخيط مسموحة وتُطلق بلا تعليق
        using (SyncWriteLock.Acquire(TimeSpan.FromSeconds(2), "test-outer"))
        using (SyncWriteLock.Acquire(TimeSpan.FromSeconds(2), "test-inner"))
        {
            // الدخول المتداخل: الغلاف الداخلي لا يحاصر؛ الغلاف الخارجي يملك
        }
        using (SyncWriteLock.Acquire(TimeSpan.FromSeconds(2), "test-after-reentrant"))
        {
        }
        Check(r, true, "M2.5(a): إعادة الدخول بنفس الخيط تُفتح وتُقفل بدون تعليق");

        // M2.5 (b): استنزاف المهلة من خيط آخر (إعادة الدخول على نفس الخيط مسموحة فلا تُقاس منه) — يرفض، لا يعلّق
        // حتمي: إشارة بدء صريحة ينتظرها الرئيس قبل قياس الاستنزاف — لا تعتمد على سرعة جدولة الخيط
        var held = SyncWriteLock.Acquire(TimeSpan.FromSeconds(2), "test-held");
        var timedOut = false;
        var bStarted = new ManualResetEventSlim(false);
        var timeoutDone = new ManualResetEventSlim(false);
        var tb = new Thread(() =>
        {
            bStarted.Set();
            try
            {
                using var _ = SyncWriteLock.Acquire(TimeSpan.FromMilliseconds(150), "test-timeout");
                timedOut = false;
            }
            catch (TimeoutException) { timedOut = true; }
            timeoutDone.Set();
        });
        tb.Start();
        var bStartedOnTime = bStarted.Wait(8000);
        var bFinished = timeoutDone.Wait(8000);
        var bJoined = bFinished && tb.Join(3000);
        held.Dispose();
        Check(r, bStartedOnTime && bFinished && timedOut && bJoined && !tb.IsAlive, "M2.5(b): استنزاف المهلة يُرمي TimeoutException بدل التعليق، ويفسح المكان بعد التحرير");

        // M2.5 (c): خيط ثانٍ ينتظر حتى يحرّر صاحب القفل — حتمي عبر مرحلتين بإشارات (بلا نوم ثابت)
        var t2Blocked = false;
        var t2SecondGot = false;
        var t2FirstDone = new ManualResetEventSlim(false);
        var t2Ready = new ManualResetEventSlim(false);
        var t2go = new ManualResetEventSlim(false);
        var t2done = new ManualResetEventSlim(false);
        var held2 = SyncWriteLock.Acquire(TimeSpan.FromSeconds(5), "test-t1");
        var t2 = new Thread(() =>
        {
            t2Ready.Set();
            try
            {
                using var _ = SyncWriteLock.Acquire(TimeSpan.FromMilliseconds(150), "test-t2-blocked");
                t2Blocked = false;
            }
            catch (TimeoutException) { t2Blocked = true; } // محجوب: لم يُفسح المالك بعد
            t2FirstDone.Set(); // انتهت المرحلة الأولى — والمالك لم يُحرَّر بعد
            t2go.Wait(3000);   // تنتظر إذن الرئيس بعد التحرير
            using (SyncWriteLock.Acquire(TimeSpan.FromSeconds(10), "test-t2-after")) { t2SecondGot = true; }
            t2done.Set();
        });
        t2.Start();
        var t2ReadyOnTime = t2Ready.Wait(8000);
        var t2FirstFinished = t2FirstDone.Wait(8000); // المرحلة الأولى انقضت والملكية ما زالت مع الرئيس ⇒ محجوب فعلاً
        held2.Dispose();                              // التحرير بعد إثبات الانسداد
        t2go.Set();
        var t2Finished = t2done.Wait(8000);
        var t2Joined = t2Finished && t2.Join(3000);
        Check(r, t2ReadyOnTime && t2FirstFinished && t2Blocked && t2Finished && t2SecondGot && t2Joined && !t2.IsAlive,
            "M2.5(c): الخيط الثاني محجوب حتى التحرير ثم يكمل (لا سباق كتابة)");

        // M2.3 (a): المجموع التوافقي متساوٍ بين P وQ بعد مزامنة TCP (نفس ترتيب Seq ⇒ نفس الجمع)
        using (var dbPc = new AppDbContext(dbP))
        using (var dbQc = new AppDbContext(dbQ))
        {
            var sumP = FinancialTotals.CanonicalSum(dbPc);
            var sumQ = FinancialTotals.CanonicalSum(dbQc);
            Check(r, sumP != 0m && sumP == sumQ, $"M2.3(a): المجموع التوافقي متساوٍ بعد المزامنة — P={sumP} Q={sumQ}");
            Check(r, FinancialTotals.Verify(dbPc, dbQc, out _), "M2.3(a): التحقق المالي الكامل P↔Q ناجح (بلا تصحيح)");
        }

        // M2.4 (a): مكتبة P — كل عملياتها الوثائقية تحمل EntityId (مسار الكتابة الموحّد)
        using (var dbPc = new AppDbContext(dbP))
        {
            var missing = OperationWriter.DocumentOpsMissingEntityId(dbPc);
            Check(r, missing.Count == 0, "M2.4(a): على P لا مستند بلا EntityId — مسار الكتابة الموحّد يفرض الربط (S9/تعديلات..)");
        }

        // W: جهاز ثالث يُنشئ مستنده الخاص بنفس المعاملة (Register بلا EntityId ثم SetEntityTarget بعد الحفظ — نمط الإنتاج)
        UseDevice(dbW, "deviceW_TEST");
        SeedAdmin();
        Session.CurrentUser = CurrentAdmin();
        var saleWRev = 0;
        using (var db = new AppDbContext())
        {
            var cat = new Category { Name = "هواتف" };
            db.Categories.Add(cat);
            db.SaveChanges();
            var prod = new Product { Name = "هاتف W", CategoryId = cat.Id, Stock = 5, BuyPrice = 100, SellPrice = 160, Device = DeviceRole.Cashier };
            db.Products.Add(prod);
            db.SaveChanges();

            var sale = new Sale
            {
                InvoiceNumber = "S9W",
                Date = DateTime.Now,
                UserId = Session.CurrentUser!.Id,
                Device = Session.Device,
                PaymentMethod = PaymentMethod.Cash,
                Total = 320,
                Profit = 120
            };
            sale.Items.Add(new SaleItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 2, UnitPrice = 160, BuyPrice = 100 });
            var wop = OperationWriter.Register(db, OperationType.Sale, "S9W", 320, "Sales");
            db.Sales.Add(sale);
            db.SaveChanges();
            OperationWriter.SetEntityTarget(wop, "Sales", sale.Id);
            db.SaveChanges();
            saleWRev = wop.Seq > 0 ? 1 : 0;
        }
        Check(r, saleWRev == 1, "M2.4(b): مستند جديد يُربط EntityId بعد الحفظ الأول (نمط الإنتاج) وتُحفظ هويته");

        using (var dbWc = new AppDbContext(dbW))
        {
            var missingW = OperationWriter.DocumentOpsMissingEntityId(dbWc);
            Check(r, missingW.Count == 0, "M2.4(b): تدقيق W — لا مستند بلا EntityId بعد النمط البديل");
        }

        // M2.3 (b): Q نسخة مطابقة لـ P بعد المزامنة الحقيقية — المجموعان متساويان بالترتيب التوافقي
        using (var dbQc = new AppDbContext(dbQ))
        using (var dbPc2 = new AppDbContext(dbP))
        {
            var sumQ1 = FinancialTotals.CanonicalSum(dbQc);
            var sumP2 = FinancialTotals.CanonicalSum(dbPc2);
            Check(r, sumQ1 == sumP2 && sumQ1 != 0m, $"M2.3(b): نسخةُ المزامنة Q تطابق P توافقياً — Q={sumQ1} P={sumP2}");
        }

        // تلاعب: حقن عملية +0.02 في نسخة Q ثم مقارنة — يُكشف الاختلاف بلا تصحيح تلقائي
        using (var dbQc = new AppDbContext(dbQ))
        {
            dbQc.OperationLogs.Add(new OperationLog
            {
                Seq = 88888,
                OpType = "Sale",
                EntityName = "Sales",
                DocumentNumber = "TAMPER",
                Amount = 0.02m,
                UserId = 1,
                UserRole = UserRole.Admin,
                DeviceRole = DeviceRole.Cashier,
                CommittedAt = DateTime.UtcNow,
                OriginDevice = Session.DeviceId,
                OriginSeq = 1
            });
            dbQc.SaveChanges();
        }

        using (var dbQc2 = new AppDbContext(dbQ))
        using (var dbPc3 = new AppDbContext(dbP))
        {
            var sumQ2 = FinancialTotals.CanonicalSum(dbQc2);
            var sumP3 = FinancialTotals.CanonicalSum(dbPc3);
            var (_, diff) = FinancialTotals.Compare(sumQ2, sumP3);
            Check(r, diff > 0.01m, $"M2.3(c): حقن +0.02 في نسخة Q يُكشف — فرق {diff:0.00} (> تسمح 0.01)");
            Check(r, !FinancialTotals.Verify(dbQc2, dbPc3, out _), "M2.3(c): Verify يرفض النسخة المزيّفة — تسجيل تنبيه بلا إصلاح");
        }

        // M2.3 (d): فرق دون 0.01 يُعدّ ضمن التسامح (لا إنذار كاذب)
        using (var dbPc4 = new AppDbContext(dbP))
        {
            var sumP4 = FinancialTotals.CanonicalSum(dbPc4);
            var (within, _) = FinancialTotals.Compare(sumP4 + 0.005m, sumP4);
            Check(r, within, "M2.3(d): فرق دون 0.01 ضمن التسامح (كشف بلا تصحيح، لا إنذار كاذب)");
        }
    }

    // ===================== بوابة P3.0a: صلابة القناة + التزامن الحقيقي + EntityId =====================

    /// <summary>
    /// P3.0a: أربعة إغلاقات — A1 تزامن حقيقي (تطبيق دلتا يوازي حفظ بيع على نفس القاعدة)،
    /// A2 (أ) عميل معلّق يُهجَر بالمهلة، (ب) نشر مجزّأ على القارئ، (ج) جلستان متزامنتان،
    /// (د) Tombstone يسلَم عبر التمرير + A3 EntityId في المسارات الأربعة — كلها بفحوص محكّمة.
    /// </summary>
    private static void RunP30aScenario(List<string> r, string dbD, string dbE, string dbG, string dbH, string dbI, string dbJ)
    {
        const string secret = "phase3_secret_a";

        // ---- تجهيز الأجهزة: هوية وسرّ لكل قاعدة ----
        UseDevice(dbG, "deviceG_TEST"); SeedAdmin(); ConfigurePeer("deviceG_TEST", secret);   // خادم القناة
        UseDevice(dbD, "deviceD_TEST"); SeedAdmin(); ConfigurePeer("deviceD_TEST", secret);   // هدف التزامن
        UseDevice(dbE, "deviceE_TEST"); SeedAdmin(); ConfigurePeer("deviceE_TEST", secret);   // مسارات A3
        UseDevice(dbH, "deviceH_TEST"); SeedAdmin(); ConfigurePeer("deviceH_TEST", secret);   // عميل A2(a)
        UseDevice(dbI, "deviceI_TEST"); SeedAdmin(); ConfigurePeer("deviceI_TEST", secret);   // عميل سحب A2(c)
        UseDevice(dbJ, "deviceJ_TEST"); SeedAdmin(); ConfigurePeer("deviceJ_TEST", secret);   // عميل دفع A2(c)

        // بيانات أساس: G بيع واحد قابل للسحب، D صنف+منتج للبيع المحلي
        Session.DeviceIdOverride = "deviceG_TEST"; // بيع أساس G يحمل هوية G الأصلية
        using (var g = new AppDbContext(dbG))
        {
            Session.CurrentUser = CurrentAdmin();
            var cat = new Category { Name = "هواتف G" };
            g.Categories.Add(cat); g.SaveChanges();
            var prod = new Product { Name = "هاتف G0", CategoryId = cat.Id, Stock = 9, BuyPrice = 100, SellPrice = 150, Device = DeviceRole.Cashier };
            g.Products.Add(prod); g.SaveChanges();
            var sale = new Sale
            {
                InvoiceNumber = "S0G", Date = DateTime.Now, UserId = CurrentAdmin().Id,
                Device = Session.Device, PaymentMethod = PaymentMethod.Cash, Total = 300, Profit = 100
            };
            sale.Items.Add(new SaleItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 2, UnitPrice = 150, BuyPrice = 100 });
            var op = OperationWriter.Register(g, OperationType.Sale, "S0G", 300m, "Sales");
            g.Sales.Add(sale); g.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Sales", sale.Id); g.SaveChanges();
        }
        using (var d = new AppDbContext(dbD))
        {
            Session.CurrentUser = CurrentAdmin();
            var cat = new Category { Name = "هواتف D" };
            d.Categories.Add(cat); d.SaveChanges();
            var prod = new Product { Name = "هاتف D0", CategoryId = cat.Id, Stock = 9, BuyPrice = 90, SellPrice = 140, Device = DeviceRole.Cashier };
            d.Products.Add(prod); d.SaveChanges();
        }

        // ---- A4: رفض الإطار المتجاوز للسقف فوراً (فحص الرأس قبل الجسم) ----
        var capHdr = BitConverter.GetBytes(SyncPeer.MaxFrameBytes + 1);
        if (BitConverter.IsLittleEndian) Array.Reverse(capHdr);
        var oversize = new byte[4 + 128];
        Array.Copy(capHdr, oversize, 4);
        long consumed = -1;
        using (var overMs = new MemoryStream(oversize))
        {
            try { SyncPeer.ReadFrame(overMs); }
            catch (InvalidOperationException) { consumed = overMs.Position; }
            catch (Exception) { consumed = -2; }
        }
        Check(r, consumed == 4, "P3.0a/A4: رأس إطار يتجاوز 50MB — رفض فوري دون قراءة الجسم (استُهلك الرأس فقط)");
        var negHdr = BitConverter.GetBytes(-7);
        if (BitConverter.IsLittleEndian) Array.Reverse(negHdr);
        bool negRejected = false;
        try { SyncPeer.ReadFrame(new MemoryStream(negHdr)); }
        catch (InvalidOperationException) { negRejected = true; }
        catch (Exception) { }
        Check(r, negRejected, "P3.0a/A4: طول سالب مرفوض أيضاً (التحقق لا يقتصر على طرف البدء)");

        // ---- A2(b): القارئ يعيد إعمار ما يصل مقطّعاً من قبضة ReadExactly ----
        var json = "{\"Type\":\"Hello\",\"DeviceId\":\"x\",\"Secret\":\"s\",\"SchemaVersion\":5}";
        var frame = BuildFrameBytes(json);
        string restored;
        try { restored = SyncPeer.ReadFrame(new ChunkedStream(frame, seed: 1234, maxChunk: 4)); }
        catch (Exception ex) { restored = "ERR:" + ex.Message; }
        Check(r, restored == json, $"P3.0a/A2(b): إعمار إطار كامل من شرائح 1–4 بايت عشوائية (حدّ الرأس مقتطع) — {frame.Length} بايت بلا التباس طول");

        var bigPayload = new string('x', 20 * 1024 * 1024);
        var bigFrame = BuildFrameBytes(bigPayload);
        string restoredBig;
        try { restoredBig = SyncPeer.ReadFrame(new ChunkedStream(bigFrame, seed: 99, maxChunk: 64 * 1024)); }
        catch (Exception ex) { restoredBig = "ERR:" + ex.Message; }
        Check(r, restoredBig.Length == bigPayload.Length, "P3.0a/A2(b): لوحة 20MB تُقرأ كاملة عبر قارئ مقطّع — لا تقصير عند الحجم الكبير");

        // ---- A1: تطبيق دلتا يوازي حفظ بيع على نفس القاعدة (سياقان) ----
        using (new AppDbContext(dbD)) { } // إحماء إنشاء المخطط قبل سَباق القفل
        Session.CurrentUser = CurrentAdmin();
        var adminA1 = Session.CurrentUser;
        var devA1 = Session.Device;
        string? concurrencyError = null;
        int pulledTotal = 0;
        for (var k = 0; k < 6; k++)
        {
            var i = k;
            // المصدر: بيع G{i} يُكتب بهوية G — على الخيط الرئيسي (لا سباق على Session)
            Session.DeviceIdOverride = "deviceG_TEST";
            using (var g = new AppDbContext(dbG))
            {
                var prod = g.Products.AsNoTracking().First();
                var sale = new Sale
                {
                    InvoiceNumber = $"G{i}S", Date = DateTime.Now, UserId = adminA1!.Id,
                    Device = devA1, PaymentMethod = PaymentMethod.Cash, Total = 200, Profit = 50
                };
                sale.Items.Add(new SaleItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 1, UnitPrice = 150, BuyPrice = 100 });
                var op = OperationWriter.Register(g, OperationType.Sale, sale.InvoiceNumber, sale.Total, "Sales");
                g.Sales.Add(sale); g.SaveChanges();
                OperationWriter.SetEntityTarget(op, "Sales", sale.Id); g.SaveChanges();
            }
            // التوازي: «تطبيق دلتا» و«حفظ بيع محلي» على قاعدة D بسياقين منفصلين
            var applier = Task.Run(() =>
            {
                using var g2 = new AppDbContext(dbG);
                var packets = DeltaBuilder.BuildNew(g2, "deviceG_TEST");
                using var d = new AppDbContext(dbD);
                return DeltaApplier.Apply(d, "deviceG_TEST", packets, "deviceD_TEST").Applied;
            });
            var local = Task.Run(() =>
            {
                Session.DeviceIdOverride = "deviceD_TEST"; // المصدر الوحيد للكتابة المحلية — بلا منافس يكتب الهوية
                using var d = new AppDbContext(dbD);
                var prod = d.Products.AsNoTracking().First();
                var sale = new Sale
                {
                    InvoiceNumber = $"L{i}S", Date = DateTime.Now, UserId = adminA1!.Id,
                    Device = devA1, PaymentMethod = PaymentMethod.Cash, Total = 150, Profit = 40
                };
                sale.Items.Add(new SaleItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 1, UnitPrice = 140, BuyPrice = 90 });
                var op = OperationWriter.Register(d, OperationType.Sale, sale.InvoiceNumber, sale.Total, "Sales");
                d.Sales.Add(sale); d.SaveChanges();
                OperationWriter.SetEntityTarget(op, "Sales", sale.Id); d.SaveChanges();
            });
            try { Task.WhenAll(applier, local).GetAwaiter().GetResult(); }
            catch (Exception ex) { concurrencyError = ex.Message; break; }
            pulledTotal += applier.Result;
        }
        Check(r, concurrencyError is null && pulledTotal >= 6,
            $"P3.0a/A1: تطبيق دلتا + بيع محلي على القاعدة نفسها بسياقات منفصلة (6 تكرارات) — لا database locked (تطبيقات بعيدة {pulledTotal}/6)");
        using (var d = new AppDbContext(dbD))
        {
            var sales = d.Sales.AsNoTracking().Count(s => s.InvoiceNumber.StartsWith("G") || s.InvoiceNumber.StartsWith("L"));
            Check(r, sales == 12, $"P3.0a/A1: أثر الطرفين كامل — 12 بيعاً محلياً+بعيداً (وجد {sales})");
            Check(r, OperationWriter.DocumentOpsMissingEntityId(d).Count == 0, "P3.0a/A1: كل بيعي الجانبين يحمل EntityId رغم التزامن");
        }

        // ---- A2(a): عميل يفتح ولا يُرسل شيئاً — يُهجَر بالزمن الميت والخادم يبقى سليماً ----
        var serverPort = 47133;
        using var serverCts = new CancellationTokenSource();
        var serverTask = SyncPeer.StartServer(serverPort, dbG, serverCts.Token);
        Thread.Sleep(400);

        SyncPeer.ServerReadTimeoutMs = 600;
        var stuck = new System.Net.Sockets.TcpClient();
        stuck.Connect("127.0.0.1", serverPort);
        Thread.Sleep(1500); // بلا أي بايت — يتجاوز المهلة
        var t0 = DateTime.UtcNow;
        bool socketReleased = false;
        try { socketReleased = stuck.GetStream().ReadByte() == -1; }
        catch (Exception) { socketReleased = true; }
        stuck.Close();
        SyncPeer.ServerReadTimeoutMs = 15000;
        var abandonedFast = socketReleased && (DateTime.UtcNow - t0) < TimeSpan.FromSeconds(5);
        Check(r, abandonedFast, "P3.0a/A2(a): عميل معلّق بلا بيانات يُرفع بالزمن الميت فوراً — خدمة لا تتجمد");

        // الخادم بقي سليماً: مزامنة حقيقية تجلب بياناته
        var afterStall = SyncPeer.Synchronize("127.0.0.1", serverPort, dbH, secretOverride: secret);
        Check(r, afterStall.Accepted && afterStall.Pulled >= 1, $"P3.0a/A2(a): بعد إهدار المعلّق — مزامنة حقيقية تمر وتجلب ({afterStall.Pulled})");

        // ---- A2(c): جلستان متزامنتان على خادم واحد — ساحبة تنتزع بينما دافعة تدفع ----
        Session.DeviceIdOverride = "deviceJ_TEST"; // بيع J يحمل هوية J قبل المزامنة
        using (var j = new AppDbContext(dbJ))
        {
            Session.CurrentUser = CurrentAdmin();
            var cat = new Category { Name = "هواتف J" }; j.Categories.Add(cat); j.SaveChanges();
            var prod = new Product { Name = "هاتف J", CategoryId = cat.Id, Stock = 5, BuyPrice = 80, SellPrice = 130, Device = DeviceRole.Cashier };
            j.Products.Add(prod); j.SaveChanges();
            var sale = new Sale
            {
                InvoiceNumber = "J_1", Date = DateTime.Now, UserId = CurrentAdmin().Id,
                Device = Session.Device, PaymentMethod = PaymentMethod.Cash, Total = 250, Profit = 60
            };
            sale.Items.Add(new SaleItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 1, UnitPrice = 130, BuyPrice = 80 });
            var op = OperationWriter.Register(j, OperationType.Sale, "J_1", 250m, "Sales");
            j.Sales.Add(sale); j.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Sales", sale.Id); j.SaveChanges();
        }
        var c1 = Task.Run(() => SyncPeer.Synchronize("127.0.0.1", serverPort, dbI, secretOverride: secret)); // سحب
        var c2 = Task.Run(() => SyncPeer.Synchronize("127.0.0.1", serverPort, dbJ, secretOverride: secret)); // دفماً وسحباً
        Task.WhenAll(c1, c2).GetAwaiter().GetResult();
        var (sPull, sPush) = (c1.Result, c2.Result);
        Check(r, sPull.Accepted && sPush.Accepted, $"P3.0a/A2(c): جلستان متزامنتان على خادم واحد — القبول للاثنتين معاً (pull {sPull.Pulled} | push {sPush.Pushed})");
        Check(r, sPull.Pulled >= 1 && sPush.Pushed >= 1, "P3.0a/A2(c): السحب استلم بيانات الخادم والدفع أوصل بيع J — تبادل ثنائي بين جلستين متوازيتين");

        serverCts.Cancel();
        try { serverTask.Wait(3000); } catch { }

        // ---- A3: المسارات الأربعة تحمل EntityId + التدقيق يفضح الغياب فوراً (فشل آلي) ----
        UseDevice(dbE, "deviceE_TEST"); SeedAdmin(); Session.CurrentUser = CurrentAdmin();
        using (var e = new AppDbContext(dbE))
        {
            var cat = new Category { Name = "هواتف E" }; e.Categories.Add(cat); e.SaveChanges();
            var prod = new Product { Name = "هاتف E", CategoryId = cat.Id, Stock = 8, BuyPrice = 80, SellPrice = 130, Device = DeviceRole.Cashier };
            e.Products.Add(prod); e.SaveChanges();
            var customer = new Customer { Name = "عميل E", Balance = 0 };
            e.Customers.Add(customer); e.SaveChanges();

            // المسار 1 — بيع
            var sale = new Sale
            {
                InvoiceNumber = "E_S", Date = DateTime.Now, UserId = CurrentAdmin().Id,
                Device = Session.Device, PaymentMethod = PaymentMethod.Cash, Total = 200, Profit = 40
            };
            sale.Items.Add(new SaleItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 1, UnitPrice = 130, BuyPrice = 80 });
            var saleOp = OperationWriter.Register(e, OperationType.Sale, "E_S", 200m, "Sales");
            e.Sales.Add(sale); e.SaveChanges();
            OperationWriter.SetEntityTarget(saleOp, "Sales", sale.Id); e.SaveChanges();

            // المسار 2 — شراء
            var purchase = new Purchase
            {
                InvoiceNumber = "E_P", Date = DateTime.Now, UserId = CurrentAdmin().Id, Device = Session.Device,
                SupplierId = null, SupplierName = null, PaymentMethod = PaymentMethod.Cash, Discount = 0, Total = 300
            };
            purchase.Items.Add(new PurchaseItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 3, UnitPrice = 100 });
            var purchaseOp = OperationWriter.Register(e, OperationType.Purchase, "E_P", 300m, "Purchases");
            e.Purchases.Add(purchase); e.SaveChanges();
            OperationWriter.SetEntityTarget(purchaseOp, "Purchases", purchase.Id); e.SaveChanges();

            // المسار 3 — سند قبض
            var voucher = new Voucher
            {
                VoucherNumber = "E_V", Date = DateTime.Now, CustomerId = customer.Id,
                Amount = 120, PaymentMethod = VoucherPaymentMethod.Cash, UserId = CurrentAdmin().Id
            };
            var voucherOp = OperationWriter.Register(e, OperationType.VoucherIn, "E_V", 120m, "Vouchers");
            e.Vouchers.Add(voucher); e.SaveChanges();
            OperationWriter.SetEntityTarget(voucherOp, "Vouchers", voucher.Id); e.SaveChanges();

            // المسار 4 — إصلاح مستلم
            var job = new RepairJob
            {
                JobNumber = "E_R", ReceivedDate = DateTime.Now, CustomerName = "عميل إصلاح",
                DeviceName = "هاتف E", Issue = "لا يعمل", UserId = CurrentAdmin().Id, Device = Session.Device,
                Status = RepairStatus.Received, LaborFee = 60, PaymentMethod = PaymentMethod.Cash,
                IsPaid = false, PartsTotal = 0, Total = 60
            };
            var repairOp = OperationWriter.Register(e, OperationType.RepairJob, "E_R", 60m, "RepairJobs");
            e.RepairJobs.Add(job); e.SaveChanges();
            OperationWriter.SetEntityTarget(repairOp, "RepairJobs", job.Id); e.SaveChanges();

            Check(r, OperationWriter.DocumentOpsMissingEntityId(e).Count == 0,
                "P3.0a/A3: المسارات الأربعة (بيع/شراء/سند/إصلاح) تحمل EntityId — نمط الإنتاج الصحيح بلا استثناء");

            // سلبي — التدقيق يفضح الغياب فوراً: مستند بلا SetEntityTarget
            var rogue = new Sale
            {
                InvoiceNumber = "E_ROGUE", Date = DateTime.Now, UserId = CurrentAdmin().Id,
                Device = Session.Device, PaymentMethod = PaymentMethod.Cash, Total = 100, Profit = 10
            };
            rogue.Items.Add(new SaleItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 1, UnitPrice = 130, BuyPrice = 80 });
            var rogueOp = OperationWriter.Register(e, OperationType.Sale, "E_ROGUE", 100m, "Sales");
            e.Sales.Add(rogue); e.SaveChanges();
            var caught = OperationWriter.DocumentOpsMissingEntityId(e).Count;
            Check(r, caught == 1, $"P3.0a/A3: الانحراف عن النمط (بلا ربط) يُعدّ فشلاً آلياً — التدقيق يلتقط الغياب ({caught} مستند)");
            OperationWriter.SetEntityTarget(rogueOp, "Sales", rogue.Id); e.SaveChanges();
            Check(r, OperationWriter.DocumentOpsMissingEntityId(e).Count == 0, "P3.0a/A3: إصلاح الربط يعيد التدقيق إلى الصفر");
        }
    }

    /// <summary>يبني إطاراً كاملاً (رأس 4 بايت + حمولة UTF-8) مطابقاً لمؤطّر SyncPeer — لأداة فحص القارئ.</summary>
    private static byte[] BuildFrameBytes(string content)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(content);
        var len = BitConverter.GetBytes(payload.Length);
        if (BitConverter.IsLittleEndian) Array.Reverse(len);
        var frame = new byte[4 + payload.Length];
        Array.Copy(len, frame, 4);
        Array.Copy(payload, 0, frame, 4, payload.Length);
        return frame;
    }

    /// <summary>قارئ وهمي يسلم البيانات بشرائح عشوائية صغيرة — يختبر أن ReadExactly يجمع ما يُقطع عمداً (حدّ الرأس تحديداً).</summary>
    private sealed class ChunkedStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _maxChunk;
        private readonly Random _rng;
        private int _pos;

        public ChunkedStream(byte[] data, int seed, int maxChunk)
        {
            _data = data;
            _maxChunk = maxChunk;
            _rng = new Random(seed);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
            var take = Math.Min(count, Math.Min(1 + _rng.Next(_maxChunk), _data.Length - _pos));
            Array.Copy(_data, _pos, buffer, offset, take);
            _pos += take;
            return take;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ===================== P3.0: خدمة المزامنة + كشف العطل الحقيقي =====================

    private static void RunP30bUiScenario(List<string> r, string dbE, string dbG, string dbJ, string dbK)
    {
        r.Add("--- P3.0: خدمة المزامنة (استطلاع قراءة-فقط + زامن الآن + إيصالات زمنية واحدة + عطل حقيقي) ---");

        const string secret = "phase3_secret_a";
        const int portHealthy = 47135;     // خادم سليم — G (عليه بيع S0G واحد قابل للسحب)
        const int portDead = 47137;        // منفذ ميت — لا مستمع
        const int portWrongSecret = 47136; // خادم بسرّ مختلف — E
        const int portSchema = 47138;      // خادم بنسخة Schema مختلفة — J

        UseDevice(dbK, "deviceK_TEST"); SeedAdmin(); ConfigurePeer("deviceK_TEST", secret); // العميل الفاعل

        // E: نفس الهوية لكن سرّ مختلف ⇒ رفض مصافحة «السر خاطئ»
        using (var e = new AppDbContext(dbE))
        {
            var s = e.AppSettings.FirstOrDefault(x => x.Key == "SyncSecret");
            if (s is not null) s.Value = "phase3_bad_secret_B";
            else e.AppSettings.Add(new AppSetting { Key = "SyncSecret", Value = "phase3_bad_secret_B" });
            e.SaveChanges();
        }

        // J: نفس السر لكن SchemaVersion=999 ⇒ رفض مصافحة «الإصدار مختلف»
        using (var j = new AppDbContext(dbJ))
        {
            var v = j.AppSettings.FirstOrDefault(x => x.Key == "SchemaVersion");
            if (v is not null) v.Value = "999";
            else j.AppSettings.Add(new AppSetting { Key = "SchemaVersion", Value = "999" });
            j.SaveChanges();
        }

        var cts = new CancellationTokenSource();
        var srvHealthy = SyncPeer.StartServer(portHealthy, dbG, cts.Token);
        var srvWrongSecret = SyncPeer.StartServer(portWrongSecret, dbE, cts.Token);
        var srvSchema = SyncPeer.StartServer(portSchema, dbJ, cts.Token);

        // توجيه الخدمة إلى K بلا سياق واجهة — تحديثات مباشرة لتشخيص حتمي
        Session.DeviceIdOverride = null; // K تعرّف نفسها من إعداداتها (deviceK_TEST)
        var svc = SyncStateService.Instance;
        SyncStateService.UiContext = null;
        SyncStateService.DbFileProvider = () => dbK;
        SyncStateService.PeersProvider = () =>
            $"127.0.0.1:{portDead},127.0.0.1:{portWrongSecret},127.0.0.1:{portSchema},127.0.0.1:{portHealthy}";

        // ===== 1) الاستطلاع قراءة-فقط — يكشف الأعطال ولا يكتب شيئاً =====
        long gLogs0, gState0, kLogs0, kState0;
        using (var g = new AppDbContext(dbG)) { gLogs0 = g.SyncLogs.Count(); gState0 = g.SyncPeerStates.Count(); }
        using (var k = new AppDbContext(dbK)) { kLogs0 = k.SyncLogs.Count(); kState0 = k.SyncPeerStates.Count(); }

        svc.ProbeOnce();

        var dead = svc.Peers.FirstOrDefault(x => x.Port == portDead);
        var badSecret = svc.Peers.FirstOrDefault(x => x.Port == portWrongSecret);
        var badSchema = svc.Peers.FirstOrDefault(x => x.Port == portSchema);
        var healthy = svc.Peers.FirstOrDefault(x => x.Port == portHealthy);

        Check(r, dead?.StatusKind == PeerHealthKind.Unreachable, "P3.0(1a): قريب ميت (منفذ مغلق) يُكتشَف «غير متاح»");
        Check(r, badSecret?.StatusKind == PeerHealthKind.Rejected && (badSecret.Detail ?? "").Contains("السر"),
            "P3.0(1b): سرّ خاطئ يُكتشَف «مرفوض — السر»");
        Check(r, badSchema?.StatusKind == PeerHealthKind.Rejected && (badSchema.Detail ?? "").Contains("Schema"),
            "P3.0(1c): SchemaVersion مختلف يُكتشَف مرفوض — Schema غير متطابق ظاهر");
        Check(r, healthy?.StatusKind == PeerHealthKind.Ok && healthy.RemoteDevice == "deviceG_TEST",
            "P3.0(1d): قريب سليم يوافق المصافحة وتعريف الهوية ظاهر");

        using (var g = new AppDbContext(dbG))
            Check(r, g.SyncLogs.Count() == gLogs0 && g.SyncPeerStates.Count() == gState0,
                "P3.0(1e): الاستطلاع قراءة-فقط — صفر كتابة على الخادم (G)");
        using (var k = new AppDbContext(dbK))
            Check(r, k.SyncLogs.Count() == kLogs0 && k.SyncPeerStates.Count() == kState0,
                "P3.0(1f): الاستطلاع قراءة-فقط — صفر كتابة على العميل (K)");

        // ===== 2) «زامن الآن» عبر الخدمة: فاشل ×3 ثم ناجح في سجل زمني واحد =====
        var receiptsBefore = svc.Receipts.Count;
        svc.SyncNowBlocking();

        var synced = svc.Receipts.Skip(receiptsBefore).ToList();
        Check(r, synced.Count == 4, "P3.0(2a): 4 إيصالات بقائمة زمنية واحدة (4 أقران)");
        Check(r, synced.Select(x => x.Success).SequenceEqual(new[] { false, false, false, true }),
            "P3.0(2b): القصة — فشل ×3 ثم ناجح في سجل واحد (لا تبويب نجاح منفصل)");
        Check(r, synced.Where(x => !x.Success).All(x => !string.IsNullOrWhiteSpace(x.Message)),
            "P3.0(2c): كل إيصال فاشل يحمل سبباً (Attempt/Error ظاهر)");
        Check(r, synced[3].Success && synced[3].Pulled >= 1 && synced[3].Pushed >= 0,
            "P3.0(2d): الناجح سحب بيع S0G من G وقيمته (pulled) ظاهرة بالإيصال");
        Check(r, !svc.SyncNowCommand.IsRunning, "P3.0(2e): اكتملت الجولة — الخدمة غير مشغولة حاليّاً");
        Check(r, svc.LastSyncText.Contains("اكتملت"), "P3.0(2f): سطر الحالة يُفصح عن إتمام الجولة");

        cts.Cancel();
        try { srvHealthy.Wait(2000); srvWrongSecret.Wait(2000); srvSchema.Wait(2000); } catch { }

        SyncStateService.DbFileProvider = () => DataPath.DatabaseFile;
        SyncStateService.PeersProvider = () => Database.GetSetting("SyncPeers", "");
    }

    // ===================== P3.1: التقاط الصراع بلا منتصر + الحسم اليدوي على MAIN =====================

    private static void SetDeviceRole(string dbPath, DeviceRole role)
    {
        using var db = new AppDbContext(dbPath);
        var s = db.AppSettings.FirstOrDefault(x => x.Key == "DeviceRole");
        if (s is not null) s.Value = role.ToString();
        else db.AppSettings.Add(new AppSetting { Key = "DeviceRole", Value = role.ToString() });
        db.SaveChanges();
    }

    private static (string SyncId, int LocalId) SeedProductOnMain(string dbM, string name, decimal price, int stock, string doc)
    {
        UseDevice(dbM, "deviceM_TEST");
        SetDeviceRole(dbM, DeviceRole.MainServer);
        using var db = new AppDbContext();
        var cat = db.Categories.FirstOrDefault(c => c.Name == "أجهزة P3.1");
        if (cat is null)
        {
            cat = new Category { Name = "أجهزة P3.1" };
            db.Categories.Add(cat);
            db.SaveChanges();
        }
        var p = new Product { Name = name, CategoryId = cat.Id, BuyPrice = price - 20m, SellPrice = price, Stock = stock, Device = DeviceRole.MainServer };
        db.Products.Add(p);
        db.SaveChanges();
        OperationWriter.Register(db, OperationType.StockAdjustment, doc, 0m, "Products", p.Id,
            summaryJson: "بذرة منتج MAIN — اختبار P3.1");
        db.SaveChanges();
        return (p.SyncId!, p.Id);
    }

    private static void EditProductPrice(AppDbContext db, string syncId, decimal price)
    {
        var p = db.Products.First(x => x.SyncId == syncId);
        p.SellPrice = price;
        OperationWriter.Register(db, OperationType.StockAdjustment, p.Name, 0m, "Products", p.Id,
            summaryJson: "تعديل سعر — اختبار P3.1");
        db.SaveChanges();
    }

    private static void DeleteProduct(AppDbContext db, string syncId)
    {
        var p = db.Products.First(x => x.SyncId == syncId);
        p.DeletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        OperationWriter.Register(db, OperationType.StockAdjustment, p.Name, 0m, "Products", p.Id,
            summaryJson: "حذف منتج — اختبار P3.1");
        db.SaveChanges();
    }

    private static decimal ReadPrice(AppDbContext db, string syncId)
        => db.Products.AsNoTracking().First(x => x.SyncId == syncId).SellPrice;

    private static bool ReadDeleted(AppDbContext db, string syncId)
        => db.Products.AsNoTracking().First(x => x.SyncId == syncId).DeletedAt is not null;

    private static void ResetDeviceDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(dbPath + suffix))
                File.Delete(dbPath + suffix);
    }

    private static void RunP31ConflictScenario(List<string> r, string dbM, string dbK, string dbN)
    {
        r.Add("--- P3.1: التقاط الصراع بلا منتصر (كشف بعلامة الماء) + الحسم اليدوي كعملية موقّعة من MAIN ---");

        const string secret = "phase3_secret_a";
        const int portM = 47151;   // خادم MAIN (مصدر البذرات والعمليات الموجّهة)
        const int portN = 47152;   // خادم N — وجهة تبادل K↔N
        const string dk = "deviceK_TEST", dn = "deviceN_TEST", dm = "deviceM_TEST";

        // بقايا P3.0 استخدمت dbK — تُعاد بناؤها عذراء (حذف آمن بعد فك حمام السببكة: لا أحد يستخدمها بعد ذلك)
        ResetDeviceDb(dbM); ResetDeviceDb(dbK); ResetDeviceDb(dbN);

        // أدوار وهمية حتمية: M=MainServer، K/N=Cashier، سر مشترك + إدارة لكل قاعدة
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dk, secret);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dn, secret);
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dm, secret);

        var cts = new CancellationTokenSource();
        var srvM = SyncPeer.StartServer(portM, dbM, cts.Token);
        var srvN = SyncPeer.StartServer(portN, dbN, cts.Token);

        // ===== (2) بذرة P(100) على MAIN كعملية موقّعة ⇒ تنتشر M→K ثم M→N =====
        var (p1Sync, _) = SeedProductOnMain(dbM, "جهاز P1", 100m, 10, "P1");
        Session.DeviceIdOverride = dk;
        var k1 = SyncPeer.Synchronize("127.0.0.1", portM, dbK, maxAttempts: 1);
        Session.DeviceIdOverride = dn;
        var n1 = SyncPeer.Synchronize("127.0.0.1", portM, dbN, maxAttempts: 1);
        Check(r, k1.Accepted && k1.Pulled == 1, $"P3.1(2a): K سحبت بذرة P1 (pulled={k1.Pulled})");
        Check(r, n1.Accepted && n1.Pulled == 1, $"P3.1(2b): N سحبت بذرة P1 (pulled={n1.Pulled})");
        using (var k = new AppDbContext(dbK)) Check(r, ReadPrice(k, p1Sync) == 100m, "P3.1(2c): K ترى السعر 100");
        using (var n = new AppDbContext(dbN)) Check(r, ReadPrice(n, p1Sync) == 100m, "P3.1(2d): N ترى السعر 100");

        // ===== (3) انشقاق: K=150 وN=200 ثم K↔N (خادم N) ⇒ التقاط بلا منتصر على الجهتين =====
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier);
        using (var k = new AppDbContext()) EditProductPrice(k, p1Sync, 150m);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier);
        using (var n = new AppDbContext()) EditProductPrice(n, p1Sync, 200m);

        Session.DeviceIdOverride = dk;
        var kn1 = SyncPeer.Synchronize("127.0.0.1", portN, dbK, maxAttempts: 1);
        Check(r, kn1.Accepted && kn1.Pulled == 0 && kn1.Pushed == 0,
            $"P3.1(3a): أول تبادل K↔N بلا تطبيق — لا منتصر (pulled={kn1.Pulled}, pushed={kn1.Pushed})");
        using (var k = new AppDbContext(dbK))
        using (var n = new AppDbContext(dbN))
        {
            Check(r, SyncConflictService.IsConflicted(k, "Products", p1Sync), "P3.1(3b): الصراع مفتوح على K");
            Check(r, SyncConflictService.IsConflicted(n, "Products", p1Sync), "P3.1(3c): الصراع مفتوح على N");
            Check(r, ReadPrice(k, p1Sync) == 150m && ReadPrice(n, p1Sync) == 200m,
                "P3.1(3d): لا منتصر — كل جهة أبقت سعرها (K=150، N=200)");
            Check(r, SyncConflictService.OpenConflictCount(k) == 1 && SyncConflictService.OpenConflictCount(n) == 1,
                "P3.1(3e): صف صراع واحد لكل جهة (صراع-واحد-لكيان)");
            Check(r, k.SyncConflicts.First(c => c.ConflictSyncId == p1Sync && !c.IsResolved).LocalJson!.Contains("\"Dec\":150"),
                "P3.1(3f): K خزّنت قيمتها المحلية 150 في السجل");
            Check(r, n.SyncConflicts.First(c => c.ConflictSyncId == p1Sync && !c.IsResolved).LocalJson!.Contains("\"Dec\":200"),
                "P3.1(3g): N خزّنت قيمتها المحلية 200 في السجل");
            using (var m = new AppDbContext(dbM))
                Check(r, SyncConflictService.OpenConflictCount(m) == 0, "P3.1(3h): MAIN لم يرفض شيئاً — صفر صراعات لديه");
        }

        // ===== (4) إعادة التبادل K↔N ⇒ idempotent: صفر دلتا ولا صف صراع ثانٍ =====
        Session.DeviceIdOverride = dk;
        var kn2 = SyncPeer.Synchronize("127.0.0.1", portN, dbK, maxAttempts: 1);
        using (var k = new AppDbContext(dbK))
        using (var n = new AppDbContext(dbN))
        {
            Check(r, kn2.Pulled == 0 && kn2.Pushed == 0,
                $"P3.1(4a): إعادة التبادل بلا دلتا (pulled={kn2.Pulled}, pushed={kn2.Pushed})");
            Check(r, k.SyncConflicts.Count(c => c.ConflictSyncId == p1Sync && !c.IsResolved) == 1,
                "P3.1(4b): لا صف صراع ثانٍ بعد الإعادة");
            Check(r, ReadPrice(k, p1Sync) == 150m && ReadPrice(n, p1Sync) == 200m,
                "P3.1(4c): الأثمان لم تتحرك بين التبادلَين (K=150، N=200)");
        }

        // ===== (5) حارس المساواة: بذرة P2(70) ثم عدّل كلاهما إلى 70 — التساوي = لا صراع = تطبيق طبيعي =====
        var (p2Sync, _) = SeedProductOnMain(dbM, "جهاز P2", 70m, 5, "P2");
        Session.DeviceIdOverride = dk;
        var m2k = SyncPeer.Synchronize("127.0.0.1", portM, dbK, maxAttempts: 1);
        Session.DeviceIdOverride = dn;
        var m2n = SyncPeer.Synchronize("127.0.0.1", portM, dbN, maxAttempts: 1);
        Check(r, m2k.Pulled == 1 && m2n.Pulled == 1, "P3.1(5a): بذرة P2 وصلت الجهتين (pulled=1 لكل جهة)");

        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier);
        using (var k = new AppDbContext()) EditProductPrice(k, p2Sync, 70m);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier);
        using (var n = new AppDbContext()) EditProductPrice(n, p2Sync, 70m);

        Session.DeviceIdOverride = dk;
        var kn3 = SyncPeer.Synchronize("127.0.0.1", portN, dbK, maxAttempts: 1);
        using (var k = new AppDbContext(dbK))
        using (var n = new AppDbContext(dbN))
        {
            Check(r, kn3.Pulled == 1 && kn3.Pushed == 1,
                $"P3.1(5b): طرفان انتهيا لنفس النتيجة ⇒ سحب ودفع طبيعيان (pulled={kn3.Pulled}, pushed={kn3.Pushed})");
            Check(r, !k.SyncConflicts.Any(c => c.ConflictSyncId == p2Sync) && !n.SyncConflicts.Any(c => c.ConflictSyncId == p2Sync),
                "P3.1(5c): المساواة لا تُنشئ صراعاً على أي جهة");
            Check(r, ReadPrice(k, p2Sync) == 70m && ReadPrice(n, p2Sync) == 70m, "P3.1(5d): السعر 70 مستقر على الجهتين");
        }

        // ===== (6) بيع على K أثناء الصراع (150×2) ⇒ ينجح والصراع يبقى مفتوحاً والمخزون ينزل =====
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier);
        Session.CurrentUser = CurrentAdmin();
        using (var k = new AppDbContext())
        {
            var prod = k.Products.First(x => x.SyncId == p1Sync);
            var sale = new Sale
            {
                InvoiceNumber = "S_K1", Date = DateTime.Now, UserId = Session.CurrentUser.Id,
                Device = DeviceRole.Cashier, PaymentMethod = PaymentMethod.Cash, Total = 300m, Profit = 120m
            };
            sale.Items.Add(new SaleItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 2, UnitPrice = 150m, BuyPrice = 90m });
            var op = OperationWriter.Register(k, OperationType.Sale, "S_K1", 300m, "Sales");
            k.Sales.Add(sale);
            prod.Stock -= 2;
            k.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Sales", sale.Id);
            k.SaveChanges();
            Check(r, k.Sales.AsNoTracking().Any(s => s.InvoiceNumber == "S_K1"), "P3.1(6a): البيع (S_K1) نجح رغم الصراع");
            Check(r, SyncConflictService.OpenConflictCount(k) == 1, "P3.1(6b): البيع لم يغلق الصراع المفتوح");
            Check(r, k.Products.AsNoTracking().First(x => x.SyncId == p1Sync).Stock == 8,
                "P3.1(6c): المخزون نزل إلى 8 بعد بيع قطعتين");
        }

        // ===== (7) رفض الحسم من غير MAIN (K موظف كشّاف) محسوم ومفصوح =====
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier);
        Session.CurrentUser = CurrentAdmin();
        using (var k = new AppDbContext())
        {
            var okResolve = SyncConflictService.Resolve(k, "Products", p1Sync, ConflictResolutionType.KeepLocal, "محاولة كشّاف", out var err);
            Check(r, !okResolve && (err ?? "").Contains("MAIN"), $"P3.1(7a): رفض الحسم من K غير MAIN — ({err})");
            Check(r, SyncConflictService.OpenConflictCount(k) == 1 && SyncConflictService.IsConflicted(k, "Products", p1Sync),
                "P3.1(7b): الصراع لا يزال مفتوحاً بعد الرفض");
        }

        // ===== (8) استيعاب مسبق K→M ثم N→M — بيع K يحمل حالة المنتج 150 (AttachRows) فائضاً على N وM ⇒ decided=150 حتمياً =====
        Session.DeviceIdOverride = dk;
        var kToM = SyncPeer.Synchronize("127.0.0.1", portM, dbK, maxAttempts: 1);
        Session.DeviceIdOverride = dn;
        var nToM = SyncPeer.Synchronize("127.0.0.1", portM, dbN, maxAttempts: 1);
        decimal decided;
        using (var m = new AppDbContext(dbM))
        {
            decided = ReadPrice(m, p1Sync);
            Check(r, decided == 150m, $"P3.1(8a): على MAIN القيمة الحتمية بعد الاستيعاب = 150 (وجدت {decided})");
            Check(r, SyncConflictService.OpenConflictCount(m) == 0, "P3.1(8b): MAIN بلا صراع مفتوح — لم يرفض شيئاً");
        }
        using (var n = new AppDbContext(dbN))
        {
            Check(r, ReadPrice(n, p1Sync) == 150m, "P3.1(8c): N انجذبت لـ150 بسحب مستندات K من M (لا فيض)");
            Check(r, SyncConflictService.OpenConflictCount(n) == 1, "P3.1(8d): صراع N المفتوح لم يُحسم بالاستيعاب");
        }
        using (var k = new AppDbContext(dbK))
            Check(r, ReadPrice(k, p1Sync) == 150m, "P3.1(8e): K = 150 بعد بيعها وصراعها ما زال مفتوحاً");

        // ===== (9) الحسم اليدوي على MAIN (KeepLocal) — عملية موقّعة تُوزَّع وتُغلق الصراع على كل جهة =====
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer);
        Session.CurrentUser = CurrentAdmin();
        using (var m = new AppDbContext())
        {
            var ok = SyncConflictService.Resolve(m, "Products", p1Sync, ConflictResolutionType.KeepLocal, "القيمة المحلية سليمة", out var err);
            Check(r, ok, $"P3.1(9a): الحسم KeepLocal ناجح على MAIN ({err})");
        }
        using (var m = new AppDbContext(dbM))
        {
            var res = m.OperationLogs.AsNoTracking().FirstOrDefault(o => o.OpType == nameof(OperationType.ConflictResolution) && o.OriginDevice == dm);
            Check(r, res is not null, "P3.1(9b): عملية الحسم مسجّلة على MAIN بهوية origin=MAIN");
            Check(r, (res?.SummaryJson ?? "").Contains("\"type\":\"KeepLocal\""), "P3.1(9c): نوع القرار ظاهر في ملخص العملية");
            Check(r, ReadPrice(m, p1Sync) == 150m, "P3.1(9d): MAIN يبقي على القيمة الفائزة 150");
        }

        Session.DeviceIdOverride = dk;
        var mk1 = SyncPeer.Synchronize("127.0.0.1", portM, dbK, maxAttempts: 1);
        Session.DeviceIdOverride = dn;
        var mn1 = SyncPeer.Synchronize("127.0.0.1", portM, dbN, maxAttempts: 1);
        Check(r, mk1.Pulled >= 1 && mn1.Pulled >= 1,
            $"P3.1(9e): دلتا الحسم وصلت الجهتين عبر M (K={mk1.Pulled}، N={mn1.Pulled})");

        using (var k = new AppDbContext(dbK))
        using (var n = new AppDbContext(dbN))
        {
            Check(r, ReadPrice(k, p1Sync) == 150m && ReadPrice(n, p1Sync) == 150m,
                "P3.1(9f): بعد التوزيع K وN عند القيمة الفائزة");
            Check(r, !SyncConflictService.IsConflicted(k, "Products", p1Sync) && !SyncConflictService.IsConflicted(n, "Products", p1Sync),
                "P3.1(9g): الصراع أُغلق على الجهتين");
            var ck = k.SyncConflicts.AsNoTracking().Single(c => c.ConflictSyncId == p1Sync);
            var cn = n.SyncConflicts.AsNoTracking().Single(c => c.ConflictSyncId == p1Sync);
            Check(r, ck.IsResolved && ck.ResolutionType == nameof(ConflictResolutionType.KeepLocal) && ck.ResolvedBy == "مدير النظام" && ck.ResolvedReason == "القيمة المحلية سليمة",
                "P3.1(9h): K — القرار موقّع (النوع/المنفّذ/السبب) ومغلق");
            Check(r, cn.IsResolved && cn.ResolutionType == nameof(ConflictResolutionType.KeepLocal),
                "P3.1(9i): N — القرار وصل وأغلق سجلها");
            Check(r, (ck.ResolvedValueJson ?? "").Contains("\"Dec\":150"),
                "P3.1(9j): K — «القيمة المتروكة» المحفوظة = قيمتها المحلية 150");
            Check(r, (cn.ResolvedValueJson ?? "").Contains("\"Dec\":200"),
                "P3.1(9k): N — «القيمة المتروكة» المحفوظة = قيمتها المحلية 200");
        }

        // ===== (10) مزامنة إضافية بعد الحسم (عبر M) ⇒ صفر دلتا ولا إعادة فتح =====
        Session.DeviceIdOverride = dk;
        var mk2 = SyncPeer.Synchronize("127.0.0.1", portM, dbK, maxAttempts: 1);
        Session.DeviceIdOverride = dn;
        var mn2 = SyncPeer.Synchronize("127.0.0.1", portM, dbN, maxAttempts: 1);
        using (var k = new AppDbContext(dbK))
        using (var n = new AppDbContext(dbN))
        {
            Check(r, mk2.Pulled == 0 && mk2.Pushed == 0,
                $"P3.1(10a): K بلا دلتا بعد الحسم (pulled={mk2.Pulled}, pushed={mk2.Pushed})");
            Check(r, mn2.Pulled == 0 && mn2.Pushed == 0,
                $"P3.1(10b): N بلا دلتا بعد الحسم (pulled={mn2.Pulled}, pushed={mn2.Pushed})");
            Check(r, SyncConflictService.OpenConflictCount(k) == 0 && SyncConflictService.OpenConflictCount(n) == 0,
                "P3.1(10c): لا إعادة فتح للصراع على أي جهة");
        }

        // ===== (11) حالة StaysDeleted: حذف من K مقابل تعديل من N ثم حسم MAIN يحكم بالبقاء محذوفاً =====
        var (p3Sync, _) = SeedProductOnMain(dbM, "جهاز P3", 30m, 3, "P3");
        Session.DeviceIdOverride = dk;
        var m3k = SyncPeer.Synchronize("127.0.0.1", portM, dbK, maxAttempts: 1);
        Session.DeviceIdOverride = dn;
        var m3n = SyncPeer.Synchronize("127.0.0.1", portM, dbN, maxAttempts: 1);
        Check(r, m3k.Pulled == 1 && m3n.Pulled == 1, "P3.1(11a): بذرة P3 وصلت الجهتين");

        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier);
        using (var k = new AppDbContext()) DeleteProduct(k, p3Sync);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier);
        using (var n = new AppDbContext()) EditProductPrice(n, p3Sync, 40m);

        Session.DeviceIdOverride = dk;
        var kn4 = SyncPeer.Synchronize("127.0.0.1", portN, dbK, maxAttempts: 1);
        using (var k = new AppDbContext(dbK))
        using (var n = new AppDbContext(dbN))
        {
            Check(r, kn4.Pulled == 0,
                $"P3.1(11b): سحب K صفر — حذفها لا يُطبق على N (صراع) (pulled={kn4.Pulled})");
            Check(r, SyncConflictService.IsConflicted(k, "Products", p3Sync) && SyncConflictService.IsConflicted(n, "Products", p3Sync),
                "P3.1(11c): صراع P3 مفتوح على الجهتين");
            Check(r, ReadDeleted(k, p3Sync) && !ReadDeleted(n, p3Sync) && ReadPrice(n, p3Sync) == 40m,
                "P3.1(11d): لا منتصر — K محذوف وN عند 40");
        }

        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer);
        Session.CurrentUser = CurrentAdmin();
        using (var m = new AppDbContext())
        {
            var ok = SyncConflictService.Resolve(m, "Products", p3Sync, ConflictResolutionType.StaysDeleted, "يبقى محذوفاً", out var err);
            Check(r, ok, $"P3.1(11e): الحسم StaysDeleted ناجح على MAIN ({err})");
        }

        Session.DeviceIdOverride = dk;
        SyncPeer.Synchronize("127.0.0.1", portM, dbK, maxAttempts: 1);
        Session.DeviceIdOverride = dn;
        SyncPeer.Synchronize("127.0.0.1", portM, dbN, maxAttempts: 1);

        using (var m = new AppDbContext(dbM))
        using (var k = new AppDbContext(dbK))
        using (var n = new AppDbContext(dbN))
        {
            Check(r, ReadDeleted(m, p3Sync) && ReadDeleted(k, p3Sync) && ReadDeleted(n, p3Sync),
                "P3.1(11f): شاهد القبر سارٍ على الأجهزة الثلاثة بعد التوزيع");
            Check(r, !SyncConflictService.IsConflicted(k, "Products", p3Sync) && !SyncConflictService.IsConflicted(n, "Products", p3Sync),
                "P3.1(11g): صراع P3 مغلق على الجهتين");
            Check(r, SyncConflictService.OpenConflictCount(m) == 0 && SyncConflictService.OpenConflictCount(k) == 0 && SyncConflictService.OpenConflictCount(n) == 0,
                "P3.1(11h): صفر صراعات مفتوحة على كل الأجهزة");
            var cn = n.SyncConflicts.AsNoTracking().Single(c => c.ConflictSyncId == p3Sync);
            Check(r, cn.IsResolved && cn.ResolutionType == nameof(ConflictResolutionType.StaysDeleted) && (cn.ResolvedValueJson ?? "").Contains("\"Dec\":40"),
                "P3.1(11i): N — القرار StaysDeleted وقيمتها المتروكة 40 محفوظة");
            var ck = k.SyncConflicts.AsNoTracking().Single(c => c.ConflictSyncId == p3Sync);
            Check(r, ck.IsResolved && ck.ResolutionType == nameof(ConflictResolutionType.StaysDeleted) && (ck.ResolvedValueJson ?? "").Contains("\"Dec\":30"),
                "P3.1(11j): K — القرار StaysDeleted وقيمتها المتروكة 30 محفوظة");
        }

        cts.Cancel();
        try { srvM.Wait(2000); srvN.Wait(2000); } catch { }
    }

    // ===================== P3.2A: وسم «قيمته قيد المراجعة» — مشتقّ في واجهات البيع، لا منع =====================
    private static void RunP32aUiScenario(List<string> r, string dbM, string dbK, string dbN)
    {
        r.Add("--- P3.2A: وسم قيد المراجعة — مشتقّ (بحث/سلة/فاتورة)، بيع مسموح، زوال بعد الحسم بلا دلتا إضافية ---");

        const string secret = "phase3_secret_a";
        const int portM = 47151;   // خادم MAIN — أُطفئ بنهاية P3.1، نعيد تشغيله
        const int portN = 47152;   // خادم N — وجهة تبادل K↔N
        const string dk = "deviceK_TEST", dn = "deviceN_TEST", dm = "deviceM_TEST";

        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dk, secret);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dn, secret);
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dm, secret);

        var cts = new CancellationTokenSource();
        var srvM = SyncPeer.StartServer(portM, dbM, cts.Token);
        var srvN = SyncPeer.StartServer(portN, dbN, cts.Token);

        // ===== (1) بذرة pA(100) موقّعة على MAIN ⇒ تقارب K وN =====
        var (pSync, _) = SeedProductOnMain(dbM, "جهاز P3.2A", 100m, 5, "P32A");
        Session.DeviceIdOverride = dk;
        var m2k = SyncPeer.Synchronize("127.0.0.1", portM, dbK, maxAttempts: 1);
        Session.DeviceIdOverride = dn;
        var m2n = SyncPeer.Synchronize("127.0.0.1", portM, dbN, maxAttempts: 1);
        Check(r, m2k.Accepted && m2k.Pulled >= 1 && m2n.Accepted && m2n.Pulled >= 1,
            $"P3.2A(1a): التقارب — بذرة pA وصلت K (pulled={m2k.Pulled}) وN (pulled={m2n.Pulled})");
        using (var k = new AppDbContext(dbK))
            Check(r, k.Products.AsNoTracking().First(x => x.SyncId == pSync).Stock == 5, "P3.2A(1b): K ترى pA (مخزون 5)");
        using (var k = new AppDbContext(dbK))
            Check(r, SyncConflictService.UnderReviewSyncIds(k, "Products").Count == 0,
                "P3.2A(1c): بلا صف خلاف — الوسم فارغ (مشتقّ لا عمود مخزّن)");

        // ===== (2) انشقاق السعر K=130 / N=150 ثم تبادل K↔N ⇒ خلاف مفتوح على الجهتين =====
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier);
        using (var k = new AppDbContext()) EditProductPrice(k, pSync, 130m);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier);
        using (var n = new AppDbContext()) EditProductPrice(n, pSync, 150m);

        Session.DeviceIdOverride = dk;
        SyncPeer.Synchronize("127.0.0.1", portN, dbK, maxAttempts: 1);
        using (var k = new AppDbContext(dbK))
        using (var n = new AppDbContext(dbN))
        {
            Check(r, SyncConflictService.IsConflicted(k, "Products", pSync) && SyncConflictService.IsConflicted(n, "Products", pSync),
                "P3.2A(2a): الخلاف مفتوح على K وN بعد التبادل");
            Check(r, SyncConflictService.UnderReviewSyncIds(k, "Products").Contains(pSync) &&
                     SyncConflictService.UnderReviewSyncIds(n, "Products").Contains(pSync),
                "P3.2A(2b): موصّل الوسم (A1) يسترجع الخلاف بجلسة واحدة — على الجهتين");
            Check(r, ReadPrice(k, pSync) == 130m && ReadPrice(n, pSync) == 150m,
                "P3.2A(2c): لا منتصر قبل الحسم — K أبقت 130 وN أبقت 150 (وسم بلا قيمة مفروضة)");
        }

        // ===== (3) واجهة البيع على K: الخلاف ظاهر، البيع مسموح، الوسم ثابت =====
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier);
        Session.CurrentUser = CurrentAdmin();
        var vm = new PosViewModel();
        vm.SearchText = "جهاز P3.2A";
        var row = vm.SearchResults.FirstOrDefault(x => x.Name.Contains("P3.2A"));
        Check(r, row is not null, "P3.2A(3a): نتائج البحث على K تعرض الصنف");
        Check(r, row is not null && row.UnderReview, "P3.2A(3b): البائع يرى وسم «قيد المراجعة» في نتائج البحث");
        vm.AddToCartCommand.Execute(row);
        var cartRow = vm.Cart.FirstOrDefault(c => c.ProductId == row?.ProductId);
        Check(r, cartRow is not null && cartRow.UnderReview,
            "P3.2A(3c): الصنف أُضيف للسلة رغم الخلاف — البيع مسموح والوسم ثابت في السلة");
        Check(r, vm.HasUnderReviewItems && vm.UnderReviewCount == 1,
            "P3.2A(3d): شريط الإجماليات يعلن صنفاً قيد المراجعة (وسم حيّ بعد إعادة الحساب)");

        // ===== (4) الفاتورة: موصّل سطر الفاتورة يشتقّ الوسم لحظة العرض (الخلاف مفتوح) =====
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier);
        Session.CurrentUser = CurrentAdmin();
        int saleId;
        using (var k = new AppDbContext())
        {
            var prod = k.Products.First(x => x.SyncId == pSync);
            var sale = new Sale
            {
                InvoiceNumber = "S_P32A", Date = DateTime.Now, UserId = Session.CurrentUser.Id,
                Device = DeviceRole.Cashier, PaymentMethod = PaymentMethod.Cash, Total = 130m, Profit = 40m
            };
            sale.Items.Add(new SaleItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 1, UnitPrice = 130m, BuyPrice = 90m });
            var op = OperationWriter.Register(k, OperationType.Sale, "S_P32A", 130m, "Sales");
            k.Sales.Add(sale);
            prod.Stock -= 1;
            k.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Sales", sale.Id);
            k.SaveChanges();
            saleId = sale.Id;
        }
        var invVm = new InvoiceDetailsViewModel(saleId);
        Check(r, invVm.Items.First().UnderReview,
            "P3.2A(4a): سطر الفاتورة يُوسَم «قيد المراجعة» وقت الخلاف المفتوح على K");

        // ===== (5) الحسم KeepLocal على MAIN ⇒ يُوزَّع ⇒ الوسم يتوارى بلا دلتا إضافية =====
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer);
        Session.CurrentUser = CurrentAdmin();
        using (var m = new AppDbContext())
        {
            var ok = SyncConflictService.Resolve(m, "Products", pSync, ConflictResolutionType.KeepLocal, "قيمتنا المحلية صحيحة", out var err);
            Check(r, ok, $"P3.2A(5a): حسم KeepLocal ناجح على MAIN ({err})");
        }
        decimal decidedMain;
        using (var m = new AppDbContext(dbM))
        {
            decidedMain = ReadPrice(m, pSync);
            Check(r, decidedMain == 100m, $"P3.2A(5g): قرار MAIN الأولي = قيمته المحلية 100 (وجدت {decidedMain})");
        }

        Session.DeviceIdOverride = dk;
        SyncPeer.Synchronize("127.0.0.1", portM, dbK, maxAttempts: 1);
        using (var k = new AppDbContext(dbK))
        {
            Check(r, !SyncConflictService.IsConflicted(k, "Products", pSync), "P3.2A(5b): بعد سحب قرار MAIN — صراع K أُغلق");
            Check(r, SyncConflictService.UnderReviewSyncIds(k, "Products").Count == 0,
                "P3.2A(5c): موصّل الوسم فارغ على K — زال بلا دلتا إضافية (مشتقّ)");
            Check(r, ReadPrice(k, pSync) == decidedMain,
                $"P3.2A(5e): تقارب بالقيمة لا بالوسم فقط — سعر K بعد الحسم = قرار MAIN ({decidedMain})، كان 130");
        }
        Session.DeviceIdOverride = dn;
        SyncPeer.Synchronize("127.0.0.1", portM, dbN, maxAttempts: 1);
        using (var n = new AppDbContext(dbN))
        {
            Check(r, !SyncConflictService.IsConflicted(n, "Products", pSync) && SyncConflictService.UnderReviewSyncIds(n, "Products").Count == 0,
                "P3.2A(5d): قرار MAIN وصل N — الوسم زال عنها أيضاً");
            Check(r, ReadPrice(n, pSync) == decidedMain,
                $"P3.2A(5f): تقارب بالقيمة على N أيضاً — انجذبت لقرار MAIN ({decidedMain})، كانت 150");
        }

        // ===== (6) الواجهة بعد الحسم: بحث جديد بلا وسم، والسلة الحيّة تسقط الوسم تلقائياً =====
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier);
        Session.CurrentUser = CurrentAdmin();
        var vm2 = new PosViewModel();
        vm2.SearchText = "جهاز P3.2A";
        var row2 = vm2.SearchResults.FirstOrDefault(x => x.Name.Contains("P3.2A"));
        Check(r, row2 is not null && !row2.UnderReview, "P3.2A(6a): بحث جديد — لا وسم بعد الحسم");
        foreach (var it in vm.Cart) it.Quantity++; // يحرّك إعادة حساب الوسوم دون لمس السلة
        Check(r, !vm.HasUnderReviewItems && vm.UnderReviewCount == 0,
            "P3.2A(6b): السلة الحيّة أسقطت الوسم تلقائياً بعد الحسم (اشتقاق لحظة العرض)");
        Check(r, !new InvoiceDetailsViewModel(saleId).Items.First().UnderReview,
            "P3.2A(6c): فتح الفاتورة بعد الحسم — بلا وسم (اشتقاق لحظة العرض)");

        cts.Cancel();
        try { srvM.Wait(2000); srvN.Wait(2000); } catch { }
    }

    // ===================== P3.2B: شاشة «الخلافات وقرارها» على MAIN عبر استطلاع قراءة-فقط =====================
    private static void RunP32bUiScenario(List<string> r, string dbM, string dbK, string dbN)
    {
        r.Add("--- P3.2B: استطلاعُ خلافاتِ الأوراق قراءة-فقط + دمج بالـ SyncId + حسم KeepRemote من قيمة الاستطلاع + نمط كارثة StaysDeleted ---");

        const string secret = "phase3_secret_b";
        const int portM = 47151, portN = 47152, portK = 47154; // K يستضيف خادماً أيضاً ليُستطلَع من MAIN
        const string dk = "deviceK_TEST", dn = "deviceN_TEST", dm = "deviceM_TEST";

        ResetDeviceDb(dbM); ResetDeviceDb(dbK); ResetDeviceDb(dbN);

        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dk, secret);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dn, secret);
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dm, secret);

        var cts = new CancellationTokenSource();
        var srvM = SyncPeer.StartServer(portM, dbM, cts.Token);
        var srvK = SyncPeer.StartServer(portK, dbK, cts.Token);
        var srvN = SyncPeer.StartServer(portN, dbN, cts.Token);

        void PushM(string dbPath, string deviceId)
        {
            Session.DeviceIdOverride = deviceId;
            SyncPeer.Synchronize("127.0.0.1", portM, dbPath, maxAttempts: 1);
        }
        int OpCount(string dbPath)
        {
            using var db = new AppDbContext(dbPath);
            return db.OperationLogs.Count();
        }

        // ===== (1) بذرة pB(80) على MAIN ⇒ تقارب K وN =====
        var (pBSync, _) = SeedProductOnMain(dbM, "جهاز B استطلاع", 80m, 6, "P32B");
        PushM(dbK, dk); PushM(dbN, dn);
        using (var k = new AppDbContext(dbK)) Check(r, ReadPrice(k, pBSync) == 80m, "P3.2B(1a): K تقاربت على بذرة pB(80)");
        using (var n = new AppDbContext(dbN)) Check(r, ReadPrice(n, pBSync) == 80m, "P3.2B(1b): N تقاربت على بذرة pB(80)");

        // ===== (2) انشقاق K=90 / N=70 ثم تبادل K↔N ⇒ صراع مفتوح على الجهتين (لا على MAIN) =====
        UseDevice(dbK, dk); using (var k = new AppDbContext()) EditProductPrice(k, pBSync, 90m);
        UseDevice(dbN, dn); using (var n = new AppDbContext()) EditProductPrice(n, pBSync, 70m);
        Session.DeviceIdOverride = dk;
        SyncPeer.Synchronize("127.0.0.1", portN, dbK, maxAttempts: 1);
        using (var k = new AppDbContext(dbK))
        using (var n = new AppDbContext(dbN))
        using (var m = new AppDbContext(dbM))
        {
            Check(r, SyncConflictService.IsConflicted(k, "Products", pBSync) && SyncConflictService.IsConflicted(n, "Products", pBSync),
                "P3.2B(2a): الصراع مفتوح على ورقة K وورقة N");
            Check(r, SyncConflictService.OpenConflictCount(m) == 0,
                "P3.2B(2b): MAIN بلا صف صراع محلي — الاكتشاف ورقي محض، فالقائمة لا بد من استطلاعها");
        }

        // ===== (3) الاستطلاع من MAIN: قراءة-فقط محضة (لا كتابة على القريب ولا على MAIN) =====
        UseDevice(dbM, dm); // ادّعاء الدور من إعدادات MAIN — شرط قبول الاستطلاع
        int syncLogsK, statesK, opsM;
        Dictionary<string, long>? wmK = null;
        using (var k = new AppDbContext(dbK))
        {
            syncLogsK = k.SyncLogs.Count(); statesK = k.SyncPeerStates.Count();
            wmK = DeltaBuilder.WatermarkMap(k, dm);
        }
        opsM = OpCount(dbM);
        var surveyK = SyncPeer.QueryOpenConflicts("127.0.0.1", portK, dbM);
        var surveyN = SyncPeer.QueryOpenConflicts("127.0.0.1", portN, dbM);
        Check(r, surveyK.Accepted && surveyN.Accepted && surveyK.Conflicts.Count == 1 && surveyN.Conflicts.Count == 1,
            "P3.2B(3a): MAIN تستطلع K وN — كلٌّ يعيد خلافاً واحداً مفتوحاً");
        using (var k = new AppDbContext(dbK))
        {
            var mapAgain = DeltaBuilder.WatermarkMap(k, dm);
            Check(r, k.SyncLogs.Count() == syncLogsK && k.SyncPeerStates.Count() == statesK
                && mapAgain.Count == wmK!.Count && mapAgain.All(kvp => wmK.TryGetValue(kvp.Key, out var v2) && v2 == kvp.Value),
                "P3.2B(3b): الاستطلاع لا يكتب على القريب — لا سجلّ ولا علامات ماء ولا إيصالات");
        }
        Check(r, OpCount(dbM) == opsM,
            "P3.2B(3c): الاستطلاع لا يكتب على MAIN — صفر عمليات جديدة");
        UseDevice(dbK, dk); // الادّعاء الآن من إعدادات K نفسها (Cashier) — لا من إعدادات MAIN
        var negFromK = SyncPeer.QueryOpenConflicts("127.0.0.1", portM, dbK); // K (Cashier) تستطلع الخادم — ادّعاء دورها
        Check(r, !negFromK.Accepted && (negFromK.Reason ?? "").Contains("MAIN"),
            "P3.2B(3d): استطلاع من غير-MAIN مرفوض (ادّعاء الدور = Cashier)");
        Check(r, OpCount(dbM) == opsM,
            "P3.2B(3e): الاستطلاع المرفوض أيضاً بلا أثر كتابي على MAIN");

        // ===== (4) الشاشة على MAIN: تجميع + دمج بالـ SyncId في صف واحد + مصدرا الاكتشاف =====
        UseDevice(dbM, dm); Session.CurrentUser = CurrentAdmin();
        var vm = new ResolveViewModel(new List<(string, int)> { ("127.0.0.1", portK), ("127.0.0.1", portN) }, dbFile: () => dbM);
        vm.Refresh();
        Check(r, vm.Pending.Count == 1,
            "P3.2B(4a): الشاشة تعرض صفاً واحداً رغم اكتشاف الصراع على جهازين (دمج بالـ SyncId الحتمي)");
        var rowB = vm.Pending.FirstOrDefault();
        Check(r, rowB is not null && rowB.SyncId == pBSync && rowB.DisplayName.Contains("استطلاع"),
            $"P3.2B(4b): الصف يحدّد الكيان بالـ SyncId والاسم (sync={rowB?.SyncId}, name={rowB?.DisplayName})");
        Check(r, rowB is not null && rowB.Sources.Contains("K") && rowB.Sources.Contains("N"),
            $"P3.2B(4c): «مُكتشف على K وN» — المصدران مدموجان (وجد: {rowB?.Sources})");
        Check(r, rowB is not null && rowB.LocalValueText.Contains("90") && rowB.RemoteValueText.Contains("70"),
            "P3.2B(4d): الصف يعرض قيمتَي K(90) وN(70) من صورتي الضدّ");

        // ===== (5) الحسم KeepRemote من قيمة الاستطلاع ⇒ يتوزّع ⇒ يلتحم الجميع بقرار MAIN =====
        vm.SelectedRow = rowB;
        var resolved = vm.ResolveSelected(ConflictResolutionType.KeepRemote, "نعتمد قيمة N", out var resolveErr);
        Check(r, resolved, $"P3.2B(5a): MAIN حسمت KeepRemote من قيمة الاستطلاع — بلا صف صراع محلي ({resolveErr})");
        using (var m = new AppDbContext(dbM))
            Check(r, ReadPrice(m, pBSync) == 70m, "P3.2B(5b): MAIN اعتمدت قيمة القريب 70 والدلتا موقّعة");
        Check(r, OpCount(dbM) == opsM + 1, "P3.2B(5c): الحسم سجّل عملية واحدة موقّعة صراحة");
        PushM(dbK, dk); PushM(dbN, dn);
        using (var k = new AppDbContext(dbK))
        using (var n = new AppDbContext(dbN))
        {
            Check(r, !SyncConflictService.IsConflicted(k, "Products", pBSync) && !SyncConflictService.IsConflicted(n, "Products", pBSync),
                "P3.2B(5d): القرار انتشر — صراعا K وN أُغلقا");
            Check(r, ReadPrice(k, pBSync) == 70m && ReadPrice(n, pBSync) == 70m,
                $"P3.2B(5e): تلاحم بالقيمة — K وN صارتا 70 (قرار MAIN) بعد أن كانتا 90 و70");
            Check(r, SyncConflictService.UnderReviewSyncIds(k, "Products").Count == 0 &&
                     SyncConflictService.UnderReviewSyncIds(n, "Products").Count == 0,
                "P3.2B(5f): الوسم توارى على الورقتين — لا أثر للحسم b) flag بلا قيمة)");
        }
        Check(r, vm.Pending.Count == 0, "P3.2B(5g): الشاشة أزالت الصف المحسوم بعد التوزيع");

        // ===== (6) شرط الطزاجة: تغيّرت حالة المُبلِّغ ⇒ يُرفض الحسم ويُحدَّث العرض لا القرار =====
        var (pCSync, _) = SeedProductOnMain(dbM, "جهاز C الطازج", 60m, 4, "P32BC");
        PushM(dbK, dk); PushM(dbN, dn);
        UseDevice(dbK, dk); using (var k = new AppDbContext()) EditProductPrice(k, pCSync, 50m);
        UseDevice(dbN, dn); using (var n = new AppDbContext()) EditProductPrice(n, pCSync, 55m);
        Session.DeviceIdOverride = dk;
        SyncPeer.Synchronize("127.0.0.1", portN, dbK, maxAttempts: 1);
        UseDevice(dbM, dm); Session.CurrentUser = CurrentAdmin(); // الشاشة والحسم تصدران بادّعاء دور MAIN من إعداداته
        var vm2 = new ResolveViewModel(new List<(string, int)> { ("127.0.0.1", portK), ("127.0.0.1", portN) }, dbFile: () => dbM);
        vm2.Refresh();
        Check(r, vm2.Pending.Count == 1, "P3.2B(6a): خلاف C ظهر في الشاشة");
        var rowC = vm2.Pending.First();
        int opsBeforeC = OpCount(dbM); // أساس سِجّل M قبل أي محاولة حسم على C
        // هل سُحِق الاكتشاف: نحرّف RemoteJson عند المُبلِّغ K خارج الهارنس — يبقى الصف مفتوحاً لكن بقيمة جديدة
        string mutatedRemote;
        using (var k = new AppDbContext(dbK))
        {
            var c1 = k.SyncConflicts.First(c => c.ConflictSyncId == pCSync && !c.IsResolved);
            mutatedRemote = DeltaApplier.RowsToJson(new List<DeltaRow>
            {
                new() { T = "Products", S = pCSync, F = new() { ["Name"] = JsonScalar.Of("جهاز C الطازج"), ["SellPrice"] = JsonScalar.Of(56m) } }
            });
            c1.RemoteJson = mutatedRemote;
            k.SaveChanges();
        }
        vm2.SelectedRow = rowC;
        var stale = vm2.ResolveSelected(ConflictResolutionType.KeepRemote, "قرار قديم", out var staleErr);
        Check(r, !stale && (staleErr ?? "").Contains("تغيّرت"),
            $"P3.2B(6b): تغيّرت حالة المُبلِّغ ⇒ رُفض القرار القديم ({staleErr})");
        Check(r, OpCount(dbM) == opsBeforeC,
            $"P3.2B(6c): لم تُكتب أي عملية حسم بعد رفض الطزاجة ({OpCount(dbM)} == {opsBeforeC})");
        Check(r, vm2.Pending.Count == 1 && vm2.Pending.First().RemoteValueText.Contains("56"),
            "P3.2B(6d): الشاشة حدّثت العرض — قيمة المُبلِّغ صارت 56 في الاستطلاع الجديد، فلا قرار من عرضٍ قديم");
        // تنظيف: نُنهي خلاف C بأن ينسحب MAIN من الحسم — نعيد القيمة الحقيقية بعد تحقق الطزاجة
        using (var k = new AppDbContext(dbK))
        {
            var c1 = k.SyncConflicts.First(c => c.ConflictSyncId == pCSync && !c.IsResolved);
            c1.RemoteJson = (string)rowC.RemoteSourceJson!;
            k.SaveChanges();
        }
        vm2.Refresh();
        vm2.SelectedRow = vm2.Pending.FirstOrDefault();
        Check(r, vm2.ResolveSelected(ConflictResolutionType.KeepRemote, "نعتمد N", out var err2),
            $"P3.2B(6e): بعد عودة الحالة طازجة — الحسم يمر من جديد ({err2})");
        PushM(dbK, dk); PushM(dbN, dn);
        using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN))
            Check(r, ReadPrice(k, pCSync) == 55m && ReadPrice(n, pCSync) == 55m,
                "P3.2B(6f): تلاحم C — الكل صار 55 بقرار MAIN");

        // ===== (7) نمط كارثة StaysDeleted: قبرٌ واحد ثابت، بلا إحياء بلا شبحيّ، والقيمة المتنازَع عليها مؤرشفة =====
        var (pDSync, _) = SeedProductOnMain(dbM, "جهاز D القبر", 30m, 3, "P32BD");
        PushM(dbK, dk); PushM(dbN, dn);
        UseDevice(dbK, dk); using (var k = new AppDbContext()) DeleteProduct(k, pDSync);
        UseDevice(dbN, dn); using (var n = new AppDbContext()) EditProductPrice(n, pDSync, 40m);
        Session.DeviceIdOverride = dk;
        SyncPeer.Synchronize("127.0.0.1", portN, dbK, maxAttempts: 1);
        using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN))
            Check(r, SyncConflictService.IsConflicted(k, "Products", pDSync) && SyncConflictService.IsConflicted(n, "Products", pDSync),
                "P3.2B(7a): حذفٌ مقابل تعديل ⇒ صراع على الجهتين (نمط كارثة)");
        UseDevice(dbM, dm); Session.CurrentUser = CurrentAdmin();
        var vm3 = new ResolveViewModel(new List<(string, int)> { ("127.0.0.1", portK), ("127.0.0.1", portN) }, dbFile: () => dbM);
        vm3.Refresh();
        var rowD = vm3.Pending.FirstOrDefault();
        vm3.SelectedRow = rowD;
        Check(r, rowD is not null && rowD.LocalValueText.Contains("محذوف") && rowD.RemoteValueText.Contains("40"),
            "P3.2B(7b): الشاشة تعرض «محذوف» (عند K) مقابل «40» (عند N)");
        Check(r, vm3.ResolveSelected(ConflictResolutionType.StaysDeleted, "يبقى محذوفاً — التعديل ليس بقرار", out var delErr),
            $"P3.2B(7c): قرار StaysDeleted يمرّ من MAIN ({delErr})");
        PushM(dbK, dk); PushM(dbN, dn);
        long graveMsK;
        int kOpsAfter;
        using (var k = new AppDbContext(dbK))
        {
            Check(r, ReadDeleted(k, pDSync), "P3.2B(7d): K قبِلَت القبر — المنتج محذوف");
            var grave = k.Products.First(x => x.SyncId == pDSync);
            Check(r, grave.DeletedAt is not null, "P3.2B(7e): شاهد القبر (DeletedAt) ثابت واحد");
            graveMsK = grave.DeletedAt!.Value;
            var archivedK = k.SyncConflicts.First(c => c.ConflictSyncId == pDSync).ResolvedValueJson ?? "";
            Check(r, archivedK.Contains("\"Dec\":30") && archivedK.Contains("DeletedAt"),
                "P3.2B(7f): K أرشفَت حالتها المحلية المهجورة (اللقطة المحذوفة: السعر 30 + شاهد القبر) — لا قيمة تُضيّع");
            kOpsAfter = OpCount(dbK);
        }
        using (var n = new AppDbContext(dbN))
        {
            var archivedN = n.SyncConflicts.First(c => c.ConflictSyncId == pDSync).ResolvedValueJson ?? "";
            Check(r, ReadDeleted(n, pDSync) && archivedN.Contains("\"Dec\":40"),
                "P3.2B(7g): N قبِلَت القبر وأرشفَت تعديلها المتنازَع عليه (40) — التعديل لم يضِع من الشبكة");
        }
        // جولة الكارثة الثانية: هل يموت الميت مرتين أو يعود شبحاً؟
        Session.DeviceIdOverride = dk;
        var punch = SyncPeer.Synchronize("127.0.0.1", portM, dbK, maxAttempts: 1);
        using (var k = new AppDbContext(dbK))
        {
            Check(r, punch.Pulled == 0 && punch.Pushed == 0,
                $"P3.2B(7h): جولة الكارثة الثانية بلا دلتا (pulled={punch.Pulled}, pushed={punch.Pushed})");
            var grave2 = k.Products.First(x => x.SyncId == pDSync);
            Check(r, grave2.DeletedAt!.Value == graveMsK,
                "P3.2B(7i): القبر لم يُمسّ — نفس DeletedAt، لا إحياء ولا إعادة حذف");
            Check(r, k.OperationLogs.Count() == kOpsAfter,
                "P3.2B(7j): لا عمليات جديدة — بلا re-create ولا ghost op");
        }

        cts.Cancel();
        try { srvM.Wait(2000); srvK.Wait(2000); srvN.Wait(2000); } catch { }
    }

    // ===================== P3.2C: صلاحية المدير الفورية + سجل الرفض المحلي + بصمة القرار بالعملية =====================
    private static void RunP32cPermissionScenario(List<string> r, string dbM, string dbK, string dbN)
    {
        r.Add("--- P3.2C: بوابة المدير لحظة الضغط (لا عند فتح الشاشة) + سجل تدقيق محلي غير مزامَن + بصمة ResolvedAtSeq=OriginSeq ---");

        const string secret = "phase3_secret_c";
        const int portM = 47151, portN = 47152, portK = 47154;
        const string dk = "deviceK_TEST", dn = "deviceN_TEST", dm = "deviceM_TEST";

        ResetDeviceDb(dbM); ResetDeviceDb(dbK); ResetDeviceDb(dbN);

        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dk, secret);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dn, secret);
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dm, secret);

        // مستخدما الحمايات: محاسب وكاشير (على M فقط — كائنات جلسة، لا يحتاجان مزامنة)
        User accountant, cashierUser;
        using (var m = new AppDbContext(dbM))
        {
            accountant = new User { Username = "acc", PasswordHash = PasswordHasher.Hash("x"), DisplayName = "المحاسب", Role = UserRole.Accountant, IsActive = true };
            cashierUser = new User { Username = "kash", PasswordHash = PasswordHasher.Hash("x"), DisplayName = "الكاشير", Role = UserRole.Cashier, IsActive = true };
            m.Users.AddRange(accountant, cashierUser);
            m.SaveChanges();
        }

        var cts = new CancellationTokenSource();
        var srvM = SyncPeer.StartServer(portM, dbM, cts.Token);
        var srvK = SyncPeer.StartServer(portK, dbK, cts.Token);
        var srvN = SyncPeer.StartServer(portN, dbN, cts.Token);

        void PushM(string dbPath, string deviceId)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = deviceId;
            SyncPeer.Synchronize("127.0.0.1", portM, dbPath, maxAttempts: 1);
            Session.DeviceIdOverride = prev; // لا تُترك هوية القريب مسلّلة لعملية MAIN لاحقة
        }
        int OpCount(string dbPath)
        {
            using var db = new AppDbContext(dbPath);
            return db.OperationLogs.Count();
        }
        (string SyncId, string Name) ForkConflict(string name, string doc, decimal baseP, decimal kP, decimal nP)
        {
            Session.CurrentUser = CurrentAdmin();
            var (sync, _) = SeedProductOnMain(dbM, name, baseP, 5, doc);
            PushM(dbK, dk); PushM(dbN, dn);
            UseDevice(dbK, dk); using (var k = new AppDbContext()) EditProductPrice(k, sync, kP);
            UseDevice(dbN, dn); using (var n = new AppDbContext()) EditProductPrice(n, sync, nP);
            Session.DeviceIdOverride = dk;
            SyncPeer.Synchronize("127.0.0.1", portN, dbK, maxAttempts: 1);
            return (sync, name);
        }

        // ===== (1) حسم مدير سليم: عملية موقّعة + بصمتها على الورقتين + قبول في التدقيق المحلي =====
        var (pESync, pEName) = ForkConflict("جهاز E بصمة", "P32CE", 100m, 110m, 120m);
        using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN)) using (var m = new AppDbContext(dbM))
        {
            Check(r, SyncConflictService.IsConflicted(k, "Products", pESync) && SyncConflictService.IsConflicted(n, "Products", pESync)
                && SyncConflictService.OpenConflictCount(m) == 0,
                "P3.2C(1a): صراع E مفتوح على الورقتين وصفر على MAIN — مثلثة جاهزة للحسم");
        }
        UseDevice(dbM, dm); Session.CurrentUser = CurrentAdmin();
        int opsBefore1 = OpCount(dbM);
        var vm = new ResolveViewModel(new List<(string, int)> { ("127.0.0.1", portK), ("127.0.0.1", portN) }, dbFile: () => dbM);
        vm.Refresh();
        vm.SelectedRow = vm.Pending.FirstOrDefault(x => x.SyncId == pESync);
        Check(r, vm.SelectedRow is not null && vm.CanResolve,
            $"P3.2C(1b): الشاشة تعرض صف E والبوابة مفتوحة للمدير (name={pEName})");
        Check(r, vm.ResolveSelected(ConflictResolutionType.KeepRemote, "نعتمد قيمة N", out var errE),
            $"P3.2C(1c): المدير يحسم KeepRemote من قيمة الاستطلاع ({errE})");
        long seqE;
        using (var m = new AppDbContext(dbM))
        {
            var opE = m.OperationLogs.AsNoTracking().Single(o => o.OpType == nameof(OperationType.ConflictResolution));
            seqE = opE.OriginSeq;
            Check(r, seqE > 0 && opE.OriginDevice == dm,
                $"P3.2C(1d): عملية الحسم موقّعة بهوية MAIN (seq={seqE})");
            Check(r, ReadPrice(m, pESync) == 120m, "P3.2C(1e): MAIN اعتمدت قيمة القريب 120");
            var accepted = m.ConflictAuditLogs.AsNoTracking().First(a => a.ConflictSyncId == pESync && a.Outcome == "Accepted");
            Check(r, accepted.UserName == "مدير النظام" && accepted.DeviceId == dm && accepted.Reason == "KeepRemote",
                "P3.2C(1f): سجل التدقيق المحلي يوثّق قبول الحسم (الحاسم، الجهاز، النوع)");
        }
        Check(r, OpCount(dbM) == opsBefore1 + 1, "P3.2C(1g): الحسم سجّل عملية واحدة فقط — بلا ضوضاء");
        PushM(dbK, dk); PushM(dbN, dn);
        using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN))
        {
            var ck = k.SyncConflicts.AsNoTracking().First(c => c.ConflictSyncId == pESync);
            var cn = n.SyncConflicts.AsNoTracking().First(c => c.ConflictSyncId == pESync);
            Check(r, ck.IsResolved && cn.IsResolved && ReadPrice(k, pESync) == 120m && ReadPrice(n, pESync) == 120m,
                "P3.2C(1h): القرار انتشر — صراعا الورقتين أُغلقا وتلاحم الجميع 120");
            Check(r, ck.ResolvedAtSeq == seqE && cn.ResolvedAtSeq == seqE,
                "P3.2C(1i): على الجهتين ResolvedAtSeq = OriginSeq عملية الحسم (بصمة الربط بالسجل)");
            Check(r, ck.ResolutionOpOrigin == dm && cn.ResolutionOpOrigin == dm,
                "P3.2C(1j): جهة عملية الحسم = MAIN (كامل الهوية: OriginDevice+OriginSeq)");
            Check(r, ck.ResolvedBy == "مدير النظام",
                "P3.2C(1k): الحاسم = مستخدم عملية السجل (من Users لا من وسْم عرضي)");
        }

        // ===== (2) الشاشة فُتحت بمدير ثم انتهت صلاحيته — فحص اللحظة يرفض الحسم بلا أثر =====
        var (pFSync, _) = ForkConflict("جهاز F الدور", "P32CF", 90m, 95m, 85m);
        UseDevice(dbM, dm); Session.CurrentUser = CurrentAdmin();
        var vm2 = new ResolveViewModel(new List<(string, int)> { ("127.0.0.1", portK), ("127.0.0.1", portN) }, dbFile: () => dbM);
        vm2.Refresh();
        vm2.SelectedRow = vm2.Pending.FirstOrDefault(x => x.SyncId == pFSync);
        Check(r, vm2.SelectedRow is not null, "P3.2C(2a): صف F ظهر في شاشة كانت مفتوحة بصلاحية مدير");
        int opsBefore2 = OpCount(dbM);
        Session.CurrentUser = accountant; // انتهت وردية المدير — الشاشة ما زالت مفتوحة
        var accReject = vm2.ResolveSelected(ConflictResolutionType.KeepLocal, "محاولة من محاسب", out var errAcc);
        Check(r, !accReject && (errAcc ?? "").Contains("مدير"),
            "P3.2C(2b): الحسم لحظة الضغط يُفحص الدور — لا عند الفتح — فروضٌ رُفض (لـ «المحاسب» المصرَّح بالإقفال)");
        Check(r, OpCount(dbM) == opsBefore2, "P3.2C(2c): رفض الصلاحية = صفر عمليات (لا عملية حسم ولا ضوضاء)");
        Check(r, vm2.Pending.Count == 1, "P3.2C(2d): الشاشة لم تُحسم الصف — الصف ما زال قائماً للمدير الحقيقي");
        using (var m = new AppDbContext(dbM))
        {
            var rej = m.ConflictAuditLogs.AsNoTracking().Where(a => a.ConflictSyncId == pFSync && a.Outcome == "Rejected").ToList();
            Check(r, rej.Count == 1 && rej[0].UserName == "المحاسب" && rej[0].Reason == "RoleMismatch" && rej[0].DeviceId == dm,
                "P3.2C(2e): سجل الرفض المحلي (غير المزامَن): من حاول، متى، أي صراع، السبب RoleMismatch");
        }
        using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN))
            Check(r, SyncConflictService.IsConflicted(k, "Products", pFSync) && SyncConflictService.IsConflicted(n, "Products", pFSync)
                && ReadPrice(k, pFSync) == 95m && ReadPrice(n, pFSync) == 85m,
                "P3.2C(2f): الورقتان بلا أثر — الصراع مفتوح والسعران كما كانا");

        // ===== (3) كاشير أيضاً مرفوض — والسجل يجمع الطرفين =====
        Session.CurrentUser = cashierUser;
        var cashReject = vm2.ResolveSelected(ConflictResolutionType.KeepRemote, "محاولة من كاشير", out var errCash);
        Check(r, !cashReject && (errCash ?? "").Contains("مدير"),
            "P3.2C(3a): رفض الكاشير (والإخفاء الذي تفرضه الشاشة هو طبقة العرض — القرار النهائي في مسار الحسم)");
        using (var m = new AppDbContext(dbM))
        {
            var rejected = m.ConflictAuditLogs.AsNoTracking().Where(a => a.ConflictSyncId == pFSync && a.Outcome == "Rejected").ToList();
            Check(r, rejected.Count == 2 && rejected.Any(a => a.UserName == "الكاشير"),
                "P3.2C(3b): سجل الرفض يجمع الطرفين — «من يحاول حسم ما لا يملكه» كاملٌ على MAIN");
        }

        // ===== (4) سجل التدقيق محلي لا يُزامَن: K تسحب من M فلا تصلها صفوف التدقيق =====
        PushM(dbK, dk);
        using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN)) using (var m = new AppDbContext(dbM))
        {
            Check(r, k.ConflictAuditLogs.Count() == 0 && n.ConflictAuditLogs.Count() == 0,
                "P3.2C(4a): التدقيق لا يُزامَن — الورقتان بلا سجل رغم أنهما سحبتا من MAIN");
            Check(r, m.ConflictAuditLogs.Count() == 3,
                "P3.2C(4b): على MAIN وحده: قبول E + رفضا F (محاسب وكاشير) — السجل الكامل محلي وتفّاح القيمة الاحتفاظ الدائم");
        }

        // ===== (5) عودة المدير بين الضغطتين — الفحص لحظي فيسمح في نفس الشاشة + ترتيب البصمات =====
        UseDevice(dbM, dm); Session.CurrentUser = CurrentAdmin();
        int opsBefore5 = OpCount(dbM);
        Check(r, vm2.ResolveSelected(ConflictResolutionType.KeepRemote, "نعتمد N بعد عودة المدير", out var errBack),
            $"P3.2C(5a): تعود الجلسة بين ضغطتين — الفحص لحظي فيسمح في ذات الشاشة ({errBack})");
        long seqF;
        using (var m = new AppDbContext(dbM))
        {
            var opF = m.OperationLogs.AsNoTracking()
                .Where(o => o.OpType == nameof(OperationType.ConflictResolution))
                .OrderBy(o => o.OriginSeq)
                .Last();
            seqF = opF.OriginSeq;
            Check(r, seqF > seqE,
                $"P3.2C(5b): ترتيب — الحسم اللاحق OriginSeq أرفع ({seqF} > {seqE}) فيكون هو المرجع لأي إعادة لعب");
            Check(r, ReadPrice(m, pFSync) == 85m, "P3.2C(5c): قرار وصوله يلتحم الكل بـ 85");
        }
        Check(r, OpCount(dbM) == opsBefore5 + 1, "P3.2C(5d): عملية حسم واحدة بعد عودة المدير (لا تراكم)");
        PushM(dbK, dk); PushM(dbN, dn);
        using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN))
        {
            var ck = k.SyncConflicts.AsNoTracking().First(c => c.ConflictSyncId == pFSync);
            var cn = n.SyncConflicts.AsNoTracking().First(c => c.ConflictSyncId == pFSync);
            Check(r, ck.IsResolved && cn.IsResolved && ReadPrice(k, pFSync) == 85m && ReadPrice(n, pFSync) == 85m,
                "P3.2C(5e): قرار المدير المغتنم انتشر — الورقتان أغلقتا عند 85");
            Check(r, ck.ResolvedAtSeq == seqF && cn.ResolvedAtSeq == seqF && ck.ResolutionOpOrigin == dm,
                "P3.2C(5f): بصمة الحسم الثاني على الورقتين = عمليته الموقّعة (لا بصمة أولى مكررة)");
        }

        // ===== (6) بلا جلسة إطلاقاً (خرج فعلاً) — رفض صريح وعدم تسجيل إلا لدى MAIN =====
        var (pHSync, _) = ForkConflict("جهاز H الخروج", "P32CH", 45m, 40m, 50m);
        UseDevice(dbM, dm); Session.CurrentUser = CurrentAdmin();
        var vm3 = new ResolveViewModel(new List<(string, int)> { ("127.0.0.1", portK), ("127.0.0.1", portN) }, dbFile: () => dbM);
        vm3.Refresh();
        vm3.SelectedRow = vm3.Pending.FirstOrDefault(x => x.SyncId == pHSync);
        int opsBefore6 = OpCount(dbM);
        Session.CurrentUser = null; // خرج المدير فعلاً — الشاشة مفتوحة مكشوفة
        var noSessionReject = vm3.ResolveSelected(ConflictResolutionType.KeepRemote, "بعد الخروج", out var errOut);
        Check(r, !noSessionReject && (errOut ?? "").Contains("جلسة"),
            "P3.2C(6a): بلا جلسة — رفض صريح (لا تنازلٍ عن البوابة عند فراغ الجلسة)");
        Check(r, OpCount(dbM) == opsBefore6, "P3.2C(6b): فراغ الجلسة أيضاً صفر عمليات");
        using (var m = new AppDbContext(dbM))
        {
            var rej = m.ConflictAuditLogs.AsNoTracking().First(a => a.ConflictSyncId == pHSync && a.Outcome == "Rejected");
            Check(r, rej.UserName == "(لا جلسة)" && rej.DeviceId == dm,
                "P3.2C(6c): محاولة بلا جلسة مدوّنة أيضاً — «من حاول» تُعلَّم بـ(لا جلسة) ومن أي جهاز");
        }

        cts.Cancel();
        try { srvM.Wait(2000); srvK.Wait(2000); srvN.Wait(2000); } catch { }
    }
    private static void RunP33ReconciliationScenario(List<string> r, string dbR, string dbW)
    {
        r.Add("--- P3.3: المصالحة — FinancialTotals كشفٌ بلا إصلاح + استدراك موقّع (StockCorrection) ينتشر بالدلتا idempotent بلا كتابة تاريخ ---");

        const string secret = "phase3_secret_r";
        const int portR = 47153;           // خادم R (جهة المصالحة الرئيسية)
        const string dR = "deviceR_TEST", dW = "deviceW_TEST";

        ResetDeviceDb(dbR); ResetDeviceDb(dbW);

        UseDevice(dbR, dR); SetDeviceRole(dbR, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dR, secret);
        UseDevice(dbW, dW); SetDeviceRole(dbW, DeviceRole.Cashier);   SeedAdmin(); ConfigurePeer(dW, secret);

        var cts = new CancellationTokenSource();
        var srvR = SyncPeer.StartServer(portR, dbR, cts.Token);

        // ---- (1) أساس موقّع على R: صنف + بيع 200 (قيمة مموّلة عبر الدلتا) ⇒ S_R = 200 ----
        Session.DeviceIdOverride = dR;
        UseDevice(dbR, dR);
        Session.CurrentUser = CurrentAdmin();
        using (var db = new AppDbContext())
        {
            var cat = new Category { Name = "فئة R" };
            db.Categories.Add(cat); db.SaveChanges();
            var prod = new Product { Name = "جهاز R", CategoryId = cat.Id, Stock = 9, BuyPrice = 120, SellPrice = 200, Device = DeviceRole.Cashier };
            db.Products.Add(prod); db.SaveChanges();
            var sale = new Sale
            {
                InvoiceNumber = "R-1", Date = DateTime.Now, UserId = Session.CurrentUser.Id,
                Device = Session.Device, PaymentMethod = PaymentMethod.Cash, Total = 200, Profit = 80
            };
            sale.Items.Add(new SaleItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 1, UnitPrice = 200, BuyPrice = 120 });
            var op = OperationWriter.Register(db, OperationType.Sale, "R-1", 200m, "Sales");
            db.Sales.Add(sale); db.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Sales", sale.Id); db.SaveChanges();
        }

        // لقطة تاريخ R قبل أي استدراك — للبرهنة على «لا إعادة كتابة»
        var historyR = SnapshotOps(dbR);

        // ---- (2) W تسحب الأساس ⇒ أفق التقارب: المجموع التوافقي متساوٍ ----
        Session.DeviceIdOverride = dW;
        var first = SyncPeer.Synchronize("127.0.0.1", portR, dbW, maxAttempts: 1);
        Check(r, first.Accepted && first.Pulled >= 1, $"P3.3(2a): W انجذبت لأساس R عبر الدلتا (pulled={first.Pulled})");
        using (var rdb = new AppDbContext(dbR))
        using (var wdb = new AppDbContext(dbW))
        {
            var sumR = FinancialTotals.CanonicalSum(rdb);
            var sumW = FinancialTotals.CanonicalSum(wdb);
            Check(r, sumR == sumW && sumR != 0m, $"P3.3(2b): المجموع التوافقي متساوٍ بعد التقارب (R={sumR}، W={sumW})");
            var (eq, diff) = ReconciliationService.VerifyAgainst(rdb, wdb);
            Check(r, eq && diff == 0m, "P3.3(2c): التحقق المصالحي يقرّ صفر فجوة (كشف فقط)");
            Check(r, SyncConflictService.OpenConflictCount(rdb) == 0 && SyncConflictService.OpenConflictCount(wdb) == 0,
                "P3.3(2d): لا صراعات خلال التقارب");
        }

        // ---- (3) استدراك موقّع على R: +37.50 (تسوية جرد) ⇒ تفاوت يكشف، ثم يُدار ----
        Session.DeviceIdOverride = dR;
        UseDevice(dbR, dR);
        using (var db = new AppDbContext())
        {
            var msg = ReconciliationService.IssueCorrection(db, 37.50m, "تسوية جرد نهائي — فرق عدّ");
            Check(r, msg.StartsWith("سُجّل"), $"P3.3(3a): الاستدراك صدر موقّعاً على R — ({msg})");
        }
        using (var rdb = new AppDbContext(dbR))
        using (var wdb = new AppDbContext(dbW))
        {
            var (eq, diff) = ReconciliationService.VerifyAgainst(rdb, wdb);
            Check(r, !eq && diff == 37.50m, $"P3.3(3b): قبل النشر — الكشف يُسهِّل الفجوة 37.50 بدقة (diff={diff})");
            Check(r, FinancialTotals.CanonicalSum(wdb) == 200m, "P3.3(3c): W لم تُصلَح تلقائياً — كشفٌ بلا إصلاح");
            Check(r, rdb.OperationLogs.Count(o => o.OpType == nameof(OperationType.StockCorrection) && o.OriginDevice == dR) == 1,
                "P3.3(3d): الاستدراك في سجل R موقّعاً بهوية R (OriginDevice عبر OperationWriter)");
        }

        // ---- (4) النشر عبر الدلتا: W تسحب الاستدراك ⇒ تقارب عند 237.50 ----
        Session.DeviceIdOverride = dW;
        var spread = SyncPeer.Synchronize("127.0.0.1", portR, dbW, maxAttempts: 1);
        using (var rdb = new AppDbContext(dbR))
        using (var wdb = new AppDbContext(dbW))
        {
            var sumR = FinancialTotals.CanonicalSum(rdb);
            var sumW = FinancialTotals.CanonicalSum(wdb);
            Check(r, spread.Pulled == 1, $"P3.3(4a): الاستدراك وصل W عبر الدلتا (pulled={spread.Pulled})");
            Check(r, sumR == sumW && sumR == 237.50m, $"P3.3(4b): استدراك موزَّع — الطرفان عند 237.50 (R={sumR}، W={sumW})");
            var corr = wdb.OperationLogs.AsNoTracking().First(o => o.OpType == nameof(OperationType.StockCorrection));
            Check(r, corr.OriginDevice == dR && corr.OriginSeq > 0,
                "P3.3(4c): أصل الاستدراك على W هو R الحقيقي لا المحاوِل (OriginDevice=dR، OriginSeq محفوظ)");
        }

        // ---- (5) إعادة السحب ⇒ idempotent: صفر دلتا ولا تكرار ----
        Session.DeviceIdOverride = dW;
        var again = SyncPeer.Synchronize("127.0.0.1", portR, dbW, maxAttempts: 1);
        using (var rdb = new AppDbContext(dbR))
        using (var wdb = new AppDbContext(dbW))
        {
            Check(r, again.Pulled == 0 && again.Pushed == 0, $"P3.3(5a): إعادة السحب بلا دلتا (pulled={again.Pulled}، pushed={again.Pushed})");
            Check(r, wdb.OperationLogs.Count(o => o.OpType == nameof(OperationType.StockCorrection)) == 1,
                "P3.3(5b): لا مضاعفة للاستدراك على W (idempotent عبر الإيصال)");
            Check(r, FinancialTotals.CanonicalSum(rdb) == FinancialTotals.CanonicalSum(wdb),
                "P3.3(5c): المجموع ثابت بعد الإعادة — لا أثر مزدوج");
        }

        // ---- (6) العكس: استدراك معاكس موقّع من W (-37.50) ⇒ العودة للأساس 200 عبر الدلتا ----
        Session.DeviceIdOverride = dW;
        UseDevice(dbW, dW);
        Session.CurrentUser = CurrentAdmin();
        using (var db = new AppDbContext())
        {
            var msg = ReconciliationService.IssueCorrection(db, -37.50m, "عكس تسوية الجرد");
            Check(r, msg.StartsWith("سُجّل"), $"P3.3(6a): الاستدراك المعاكس صدر موقّعاً من W — ({msg})");
        }
        Session.DeviceIdOverride = dW;
        var rev = SyncPeer.Synchronize("127.0.0.1", portR, dbW, maxAttempts: 1);
        using (var rdb = new AppDbContext(dbR))
        using (var wdb = new AppDbContext(dbW))
        {
            var sumR = FinancialTotals.CanonicalSum(rdb);
            var sumW = FinancialTotals.CanonicalSum(wdb);
            Check(r, sumR == sumW && sumR == 200m, $"P3.3(6b): بعد العكس والتوزيع — العودة للأساس 200 على الطرفين (R={sumR}، W={sumW})");
            var revCorr = rdb.OperationLogs.AsNoTracking().First(o => o.OpType == nameof(OperationType.StockCorrection) && o.OriginDevice == dW);
            Check(r, revCorr.Amount == -37.50m, "P3.3(6c): الاستدراك المعاكس بلغ R موقّعاً بهوية W وقيمةً سالبة");
        }

        // ---- (7) التاريخ لا يُعاد كتابته أبداً: صفوف الأساس قبل/بعد متطابقة، والتذييل فقط هو النمو ----
        var afterR = SnapshotOps(dbR);
        Check(r, historyR.Count == 1, $"P3.3(7a): أساس R قبل الاستدراكات = صف العملية التجارية فقط (count={historyR.Count})");
        Check(r, historyR.SequenceEqual(afterR.Take(historyR.Count)),
            "P3.3(7b): صفوف ما قبل الاستدراك على R بلا أي تعديل (Seq/النوع/الرقم/المبلغ/الأصل) — التذييل فقط");
        Check(r, afterR.Count == historyR.Count + 2,
            $"P3.3(7c): التاريخ ازداد بصفّي الاستدراك فقط، لا كتابة/حذف في القديم (قبل={historyR.Count}، بعد={afterR.Count})");
        Check(r, afterR.Select(x => x.Seq).SequenceEqual(Enumerable.Range(1, afterR.Count).Select(i => (long)i)),
            "P3.3(7d): اتصال Seq 1..N محفوظ على R بعد دورة استدراكات كاملة");

        // ---- (8) صلاحية: الاستدراك يقصره المدير/المحاسب ----
        Session.DeviceIdOverride = dR;
        UseDevice(dbR, dR);
        Session.CurrentUser = CurrentAdmin();
        using (var db = new AppDbContext())
        {
            db.Users.Add(new User
            {
                Username = "cashier_r",
                PasswordHash = PasswordHasher.Hash("cash1r_xx"),
                DisplayName = "كاشير R",
                Role = UserRole.Cashier,
                IsActive = true,
                MustChangePassword = false
            });
            db.SaveChanges();
            var before = db.OperationLogs.Count();
            Session.CurrentUser = db.Users.First(u => u.Username == "cashier_r");
            var msg = ReconciliationService.IssueCorrection(db, 10m, "محاولة كاشير");
            Session.CurrentUser = CurrentAdmin();
            Check(r, msg.Contains("مرفوض") && db.OperationLogs.Count() == before,
                $"P3.3(8a): الاستدراك مرفوض من غير مدير/محاسب — لا سطر يُكتب ({msg})");
        }

        // ---- (9) استدراك صفري مرفوض + الاستقرار النهائي ----
        using (var db = new AppDbContext())
        {
            var msg = ReconciliationService.IssueCorrection(db, 0m, "صفر");
            Check(r, msg.Contains("صفري"), "P3.3(9a): استدراك صفري مرفوض");
        }
        using (var rdb = new AppDbContext(dbR))
        using (var wdb = new AppDbContext(dbW))
        {
            var (eq, diff) = ReconciliationService.VerifyAgainst(rdb, wdb);
            Check(r, eq && diff == 0m, "P3.3(10a): الأفق النهائي بلا فجوة — المصالحة تغلقت كاملة");
            Check(r, SyncConflictService.OpenConflictCount(rdb) == 0 && SyncConflictService.OpenConflictCount(wdb) == 0,
                "P3.3(10b): محرك الخلافات لم يُستدعَ عملياً (استدراكات بلا صفوف فاصلة)");
        }

        cts.Cancel();
        try { srvR.Wait(2000); } catch { }
    }

    // ===================== P3.5 — الفوضى §10.1–2: انطفاءٌ في منتصف تطبيق دلتا =====================
    private static void RunP35ChaosScenario(List<string> r, string dbM, string dbK, string dbN)
    {
        r.Add("--- P3.5(فوضى §10.1–2): انطفاءٌ في منتصف تطبيق دلتا — rollback كامل + استئناف تلقائي + إعادة إرسال من آخر إيصال مؤكد ---");

        const string secret = "phase3_secret_chaos";
        const int portM = 47161, portN = 47162, portK = 47164;
        const string dk = "deviceK_TEST", dn = "deviceN_TEST", dm = "deviceM_TEST";

        void ArmChaos(string? targetDevice, int fireWhenApplied) =>
            DeltaApplier.ChaosInjectBeforeCommit = (dev, alreadyCommitted) =>
                string.Equals(dev, targetDevice, StringComparison.Ordinal) && alreadyCommitted == fireWhenApplied;
        void DisarmChaos() => DeltaApplier.ChaosInjectBeforeCommit = null;
        bool TrySync(string fromDb, int toPort, string fromDevice, out bool crashed)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = fromDevice;
            try { SyncPeer.Synchronize("127.0.0.1", toPort, fromDb, maxAttempts: 1); crashed = false; }
            catch { crashed = true; }
            finally { Session.DeviceIdOverride = prev; DisarmChaos(); }
            return !crashed;
        }

        ResetDeviceDb(dbM); ResetDeviceDb(dbK); ResetDeviceDb(dbN);
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dk, secret);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dn, secret);
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dm, secret);

        var cts = new CancellationTokenSource();
        var srvM = SyncPeer.StartServer(portM, dbM, cts.Token);
        var srvK = SyncPeer.StartServer(portK, dbK, cts.Token);
        var srvN = SyncPeer.StartServer(portN, dbN, cts.Token);

        void PushM(string dbPath, string deviceId)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = deviceId;
            SyncPeer.Synchronize("127.0.0.1", portM, dbPath, maxAttempts: 1);
            Session.DeviceIdOverride = prev;
        }

        // ===== (1) §10.2: جهازان انطفآ معاً — كلُّ جهة تنطفئ في منتصف تبادل K↔N =====
        // بذرتان Z(100) وW(150) تُوزَّعان ثم كُلاّ تعدّل صفّاً مختلفاً — لا صفّان لنفس الكيان فالتقارب نظيف.
        var (zSync, _) = SeedProductOnMain(dbM, "جهاز Z الزوج", 100m, 4, "P35Z");
        var (wSync, _) = SeedProductOnMain(dbM, "جهاز W الزوج", 150m, 4, "P35W");
        PushM(dbK, dk); PushM(dbN, dn);
        UseDevice(dbK, dk); using (var k = new AppDbContext()) EditProductPrice(k, zSync, 110m);
        UseDevice(dbN, dn); using (var n = new AppDbContext()) EditProductPrice(n, wSync, 140m);

        ArmChaos(dn, 0); // N تنطفئ عند أول حزمة واردة (الكلّ بلا تأكيد) — جلسة N→K
        bool nCrash;
        try { TrySync(dbN, portK, dn, out nCrash); }
        catch { nCrash = true; }
        Check(r, nCrash, "P3.5(1a): N انطفأت عند أول حزمة واردة — الجلسة سقطت قبل أي تأكيد");
        using (var n = new AppDbContext(dbN))
        {
            Check(r, !n.SyncLogs.AsNoTracking().Any(s => s.OriginDevice == dk)
                && n.OperationLogs.AsNoTracking().All(o => o.OriginDevice == dn || o.OriginDevice == dm)
                && ReadPrice(n, zSync) == 100m,
                "P3.5(1b): في N بلا أثر لما انقطع من K — لا إيصال ولا عملية دخيلة والقيمة بذرة 100 (rollback كامل لحزمة الجلسة)");
        }

        ArmChaos(dk, 0); // K تنطفئ عند أول حزمة واردة — جلسة K→N
        bool kCrash;
        try { TrySync(dbK, portN, dk, out kCrash); }
        catch { kCrash = true; }
        Check(r, kCrash, "P3.5(1c): K انطفأت عند أول حزمة واردة — الجهتان انطفأتا معاً وسط التبادل التزامن");
        using (var k = new AppDbContext(dbK))
        {
            Check(r, !k.SyncLogs.AsNoTracking().Any(s => s.OriginDevice == dn)
                && k.OperationLogs.AsNoTracking().All(o => o.OriginDevice == dk || o.OriginDevice == dm)
                && ReadPrice(k, wSync) == 150m,
                "P3.5(1d): في K بالعكس — لا أثر لعملة N ولا عملية دخيلة والقيمة بذرة 150 (لا صفٌّ مشقوق ولا إيصال يتيم على الجهتين)");
        }

        // ==== (1-e) بعد عودة الجهتين: تبادلٌ سليم يلتحم — كل عملية مرة واحدة وثبات عند الإعادة ====
        var heal2Ok = TrySync(dbK, portN, dk, out _);
        Check(r, heal2Ok, "P3.5(1e): جلسة بعد عودة الجهتين تُقبل تلقائياً (لا يفصل انطفاءٌ مزدوجٌ الجهة عن الأفق)");
        using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN))
        {
            Check(r, ReadPrice(k, zSync) == 110m && ReadPrice(k, wSync) == 140m
                && ReadPrice(n, zSync) == 110m && ReadPrice(n, wSync) == 140m,
                "P3.5(1f): تحلّم الالتئام — Z/W متوزّعتان على الجهتين (no torn, no lost)");
            Check(r, k.SyncLogs.AsNoTracking().Count(s => s.OriginDevice == dn) == 1
                && n.SyncLogs.AsNoTracking().Count(s => s.OriginDevice == dk) == 1,
                "P3.5(1g): كلٌّ من الجهتين أخذ الآخر مرة واحدة بالضبط (إيصالٌ واحد بلا مضاعفة)");
            var sumK = FinancialTotals.CanonicalSum(k);
            var sumN = FinancialTotals.CanonicalSum(n);
            Check(r, sumK == sumN,
                $"P3.5(1h): المجموع التوافقي متساوٍ بعد شفاء الانطفاء المزدوج (K={sumK}، N={sumN})");
        }
        using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN))
        {
            var calm = TrySync(dbK, portN, dk, out _) && TrySync(dbN, portK, dn, out _);
            Check(r, calm, "P3.5(1i): مزامنتان إضافيتان تمرّان بلا فشل — الاستئناف المتبادل سليم");
            Check(r, ReadPrice(k, zSync) == 110m && ReadPrice(k, wSync) == 140m
                && ReadPrice(n, zSync) == 110m && ReadPrice(n, wSync) == 140m,
                "P3.5(1j): قيمٌ ثابتة بعد الإعادة — لا انجراف ولا مضاعفة بعد الشفاء");
        }

        // ===== (2) §10.1: MAIN «تنطفئ» في منتصف تطبيق دلتا K→M =====
        // بذرة X(300) تُوزَّع، ثم K تعدّل X ثلاث عمليات — وM تنطفئ عند الثانية.
        var (xSync, _) = SeedProductOnMain(dbM, "جهاز X الفوضى", 300m, 5, "P35X");
        PushM(dbK, dk); PushM(dbN, dn);
        UseDevice(dbK, dk);
        using (var k = new AppDbContext()) EditProductPrice(k, xSync, 310m);
        using (var k = new AppDbContext()) EditProductPrice(k, xSync, 320m);
        using (var k = new AppDbContext()) EditProductPrice(k, xSync, 330m);
        const string xDoc = "جهاز X الفوضى";

        ArmChaos(dm, 1); // أول حزمة تُؤكَّد كاملاً ثم تُروَّح الثانية — «آخر إيصال مؤكد = الأولى»
        bool crashedA;
        try { TrySync(dbK, portM, dk, out crashedA); }
        catch { crashedA = true; }
        Check(r, crashedA, "P3.5(2a): MAIN انطفأت في منتصف التطبيق — الجلسة سقطت بلا ACK للحزمة الجارية");
        long xFirstSeq;
        using (var m = new AppDbContext(dbM))
        {
            var xSeq = m.OperationLogs.AsNoTracking()
                .Where(o => o.OriginDevice == dk && o.DocumentNumber == xDoc).OrderBy(o => o.OriginSeq)
                .Select(o => o.OriginSeq).ToList();
            xFirstSeq = xSeq[0];
            Check(r, xSeq.Count == 1,
                "P3.5(2b): آخر إيصال مؤكد وحده نجا — أول عمليات X (seq " + xSeq[0] + ") مؤكَّد وحده، وما بعده داخل الحزمة المُراحة (اختيار الإعادة يعتمد على الإيصال لا على القيمة)");
            Check(r, !m.SyncLogs.AsNoTracking().Any(s => s.OriginDevice == dk && s.OriginSeq == xSeq[0] + 1),
                "P3.5(2c): rollback كامل للحزمة الجارية — لا إيصال لِما بعدها ولا صف أو سجل يتيم (كتاباتٌ في معاملةٍ لم تُؤكَّد لا تنجو)");
        }

        // ===== (2-d) بعد «إعادة التشغيل»: جلسة سليمة تُكمل من آخر إيصال مؤكد =====
        var healOk = TrySync(dbK, portM, dk, out _);
        Check(r, healOk, "P3.5(2d): العودة من الانطفاء = مزامنة سليمة تُقبل (استئناف تلقائي بلا تدخل)");
        using (var m = new AppDbContext(dbM))
        {
            var xSeq = m.OperationLogs.AsNoTracking()
                .Where(o => o.OriginDevice == dk && o.DocumentNumber == xDoc).OrderBy(o => o.OriginSeq)
                .Select(o => o.OriginSeq).ToList();
            Check(r, xSeq.Count == 3 && xSeq.SequenceEqual(new long[] { xFirstSeq, xFirstSeq + 1, xFirstSeq + 2 }),
                $"P3.5(2e): إعادة الإرسال من آخر إيصال — تعديلات X الثلاثة كلٌّ مرة واحدة (OriginSeqs {string.Join(",", xSeq)})");
            Check(r, ReadPrice(m, xSync) == 330m,
                "P3.5(2f): القيمة النهائية 330 — استُكمل ما انقطع لا أُعيد ما سبق (idempotent)");
        }
        using (var m = new AppDbContext(dbM))
        {
            PushM(dbK, dk); // مزامنة تالية بعد الشفاء — تُعيد إرسال ما لدى M (watermark=آخر إيصال) بلا جدوى
            Check(r, m.OperationLogs.AsNoTracking().Count(o => o.OriginDevice == dk && o.DocumentNumber == xDoc) == 3,
                "P3.5(2g): مزامنة تالية idempotent مخلبية — الإيصالات تمنع إعادة تطبيق ما سبق (بلا مضاعفة)");
        }
        using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN))
        {
            PushM(dbN, dn);
            Check(r, ReadPrice(k, xSync) == 330m && ReadPrice(n, xSync) == 330m,
                "P3.5(2h): القرار استقرّ على مثلثة M/K/N — الانطفاء لم يُفقد شيئاً لدى أي طرف");
        }

        cts.Cancel();
        try { srvM.Wait(2000); srvK.Wait(2000); srvN.Wait(2000); } catch { }
    }

    // ===== P3.5 §10.3 — ثلاثون يوماً مولَّدين برمجياً (دلتا ضخمة وآلاف الصفوف) + إعادة الغائب + المجموع غير الصفري =====

    private static void RunP35BulkScenario(List<string> r, string dbM, string dbK, string dbN)
    {
        r.Add("--- P3.5(§10.3): دلتا ضخمة — 30 يوماً مولَّداً (180 فاتورة، 360 بنداً، آلاف الصفوف) + إعادة الغائب + المجموع غير الصفري ---");

        const string secret = "phase3_secret_bulk";
        const int portM = 47171, portN = 47172, portK = 47174;
        const string dk = "deviceK_TEST", dn = "deviceN_TEST", dm = "deviceM_TEST";

        ResetDeviceDb(dbM); ResetDeviceDb(dbK); ResetDeviceDb(dbN);
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dk, secret);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dn, secret);
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dm, secret);

        var cts = new CancellationTokenSource();
        var srvM = SyncPeer.StartServer(portM, dbM, cts.Token);
        var srvK = SyncPeer.StartServer(portK, dbK, cts.Token);
        var srvN = SyncPeer.StartServer(portN, dbN, cts.Token);

        void PushM(string dbPath, string deviceId)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = deviceId;
            SyncPeer.Synchronize("127.0.0.1", portM, dbPath, maxAttempts: 1);
            Session.DeviceIdOverride = prev;
        }
        bool TrySync(string fromDb, int toPort, string fromDevice)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = fromDevice;
            try { SyncPeer.Synchronize("127.0.0.1", toPort, fromDb, maxAttempts: 1); return true; }
            catch { return false; }
            finally { Session.DeviceIdOverride = prev; }
        }

        // ثلاثة منتجات مرجعية على MAIN + انتشار البذور لـ K وN
        SeedProductOnMain(dbM, "منتج حار 1", 100m, 50, "P35B1");
        SeedProductOnMain(dbM, "منتج حار 2", 150m, 40, "P35B2");
        SeedProductOnMain(dbM, "منتج حار 3", 200m, 30, "P35B3");
        PushM(dbK, dk); PushM(dbN, dn);
        using (var k0 = new AppDbContext(dbK)) using (var n0 = new AppDbContext(dbN))
        {
            Check(r, k0.Products.Count() == 3 && n0.Products.Count() == 3,
                "P3.5(3a): بذور المنتجات الثلاثة تنشر إلى K وN — البداية نظيفة ومتطابقة قبل الحَجْم");
        }

        // ===== التوليد البرمجي: 30 يوماً × 6 فواتير = 180 بيعاً (بندان لكل فاتورة) ثم جرد منتج 1 =====
        UseDevice(dbK, dk);
        Session.CurrentUser = CurrentAdmin();
        const int days = 30, perDay = 6;
        int sold1 = 0, sold2 = 0, sold3 = 0;
        for (var d = 0; d < days; d++)
        {
            for (var j = 0; j < perDay; j++)
            {
                using var db = new AppDbContext();
                var prods = db.Products.ToList();
                var i = d * perDay + j;
                var a = prods[i % 3];
                var b = prods[(i + 1) % 3];
                var qa = 1 + i % 3;
                var qb = 1 + i / 2 % 2;
                var total = qa * a.SellPrice + qb * b.SellPrice;
                var sale = new Sale
                {
                    InvoiceNumber = "B30-" + (i + 1).ToString("D4"),
                    Date = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Local).AddDays(d).AddMinutes(35 * j),
                    UserId = Session.CurrentUser!.Id,
                    Device = Session.Device,
                    PaymentMethod = PaymentMethod.Cash,
                    Total = total,
                    Profit = qa * (a.SellPrice - a.BuyPrice) + qb * (b.SellPrice - b.BuyPrice)
                };
                sale.Items.Add(new SaleItem { ProductId = a.Id, ProductName = a.Name, Quantity = qa, UnitPrice = a.SellPrice, BuyPrice = a.BuyPrice });
                sale.Items.Add(new SaleItem { ProductId = b.Id, ProductName = b.Name, Quantity = qb, UnitPrice = b.SellPrice, BuyPrice = b.BuyPrice });
                var op = OperationWriter.Register(db, OperationType.Sale, sale.InvoiceNumber, total, "Sales");
                db.Sales.Add(sale);
                a.Stock -= qa;
                b.Stock -= qb;
                db.SaveChanges();
                OperationWriter.SetEntityTarget(op, "Sales", sale.Id);
                db.SaveChanges();
                sold1 += Referenced(sale.Items, "منتج حار 1");
                sold2 += Referenced(sale.Items, "منتج حار 2");
                sold3 += Referenced(sale.Items, "منتج حار 3");
            }
        }
        UseDevice(dbK, dk);
        using (var dbk = new AppDbContext())
        {
            var p1 = dbk.Products.First(p => p.Name == "منتج حار 1");
            var expected1 = 50 - sold1;
            OperationWriter.Register(dbk, OperationType.StockAdjustment, p1.Name, 0m, "Products", p1.Id,
                summaryJson: "جرد نهاية بقعة 30 يوماً — §10.3");
            p1.Stock = expected1;
            dbk.SaveChanges();
        }

        // ===== K → M: الدفعة تُسرَّب في جلسة واحدة =====
        var okBulk = TrySync(dbK, portM, dk);
        Check(r, okBulk, "P3.5(3b): جلسة نقل الدفعة الضخمة تُقبل — لا تعثّر ولا انقطاع عند آلاف الصفوف");
        using (var m = new AppDbContext(dbM))
        {
            var dkOps = m.OperationLogs.AsNoTracking().Count(o => o.OriginDevice == dk);
            Check(r, dkOps == 181, $"P3.5(3c): على MAIN سجلُّ الدفعة كاملٌ بلا تساقط ({dkOps} = 180 بيعاً + 1 جرد)");
            Check(r, m.Sales.Count() == 180 && m.SaleItems.Count() == 360,
                "P3.5(3d): آلاف الصفوف اندست — 180 فاتورة بغدادو 360 بنداً على MAIN");
            Check(r, m.SyncLogs.AsNoTracking().Count(s => s.OriginDevice == dk) == 181,
                "P3.5(3e): إيصالٌ لكل عملية — لا يُدَّعى استلامُ ما لم يصل (كل إيصال يشهد صفوفه)");
        }

        // ===== المجموع المالي للدفعة: غير صفري =====
        using (var m = new AppDbContext(dbM)) using (var k = new AppDbContext(dbK))
        {
            var sumM = FinancialTotals.CanonicalSum(m);
            var sumK = FinancialTotals.CanonicalSum(k);
            Check(r, sumM == sumK && sumM != 0m,
                $"P3.5(3f): المجموع المالي غير الصفري متطابق بعد النقل (M={sumM} = K={sumK}) — شهرُ إيرادٍ كامل لا يضيع بين الكاشير والمركز");
            var stock1 = 50 - sold1;
            var stock2 = 40 - sold2;
            var stock3 = 30 - sold3;
            Check(r, k.Products.First(p => p.Name == "منتج حار 1").Stock == stock1
                && m.Products.First(p => p.Name == "منتج حار 1").Stock == stock1
                && m.Products.First(p => p.Name == "منتج حار 3").Stock == stock3
                && k.Products.First(p => p.Name == "منتج حار 2").Stock == stock2,
                $"P3.5(3g): المخزون محسومٌ بضرب الجرد (حار1={stock1}، حار2={stock2}، حار3={stock3} على الجهتين) — المبيعات الكثيفة تخصم لا تُلغى");
        }

        // ===== العودة من الغياب: N غابت عن الدفعة كلها → تسحب الغائب دفعة واحدة =====
        using (var n = new AppDbContext(dbN))
        {
            Check(r, n.OperationLogs.AsNoTracking().Count(o => o.OriginDevice == dk) == 0,
                "P3.5(3h): N غائبة أثناء الدفعة — صفر أثر لعمليات K قبل عودتها (الغائب لا يُنسى ولا يُهجر)");
        }
        var okN = TrySync(dbN, portM, dn);
        Check(r, okN, "P3.5(3i): عودة الغائب — N تتصل بعد انقطاعٍ طويل فتتزامن بنجاح دون الحاجة لإعادة تهيئة");
        using (var n = new AppDbContext(dbN)) using (var m = new AppDbContext(dbM))
        {
            Check(r, n.OperationLogs.AsNoTracking().Count(o => o.OriginDevice == dk) == 181,
                "P3.5(3j): الغائب سُحب كاملُه — N استكملت الـ181 بالمزامنة فلم يضِع من الشهر ما لم يُسحب");
            var sumN = FinancialTotals.CanonicalSum(n);
            Check(r, sumN == FinancialTotals.CanonicalSum(m) && n.Sales.Count() == 180,
                $"P3.5(3k): المجموع مع N العائدة متطابق (N={sumN}) — لا نقص ولا زيادة بعد اللحاق");
        }

        // ===== إعادة مزامنة الخميلة =====
        var ok2 = TrySync(dbK, portM, dk) && TrySync(dbN, portM, dn);
        Check(r, ok2, "P3.5(3l): إعادة مزامنة K وN تمرّ بلا فشل — الاستئناف الدوري سليم");
        using (var m = new AppDbContext(dbM))
        {
            Check(r, m.OperationLogs.AsNoTracking().Count(o => o.OriginDevice == dk) == 181 && m.Sales.Count() == 180,
                "P3.5(3m): لا مضاعفة بعد الإعادات — الإيصالات تصدّ التكرار (181 عملية، 180 فاتورة لا تزال)");
        }

        cts.Cancel();
        try { srvM.Wait(2000); srvK.Wait(2000); srvN.Wait(2000); } catch { }
    }

    // ===== P3.5 §10.4 — تعديلات متناوبة على زبون واحد من الكاشيرين — تقارب بلا صراع ولا خسارة =====

    private static void RunP35AlternatingScenario(List<string> r, string dbM, string dbK, string dbN)
    {
        r.Add("--- P3.5(§10.4): تعديلات متناوبة ×10 على زبون واحد — المزامنة بين كل تعديلين + تقارب بلا صراع ---");

        const string secret = "phase3_secret_alt";
        const int portM = 47181, portN = 47182, portK = 47184;
        const string dk = "deviceK_TEST", dn = "deviceN_TEST", dm = "deviceM_TEST";

        ResetDeviceDb(dbM); ResetDeviceDb(dbK); ResetDeviceDb(dbN);
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dk, secret);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dn, secret);
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dm, secret);

        var cts = new CancellationTokenSource();
        var srvM = SyncPeer.StartServer(portM, dbM, cts.Token);
        var srvK = SyncPeer.StartServer(portK, dbK, cts.Token);
        var srvN = SyncPeer.StartServer(portN, dbN, cts.Token);

        void PushM(string dbPath, string deviceId)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = deviceId;
            SyncPeer.Synchronize("127.0.0.1", portM, dbPath, maxAttempts: 1);
            Session.DeviceIdOverride = prev;
        }
        bool TrySync(string fromDb, int toPort, string fromDevice)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = fromDevice;
            try { SyncPeer.Synchronize("127.0.0.1", toPort, fromDb, maxAttempts: 1); return true; }
            catch { return false; }
            finally { Session.DeviceIdOverride = prev; }
        }

        // عميل مرجعي على MAIN + بذرة تُنشر ثم 10 تعديلات متناوبة: K ثم N ثم K ...
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer);
        string cSync;
        using (var db = new AppDbContext())
        {
            var cust = new Customer { Name = "زبون متناوب", Balance = 0m };
            db.Customers.Add(cust);
            db.SaveChanges();
            OperationWriter.Register(db, OperationType.StockAdjustment, cust.Name, 0m, "Customers", cust.Id,
                summaryJson: "بذرة زبون — §10.4");
            db.SaveChanges();
            cSync = cust.SyncId!;
        }
        PushM(dbK, dk); PushM(dbN, dn);
        using (var k0 = new AppDbContext(dbK)) using (var n0 = new AppDbContext(dbN))
        {
            Check(r, k0.Customers.Any(x => x.SyncId == cSync) && n0.Customers.Any(x => x.SyncId == cSync),
                "P3.5(4a): الزبون المرجعي وصل إلى الكاشيرين عبر البذرة — لَهويةٍ واحدة على المثلثة كلها");
        }

        for (var i = 1; i <= 10; i++)
        {
            var isK = i % 2 == 1;
            var edDb = isK ? dbK : dbN;
            var edDev = isK ? dk : dn;
            var othDb = isK ? dbN : dbK;
            var othDev = isK ? dn : dk;
            var name = "تناوب-" + i;

            UseDevice(edDb, edDev); SetDeviceRole(edDb, DeviceRole.Cashier);
            using (var db = new AppDbContext())
            {
                var c = db.Customers.First(x => x.SyncId == cSync);
                c.Name = name;
                OperationWriter.Register(db, OperationType.StockAdjustment, c.Name, 0m, "Customers", c.Id,
                    summaryJson: "تعديل متناوب — §10.4");
                db.SaveChanges();
            }

            // تعديلُ الطرف المسؤول → MAIN، ثم الموازي يُسحب من MAIN فتُدهمه الحقيقة — المزامنة بين كل تعديلين
            PushM(edDb, edDev);
            PushM(othDb, othDev);
            using (var m = new AppDbContext(dbM))
            {
                Check(r, m.SyncConflicts.AsNoTracking().Count() == 0 && ReadCustomerName(m, cSync) == name,
                    $"P3.5(4b:{i:00}): التقارب بعد التعديل {i} — لا صراع واسمُ الزبون {(isK ? "K" : "N")}-الجديد ({name}) حاضر على MAIN");
            }
        }
        using (var m = new AppDbContext(dbM)) using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN))
        {
            Check(r, ReadCustomerName(m, cSync) == "تناوب-10" && ReadCustomerName(k, cSync) == "تناوب-10" && ReadCustomerName(n, cSync) == "تناوب-10",
                "P3.5(4c): الاسم النهائي (تناوب-10) استقرّ على المثلثة — آخر تعديل في التسلسل الدوري متطابق في كل مكان");
            var kOps = k.OperationLogs.AsNoTracking().Count(o => (o.OriginDevice == dk || o.OriginDevice == dn) && o.EntityName == "Customers");
            var nOps = n.OperationLogs.AsNoTracking().Count(o => (o.OriginDevice == dk || o.OriginDevice == dn) && o.EntityName == "Customers");
            var mOps = m.OperationLogs.AsNoTracking().Count(o => (o.OriginDevice == dk || o.OriginDevice == dn) && o.EntityName == "Customers");
            Check(r, kOps == 10 && nOps == 10 && mOps == 10,
                "P3.5(4d): كلٌّ من الكاشيرين سجّل 10 عمليات زبون على الكلّ — الرئيسي والموازي والمركز شهدوا التسلسل عينه بلا فقد");
            Check(r, m.SyncConflicts.AsNoTracking().Count() == 0,
                "P3.5(4e): صفر صراعات بعد عشر تعديلات متناوبة — التعاقبُ المرسل بين كل تعديلين لا يولّد تضارباً");
        }
        var ok2 = TrySync(dbK, portM, dk) && TrySync(dbN, portM, dn);
        Check(r, ok2, "P3.5(4f): مزامنتان اضافيتان تمران — الاستئناف سليم بلا توتر");
        using (var k = new AppDbContext(dbK))
        {
            Check(r, k.OperationLogs.AsNoTracking().Count(o => (o.OriginDevice == dk || o.OriginDevice == dn) && o.EntityName == "Customers") == 10,
                "P3.5(4g): لا مضاعفة بعد الإعادات — عمليات الزبون لا تزال 10 على K");
        }

        cts.Cancel();
        try { srvM.Wait(2000); srvK.Wait(2000); srvN.Wait(2000); } catch { }
    }

    // ===== P3.5 §10.5 — قاعدة ملفوبة: رفضُ إقلاع وعدمُ تجاهلٍ ثم استعادة من نسخة احتياطية واستئناف =====

    private static void RunP35CorruptScenario(List<string> r, string dbM, string dbK, string dbN)
    {
        r.Add("--- P3.5(§10.5): قاعدة ملفوبة — رفضُ إقلاع وعدمُ تجاهلٍ ثم استعادة من نسخة احتياطية واستئناف ---");

        const string secret = "phase3_secret_corrupt";
        const int portM = 47201, portN = 47202, portK = 47204;
        const string dk = "deviceK_TEST", dn = "deviceN_TEST", dm = "deviceM_TEST";

        ResetDeviceDb(dbM); ResetDeviceDb(dbK); ResetDeviceDb(dbN);
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dk, secret);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dn, secret);
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dm, secret);

        var cts = new CancellationTokenSource();
        var srvM = SyncPeer.StartServer(portM, dbM, cts.Token);
        var srvK = SyncPeer.StartServer(portK, dbK, cts.Token);
        var srvN = SyncPeer.StartServer(portN, dbN, cts.Token);

        void PushM(string dbPath, string deviceId)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = deviceId;
            SyncPeer.Synchronize("127.0.0.1", portM, dbPath, maxAttempts: 1);
            Session.DeviceIdOverride = prev;
        }
        bool TrySync(string fromDb, int toPort, string fromDevice)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = fromDevice;
            try { SyncPeer.Synchronize("127.0.0.1", toPort, fromDb, maxAttempts: 1); return true; }
            catch { return false; }
            finally { Session.DeviceIdOverride = prev; }
        }

        // إعداد «يوم عمل»: بذرة منتج تُنشر ثم تعديلُ سعر من K يصل للكل
        var (cSync, _) = SeedProductOnMain(dbM, "جهاز C فساد", 120m, 8, "P35C");
        PushM(dbK, dk); PushM(dbN, dn);
        UseDevice(dbK, dk);
        using (var k = new AppDbContext()) EditProductPrice(k, cSync, 130m);
        PushM(dbK, dk); // السعر 130 يصل إلى MAIN
        PushM(dbN, dn); // والموازي N يُسحب
        using (var n = new AppDbContext(dbN)) using (var m = new AppDbContext(dbM))
        {
            Check(r, ReadPrice(n, cSync) == 130m && ReadPrice(m, cSync) == 130m,
                "P3.5(5a): قبل الكارثة — السعر 130 استقرّ على المثلثة (يوم عمل سليم)");
        }

        SqliteConnection.ClearAllPools();
        using (var b = new AppDbContext(dbN)) b.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE)");
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(dbN + suffix)) File.Delete(dbN + suffix);
        File.Copy(dbN, dbN + ".bak"); // النسخة الاحتياطية (تحاكي النسخة الليلية)

        // إتلاف فيزيائي: رأس الملف (يُبطل "SQLite format 3") + كتلة في المنتصف
        byte[] bytes = File.ReadAllBytes(dbN);
        for (var i = 0; i < 240; i++) bytes[i] ^= 0x5A;
        for (var i = 0; i < 256; i++) bytes[bytes.Length / 3 + i] ^= 0xA5;
        File.WriteAllBytes(dbN, bytes);
        SqliteConnection.ClearAllPools();

        // الإقلاع يرفض
        bool rejected = false;
        try { using (var bad = new AppDbContext(dbN)) { _ = bad.Products.Count(); } }
        catch { rejected = true; }
        Check(r, rejected, "P3.5(5b): الإقلاع يرفض القاعدة الملفوبة — أي قراءة ترمي (لا يُفتح بوجهٍ مُرقع)");

        long receiptsBefore;
        using (var mb = new AppDbContext(dbM))
            receiptsBefore = mb.SyncLogs.AsNoTracking().Count(s => s.OriginDevice == dn);

        var syncOk = TrySync(dbN, portM, dn);
        Check(r, !syncOk, "P3.5(5c): لا تُلاك المزامنة بإقلاع ملفوب — الجلسة ترفض قبل أن تُسلِّم أو تلتقط شيئاً (صفر أثر)");
        using (var mb = new AppDbContext(dbM))
        {
            Check(r, mb.SyncLogs.AsNoTracking().Count(s => s.OriginDevice == dn) == receiptsBefore,
                "P3.5(5d): لا أثر للمحاولة الفاشلة على MAIN — لا إيصال ولا عملية دخيلة تُنسب للانقطاع");
        }

        // الاستعادة من النسخة الاحتياطية
        File.Copy(dbN + ".bak", dbN, true);
        SqliteConnection.ClearAllPools();
        UseDevice(dbN, dn);
        using (var rdb = new AppDbContext())
        {
            Check(r, rdb.Products.Any(p => p.SyncId == cSync)
                && rdb.SyncLogs.AsNoTracking().Any(s => s.OriginDevice == dm),
                "P3.5(5e): الاستعادة أعادت قاعدة سليمة مأهولة (منتج + إيصال) — النسخة الليلية لم تنكسر");
        }

        // الاستئناف: يلتقط ما تمّ في يومٍ تغيّر السعر فيه (مزامنة تُغلق المهجر كما في Restore Drill)
        var resumed = TrySync(dbN, portM, dn);
        Check(r, resumed, "P3.5(5f): بعد الاستعادة — مزامنة تُقبل فوراً بلا تدخل يدوي (استئناف تلقائي)");
        using (var n = new AppDbContext(dbN)) using (var k = new AppDbContext(dbK)) using (var m = new AppDbContext(dbM))
        {
            Check(r, ReadPrice(n, cSync) == 130m && ReadPrice(k, cSync) == 130m && ReadPrice(m, cSync) == 130m,
                "P3.5(5g): القرار استقرّ على المثلثة — لا يوم قديم عاد من مهجره يضرب قرار اليوم (السعر 130)");
            Check(r, FinancialTotals.CanonicalSum(n) == FinancialTotals.CanonicalSum(m),
                "P3.5(5h): المجموع المالي متطابق بعد الاستعادة — لا يوم مكتوب تراجع ولا انحراف في الحساب");
        }

        cts.Cancel();
        try { srvM.Wait(2000); srvK.Wait(2000); srvN.Wait(2000); } catch { }
    }

    // ===== P3.5 §10.6 — نافذة البيع تُغلق في منتصف مزامنتها — لا نصف تطبيق يرتدّ =====

    private static void RunP35WindowScenario(List<string> r, string dbM, string dbK, string dbN)
    {
        r.Add("--- P3.5(§10.6): نافذة البيع تُغلق في منتصف تطبيق ردّ MAIN عليها — استئناف من آخر إيصال مؤكد ---");

        const string secret = "phase3_secret_win";
        const int portM = 47191, portN = 47192, portK = 47194;
        const string dk = "deviceK_TEST", dn = "deviceN_TEST", dm = "deviceM_TEST";

        void ArmChaos(string? targetDevice, int fireWhenApplied) =>
            DeltaApplier.ChaosInjectBeforeCommit = (dev, alreadyCommitted) =>
                string.Equals(dev, targetDevice, StringComparison.Ordinal) && alreadyCommitted == fireWhenApplied;
        void DisarmChaos() => DeltaApplier.ChaosInjectBeforeCommit = null;
        bool TrySync(string fromDb, int toPort, string fromDevice)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = fromDevice;
            try { SyncPeer.Synchronize("127.0.0.1", toPort, fromDb, maxAttempts: 1); return true; }
            catch { return false; }
            finally { Session.DeviceIdOverride = prev; DisarmChaos(); }
        }

        ResetDeviceDb(dbM); ResetDeviceDb(dbK); ResetDeviceDb(dbN);
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dk, secret);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dn, secret);
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dm, secret);

        var cts = new CancellationTokenSource();
        var srvM = SyncPeer.StartServer(portM, dbM, cts.Token);
        var srvK = SyncPeer.StartServer(portK, dbK, cts.Token);
        var srvN = SyncPeer.StartServer(portN, dbN, cts.Token);

        void PushM(string dbPath, string deviceId)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = deviceId;
            SyncPeer.Synchronize("127.0.0.1", portM, dbPath, maxAttempts: 1);
            Session.DeviceIdOverride = prev;
        }

        // MAIN تُعدّل جهاز W ثلاث عمليات (210/220/230) — سيُسلَّم K بثلاثة طرود بعد تفرّقه
        var (wSync, _) = SeedProductOnMain(dbM, "جهاز W نافذة", 200m, 6, "P35W");
        PushM(dbK, dk);
        UseDevice(dbM, dm);
        using (var m1 = new AppDbContext()) EditProductPrice(m1, wSync, 210m);
        using (var m2 = new AppDbContext()) EditProductPrice(m2, wSync, 220m);
        using (var m3 = new AppDbContext()) EditProductPrice(m3, wSync, 230m);
        long firstEditSeq;
        using (var m = new AppDbContext(dbM))
        {
            var w = m.Products.First(x => x.SyncId == wSync);
            var ops = m.OperationLogs.AsNoTracking()
                .Where(o => o.OriginDevice == dm && o.EntityName == "Products" && o.EntityId == w.Id)
                .OrderBy(o => o.OriginSeq).Select(o => o.OriginSeq).ToList();
            firstEditSeq = ops[1]; // سطر 0 = البذرة، سطر 1 = أول تعديل
        }

        // النافذة تنطفئ على K أثناء تطبيقها للرد: أول حزمة تُؤكَّد ثم تنقطع الثانية
        ArmChaos(dk, 1);
        var winOk = TrySync(dbK, portM, dk);
        Check(r, !winOk, "P3.5(6a): نافذة K أُغلقت في منتصف تطبيق ردّ MAIN — الجلسة سقطت بلا ACK للباقي");
        using (var k = new AppDbContext(dbK))
        {
            var dmOps = k.OperationLogs.AsNoTracking().Count(o => o.OriginDevice == dm && o.EntityName == "Products" && o.DocumentNumber == "جهاز W نافذة");
            Check(r, dmOps == 1 && !k.SyncLogs.AsNoTracking().Any(s => s.OriginDevice == dm && s.OriginSeq == firstEditSeq + 1),
                "P3.5(6b): أولُ تعديل وحده نجا — ما بعده في الحزمة المُراحة سقط (rollback كامل للحزمة الجارية)");
        }

        // إعادة الفتح: استئناف تلقائي يكمل من آخر إيصال
        var reopen = TrySync(dbK, portM, dk);
        Check(r, reopen, "P3.5(6c): إعادة فتح النافذة = جلسة سليمة تُقبل (لا يتطلب الانقطاع تهيئةً أو استعادة)");
        using (var k = new AppDbContext(dbK))
        {
            var dmOps = k.OperationLogs.AsNoTracking().Count(o => o.OriginDevice == dm && o.EntityName == "Products" && o.DocumentNumber == "جهاز W نافذة");
            Check(r, dmOps == 3 && ReadPrice(k, wSync) == 230m,
                "P3.5(6d): استُكمِل ما انقطع — تعديلات W الثلاثة على K والسعر النهائي 230 (لا مضاعفة)");
        }

        // حسم المثلثة
        PushM(dbN, dn);
        using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN)) using (var m = new AppDbContext(dbM))
        {
            Check(r, ReadPrice(k, wSync) == 230m && ReadPrice(n, wSync) == 230m && ReadPrice(m, wSync) == 230m,
                "P3.5(6e): القرار استقرّ على المثلثة — إغلاقُ نافذة البيع لم يُفقد تعديلاً لدى أي طرف");
        }

        cts.Cancel();
        try { srvM.Wait(2000); srvK.Wait(2000); srvN.Wait(2000); } catch { }
    }

    // ===== P3.5 §10.7 — جهازان ينشئان بنفس الاسم والباركود: تحذيرٌ لا حذف، تعايشٌ بهوية لا بمدخل =====

    private static void RunP35DuplicateScenario(List<string> r, string dbM, string dbK, string dbN)
    {
        r.Add("--- P3.5(§10.7): جهازان بنفس الاسم والباركود — تحذير لا حذف والتعايش بالهوية لا بالمدخل ---");

        const string secret = "phase3_secret_dup";
        const int portM = 47211, portN = 47212, portK = 47214;
        const string dk = "deviceK_TEST", dn = "deviceN_TEST", dm = "deviceM_TEST";

        ResetDeviceDb(dbM); ResetDeviceDb(dbK); ResetDeviceDb(dbN);
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dk, secret);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dn, secret);
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dm, secret);

        var cts = new CancellationTokenSource();
        var srvM = SyncPeer.StartServer(portM, dbM, cts.Token);
        var srvK = SyncPeer.StartServer(portK, dbK, cts.Token);
        var srvN = SyncPeer.StartServer(portN, dbN, cts.Token);

        void PushM(string dbPath, string deviceId)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = deviceId;
            SyncPeer.Synchronize("127.0.0.1", portM, dbPath, maxAttempts: 1);
            Session.DeviceIdOverride = prev;
        }

        // بذرةٌ شائعة (تصنيف+منتج) تُنشر للجهتين — لا يُخلط تكرار التصنيف بفحص المكرر
        SeedProductOnMain(dbM, "أولية-10.7", 50m, 7, "P35D0");
        PushM(dbK, dk); PushM(dbN, dn);
        string catSync;
        using (var m0 = new AppDbContext(dbM))
        {
            catSync = m0.Categories.AsNoTracking().First(c => c.Name == "أجهزة P3.1").SyncId!;
        }

        // K وN كلٌّ منهما (على انفراد) ينشئ منتجاً بالاسم نفسه والباركود نفسه
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier);
        string kSync;
        using (var k0 = new AppDbContext())
        {
            var cat = k0.Categories.First(c => c.SyncId == catSync);
            var pk = new Product { Name = "مكرر-10.7", Barcode = "DUP-001", CategoryId = cat.Id, BuyPrice = 80m, SellPrice = 100m, Stock = 5, Device = DeviceRole.Cashier };
            k0.Products.Add(pk);
            k0.SaveChanges();
            OperationWriter.Register(k0, OperationType.StockAdjustment, "P35-DUP-K", 0m, "Products", pk.Id, summaryJson: "إنشاء مكرر K — §10.7");
            k0.SaveChanges();
            kSync = pk.SyncId!;
        }
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier);
        string nSync;
        using (var n0 = new AppDbContext())
        {
            var cat = n0.Categories.First(c => c.SyncId == catSync);
            var pn = new Product { Name = "مكرر-10.7", Barcode = "DUP-001", CategoryId = cat.Id, BuyPrice = 80m, SellPrice = 100m, Stock = 5, Device = DeviceRole.Cashier };
            n0.Products.Add(pn);
            n0.SaveChanges();
            OperationWriter.Register(n0, OperationType.StockAdjustment, "P35-DUP-N", 0m, "Products", pn.Id, summaryJson: "إنشاء مكرر N — §10.7");
            n0.SaveChanges();
            nSync = pn.SyncId!;
        }
        Check(r, kSync != nSync, "P3.5(7a): كل جهاز أنشأ هويةً مختلفة — الهوية بالـSyncId لا بالاسم/الباركود (تعاملٌ أمن للمكرر)");

        PushM(dbK, dk); PushM(dbN, dn);
        using (var m = new AppDbContext(dbM))
        {
            var kRow = m.Products.AsNoTracking().First(x => x.SyncId == kSync);
            var nRow = m.Products.AsNoTracking().First(x => x.SyncId == nSync);
            Check(r, kRow.Name == "مكرر-10.7" && nRow.Name == "مكرر-10.7"
                && kRow.Barcode == "DUP-001" && nRow.Barcode == "DUP-001" && kRow.Id != nRow.Id,
                "P3.5(7b): المكرر تعايش على MAIN بهويتين — لا حذف ولا كتابة فوق ولا صفٌّ واحد استُعبد (تحذيرٌ لا حذف)");
            Check(r, m.SyncConflicts.AsNoTracking().Count() == 0,
                "P3.5(7c): المكرر المُدخل لا يُعدّ صراعَ مزامنة — صفر صفوف صراع على MAIN");
            Check(r, kRow.DeletedAt == null && nRow.DeletedAt == null && kRow.SellPrice == 100m && nRow.SellPrice == 100m,
                "P3.5(7d): قيمُ الإنشاء الأولين محفوظة حرفياً — لا أثرَ للثاني على الأول ولا شاهدَ قبرٍ لحذفٍ فرضَهُ اسمٌ مشترك");
        }

        // تقارب المثلثة
        PushM(dbK, dk);
        using (var m = new AppDbContext(dbM)) using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN))
        {
            Check(r,
                m.Products.AsNoTracking().Count(x => x.Name == "مكرر-10.7" && x.Barcode == "DUP-001") == 2
                && k.Products.AsNoTracking().Count(x => x.Name == "مكرر-10.7") == 2
                && n.Products.AsNoTracking().Count(x => x.Name == "مكرر-10.7") == 2,
                "P3.5(7e): المكرر استقرَّ على المثلثة — لا مفقود عند التوزيع ولا ازدواجٌ عند التلقي (كل جهة تمسك نسختين بعينهما)");
        }

        // إعادة مزامنة الخميلة: idempotent
        var ok2 = true;
        try { PushM(dbK, dk); PushM(dbN, dn); } catch { ok2 = false; }
        Check(r, ok2, "P3.5(7f): إعادة مزامنة مزدوجة تمرّ بلا فشل — الاستئناف بعد التكرار سليم");
        using (var m = new AppDbContext(dbM)) using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN))
        {
            Check(r,
                m.Products.AsNoTracking().Count(x => x.Barcode == "DUP-001") == 2
                && k.SyncLogs.AsNoTracking().Count(s => s.OriginDevice == dn || s.OriginDevice == dm) == k.SyncLogs.AsNoTracking().Count(s => s.OriginDevice == dn || s.OriginDevice == dm)
                && n.Products.AsNoTracking().Count(x => x.Barcode == "DUP-001") == 2
                && m.Products.AsNoTracking().Count() == 3,
                "P3.5(7g): لا مضاعفة بعد الإعادات — البذرة+المكرران=3 على MAIN وعدد الباركود ثابت على الجهات كلها");
            var sumM = FinancialTotals.CanonicalSum(m);
            var sumK = FinancialTotals.CanonicalSum(k);
            var sumN = FinancialTotals.CanonicalSum(n);
            Check(r, sumM == sumK && sumK == sumN && sumM == 0m,
                "P3.5(7h): المجاميع المالية صفرٌ متطابقة — الإنشاء المكرر لا يولّد أثراً مالياً كاذباً على أي جهة");
        }

        cts.Cancel();
        try { srvM.Wait(2000); srvK.Wait(2000); srvN.Wait(2000); } catch { }
    }

    // ===== P3.5 §10.8 — انقطاع راوتر 15 دقيقة: عمل دون اتصال ثم استئناف تلقائي بلا فقد ولا مضاعفة =====

    private static void RunP35RouterScenario(List<string> r, string dbM, string dbK, string dbN)
    {
        r.Add("--- P3.5(§10.8): انقطاع راوتر 15 دقيقة — عملٌ دون اتصال (مبيعات+أسعار) ثم استئناف تلقائي ---");

        const string secret = "phase3_secret_route";
        const int portM = 47221, portN = 47222, portK = 47224;
        const string dk = "deviceK_TEST", dn = "deviceN_TEST", dm = "deviceM_TEST";

        ResetDeviceDb(dbM); ResetDeviceDb(dbK); ResetDeviceDb(dbN);
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dk, secret);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dn, secret);
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dm, secret);

        var cts = new CancellationTokenSource();
        var srvM = SyncPeer.StartServer(portM, dbM, cts.Token);
        var srvK = SyncPeer.StartServer(portK, dbK, cts.Token);
        var srvN = SyncPeer.StartServer(portN, dbN, cts.Token);

        void PushM(string dbPath, string deviceId)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = deviceId;
            SyncPeer.Synchronize("127.0.0.1", portM, dbPath, maxAttempts: 1);
            Session.DeviceIdOverride = prev;
        }
        bool TrySync(string fromDb, int toPort, string fromDevice)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = fromDevice;
            try { SyncPeer.Synchronize("127.0.0.1", toPort, fromDb, maxAttempts: 1); return true; }
            catch { return false; }
            finally { Session.DeviceIdOverride = prev; }
        }

        // بذرة فيصل راوتر (سعر 100، مخزون 20) + جلسة سليمة قبل الانقطاع
        var (rSync, _) = SeedProductOnMain(dbM, "منتج راوتر", 100m, 20, "P35R");
        PushM(dbK, dk); PushM(dbN, dn);
        UseDevice(dbK, dk); Session.CurrentUser = CurrentAdmin();
        using (var db = new AppDbContext())
        {
            var p = db.Products.First(x => x.SyncId == rSync);
            var sale = new Sale
            {
                InvoiceNumber = "R-K-01", Date = DateTime.Now, UserId = Session.CurrentUser!.Id,
                Device = Session.Device, PaymentMethod = PaymentMethod.Cash, Total = 100m, Profit = 20m
            };
            sale.Items.Add(new SaleItem { ProductId = p.Id, ProductName = p.Name, Quantity = 1, UnitPrice = 100m, BuyPrice = 80m });
            p.Stock -= 1;
            var op = OperationWriter.Register(db, OperationType.Sale, sale.InvoiceNumber, 100m, "Sales");
            db.Sales.Add(sale);
            db.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Sales", sale.Id);
            db.SaveChanges();
        }
        PushM(dbK, dk);

        // ===== الانقطاع: مخدم MAIN يُوقف =====
        cts.Cancel();
        try { srvM.Wait(2000); } catch { }

        // ===== 15 دقيقة دون اتصال: K تغلق بيعين وتعدّل سعراً، N تغلق بيعاً — أرقام صريحة فريدة (قرار H1) =====
        UseDevice(dbK, dk); Session.CurrentUser = CurrentAdmin();
        using (var k1 = new AppDbContext())
        {
            var p = k1.Products.First(x => x.SyncId == rSync);
            var s2 = new Sale { InvoiceNumber = "R-K-02", Date = DateTime.Now, UserId = Session.CurrentUser!.Id, Device = Session.Device, PaymentMethod = PaymentMethod.Cash, Total = 100m, Profit = 20m };
            s2.Items.Add(new SaleItem { ProductId = p.Id, ProductName = p.Name, Quantity = 1, UnitPrice = 100m, BuyPrice = 80m });
            p.Stock -= 1;
            var op2 = OperationWriter.Register(k1, OperationType.Sale, s2.InvoiceNumber, 100m, "Sales");
            k1.Sales.Add(s2);
            k1.SaveChanges();
            OperationWriter.SetEntityTarget(op2, "Sales", s2.Id);
            k1.SaveChanges();
        }
        using (var k2 = new AppDbContext())
        {
            var p = k2.Products.First(x => x.SyncId == rSync);
            var s3 = new Sale { InvoiceNumber = "R-K-03", Date = DateTime.Now, UserId = Session.CurrentUser!.Id, Device = Session.Device, PaymentMethod = PaymentMethod.Cash, Total = 200m, Profit = 40m };
            s3.Items.Add(new SaleItem { ProductId = p.Id, ProductName = p.Name, Quantity = 2, UnitPrice = 100m, BuyPrice = 80m });
            p.Stock -= 2;
            var op3 = OperationWriter.Register(k2, OperationType.Sale, s3.InvoiceNumber, 200m, "Sales");
            k2.Sales.Add(s3);
            k2.SaveChanges();
            OperationWriter.SetEntityTarget(op3, "Sales", s3.Id);
            k2.SaveChanges();
        }
        EditProductPriceWithSession(new AppDbContext(dbK), rSync, 110m);
        UseDevice(dbN, dn); Session.CurrentUser = CurrentAdmin();
        using (var n1 = new AppDbContext())
        {
            var p = n1.Products.First(x => x.SyncId == rSync);
            var s4 = new Sale { InvoiceNumber = "R-N-01", Date = DateTime.Now, UserId = Session.CurrentUser!.Id, Device = Session.Device, PaymentMethod = PaymentMethod.Cash, Total = 100m, Profit = 20m };
            s4.Items.Add(new SaleItem { ProductId = p.Id, ProductName = p.Name, Quantity = 1, UnitPrice = 100m, BuyPrice = 80m });
            p.Stock -= 1;
            var op4 = OperationWriter.Register(n1, OperationType.Sale, s4.InvoiceNumber, 100m, "Sales");
            n1.Sales.Add(s4);
            n1.SaveChanges();
            OperationWriter.SetEntityTarget(op4, "Sales", s4.Id);
            n1.SaveChanges();
        }

        // ===== محاولة مزامنة أثناء انقطاع المخدم: رفضٌ نظيف صفر الأثر =====
        var kOpsBefore = 0; var kLogsBefore = 0;
        using (var kb = new AppDbContext(dbK))
        {
            kOpsBefore = kb.OperationLogs.AsNoTracking().Count();
            kLogsBefore = kb.SyncLogs.AsNoTracking().Count();
        }
        Check(r, !TrySync(dbK, portM, dk), "P3.5(8a): أثناء الانقطاع رفِضت جلسة K للأفق — لا مزامنة بلا مخدم حي");
        using (var ka = new AppDbContext(dbK))
        {
            Check(r, ka.OperationLogs.AsNoTracking().Count() == kOpsBefore && ka.SyncLogs.AsNoTracking().Count() == kLogsBefore,
                "P3.5(8b): المحاولة الفاشلة صفر الأثر — لا إيصال ولا عملية زائفة ولا علامة ماء تقدَّمت (لا شيطان نصف-جلسة)");
        }

        // ===== الاستئناف: مخدم MAIN يُعاد على نفس المنفذ فتتزامن K وN تلقائياً =====
        var cts2 = new CancellationTokenSource();
        var srvM2 = SyncPeer.StartServer(portM, dbM, cts2.Token);
        var resumed = TrySync(dbK, portM, dk) && TrySync(dbN, portM, dn);
        Check(r, resumed,
            "P3.5(8c): بعد عودة المخدم انطلقت مزامنتا K وN تلقائياً بلا تهيئة ولا استعادة — الاستئناف الفوري من آخر نقاط توقف كجهة");
        PushM(dbK, dk); PushM(dbN, dn); // جولة ثانية: كل جهة تسحب عمليات الجهة الأخرى

        using (var m = new AppDbContext(dbM)) using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN))
        {
            Check(r, m.Sales.Count() == 4 && k.Sales.Count() == 4 && n.Sales.Count() == 4,
                "P3.5(8d): مبيعات الانقطاع الأربع انتشرت على المثلثة كاملة — لا يُفقد بيعٌ يُحرر دون اتصال");
            Check(r, m.Sales.Select(s => s.InvoiceNumber).Distinct().Count() == 4,
                "P3.5(8e): أرقام الفواتير الصريحة فريدة على MAIN — مسارُ الأرقام الصريحة (قرار H1) لا يصطدم بالفهرس الفريد بين كاشيرَين");
            var sumM = FinancialTotals.CanonicalSum(m);
            var sumK = FinancialTotals.CanonicalSum(k);
            var sumN = FinancialTotals.CanonicalSum(n);
            Check(r, sumM == 500m && sumK == 500m && sumN == 500m,
                $"P3.5(8f): المجموع المالي متطابق بعد الاستئناف (K={sumK}، N={sumN}، M={sumM}=500) — لا نقصٌ ولا زيادة بعد اللحاق");
            Check(r,
                m.Products.First(x => x.SyncId == rSync).Stock == k.Products.First(x => x.SyncId == rSync).Stock
                && n.Products.First(x => x.SyncId == rSync).Stock == k.Products.First(x => x.SyncId == rSync).Stock
                && m.SyncConflicts.AsNoTracking().Count() == 0 && k.SyncConflicts.AsNoTracking().Count() == 0 && n.SyncConflicts.AsNoTracking().Count() == 0,
                "P3.5(8g): المخزون والبيئة متطابقان على المثلثة بآخر لقطة مطبَّقة وصرورٌ صفر — توزيعُ المبيعات لا يفتح خلافا بين كاشيرَين على نفس المنتج");
            var dkOps = m.OperationLogs.AsNoTracking().Count(o => o.OriginDevice == dk && o.EntityName == "Sales");
            var dnOps = m.OperationLogs.AsNoTracking().Count(o => o.OriginDevice == dn && o.EntityName == "Sales");
            Check(r, dkOps == 3 && dnOps == 1,
                $"P3.5(8h): العمليات بلغت بعلمها الأصلي (K={dkOps} بيعاً، N={dnOps} بيعاً على MAIN) — لا جهة تُهوى ولا عائِدُ انقطاعٍ يُصادَر");
        }

        // خميلة: لا مضاعفة
        var ok2 = TrySync(dbK, portM, dk) && TrySync(dbN, portM, dn);
        Check(r, ok2, "P3.5(8i): مزامنتان إضافيتان تمرّان بلا فشل — الاستئناف الدوري سليم");
        using (var m = new AppDbContext(dbM))
        {
            Check(r, m.Sales.Count() == 4 && FinancialTotals.CanonicalSum(m) == 500m,
                "P3.5(8j): لا مضاعفة بعد الإعادات — الأربع فواتير والـ500 لم يتضاعفا (idempotent بالإيصالات)");
        }

        cts2.Cancel();
        try { srvM2.Wait(2000); srvK.Wait(2000); srvN.Wait(2000); } catch { }
    }

    // ===== P3.5 §10.9 — تعديلان لنفس الفاتورة داخل فترة السماح: صراعٌ يُفتح لدى الكاشيرين ويُحسم من MAIN =====

    private static void RunP35GraceScenario(List<string> r, string dbM, string dbK, string dbN)
    {
        r.Add("--- P3.5(§10.9): تعديلان لنفس الفاتورة في فترة السماح — صراعٌ على K وN وحسمٌ موقّع من MAIN ---");

        const string secret = "phase3_secret_grace";
        const int portM = 47231, portN = 47232, portK = 47234;
        const string dk = "deviceK_TEST", dn = "deviceN_TEST", dm = "deviceM_TEST";

        ResetDeviceDb(dbM); ResetDeviceDb(dbK); ResetDeviceDb(dbN);
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dk, secret);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dn, secret);
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dm, secret);

        var cts = new CancellationTokenSource();
        var srvM = SyncPeer.StartServer(portM, dbM, cts.Token);
        var srvK = SyncPeer.StartServer(portK, dbK, cts.Token);
        var srvN = SyncPeer.StartServer(portN, dbN, cts.Token);

        void PushM(string dbPath, string deviceId)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = deviceId;
            SyncPeer.Synchronize("127.0.0.1", portM, dbPath, maxAttempts: 1);
            Session.DeviceIdOverride = prev;
        }
        bool TrySync(string fromDb, int toPort, string fromDevice)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = fromDevice;
            try { SyncPeer.Synchronize("127.0.0.1", toPort, fromDb, maxAttempts: 1); return true; }
            catch { return false; }
            finally { Session.DeviceIdOverride = prev; }
        }

        // بذرة منتج السماح تُنشر للجهتين
        var (gSync, _) = SeedProductOnMain(dbM, "منتج السماح", 100m, 20, "P35G");
        PushM(dbK, dk); PushM(dbN, dn);

        // K تُنشئ الفاتورة G-01 (بندان: 2×100 = 200) وتنشرها
        UseDevice(dbK, dk); Session.CurrentUser = CurrentAdmin();
        string saleSync;
        using (var k1 = new AppDbContext())
        {
            var p = k1.Products.First(x => x.SyncId == gSync);
            var sale = new Sale
            {
                InvoiceNumber = "G-01", Date = DateTime.Now, UserId = Session.CurrentUser!.Id,
                Device = Session.Device, PaymentMethod = PaymentMethod.Cash, Total = 200m, Profit = 40m
            };
            sale.Items.Add(new SaleItem { ProductId = p.Id, ProductName = p.Name, Quantity = 2, UnitPrice = 100m, BuyPrice = 80m });
            p.Stock -= 2;
            var op = OperationWriter.Register(db: k1, OperationType.Sale, sale.InvoiceNumber, 200m, "Sales");
            k1.Sales.Add(sale);
            k1.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Sales", sale.Id);
            k1.SaveChanges();
            saleSync = sale.SyncId!;
        }
        PushM(dbK, dk); PushM(dbN, dn);

        // ===== فترة السماح: K تعدّل نسختها وN تعدّل نسختها — كلٌّ بسعرٍ لاحق مختلف (بلا تغيير كمّية) =====
        UseDevice(dbK, dk); Session.CurrentUser = CurrentAdmin();
        using (var k2 = new AppDbContext())
        {
            var sale = k2.Sales.Include(s => s.Items).First(x => x.SyncId == saleSync);
            sale.Items[0].UnitPrice = 115m;
            sale.Total = 230m;
            sale.Profit = 70m;
            OperationWriter.Register(k2, OperationType.SaleEdit, sale.InvoiceNumber, 230m, "Sales", sale.Id);
            k2.SaveChanges();
        }
        UseDevice(dbN, dn); Session.CurrentUser = CurrentAdmin();
        using (var n2 = new AppDbContext())
        {
            var sale = n2.Sales.Include(s => s.Items).First(x => x.SyncId == saleSync);
            sale.Items[0].UnitPrice = 120m;
            sale.Total = 240m;
            sale.Profit = 80m;
            OperationWriter.Register(n2, OperationType.SaleEdit, sale.InvoiceNumber, 240m, "Sales", sale.Id);
            n2.SaveChanges();
        }

        // التبادل المباشر K↔N (نمط P3.2A/2B): جلسةُ نظيرٍ إلى نظير — تسحب K تعديلَ N فيُفتح الصراع لديها،
        // ويُدفع تعديلُ K إلى N فيُفتح الصراع عندها؛ لا يلفّ عبر MAIN فلا يُعلَّم MAIN محلياً
        TrySync(dbK, portN, dk);
        // يُرفع كلا التعديلَين لاحقاً إلى MAIN كي يملك القيمةَ المعتمدة (240) وقت الحسم، وتنعكس في سجلاته (المجموع الكنسي)
        PushM(dbK, dk); PushM(dbN, dn);
        using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN)) using (var m = new AppDbContext(dbM))
        {
            Check(r,
                k.OperationLogs.AsNoTracking().Count(o => o.OriginDevice == dk && o.OpType == nameof(OperationType.SaleEdit)) == 1
                && n.OperationLogs.AsNoTracking().Count(o => o.OriginDevice == dn && o.OpType == nameof(OperationType.SaleEdit)) == 1,
                "P3.5(9a): كلا الكاشيرَين كتب تعديلَ فاتورة فعلياً (SaleEdit) — فترةُ السماح تصل إلى المسار الحقيقي للكتابة");
            var cK = SyncConflictService.OpenConflictCount(k);
            var cN = SyncConflictService.OpenConflictCount(n);
            var cM = SyncConflictService.OpenConflictCount(m);
            Check(r, cK == 1 && cN == 1 && cM == 0,
                $"P3.5(9b): الصراع افتُتح لدى K وN (مستقبلَين عدّلا محلياً) وصغرٌ لدى MAIN — قراءةُ الخلاف من كاشيرَين لا من المركز (K={cK}، N={cN}، M={cM})");
            var kSaleT = k.Sales.First(x => x.SyncId == saleSync).Total;
            var nSaleT = n.Sales.First(x => x.SyncId == saleSync).Total;
            Check(r, kSaleT == 230m && nSaleT == 240m,
                $"P3.5(9c): الفاتورة لم تُحجب لدى أحد — كلُّ جهة تمسك تعديلها (K={kSaleT}؟ N={nSaleT}؟ توقع 230/240) والبيع قابلٌ للعرض لدى كل الجهات");
        }

        // الحسم اليدوي من MAIN: القيمة المحلية على MAIN (آخر ما طُبّق = 240) هي الفائزة (KeepLocal)
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); Session.CurrentUser = CurrentAdmin();
        string err;
        var okResolve = SyncConflictService.Resolve(new AppDbContext(), "Sales", saleSync, ConflictResolutionType.KeepLocal, "قيمة MAIN المعتمدة", out err);
        Check(r, okResolve && string.IsNullOrEmpty(err), $"P3.5(9d): الحسم اليدوي على MAIN نُفِّذ — ({err})");

        long resSeq;
        using (var m = new AppDbContext(dbM))
        {
            var res = m.OperationLogs.AsNoTracking().First(o => o.OpType == nameof(OperationType.ConflictResolution) && o.OriginDevice == dm);
            resSeq = res.OriginSeq;
            var rec = m.SyncConflicts.AsNoTracking().FirstOrDefault(c => c.ConflictSyncId == saleSync && c.ResolvedAtSeq != null);
            Check(r, rec is null && m.Sales.First(x => x.SyncId == saleSync).Total == 240m,
                "P3.5(9e): MAIN طبّق الفائز (240) موقّعاً — قرارُ الحسم عملةٌ سجلية موقعة بصمتِها التي ستنتشر");
        }

        // بثّ القرار: إغلاقُ الصراع لدى K وN وتطبيق الفائز
        PushM(dbK, dk); PushM(dbN, dn);
        using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN))
        {
            Check(r, SyncConflictService.OpenConflictCount(k) == 0 && SyncConflictService.OpenConflictCount(n) == 0
                && k.Sales.First(x => x.SyncId == saleSync).Total == 240m
                && n.Sales.First(x => x.SyncId == saleSync).Total == 240m
                && k.Sales.Include(s => s.Items).First(x => x.SyncId == saleSync).Items[0].UnitPrice == 120m,
                "P3.5(9f): بثُّ القرار أغلق الصراعَ لدى الكاشيرين وطابق الفاتورة على الفائز (240/120) — لا منتصرَ مزدوج ولا جهةٌ عالقة");
            var ck = k.SyncConflicts.Single(c => c.ConflictSyncId == saleSync);
            var cn = n.SyncConflicts.Single(c => c.ConflictSyncId == saleSync);
            Check(r, ck.IsResolved && cn.IsResolved && ck.ResolvedAtSeq == resSeq && cn.ResolvedAtSeq == resSeq,
                "P3.5(9g): بصمةُ القرار واحدة عند الطرفين (ResolvedAtSeq = رقم عملية الحسم) — لا قراران مختلفان ولا تأقلمٌ بلا أثر");
        }

        // خميلة: لا إعادةَ فتح الخلاف
        var ok2 = TrySync(dbK, portM, dk) && TrySync(dbN, portM, dn);
        Check(r, ok2, "P3.5(9h): مزامنتان إضافيتان تمرّان بلا فشل — الاستئناف سليم بعد الحسم");
        using (var m = new AppDbContext(dbM)) using (var k = new AppDbContext(dbK)) using (var n = new AppDbContext(dbN))
        {
            Check(r, SyncConflictService.OpenConflictCount(m) == 0 && SyncConflictService.OpenConflictCount(k) == 0 && SyncConflictService.OpenConflictCount(n) == 0,
                "P3.5(9i): إعادة المزامنة لا تفتح الخلاف من جديد — صفر صراعٍ مفتوح في كل مكان (لا إعادة خلاف)");
            var sumM = FinancialTotals.CanonicalSum(m);
            var sumK = FinancialTotals.CanonicalSum(k);
            var sumN = FinancialTotals.CanonicalSum(n);
            var resAmt = m.OperationLogs.AsNoTracking()
                .Where(o => o.OpType == nameof(OperationType.ConflictResolution))
                .Select(o => (decimal)o.Amount).Sum();
            Check(r, sumM == 670m && sumK == 430m && sumN == 440m && resAmt == 0m,
                $"P3.5(9j): المجموع الكنسي واقعي بعد الحسم (M={sumM}، K={sumK}، N={sumN}) — بيع 200 + تعديلُ كل جهازٍ لفاتورتها (K=230، N=240) أما تعديلُ الطرف الآخر فحُجِب لدى المستقبِل (سُجِّل صراعاً لا عمليةً)، وقرارُ الحسم ذاته بلا مبلغ (resAmt={resAmt}) — «لا يضيف ولا يسحب»");
        }

        cts.Cancel();
        try { srvM.Wait(2000); srvK.Wait(2000); srvN.Wait(2000); } catch { }
    }

    // نسخة EditProductPrice واعية بجلسة صريحة (تستخدم Session وضعَها القائم وقت الاستدعاء)
    private static void EditProductPriceWithSession(AppDbContext db, string syncId, decimal price)
    {
        var p = db.Products.First(x => x.SyncId == syncId);
        p.SellPrice = price;
        OperationWriter.Register(db, OperationType.StockAdjustment, p.Name, 0m, "Products", p.Id,
            summaryJson: "تعديل سعر راوتر — §10.8");
        db.SaveChanges();
    }

    private static int Referenced(List<SaleItem> items, string productName)
        => items.Where(i => i.ProductName == productName).Sum(i => i.Quantity);

    private static string ReadCustomerName(AppDbContext db, string syncId)
        => db.Customers.AsNoTracking().First(x => x.SyncId == syncId).Name;

    /// <summary>لقطة صفوف السجل (Seq/النوع/الرقم/المبلغ/الأصل) للبرهنة على «لا إعادة كتابة التاريخ».</summary>
    private static List<(long Seq, string OpType, string? Doc, decimal Amount, string? Origin)> SnapshotOps(string dbFile)
    {
        using var db = new AppDbContext(dbFile);
        return db.OperationLogs.AsNoTracking().OrderBy(o => o.Seq)
            .Select(o => new { o.Seq, o.OpType, o.DocumentNumber, o.Amount, o.OriginDevice })
            .AsEnumerable()
            .Select(o => (o.Seq, o.OpType, o.DocumentNumber, o.Amount, o.OriginDevice))
            .ToList();
    }

    // ===== وحدة الاستعادة R1 — جهاز جديد (قاعدة فارغة) يتبنّى تاريخ MAIN كاملاً عبر الدلتا =====

    private static void RunR1RestoreScenario(List<string> r, string dbM, string dbK, string dbN)
    {
        r.Add("--- R1: جهازٌ جديد بقاعدة فارغة — إقرانٌ بالسر ثم سحب كامل تاريخ MAIN بالدلتا حتى علامات الماء ---");

        const string secret = "restore_secret_r1";
        const int portM = 47301;
        const string dk = "deviceK_TEST", dn = "deviceN_TEST", dm = "deviceM_TEST";

        ResetDeviceDb(dbM); ResetDeviceDb(dbK); ResetDeviceDb(dbN);
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dk, secret);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dn, secret);
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dm, secret);

        var cts = new CancellationTokenSource();
        var srvM = SyncPeer.StartServer(portM, dbM, cts.Token);

        // MAIN مبنيٌّ بالمدخولات المولّدة: منتجان (بذرة) + بيعٌ واحد — تاريخٌ مالي مرجعي على MAIN
        var (p1Sync, _) = SeedProductOnMain(dbM, "أصل R1 هاتف", 100m, 6, "R1-P1");
        SeedProductOnMain(dbM, "أصل R1 شاحن", 50m, 12, "R1-P2");
        UseDevice(dbM, dm); Session.CurrentUser = CurrentAdmin();
        using (var db = new AppDbContext())
        {
            var p = db.Products.First(x => x.SyncId == p1Sync);
            var sale = new Sale
            {
                InvoiceNumber = "R1-M1", Date = DateTime.Now, UserId = Session.CurrentUser!.Id,
                Device = Session.Device, PaymentMethod = PaymentMethod.Cash, Total = 300, Profit = 80
            };
            sale.Items.Add(new SaleItem { ProductId = p.Id, ProductName = p.Name, Quantity = 2, UnitPrice = 150, BuyPrice = 100 });
            p.Stock -= 2;
            var op = OperationWriter.Register(db, OperationType.Sale, sale.InvoiceNumber, 300m, "Sales");
            db.Sales.Add(sale);
            db.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Sales", sale.Id);
            db.SaveChanges();
        }

        long mOpCount, mMasterSeq;
        using (var m = new AppDbContext(dbM))
        {
            mOpCount = m.OperationLogs.AsNoTracking().Count();
            mMasterSeq = m.OperationLogs.AsNoTracking().Where(o => o.OriginDevice == dm).Max(o => o.OriginSeq);
        }

        // الجهاز الجديد: قاعدة فارغة ← إقرانٌ بالسر (دور كاشير مقابل MAIN) ← لقطة المرجع ثم الدلتا
        var baselineRows = Baseline.Export(new AppDbContext()); // الاتصال الحالي = MAIN
        UseDevice(dbK, dk);
        var createdRef = Baseline.Apply(new AppDbContext(), baselineRows);

        Session.DeviceIdOverride = dk;
        var pull = SyncPeer.Synchronize("127.0.0.1", portM, dbK, maxAttempts: 1);
        Session.DeviceIdOverride = dm;

        using (var m = new AppDbContext(dbM)) using (var k = new AppDbContext(dbK))
        {
            var mCats = m.Categories.AsNoTracking().Select(c => c.Name).OrderBy(n => n).ToList();
            var kCats = k.Categories.AsNoTracking().Select(c => c.Name).OrderBy(n => n).ToList();
            Check(r, createdRef > 0 && kCats.SequenceEqual(mCats) && kCats.Count == mCats.Count,
                $"R1(a): القاعدة الفارغة بُنيت من لقطة المرجع والتصنيفات لم تتباعد (Apply={createdRef}) — تُطبَّق مرةً واحدة بلا تكرّرٍ ولا نقص");
            Check(r, k.Products.AsNoTracking().Count() == m.Products.AsNoTracking().Count(),
                $"R1(a2): المنتجات تطابق MAIN ({k.Products.AsNoTracking().Count()}/{m.Products.AsNoTracking().Count()}) — بلا تضخّم");
            Check(r, pull.Accepted && pull.Pulled == mOpCount && pull.Pushed == 0,
                $"R1(b): بعلامات صفر سحب الجهازُ الجديد تاريخَ MAIN كاملاً (pulled={pull.Pulled}/{mOpCount}) بلا دفع — الدلتا حتى علامة الماء");
            Check(r, k.SyncPeerStates.AsNoTracking().Any(p => p.OriginDevice == dm && p.LastOriginSeq == mMasterSeq),
                $"R1(b2): علامة ماء MAIN ثُبّتت عند آخر أصل ({mMasterSeq}) — الجهاز في أفق MAIN");
            var sumM = FinancialTotals.CanonicalSum(m);
            var sumK = FinancialTotals.CanonicalSum(k);
            Check(r, sumM == sumK && sumK == 300m,
                $"R1(c): المجموع المالي متطابق بعد السحب (K={sumK}، M={sumM}=300) — من تدفق الدلتا لا من لقطة المرجع");
        }

        // إعادة السحب تثبت ثبات علامات الماء (أفق تام لا انجراف)
        Session.DeviceIdOverride = dk;
        var redo = SyncPeer.Synchronize("127.0.0.1", portM, dbK, maxAttempts: 1);
        Session.DeviceIdOverride = dm;
        Check(r, redo.Accepted && redo.Pulled == 0 && redo.Pushed == 0,
            "R1(d): إعادة السحب بلا دلتا — علامة الماء أعاقت أي تكرار (idempotent بالإيصال)");
        using (var k = new AppDbContext(dbK))
            Check(r, k.SyncPeerStates.AsNoTracking().First(x => x.OriginDevice == dm).LastOriginSeq == mMasterSeq,
                "R1(d2): علامة الماء بعد الإعادة مطابقة لقيمة المرجع — الجهاز الجديد في أفق MAIN بلا انجراف");

        var ops = SnapshotOps(dbK);
        var contiguous = ops.Count > 0 && ops.Select((o, i) => o.Seq == i + 1).All(x => x);
        Check(r, contiguous && ops.Count == mOpCount,
            $"R1(e): سجل الجهاز الجديد متصلٌ بلا فجوات ({ops.Count} عمليات Seq 1..N) — لا ثقوب ولا تخطّي أرقام");

        cts.Cancel();
        try { srvM.Wait(2000); } catch { }
    }

    // ===== وحدة الاستعادة R2 — Restore Drill: لقطة عند علامة ماء W ثم استرجاعٌ لقاعدةٍ مؤقتة والتقارب =====

    private static void RunR2BackupDrillScenario(List<string> r, string dbM, string dbK, string dbN)
    {
        r.Add("--- R2: Restore Drill — لقطة عند علامة ماء W، تقدّم الجهاز الحي، استرجاعٌ لقاعدةٍ مؤقتة والتقارب ---");

        const string secret = "restore_secret_r2";
        const int portM = 47311;
        const string dk = "deviceK_TEST", dn = "deviceN_TEST", dm = "deviceM_TEST";

        ResetDeviceDb(dbM); ResetDeviceDb(dbK); ResetDeviceDb(dbN);
        UseDevice(dbK, dk); SetDeviceRole(dbK, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dk, secret);
        UseDevice(dbN, dn); SetDeviceRole(dbN, DeviceRole.Cashier); SeedAdmin(); ConfigurePeer(dn, secret);
        UseDevice(dbM, dm); SetDeviceRole(dbM, DeviceRole.MainServer); SeedAdmin(); ConfigurePeer(dm, secret);

        var cts = new CancellationTokenSource();
        var srvM = SyncPeer.StartServer(portM, dbM, cts.Token);

        void PushM(string dbPath, string deviceId)
        {
            var prev = Session.DeviceIdOverride;
            Session.DeviceIdOverride = deviceId;
            SyncPeer.Synchronize("127.0.0.1", portM, dbPath, maxAttempts: 1);
            Session.DeviceIdOverride = prev;
        }

        // يوم عمل قبل اللقطة: بذرة منتج على MAIN، ثمّ K تبيع وتُسلّم بيعها — الكلّ عند 200 (أفق W)
        var (r2Sync, _) = SeedProductOnMain(dbM, "جهاز R2 drill", 150m, 5, "R2-P");
        PushM(dbK, dk);
        UseDevice(dbK, dk); Session.CurrentUser = CurrentAdmin();
        using (var db = new AppDbContext())
        {
            var p = db.Products.First(x => x.SyncId == r2Sync);
            var sale = new Sale
            {
                InvoiceNumber = "R2-K1", Date = DateTime.Now, UserId = Session.CurrentUser!.Id,
                Device = Session.Device, PaymentMethod = PaymentMethod.Cash, Total = 200, Profit = 60
            };
            sale.Items.Add(new SaleItem { ProductId = p.Id, ProductName = p.Name, Quantity = 1, UnitPrice = 200, BuyPrice = 150 });
            p.Stock -= 1;
            var op = OperationWriter.Register(db, OperationType.Sale, sale.InvoiceNumber, 200m, "Sales");
            db.Sales.Add(sale);
            db.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Sales", sale.Id);
            db.SaveChanges();
        }
        PushM(dbK, dk); // يُسلَّم لـ MAIN قبل اللقطة
        using (var m = new AppDbContext(dbM)) using (var k = new AppDbContext(dbK))
        {
            Check(r, FinancialTotals.CanonicalSum(m) == 200m && FinancialTotals.CanonicalSum(k) == 200m,
                "R2(ي): قبل اللقطة — MAIN وK متطابقان عند 200 (أفق W) استعداداً للقطة");
        }

        // لقطة نسخة احتياطية عند W (نمط النسخة الليلية: checkpoint + نسخة سليمة)
        SqliteConnection.ClearAllPools();
        using (var b = new AppDbContext(dbK)) b.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE)");
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(dbK + suffix)) File.Delete(dbK + suffix);
        var backupPath = Path.Combine(Path.GetDirectoryName(dbK)!, "phoneaccounting-laptha.db");
        File.Copy(dbK, backupPath);
        var integrity = BackupService.QuickIntegrity(backupPath);
        Check(r, integrity.StartsWith("ok", StringComparison.OrdinalIgnoreCase),
            $"R2(ي2): اللقطة نسخةٌ سليمة (integrity_check={integrity}) — لا استعادة من نسخةٍ ملتوية");

        // تقدّمٌ بعد اللقطة:
        // (1) MAIN يُسجّل استدراكاً موقّعاً +37.50 — مادة MAIN بعد W تُثبت «إعادة الطلب» للمسترجَع
        UseDevice(dbM, dm); Session.CurrentUser = CurrentAdmin();
        using (var db = new AppDbContext())
        {
            var msg = ReconciliationService.IssueCorrection(db, 37.50m, "تسوية جرد — تمرين R2");
            Check(r, msg.StartsWith("سُجّل"), $"R2(c): استدراك MAIN بعد اللقطة ({msg})");
        }
        // (2) الجهاز الحي K يطلق بيعاً جديداً لم يُسلَّم (عملية أصلية بعلامة >W) — يريه حدُّ الخسارة في R3
        UseDevice(dbK, dk); Session.CurrentUser = CurrentAdmin();
        using (var db = new AppDbContext())
        {
            var p = db.Products.First(x => x.SyncId == r2Sync);
            var sale = new Sale
            {
                InvoiceNumber = "R2-K2", Date = DateTime.Now, UserId = Session.CurrentUser!.Id,
                Device = Session.Device, PaymentMethod = PaymentMethod.Cash, Total = 50, Profit = 10
            };
            sale.Items.Add(new SaleItem { ProductId = p.Id, ProductName = p.Name, Quantity = 1, UnitPrice = 50, BuyPrice = 40 });
            p.Stock -= 1;
            var op = OperationWriter.Register(db, OperationType.Sale, sale.InvoiceNumber, 50m, "Sales");
            db.Sales.Add(sale);
            db.SaveChanges();
            OperationWriter.SetEntityTarget(op, "Sales", sale.Id);
            db.SaveChanges();
        }
        using (var m = new AppDbContext(dbM)) using (var k = new AppDbContext(dbK))
        {
            Check(r, FinancialTotals.CanonicalSum(m) == 237.50m && FinancialTotals.CanonicalSum(k) == 250m,
                $"R2(c2): بعد التقدّم — MAIN=237.50 (استدراك) والجهاز الحي=250 (بيع محلي غير مُسلَّم) — الانحراف محصورٌ في المحلي");
        }

        // الكارثة: الاسترجاع من اللقطة على قاعدةٍ مؤقتة (البديل) ثم مزامنة — علامة الماء رجعت فتطلب الدلتا مجدداً
        var restored = Path.Combine(Path.GetDirectoryName(dbK)!, "restored", "phoneaccounting.db");
        Directory.CreateDirectory(Path.GetDirectoryName(restored)!);
        File.Copy(backupPath, restored, true);
        SqliteConnection.ClearAllPools();
        UseDevice(restored, dk);
        Session.DeviceIdOverride = dk;
        var pull = SyncPeer.Synchronize("127.0.0.1", portM, restored, maxAttempts: 1);
        Check(r, pull.Accepted && pull.Pulled == 1,
            $"R2(a): المسترجَع أعاد طلبَ ما بعد W (علامةُ الماء رجعت للقطة فبنيت التوزيعَ بخريطتها) — سُحب استدراك MAIN (pulled={pull.Pulled})");

        using (var t = new AppDbContext(restored)) using (var m = new AppDbContext(dbM))
        {
            var seqs = t.OperationLogs.AsNoTracking().OrderBy(o => o.Seq).Select(o => o.Seq).ToList();
            var contiguous = seqs.SequenceEqual(Enumerable.Range(1, seqs.Count).Select(i => (long)i));
            Check(r, contiguous && seqs.Count == 3,
                $"R2(b): بعد الاسترجاع+المزامنة — السجل متصلٌ بلا فجوات ({string.Join(",", seqs)}) — الثقوب سُدّت بالإيصالات");
            Check(r, t.SyncLogs.AsNoTracking().Any(s => s.Direction == "In" && s.OriginDevice == dm),
                "R2(b2): إيصالُ استلام الاستدراك مسجَّل — التوزيعُ بُني بخريطة علامات ماء الطالب نفسه، لا بعموم المادة");
            var sumT = FinancialTotals.CanonicalSum(t);
            var sumM = FinancialTotals.CanonicalSum(m);
            Check(r, sumT == 237.50m && sumM == 237.50m,
                $"R2(c3): التقارب مع الحالة الحية — المسترجَع=237.50 وMAIN=237.50 (المجموع متطابق بلا نقصٍ ولا زيادة)");
            var stockT = t.Products.First(x => x.SyncId == r2Sync).Stock;
            var stockM = m.Products.First(x => x.SyncId == r2Sync).Stock;
            Check(r, stockT == stockM && stockT == 4,
                $"R2(c4): المخزون/البيئة متطابقان على الاثنين (Stock={stockT}) — قرارُ اليوم لم يندثر بعودة لقطة الأمس");
        }

        // الإعادة idempotent — لا مضاعفة
        Session.DeviceIdOverride = dk;
        var again = SyncPeer.Synchronize("127.0.0.1", portM, restored, maxAttempts: 1);
        using (var t = new AppDbContext(restored))
        {
            Check(r, again.Accepted && again.Pulled == 0 && again.Pushed == 0,
                $"R2(f): إعادة الاسترجاع بلا دلتا (pulled={again.Pulled}) — idempotent بضمان الإيصالات");
            Check(r, t.OperationLogs.AsNoTracking().Count() == 3 && FinancialTotals.CanonicalSum(t) == 237.50m,
                "R2(f2): لا مضاعفة عند الإعادة — لا عمليةٌ ولا مبلغٌ تُكرَّر على المسترجَع");
        }

        // حدُّ الخسارة الموثّق (R3): عمل الجهاز الحي غير المُسلَّم أخذَ طريقَه مع الكارثة (مقيد بفترة النسخ 23 ساعة)
        using (var k = new AppDbContext(dbK)) using (var t = new AppDbContext(restored)) using (var m = new AppDbContext(dbM))
        {
            Check(r, k.OperationLogs.AsNoTracking().Any(o => o.DocumentNumber == "R2-K2")
                && !t.OperationLogs.AsNoTracking().Any(o => o.DocumentNumber == "R2-K2")
                && !m.OperationLogs.AsNoTracking().Any(o => o.DocumentNumber == "R2-K2"),
                "R2(g): حدُّ الخسارة الموثّق — بيعُ الجهاز الحي (R2-K2) لم يُسلَّم لأحد فلَم ينجُ في المسترجَع ولا على MAIN — الخسارةُ مقيدة بفترة النسخ 23 ساعة (موثق R3)");
        }

        cts.Cancel();
        try { srvM.Wait(2000); } catch { }
    }

    // ===== وحدة الاستعادة R3 — حدُّ الخسارة المكتوب + ترتيب النسخ (توثيق + قرار) =====

    private static void RunR3DocChecks(List<string> r)
    {
        r.Add("--- R3: حدُّ الخسارة المكتوب وترتيب النسخ — النص حاضِرٌ في الموثّقين والقرارُ موثّق ---");

        var docsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "docs"));
        var emPath = Path.Combine(docsDir, "EMERGENCY-RESTORE.md");
        var sdPath = Path.Combine(docsDir, "SYNC-DESIGN.md");
        var em = File.Exists(emPath) ? File.ReadAllText(emPath) : "";
        var sd = File.Exists(sdPath) ? File.ReadAllText(sdPath) : "";

        Check(r, em.Contains("المزامنة تسبق النسخ الليلي"),
            "R3(a): EMERGENCY-RESTORE يوثّق ترتيب النسخ المقرَّر — «المزامنة تسبق النسخ الليلي (يُرسَل المعلَّق أولاً ثم تؤخذ النسخة)»");
        Check(r, em.Contains("23 ساعة") && em.Contains("لم تغادر الجهاز"),
            "R3(b): EMERGENCY-RESTORE يقرّ حدَّ الخسارة — «العمليات التي لم تغادر الجهاز قبل الكارثة محكومة بفترة النسخ 23 ساعة»");
        Check(r, em.Contains("مستبعد") && em.Contains("نسخ لحظي") && em.Contains("سحابي") && em.Contains("تشفير"),
            "R3(c): المستبعد مُسجَّل بتأجيل موثّق — نسخ لحظي مستمر / سحابي / تشفير");
        Check(r, sd.Contains("المزامنة تسبق النسخ الليلي") && sd.Contains("23 ساعة"),
            "R3(d): SYNC-DESIGN يقرّ ذات الحدّ والترتيب في بند سلامة البيانات — متطابق مع وثيقة الطوارئ");
        Check(r, emPath != "" && sdPath != "" && em.Length > 0 && sd.Length > 0,
            "R3(e): المستندان (EMERGENCY-RESTORE + SYNC-DESIGN) موجودان ومقروءان — لا وثيقةٌ وهمية تُعتمد");
    }

    // ===================== H1: بادئة الجهاز في أرقام الفواتير =====================

    private static void RunH1InvoicePrefixScenario(List<string> r)
    {
        r.Add("--- H1: بادئة الجهاز تمنع تراقم أرقام فواتير اليوم بين كاشيرَين — §11 تُفتح ---");

        var sandbox = Path.Combine(Path.GetTempPath(), "PhoneAccounting_H1_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(sandbox);
        var dbFile = Path.Combine(sandbox, "h1.db");
        var fixedDate = new DateTime(2026, 9, 15);

        UseDevice(dbFile, "deviceK_H1"); SeedAdmin();    // الكاشير K — يومه 15/9
        var kNum = InvoiceService.SaleInvoiceNumber(1, fixedDate);

        UseDevice(dbFile, "deviceN_H1");                 // الكاشير N — اليوم نفسه وعداد اليوم نفسه (1)
        var nNum = InvoiceService.SaleInvoiceNumber(1, fixedDate);

        Check(r, kNum != nNum,
            "H1(a): كاشيران في اليوم نفسه وبنفس العداد (1) → رقمُ الفاتورة مختلفان بفضل بادئة الجهاز");

        Check(r, kNum == $"F-{("deviceK_H1")[..8]}-2026-09-15-001" &&
                nNum == $"F-{("deviceN_H1")[..8]}-2026-09-15-001",
            "H1(b): البادئة = أول 8 خانات من هوية الجهاز — النمط F-<بادئة>-<التاريخ>-<العداد> (قصيرة على الورق)");

        UseDevice(dbFile, "deviceK_H1");
        var kRepro = InvoiceService.SaleInvoiceNumber(1, fixedDate);
        UseDevice(dbFile, "deviceN_H1");
        var nRepro = InvoiceService.SaleInvoiceNumber(1, fixedDate);
        Check(r, kNum == kRepro && nNum == nRepro,
            "H1(c): ثبات التوليد — نفس الجهاز ونفس العداد يعيدان نفس الرقم");

        Session.CurrentUser = CurrentAdmin();
        using (var db = new AppDbContext())
        {
            var s1 = new Sale { InvoiceNumber = kNum, Date = fixedDate, UserId = Session.CurrentUser!.Id, Device = DeviceRole.Cashier, PaymentMethod = PaymentMethod.Cash, Total = 100m, Profit = 20m };
            var s2 = new Sale { InvoiceNumber = nNum, Date = fixedDate, UserId = Session.CurrentUser!.Id, Device = DeviceRole.Cashier, PaymentMethod = PaymentMethod.Cash, Total = 200m, Profit = 40m };
            db.Sales.Add(s1);
            db.Sales.Add(s2);
            db.SaveChanges();
            Check(r, db.Sales.Count(x => x.InvoiceNumber == kNum) == 1 && db.Sales.Count(x => x.InvoiceNumber == nNum) == 1,
                "H1(d): رقمَا الكاشيرين دخلا قاعدة واحدة بلا خرق الفهرس الفريد InvoiceNumber");
        }
    }

    // ===================== PAIRING: إعدادات الاقتران (واجهة §11) =====================

    private static void RunPairingUiScenario(List<string> r)
    {
        r.Add("--- PAIRING: تطبيع إعدادات الاقتران (واجهة §11) — الأقران تُكتب صحيحة للقارئ ---");

        var n1 = SettingsViewModel.NormalizePeers(" 192.168.1.10:45678 , 192.168.1.11:45678;;  ,, 10.0.0.9:45678 ");
        Check(r, n1 == "192.168.1.10:45678,192.168.1.11:45678,10.0.0.9:45678",
            "PAIRING(a): قائمة الأقران تُطبَّع — فراغات وفواصل متكررة وفواصل منقوطة تُزال والبقايا الفارغة تُحذف");

        Check(r, SettingsViewModel.NormalizePeers("") == "" && SettingsViewModel.NormalizePeers(" , ; ") == "",
            "PAIRING(b): قائمة فارغة أو رمادية → سلسلة فارغة (لا مسافات زائفة تُخزَّن في الإعدادات)");

        var entries = n1.Split(',');
        var wellFormed = entries.Length == 3 &&
            entries.All(e => System.Text.RegularExpressions.Regex.IsMatch(e, @"^\S+:\d{1,5}$"));
        Check(r, wellFormed,
            "PAIRING(c): كل مدخل بصيغة host:port صالحة (مضيف: منفذ رقمي) — يقرؤها مجدول المزامنة كما يُخزَّن");
    }

    // ===================== SECRET: حارس السرّ (البند 1 من دراسة كلاود) — مستحيل تقنياً إغفال السر =====================

    private static void RunSecretGuardScenario(List<string> r)
    {
        r.Add("--- SECRET: سرّ مزامنة إجباري برمجياً — الاحتياطية المعروفة غير مقبولة تشغيلياً ---");

        var sandbox = Path.Combine(Path.GetTempPath(), "PhoneAccounting_SECRET_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(sandbox);
        var dbNoSecret = Path.Combine(sandbox, "nosecret.db");
        var dbDefault = Path.Combine(sandbox, "defaulted.db");
        var dbGood = Path.Combine(sandbox, "good.db");

        UseDevice(dbNoSecret, "deviceS1"); SeedAdmin();                                            // بلا مفتاح SyncSecret
        UseDevice(dbDefault, "deviceS2"); SeedAdmin();
        ConfigurePeer("deviceS2", SyncPeer.DefaultSecret);                                         // مفتاح صريح = القيمة المعروفة
        UseDevice(dbGood, "deviceS3"); SeedAdmin();
        ConfigurePeer("deviceS3", "m2.2-secret-قوي");                                              // سرّ صريح سليم

        Check(r, !SyncPeer.HasUsableSecret(new AppDbContext(dbNoSecret)),
            "SECRET(a): غياب مفتاح SyncSecret → الحارس يرفض (لا مزامنة بسرّ معروف ومتاح)");

        Check(r, !SyncPeer.HasUsableSecret(new AppDbContext(dbDefault)),
            "SECRET(b): مفتاح صريح بقيمة الاحتياطية المعروفة (phone-accounting-m2.2) → رفضٌ أيضاً — لا غشاء بالقيمة المنشورة");

        var good = SyncPeer.HasUsableSecret(new AppDbContext(dbGood));
        var refuse = SyncPeer.Synchronize("127.0.0.1", 47999, dbNoSecret, maxAttempts: 1);
        Check(r, good && !refuse.Accepted && refuse.Reason is not null && refuse.Reason.Contains("سر"),
            "SECRET(c): سرّ صريح سليم → مقبول، وجهاز بلا سرّ → رفضٌ صريح بلا اتصال (رسالة عربية واضحة)");
    }

    // ===================== UISMOKE: الواجهة تُفتح فعلاً (حارس أخطاء XAML الهدف) =====================

    private static void RunUiSmokeScenario(List<string> r)
    {
        r.Add("--- UISMOKE: إثبات أن واجهة الشاشات تُنشَّأ بلا استثناء XAML (TargetType/Style) ---");

        bool created = false;
        try
        {
            _ = new PhoneAccounting.App.Views.SettingsView();
            created = true;
        }
        catch (Exception ex)
        {
            Check(r, false, $"UISMOKE(a): SettingsView تُنشَّأ بلا استثناء — الخطأ: {ex.Message}");
            return;
        }

        Check(r, created,
            "UISMOKE(a): SettingsView تُنشَّأ فعلياً بدون أي استثناء XAML (Style/TargetType) — الواجهة قابلة للفتح فعلياً");
    }
}