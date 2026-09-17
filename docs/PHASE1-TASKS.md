# قائمة مهام المرحلة 1 — PHASE1-TASKS.md
## تطبيق كاشير — محل تجارة هواتف وصيانة

المرجع: `docs/SYNC-DESIGN.md` (إصدار 1.6). الحالة: **M2.1 محرك الدلتا مكتمل + مُثبت تشغيلياً** — آخر تحديث 2026-09-11.

---

## ترتيب التنفيذ

> الترتيب حاسم: `OperationLog` يجب أن يوجد **قبل** `CloseDay` و`StockAdjustment`،
> لأن الإقفال والتسوية **هما أنفسهما عمليات تُسجَّل** في السجل.

### ✅ 1. Migration المرحلة 1
- [x] نموذج `OperationLog` (`Models/OperationLog.cs`) + `OperationType`.
- [x] جدول `OperationLogs` + فهارسه (`Data/Database.cs` → `EnsureOperationLogTable`).
- [x] أعمدة `SyncId/OriginDevice/DeletedAt` على كل الجداول المشمولة (`EnsureSyncColumns`، تشمل `Users`).
- [x] `DeviceId` (UUID) لكل جهاز عند أول تشغيل (`Database.SeedSettings` + `Session.DeviceId`).
- [x] فحص ترقية قاعدة DEV قائمة دون حذف (تحقّق فعلي: 163 صنفاً بقيت، جُدّدت الأعمدة فقط).

### ✅ 2. مسار الكتابة الموحّد + سجل العمليات
- [x] `Services/OperationWriter.cs` (`Register` يمنح Seq عند Commit ويربط هوية المستخدم/الدور/الجهاز).
- [x] `Session.DeviceId`.
- [x] **تعيين SyncId/OriginDevice عند الإنشاء مركّزاً** في `AppDbContext.Save*` (`AssignSyncFields`) — أي صف جديد من أي مسار كتابة يُعطى SyncId تلقائياً (تحقّق فعلي: 163 صنفاً جديدة كلها حصلت SyncId + OriginDevice).
- [x] مواضع الكتابة "العمليات التجارية" تُسجَّل سطراً في السجل عبر `Register`:
  - [x] `PosViewModel.SaveSale` (بيع + تعديل بيع) — `PosViewModel.cs`
  - [x] `PurchaseEditViewModel.Save` — `PurchaseEditViewModel.cs`
  - [x] `PosViewModel.Purchase.SavePurchase` — `PosViewModel.Purchase.cs`
  - [x] `PosViewModel.Voucher.SaveVoucher` — `PosViewModel.Voucher.cs`
  - [x] `RepairReceiveViewModel.Save` — `RepairReceiveViewModel.cs`
  - [x] `RepairJobEditViewModel.Save` (RepairPartUsage عند قطع، RepairJob عند حالة فقط)
- [x] الصفوف المرجعية (منتجات/زبائن/موردون/موظفون) تحصل SyncId تلقائياً عبر `AssignSyncFields`؛ سجل «دخول» خفيف لها مؤجَّل (لا يمنع المزامنة).

### ✅ 3. CloseDay + قفل الاعتماد
- [x] `Services/CloseDayService.cs` (تنفيذ إقفال + `IsLocked` + `CanClose`).
- [x] بوابات التعديل: `PosViewModel.SaveSale`، `PurchaseEditViewModel.Save`،
      `InvoiceDetailsViewModel.EditInvoice` (حجب فتح النوافذ).
- [x] واجهة الإقفال في `SettingsView` (زر DangerButton + `LastCloseDateText` + `CanCloseDay`).

### ✅ 4. عمليات التصحيح بعد الإقفال (Void/استرجاع + StockAdjustment + تدقيق)
- [x] `Services/CorrectionService.cs`:
  - [x] `VoidSale`: إلغاء فاتورة بيع (لا حذف) — إعادة الكمية للمخزون + عكس دين الزبون + حذف ناعم (`DeletedAt`) + سطر `SaleVoid`.
  - [x] `ReturnPurchase`: مرتجع شراء — خصم الكمية + عكس رصيد المورد + حذف ناعم + سطر `PurchaseReturn`.
  - [x] شرطان: «لا تُلغى مرتين» (تفحّص سجل)، والسبب إجباري، والصلاحية للمدير/المحاسب.
- [x] `StockAdjustment`: في `ProductsViewModel.SaveProduct` — تغيير كمية منتج قائم يتطلب سبباً إجبارياً عبر `InputDialog` + سطر `StockAdjustment` بنفس الحفظ.
- [x] واجهات: زر «إلغاء الفاتورة»/«مرتجع شراء» في `InvoiceDetailsWindow` (حسب نوع الفاتورة) + رسالة النتيجة.
- [x] عرض «سجل العمليات» (آخر 50) — `Views/AuditLogWindow` + زرّ في `SettingsView`.

### ⏳ 5. تحقق نهائي (تجارب يدوية — جاهزة الآن بالبرنامج)
- [x] بناء 0/0 و 0 تحذيرات (تحقّق متكرر بعد كل دفعة؛ آخر دفعة نجحت).
- [x] استثناء الفواتير الملغاة/المرتجعة من كل التقارير والرئيسية وكشف العملاء والفاتورة المشمولة (مرشحات `DeletedAt == null` في `ReportsViewModel`/`HomeViewModel`/`CustomersViewModel`/`PosViewModel`).
- [x] **تحصين الأمان (طبقة الهوية)** + تحقّق تشغيلي على القاعدة الحقيقية (جدول `LoginLogs` + عمود `MustChangePassword` + وسم كل الحسابات الافتراضية).
- [~] تجربة: بيع → إقفال اليوم → محاولة تعديل الفاتورة → ممنوعة (بوابة `IsLocked` تعمل برمجياً؛ اليدوي للتأكيد).
- [~] تجربة: تعديل بيع في فترة السماح (قبل الإقفال) → مسموح ومسجَّل `SaleEdit`.
- [~] تجربة: بيع → `Void` → إعادة الكمية للمخزون + سطر `SaleVoid` + منع الإلغاء مرة ثانية + اختفاؤه من التقارير.
- [~] تجربة: شراء → `PurchaseReturn` → خصم الكمية + سطر `PurchaseReturn` + اختفاؤه من المشتريات.
- [~] تجربة: تغيير كمية منتج → سبب إجباري → سطر `StockAdjustment` يظهر في «سجل العمليات» (زر في الإعدادات).

**مسار التجارب**: شغّل `bin\Debug\net10.0-windows\PhoneAccounting.exe` → تسجيل الدخول **يُجبر المدير على تغيير كلمة المرور للمرة الأولى** (كلمات «1234»/«admin» الافتراضية تُجبر على التغيير بعد أول دخول) → بيع من «نقطة البيع» → «الإعدادات» إقفال اليوم → راجع «سجل العمليات» → زر «تغيير كلمة مروري» لتغيير المرور طوعاً.
**تجربة القفل**: أدخل كلمة مرور خاطئة 5 مرات → الرسالة تُشير إلى قفل مؤقت؛ عد للمحاولة بعد المدة. (المحاولات تُسجَّل في `LoginLogs`).

### ✅ 6. تحصين الأمان (مكتمل 2026-09-11)
- [x] `Models/LoginLog.cs` (سجل محاولات الدخول — محلي لا يُزامَن) + جدوله (`EnsureLoginLogTable`).
- [x] `Services/AuthService.cs`: قفل ضد التخمين (5 فاشلة خلال 10د → قفل 5د، يُحسب من السجل فينجو من إعادة التشغيل) + تسجيل المحاولات + سياسة كلمة المرور (8+ حرف، حرف ورقم).
- [x] `MustChangePassword` على `Users` (`EnsureLoginSecurityColumn`) — يكتشف كلمات «admin»/«1234» الشائعة ويُعلّم الحساب، و`SeedAdmin` يُعلّم البذر إجبارياً.
- [x] `Views/ChangePasswordWindow` + `ViewModels/ChangePasswordViewModel` (تتحقق من كلمة المرور الحالية وتفرض السياسة) — تُفتح **إجبارياً** عند أول دخول بعد كلمة افتراضية، وطوعياً من زر في `SettingsView`.
- [x] تسليم `LoginViewModel`: فحص القفل قبل التحقق + تسجيل كل محاولة + مسار إجبار التغيير (بدون تجاوز إلى الرئيسية).
- [x] سياسة كلمة المرور في `EmployeesViewModel` (كانت 4 خانات → 8 + حرف/رقم).
- [x] إخفاء شاشة «الموظفون» لغير المدير في `MainViewModel` + تقييد «سجل العمليات» في الإعدادات للمدير/المحاسب.
- [x] إزالة تلميح «admin/admin» من شاشة الدخول.

### ✅ 7. سلامة البيانات والنسخ الاحتياطي (مكتمل 2026-09-11)
- [x] `Services/BackupService.cs`: نسخ بـ «VACUUM INTO» (صورة سليمة آمنة مع WAL) + `integrity_check` لكل نسخة (تُحذف التالفة) + الاحتفاظ بآخر 30 + استعادة محمية (فحص سلامة → حارس للقاعدة الحالية → إغلاق الاتصالات → استبدال الملف → إعادة تشغيل إلزامية).
- [x] فحص سلامة عند الإقلاع في `Database.Initialize` — قاعدة تالفة تُرفض (يمكن الاستعادة من النسخ) لا تُفتح.
- [x] نسخ تلقائي عند الإقلاع إن مضى ~23 ساعة على آخر نسخة (`App.OnStartup` → `RunAutoIfDue`).
- [x] زرّ «نسخ احتياطي الآن» + «استعادة نسخة» في `SettingsView` (مدير/محاسب) + عرض آخر نسخة وعددها.
- [x] تحقّق تشغيلي حقيقي: نسخة تلقائية أُنشئت، `integrity_check = ok`، تطابق 163/163 منتجاً مع القاعدة الحية.
- [~] تجربة يدوية: «الإعدادات» → «نسخ احتياطي الآن» → لاحظ الاسم الجديد/العدد → «استعادة نسخة» باختيارها → أعد تشغيل التطبيق وتأكد من بقاء البيانات.
- ملاحظة: البيانات لا «تُنقل» بين الأجهزة في المرحلة 1 بعد — النقل الفعلي (دلتا/مصافحة/خلافات/طبقة تحقق) كله بانتظار **المرحلة 2**.

### ✅ 8. محرك الدلتا المحلي — M2.1 (مكتمل 2026-09-11)
- [x] `Models/SyncLogEntry.cs` (جدول SyncLogs — سجل إرسال/استلام، UNIQUE(DeviceId,Seq) لمنع التكرار، Direction In/Out).
- [x] `Models/OperationLog.cs`: عمود `OriginDevice` (ملكية العملية — منع الإرجاع echo prevention).
- [x] `Data/Database.cs`: `EnsureSyncLogTable` + `EnsureOperationLogTable` (ترقية سلامة الأعمدة).
- [x] `Data/AppDbContext`: `DbSet<SyncLogEntry>` + `TestConnectionString` ثابت للاختبار + ضبط fluent لـ SyncLog.
- [x] `Services/Sync/SyncModels.cs` (new): DeltaRow (T/S/F/R/Origin), JsonScalar, DeltaPacket (metadata + rows list).
- [x] `Services/Sync/DeltaBuilder.cs`: `BuildNew(db, deviceId, excludeOrigin?)` — يبني الحزم من عمليات ما بعد آخر إرسال + `AttachRows` لكل نوع كيان (Sales→Sale+AffectedProducts+CategoryParents، Purchases، Repairs، Vouchers) مع ترتيب الكيانات parents-first + `MarkSent` لتسجيل الإرسال + `excludeOrigin` لمنع الإرجاع.
- [x] `Services/Sync/DeltaApplier.cs`: تطبيق idempotent داخل transaction واحدة لكل حزمة: `RecordReceipt` + `InsertLocalOp` (OriginDevice=remoteDeviceId) + `UpsertRow` لكل كيان مع إعادة ربط FKs عبر `parentKeys` + `EnsureUserForOp` (مستخدم وهمي IsActive=false عند غياب المنفّذ) + `db.SaveChanges()` قبل `Commit()`.
- [x] `Services/Sync/Baseline.cs`: تصدير لقطة جداول المرجع (Categories/Products/Suppliers/Customers) + تطبيق `CreateIfMissing`.
- [x] `Services/Sync/SyncTestHarness.cs` (DEBUG-only بقوة `$env:PHONEACCOUNTING_SYNCTEST`): سيناريو مزدوج على قاعدة مؤقتة — جهاز A (ينفّذ 5 عمليات: Sale+Purchase+StockAdjustment+Sale+SaleVoid) → لقطة مرجعية → تطبيق على B → تحقق B (stock=15, 2 مبيعات نشطة + ملغاة, 5 عمليات + أنواع + مبالغ) → إعادة التسليم idempotent=0 → B تنشئ S3 (دلتا عكسي) → تطبيق B→A → تحقق A (stock=12, 6 عمليات, sum=1250) → echo prevention.
- [x] إثبات تشغيلي: ALL_TESTS_PASSED=TRUE — شامل: تطبيق الدلتا الكامل + المخزون + المبيعات والمرتجعات + المستخدمين الوهميين + الموردين + الملفات المرجعية + منع التكرار + منع الإرجاع + التزامن العكسي.
- [x] **إحياء الشرط المجمَّد 1 (تدقيق كلود)**: منح `Seq` صار عند الـ flush (`AppDbContext.AssignPendingOpSeqs`) داخل نفس `SaveChanges` — لا عند `Register`؛ + مزامنة `OperationLog.DocumentNumber` مع رقم الفاتورة الجديد في حلقة إعادة المحاولة (`PosViewModel.SaveSale`)؛ + 5 فحوصات Regression (حفظ مكرر لا يُستهلك Seq / الناجحة تالية مباشرة / رقم المستند يطابق الفاتورة / Seq متصلة 1..N) — الخروج 0.

---

## مواضع الكتابة المعروفة (مرجع الدمج — 14 موضعاً)
`PosViewModel.SaveSale`, `PosViewModel.Purchase.SavePurchase`, `PosViewModel.Voucher.SaveVoucher`,
`PurchaseEditViewModel.Save`, `RepairJobEditViewModel.Save`, `RepairReceiveViewModel.Save`,
`ProductsViewModel` (8 مواضع)، `CustomersViewModel.SaveNewCustomer`, `EmployeesViewModel` (3 مواضع)،
`Database.cs` (seed فقط — لا يُسجَّل عمليات)، `DemoSeeder` (DEV فقط — لا يُسجَّل).
> كل هذه تصل للحفظ عبر `AppDbContext.SaveChanges` الذي يضمن SyncId للصفوف الجديدة؛ العمليات التجارية تُسجَّل بالإضافة.

## قواعد إلزامية أثناء التنفيذ
1. `Seq` يُمنح عند Commit عبر `OperationWriter.Register` قبل `SaveChanges` — لا يدوياً.
2. عملية واحدة = سطر واحد في السجل (+ ربط EntityId عند الحاجة).
3. لا تعديلات بعد الإقفال إلا عبر عمليات جديدة (تصحيح = Void/مرتجع/تسوية).
4. التصحيح لا يحذف الصف: حذف ناعم `DeletedAt` + عمود `SyncId` باقٍ للمزامنة.
5. أي خرق لوثيقة SYNC-DESIGN في الكود = عيب يُصلَّح، لا يُبرَّر.