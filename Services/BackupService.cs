using System.IO;
using Microsoft.Data.Sqlite;
using PhoneAccounting.App.Data;

namespace PhoneAccounting.App.Services;

/// <summary>
/// سلامة البيانات (البند 7 من SYNC-DESIGN): النسخ الاحتياطي مستقل عن المزامنة تماماً.
/// - النسخ يتم بـ «VACUUM INTO» (صورة نقطية سليمة — لا نسخ الملف أثناء فتحه، آمنة مع WAL).
/// - كل نسخة تُفحص بـ integrity_check قبل قبولها؛ نسخة تالفة تُحذف فوراً.
/// - الاحتفاظ بآخر 30 نسخة، والنسخ التلقائي يُشغَّل عند الإقلاع إن مضى أكثر من 23 ساعة على آخرها.
/// - الاستعادة: حارس أمني (القاعدة الحالية تُحفظ) + إغلاق الاتصالات، ثم استبدال الملف وإعادة تشغيل الزامية.
/// </summary>
public static class BackupService
{
    public const int KeepCount = 30;

    public static string BackupFolder => Path.Combine(DataPath.BaseFolder, "Backups");

    /// <summary>هل حان موعد النسخ التلقائي (الإعداد مفعل + مضى ~يوم على آخر نسخة)؟</summary>
    public static bool ShouldRunAutoBackup
    {
        get
        {
            var enabled = Database.GetSetting("AutoBackup", "true");
            if (!enabled.Equals("true", StringComparison.OrdinalIgnoreCase)) return false;

            var files = ListBackups();
            if (files.Count == 0) return true;
            var lastWrite = File.GetLastWriteTime(files[0]);
            return (DateTime.Now - lastWrite).TotalHours >= 23;
        }
    }

    /// <summary>ينفّذ النسخ التلقائي بصمت إن كان مستحقاً (يُستدعى عند إقلاع التطبيق).</summary>
    public static void RunAutoIfDue()
    {
        if (!File.Exists(DataPath.DatabaseFile)) return;
        if (!ShouldRunAutoBackup) return;

        try
        {
            CreateBackup();
        }
        catch
        {
            // النسخ التلقائي لا يوقف العمل — يُسجَّل الفشل في سجل الأخطاء
        }
    }

    /// <summary>يُنشئ نسخة احتياطية سليمة ويعيد آخر 30 نسخة فقط. يُعيد رسالة النتيجة.</summary>
    public static string CreateBackup()
    {
        Directory.CreateDirectory(BackupFolder);
        var name = $"phoneaccounting-{DateTime.Now:yyyyMMdd-HHmmss}.db";
        var path = Path.Combine(BackupFolder, name);

        using (var connection = new SqliteConnection(DataPath.ConnectionString))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VACUUM INTO @path;";
            cmd.Parameters.AddWithValue("@path", path);
            cmd.ExecuteNonQuery();
        }

        var check = QuickIntegrity(path);
        if (check != "ok" && !check.StartsWith("ok", StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(path); } catch { }
            return $"فشل النسخ: النسخة الناتجة تالفة (integrity_check = {check})";
        }

        PruneOldBackups();
        return $"تم إنشاء نسخة احتياطية سليمة: {name}";
    }

    /// <summary>فحص سريع لسلامة قاعدة (يُستخدم للإشارة للنسخات والاستعادة).</summary>
    public static string QuickIntegrity(string dbPath)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
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

    /// <summary>قائمة النسخ الاحتياطية (الأحدث أولاً).</summary>
    public static List<string> ListBackups()
    {
        if (!Directory.Exists(BackupFolder)) return new List<string>();
        return Directory.GetFiles(BackupFolder, "*.db")
            .Where(f => Path.GetFileName(f).StartsWith("phoneaccounting-", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f)
            .ToList();
    }

    /// <summary>
    /// استعادة من نسخة موثقة: يفحص سلامة النسخة، ويحفظ القاعدة الحالية حارساً،
    /// ويغلق كل الاتصالات، ويستبدل الملف. المطلوب بعدها إعادة تشغيل التطبيق.
    /// </summary>
    public static string Restore(string backupPath)
    {
        if (!File.Exists(backupPath)) return "ملف النسخة غير موجود";

        var check = QuickIntegrity(backupPath);
        if (check != "ok" && !check.StartsWith("ok", StringComparison.OrdinalIgnoreCase))
            return $"النسخة المختارة تالفة (integrity_check = {check}) — لم تُستبدل القاعدة";

        SqliteConnection.ClearAllPools();
        TryDeleteWalFiles();

        try
        {
            var live = DataPath.DatabaseFile;
            var guard = Path.Combine(DataPath.BaseFolder,
                $"phoneaccounting.pre-restore-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            File.Copy(live, guard, true);

            File.Copy(backupPath, live, true);
            return $"تمت الاستعادة من: {Path.GetFileName(backupPath)} — أعد تشغيل التطبيق الآن. (نسخة أمان من القاعدة السابقة: {Path.GetFileName(guard)})";
        }
        catch (Exception ex)
        {
            return $"تعذرت الاستعادة: {ex.Message}";
        }
    }

    private static void PruneOldBackups()
    {
        foreach (var old in ListBackups().Skip(KeepCount))
        {
            try { File.Delete(old); } catch { }
        }
    }

    private static void TryDeleteWalFiles()
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var path = DataPath.DatabaseFile + suffix;
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}