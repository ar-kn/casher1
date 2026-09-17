using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using PhoneAccounting.App.Infrastructure;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;

namespace PhoneAccounting.App.Data;

public static class Database
{
    public const int CurrentSchemaVersion = 5;

    public static void Initialize()
    {
        DataPath.Initialize();

        EnsureSchemaUpToDate();

        using var db = new AppDbContext();
        db.Database.EnsureCreated();

        ApplyPragmas();

        var integrity = QuickIntegrityCheck();
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"فحص سلامة قاعدة البيانات فشل (integrity_check = {integrity}). " +
                "لا يجوز فتح قاعدة تالفة — استعد من أحدث نسخة احتياطية ثم أعد تشغيل التطبيق.");
        }

        ApplyLightweightMigrations(db);
        BackfillSubTypes(db);

        SeedAdmin(db);
        SeedSettings(db);
#if DEBUG
        DemoSeeder.Seed(db);
#endif
    }

    /// <summary>ترقيات خفيفة تحافظ على بيانات المستخدم دون حذف القاعدة.</summary>
    private static void ApplyLightweightMigrations(AppDbContext db)
    {
        if (!ColumnExists("Purchases", "UserId"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE Purchases ADD COLUMN UserId INTEGER NOT NULL DEFAULT 0;");
        }

        if (!ColumnExists("Products", "SubType"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE Products ADD COLUMN SubType TEXT NULL;");
        }

        if (!ColumnExists("Sales", "Device"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE Sales ADD COLUMN Device INTEGER NOT NULL DEFAULT 0;");
        }

        if (!ColumnExists("Purchases", "Device"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE Purchases ADD COLUMN Device INTEGER NOT NULL DEFAULT 0;");
        }

        if (!ColumnExists("Customers", "Balance"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE Customers ADD COLUMN Balance TEXT NOT NULL DEFAULT '0';");
        }

        if (!ColumnExists("SaleItems", "Imei"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE SaleItems ADD COLUMN Imei TEXT NULL;");
        }

        if (!TableExists("Vouchers"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS Vouchers (
                    Id INTEGER NOT NULL CONSTRAINT PK_Vouchers PRIMARY KEY AUTOINCREMENT,
                    VoucherNumber TEXT NOT NULL,
                    Date TEXT NOT NULL,
                    CustomerId INTEGER NOT NULL,
                    Amount TEXT NOT NULL,
                    PaymentMethod INTEGER NOT NULL DEFAULT 0,
                    Note TEXT NULL,
                    UserId INTEGER NOT NULL,
                    CONSTRAINT FK_Vouchers_Customers_CustomerId FOREIGN KEY (CustomerId) REFERENCES Customers (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_Vouchers_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
                );
                """);
            db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_Vouchers_VoucherNumber ON Vouchers (VoucherNumber);");
            db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Vouchers_Date ON Vouchers (Date);");
            db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Vouchers_CustomerId ON Vouchers (CustomerId);");
            db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Vouchers_UserId ON Vouchers (UserId);");
        }

        if (!ColumnExists("Vouchers", "PaymentMethod"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE Vouchers ADD COLUMN PaymentMethod INTEGER NOT NULL DEFAULT 0;");
        }

        if (!TableExists("Suppliers"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS Suppliers (
                    Id INTEGER NOT NULL CONSTRAINT PK_Suppliers PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Phone TEXT NULL,
                    Balance TEXT NOT NULL DEFAULT '0',
                    CreatedAt TEXT NOT NULL
                );
                """);
            db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Suppliers_Name ON Suppliers (Name);");
        }

        if (!ColumnExists("Purchases", "SupplierId"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE Purchases ADD COLUMN SupplierId INTEGER NULL;");
        }

        if (!ColumnExists("Purchases", "PaymentMethod"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE Purchases ADD COLUMN PaymentMethod INTEGER NOT NULL DEFAULT 0;");
        }

        if (!ColumnExists("Purchases", "Discount"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE Purchases ADD COLUMN Discount TEXT NOT NULL DEFAULT '0';");
        }

        if (!ColumnExists("Purchases", "Note"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE Purchases ADD COLUMN Note TEXT NULL;");
        }

        EnsureOperationLogTable(db);
        EnsureSyncColumns(db);
        EnsureLoginLogTable(db);
        EnsureLoginSecurityColumn(db);
        EnsureSyncLogTable(db);
        EnsureSyncPeerStateTable(db);
        EnsureSyncConflictsTable(db);
        EnsureConflictAuditLogTable(db);
    }

    /// <summary>سجل الدلتا (إيصالات العملية بهويتها (OriginDevice, OriginSeq) — البند 8.2/15.3). لا يُزامَن هو نفسه (أداة، لا بيانات عمل).</summary>
    private static void EnsureSyncLogTable(AppDbContext db)
    {
        if (!TableExists("SyncLogs"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS SyncLogs (
                    Id INTEGER NOT NULL CONSTRAINT PK_SyncLogs PRIMARY KEY AUTOINCREMENT,
                    DeviceId TEXT NOT NULL,
                    Direction TEXT NULL,
                    OriginDevice TEXT NOT NULL DEFAULT '',
                    OriginSeq INTEGER NOT NULL DEFAULT 0,
                    OpType TEXT NULL,
                    EntityName TEXT NULL,
                    EntitySyncId TEXT NULL,
                    PayloadJson TEXT NULL,
                    CommittedAtUtc TEXT NULL,
                    SentAtUtc TEXT NULL,
                    ReceivedAtUtc TEXT NULL
                );
                """);
        }
        else
        {
            // ترحيل لطيف لقاعدة قائمة (كانت بمفتاح (DeviceId, Seq)):
            // OriginDevice/OriginSeq يُضافان قبل اعتماد الفهارس الجديدة.
            if (!ColumnExists("SyncLogs", "OriginDevice"))
                db.Database.ExecuteSqlRaw("ALTER TABLE SyncLogs ADD COLUMN OriginDevice TEXT NULL;");
            if (!ColumnExists("SyncLogs", "OriginSeq"))
                db.Database.ExecuteSqlRaw("ALTER TABLE SyncLogs ADD COLUMN OriginSeq INTEGER NULL;");
        }

        db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_SyncLogs_DeviceSeq;");
        db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_SyncLogs_OriginUnique ON SyncLogs (OriginDevice, OriginSeq);");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_SyncLogs_Direction ON SyncLogs (Direction);");
    }

    /// <summary>خريطة علامات الماء لكل مصدر (البند 15.2): حد لكل قريب×مصدر يحدد ما تلقاه — أساس delta_request في الانتشارية.</summary>
    private static void EnsureSyncPeerStateTable(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS SyncPeerState (
                Id INTEGER NOT NULL CONSTRAINT PK_SyncPeerState PRIMARY KEY AUTOINCREMENT,
                OwnerDeviceId TEXT NOT NULL,
                OriginDevice TEXT NOT NULL,
                LastOriginSeq INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_SyncPeerState_OwnerOrigin ON SyncPeerState (OwnerDeviceId, OriginDevice);");
    }

    /// <summary>جدول الصراعات الملتقطة (P3.1 §3) — يُنشأ في أي قاعدة جديدة، ويُضمَّن للقائمة بترحيل خفيف (نفس نمط Ensure* أعلاه).</summary>
    private static void EnsureSyncConflictsTable(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS SyncConflicts (
                Id INTEGER NOT NULL CONSTRAINT PK_SyncConflicts PRIMARY KEY AUTOINCREMENT,
                ConflictSyncId TEXT NOT NULL,
                EntityTable TEXT NOT NULL,
                LocalJson TEXT NULL,
                RemoteJson TEXT NULL,
                IsResolved INTEGER NOT NULL DEFAULT 0,
                ResolutionType TEXT NULL,
                ResolvedBy TEXT NULL,
                ResolvedReason TEXT NULL,
                ResolvedAt TEXT NULL,
                ResolvedValueJson TEXT NULL,
                LastUpdatedAt TEXT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_SyncConflicts_Open ON SyncConflicts (ConflictSyncId, IsResolved);");

        // بصمة القرار (P3.2C): عمودا ربط الحسم بعملية السجل الموقّعة — تُضاف لقواعد قائمة بترحيل خفيف.
        if (!ColumnExists("SyncConflicts", "ResolvedAtSeq"))
            db.Database.ExecuteSqlRaw("ALTER TABLE SyncConflicts ADD COLUMN ResolvedAtSeq INTEGER NULL;");
        if (!ColumnExists("SyncConflicts", "ResolutionOpOrigin"))
            db.Database.ExecuteSqlRaw("ALTER TABLE SyncConflicts ADD COLUMN ResolutionOpOrigin TEXT NULL;");
    }

    /// <summary>جدول سجل العمليات — أساس سجل التدقيق ونقطة منح الـ Seq عند Commit.</summary>
    private static void EnsureOperationLogTable(AppDbContext db)
    {
        if (TableExists("OperationLogs")) return;

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS OperationLogs (
                Id INTEGER NOT NULL CONSTRAINT PK_OperationLogs PRIMARY KEY AUTOINCREMENT,
                Seq INTEGER NOT NULL,
                OpType TEXT NOT NULL,
                EntityName TEXT NULL,
                EntityId INTEGER NULL,
                DocumentNumber TEXT NULL,
                Amount TEXT NOT NULL DEFAULT '0',
                UserId INTEGER NOT NULL DEFAULT 0,
                UserRole INTEGER NOT NULL DEFAULT 0,
                DeviceRole INTEGER NOT NULL DEFAULT 0,
                SummaryJson TEXT NULL,
                CommittedAt TEXT NOT NULL,
                OriginDevice TEXT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_OperationLogs_Seq ON OperationLogs (Seq);");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_OperationLogs_OpType ON OperationLogs (OpType);");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_OperationLogs_CommittedAt ON OperationLogs (CommittedAt);");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_OperationLogs_Entity ON OperationLogs (EntityName, EntityId);");

        if (!ColumnExists("OperationLogs", "OriginDevice"))
            db.Database.ExecuteSqlRaw("ALTER TABLE OperationLogs ADD COLUMN OriginDevice TEXT NULL;");

        if (!ColumnExists("OperationLogs", "OriginSeq"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE OperationLogs ADD COLUMN OriginSeq INTEGER NULL;");
            // دمج لطيف لقاعدة قائمة: كل السجلات الحالية عمليات محلية (لم تصل مزامنة بعد) — أصلها نفسها.
            db.Database.ExecuteSqlRaw("UPDATE OperationLogs SET OriginSeq = Seq WHERE OriginSeq IS NULL;");
        }
    }

    /// <summary>أعمدة المزامنة القياسية (البند 8.1 من SYNC-DESIGN) على كل جدول مشمول بالمزامنة.
    /// تُضاف الآن كي لا تكون هناك "خطوة Migration منفصلة" لاحقاً عند بناء المزامنة (المرحلة 2).</summary>
    private static void EnsureSyncColumns(AppDbContext db)
    {
        var tables = new[]
        {
            "Products", "Categories", "Customers", "Users", "Suppliers",
            "Sales", "SaleItems", "Purchases", "PurchaseItems",
            "RepairJobs", "RepairParts", "Vouchers"
        };

        foreach (var table in tables)
        {
            if (!TableExists(table)) continue;

#pragma warning disable EF1002 // أسماء الجداول من قائمة سماح ثابتة أدناه (ليست مدخلات مستخدم)
            if (!ColumnExists(table, "SyncId"))
                db.Database.ExecuteSqlRaw($"ALTER TABLE {table} ADD COLUMN SyncId TEXT NULL;");
            if (!ColumnExists(table, "OriginDevice"))
                db.Database.ExecuteSqlRaw($"ALTER TABLE {table} ADD COLUMN OriginDevice TEXT NULL;");
            if (!ColumnExists(table, "DeletedAt"))
                db.Database.ExecuteSqlRaw($"ALTER TABLE {table} ADD COLUMN DeletedAt INTEGER NULL;");
#pragma warning restore EF1002
        }
    }

    /// <summary>سجل محاولات الدخول (مرجع تدقيق أمني محلي + أساس قفل التخمين). لا يُزامَن بين الأجهزة.</summary>
    private static void EnsureLoginLogTable(AppDbContext db)
    {
        if (TableExists("LoginLogs")) return;

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS LoginLogs (
                Id INTEGER NOT NULL CONSTRAINT PK_LoginLogs PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL,
                DeviceId TEXT NULL,
                Success INTEGER NOT NULL DEFAULT 0,
                Reason TEXT NULL,
                AttemptedAt TEXT NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_LoginLogs_Username_Time ON LoginLogs (Username, AttemptedAt);");
    }

    /// <summary>سجل تدقيق الحسم (P3.2C) — محلي غير مزامَن على نمط LoginLogs: من حاول حسم ما لا يملكه (RoleMismatch) + موازنة القبول. الاحتفاظ دائم.</summary>
    private static void EnsureConflictAuditLogTable(AppDbContext db)
    {
        if (TableExists("ConflictAuditLogs")) return;

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ConflictAuditLogs (
                Id INTEGER NOT NULL CONSTRAINT PK_ConflictAuditLogs PRIMARY KEY AUTOINCREMENT,
                UserName TEXT NOT NULL,
                UserRole TEXT NOT NULL DEFAULT '',
                DeviceId TEXT NULL,
                ConflictSyncId TEXT NOT NULL,
                EntityTable TEXT NULL,
                Outcome TEXT NOT NULL,
                Reason TEXT NULL,
                AttemptedAt TEXT NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ConflictAuditLogs_Device_Time ON ConflictAuditLogs (DeviceId, AttemptedAt);");
    }

    /// <summary>عمود «يجب تغيير كلمة المرور» على Users + إجبار التغيير لأي حساب ما يزال يستعمل كلمة مرور افتراضية معروفة.
    /// حساب المدير (admin/admin) يبقى صالحاً للدخول مباشرة ولا يُجبر على التغيير.</summary>
    private static void EnsureLoginSecurityColumn(AppDbContext db)
    {
        if (!ColumnExists("Users", "MustChangePassword"))
            db.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN MustChangePassword INTEGER NOT NULL DEFAULT 0;");

        var flagged = false;
        foreach (var u in db.Users.ToList())
        {
            // حساب المدير الافتراضي admin/admin يُسمح به كما هو (إعداد صريح من المسؤول).
            if (u.Username == "admin" && PasswordHasher.Verify("admin", u.PasswordHash))
            {
                if (u.MustChangePassword)
                {
                    u.MustChangePassword = false;
                    flagged = true;
                }
                continue;
            }

            if (u.MustChangePassword) continue;
            if (PasswordHasher.Verify("1234", u.PasswordHash))
            {
                u.MustChangePassword = true;
                flagged = true;
            }
        }
        if (flagged) db.SaveChanges();
    }

    private static void BackfillSubTypes(AppDbContext db)
    {
        var pending = db.Products.Where(p => p.SubType == null).Include(p => p.Category).ToList();
        if (pending.Count == 0) return;

        foreach (var product in pending)
        {
            product.SubType = SubTypeHelper.Detect(product.Name, product.Category?.Name ?? "");
        }
        db.SaveChanges();
    }

    private static bool TableExists(string table)
    {
        using var connection = new SqliteConnection(DataPath.ConnectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = $name;";
        cmd.Parameters.AddWithValue("$name", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static bool ColumnExists(string table, string column)
    {
        using var connection = new SqliteConnection(DataPath.ConnectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void EnsureSchemaUpToDate()
    {
        if (!File.Exists(DataPath.DatabaseFile)) return;

        var version = ReadSchemaVersion();
        if (version >= CurrentSchemaVersion) return;

        // مرحلة التطوير: حذف القاعدة القديمة وإعادة إنشائها بالمخطط الجديد
        DeleteDatabaseFiles();
    }

    private static int ReadSchemaVersion()
    {
        try
        {
            using var connection = new SqliteConnection(DataPath.ConnectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT IFNULL((SELECT Value FROM AppSettings WHERE Key='SchemaVersion'),'0')";
            var value = cmd.ExecuteScalar()?.ToString();
            return int.TryParse(value, out var v) ? v : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void DeleteDatabaseFiles()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = DataPath.DatabaseFile + suffix;
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    private static void ApplyPragmas()
    {
        using var connection = new SqliteConnection(DataPath.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }

    /// <summary>كشف تالف القاعدة عند الإقلاع — «كشف لا إصلاح»: قاعدة تالفة تُرفض لا تُفتح.</summary>
    private static string QuickIntegrityCheck()
    {
        try
        {
            using var connection = new SqliteConnection(DataPath.ConnectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            return cmd.ExecuteScalar()?.ToString() ?? "unknown";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static void SeedAdmin(AppDbContext db)
    {
        if (db.Users.Any()) return;

        db.Users.Add(new User
        {
            Username = "admin",
            PasswordHash = PasswordHasher.Hash("admin"),
            DisplayName = "مدير النظام",
            Role = UserRole.Admin,
            IsActive = true,
            MustChangePassword = false
        });
        db.SaveChanges();
    }

    private static void SeedSettings(AppDbContext db)
    {
        var defaults = new Dictionary<string, string>
        {
            ["CompanyName"] = "شركة الهواتف",
            ["Currency"] = "د.ع",
            ["DeviceRole"] = "Cashier",
            ["RepairDefaultFee"] = "25000",
            ["AutoBackup"] = "true",
            ["SchemaVersion"] = CurrentSchemaVersion.ToString(),
            ["AutoSync"] = "true",
            ["SyncPort"] = "45678",
            ["SyncPeers"] = "",
            ["SyncIntervalMinutes"] = "5"
        };

        foreach (var (key, value) in defaults)
        {
            if (db.AppSettings.Any(s => s.Key == key)) continue;
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }

        // معرّف الجهاز الثابت (UUID) — يُنشأ مرة واحدة عند أول تشغيل، أساس المزامنة (البند 8.4)
        if (!db.AppSettings.Any(s => s.Key == "DeviceId"))
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = "DeviceId",
                Value = Guid.NewGuid().ToString("N")
            });
        }

        db.SaveChanges();
    }

    public static string GetSetting(string key, string defaultValue = "")
    {
        using var db = new AppDbContext();
        return db.AppSettings.AsNoTracking()
            .FirstOrDefault(s => s.Key == key)?.Value ?? defaultValue;
    }

    public static void SetSetting(string key, string value)
    {
        using var db = new AppDbContext();
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == key);
        if (setting is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }
        db.SaveChanges();
    }
}
