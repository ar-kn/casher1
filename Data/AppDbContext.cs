using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services.Sync;

namespace PhoneAccounting.App.Data;

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();
    public DbSet<RepairJob> RepairJobs => Set<RepairJob>();
    public DbSet<RepairPart> RepairParts => Set<RepairPart>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<OperationLog> OperationLogs => Set<OperationLog>();
    public DbSet<LoginLog> LoginLogs => Set<LoginLog>();
    public DbSet<ConflictAuditLog> ConflictAuditLogs => Set<ConflictAuditLog>();
    public DbSet<SyncLogEntry> SyncLogs => Set<SyncLogEntry>();
    public DbSet<SyncPeerState> SyncPeerStates => Set<SyncPeerState>();
    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();

    /// <summary>اتصال بديل للاختبار/المحاكاة (قاعدتان على نفس الجهاز) — يُستهَلّ بشكل صريح فقط من SyncTestHarness/M2.x.</summary>
    public static string? TestConnectionString { get; set; }

    private readonly string? _fixedConnectionString;

    public AppDbContext() { }

    /// <summary>سياق مربوط بقاعدة محددة بلا الاعتماد على الحالة الثابتة — لقناة M2.2 (خادم وعميل على مخزنَين في نفس العملية).</summary>
    public AppDbContext(string dbFile)
        : this(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dbFile }.ToString(), false)
    {
    }

    private AppDbContext(string? connectionString, bool _)
    {
        _fixedConnectionString = connectionString;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite(_fixedConnectionString ?? TestConnectionString ?? DataPath.ConnectionString);
    }

    // ===== مسار الكتابة الموحّد (Unit of Work): ضمان تعيين SyncId/OriginDevice عند الإنشاء + منح Seq عند Commit =====
    public override int SaveChanges()
    {
        // M2.5: الكتابة داخل العملية الواحدة مُصفَّفة بقفل الكاتب لئلّا يتزامن «تطبيق دلتا» و«حفظ بيع»
        // (إعادة الدخول بنفس الخيط مسموحة — للطبقات الداخلية مثل EnsureUserForOp/التطبيق).
        using (SyncWriteLock.Acquire(TimeSpan.FromSeconds(SyncWriteLock.AcquireTimeoutSeconds), "حفظ"))
        {
            AssignSyncFields();
            AssignPendingOpSeqs();
            return base.SaveChanges();
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        using (SyncWriteLock.Acquire(TimeSpan.FromSeconds(SyncWriteLock.AcquireTimeoutSeconds), "حفظ async"))
        {
            AssignSyncFields();
            AssignPendingOpSeqs();
            return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// «الشرط المجمَّد 1»: Seq يُمنح عند الـ Commit لا عند إنشاء الصف.
    /// سطر العملية يُضاف عبر OperationWriter.Register بعلم Seq=0 (بانتظار)، وهنا — لحظة الـ flush داخل
    /// نفس SaveChanges (ذات المعاملة الضمنية) — يُسند الرقم التالي. إن فشل الحفظ يُرجع كل شيء فلا يُستهلك رقم.
    /// أي صف بـ Seq واضح غير صفري (مثل دلتا مدمجة محلياً) يُترك كما هو ولا يُعاد ترقيمه.
    /// </summary>
    private void AssignPendingOpSeqs()
    {
        var pending = ChangeTracker.Entries<OperationLog>()
            .Where(e => e.State == EntityState.Added && e.Entity.Seq == 0)
            .Select(e => e.Entity)
            .ToList();
        if (pending.Count == 0) return;

        var taken = new HashSet<long>(ChangeTracker.Entries<OperationLog>()
            .Where(e => e.State == EntityState.Added && e.Entity.Seq > 0)
            .Select(e => e.Entity.Seq));

        long next = OperationLogs.Any() ? OperationLogs.Max(o => o.Seq) + 1 : 1;
        foreach (var op in pending)
        {
            while (taken.Contains(next)) next++;
            op.Seq = next;
            // العمليات المحلية: هويتها عبر الشبكة = (OriginDevice = نفسي، OriginSeq = Seq).
            if (string.IsNullOrWhiteSpace(op.OriginDevice))
                op.OriginDevice = PhoneAccounting.App.Services.Session.DeviceId ?? "unknown";
            op.OriginSeq = next;
            taken.Add(next);
            next++;
        }
    }

    /// <summary>يُعيّن أعمدة المزامنة القياسية لكل صف جديد يُنشأ عبر أي مسار كتابة (البند 8.1 + الشرط المجمَّد 2).</summary>
    private void AssignSyncFields()
    {
        var origin = PhoneAccounting.App.Services.Session.DeviceId;
        if (string.IsNullOrWhiteSpace(origin)) origin = "unknown";

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added) continue;

            switch (entry.Entity)
            {
                case Product p: Sync(p, origin); break;
                case Category c: Sync(c, origin); break;
                case Customer c: Sync(c, origin); break;
                case User u: Sync(u, origin); break;
                case Supplier s: Sync(s, origin); break;
                case Sale s: Sync(s, origin); break;
                case SaleItem i: Sync(i, origin); break;
                case Purchase p: Sync(p, origin); break;
                case PurchaseItem i: Sync(i, origin); break;
                case RepairJob j: Sync(j, origin); break;
                case RepairPart p: Sync(p, origin); break;
                case Voucher v: Sync(v, origin); break;
            }
        }
    }

    private static void Sync(Product p, string origin) { p.SyncId ??= Guid.NewGuid().ToString("N"); p.OriginDevice ??= origin; }
    private static void Sync(Category c, string origin) { c.SyncId ??= Guid.NewGuid().ToString("N"); c.OriginDevice ??= origin; }
    private static void Sync(Customer c, string origin) { c.SyncId ??= Guid.NewGuid().ToString("N"); c.OriginDevice ??= origin; }
    private static void Sync(User u, string origin) { u.SyncId ??= Guid.NewGuid().ToString("N"); u.OriginDevice ??= origin; }
    private static void Sync(Supplier s, string origin) { s.SyncId ??= Guid.NewGuid().ToString("N"); s.OriginDevice ??= origin; }
    private static void Sync(Sale s, string origin) { s.SyncId ??= Guid.NewGuid().ToString("N"); s.OriginDevice ??= origin; }
    private static void Sync(SaleItem i, string origin) { i.SyncId ??= Guid.NewGuid().ToString("N"); i.OriginDevice ??= origin; }
    private static void Sync(Purchase p, string origin) { p.SyncId ??= Guid.NewGuid().ToString("N"); p.OriginDevice ??= origin; }
    private static void Sync(PurchaseItem i, string origin) { i.SyncId ??= Guid.NewGuid().ToString("N"); i.OriginDevice ??= origin; }
    private static void Sync(RepairJob j, string origin) { j.SyncId ??= Guid.NewGuid().ToString("N"); j.OriginDevice ??= origin; }
    private static void Sync(RepairPart p, string origin) { p.SyncId ??= Guid.NewGuid().ToString("N"); p.OriginDevice ??= origin; }
    private static void Sync(Voucher v, string origin) { v.SyncId ??= Guid.NewGuid().ToString("N"); v.OriginDevice ??= origin; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.ToTable("AppSettings");
            e.HasIndex(s => s.Key).IsUnique();
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.ToTable("Categories");
            e.HasIndex(c => c.Name);
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("Products");
            e.HasIndex(p => p.Barcode);
            e.HasIndex(p => p.Name);
            e.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId);
        });

        modelBuilder.Entity<Customer>(e =>
        {
            e.ToTable("Customers");
            e.HasIndex(c => c.Name);
        });

        modelBuilder.Entity<Sale>(e =>
        {
            e.ToTable("Sales");
            e.HasIndex(s => s.InvoiceNumber).IsUnique();
            e.HasIndex(s => s.Date);
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId);
            e.HasOne(s => s.Customer).WithMany(c => c.Sales).HasForeignKey(s => s.CustomerId);
        });

        modelBuilder.Entity<SaleItem>(e =>
        {
            e.ToTable("SaleItems");
            e.HasOne(i => i.Sale).WithMany(s => s.Items).HasForeignKey(i => i.SaleId);
        });

        modelBuilder.Entity<Purchase>(e =>
        {
            e.ToTable("Purchases");
            e.HasIndex(p => p.InvoiceNumber).IsUnique();
            e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId);
            e.HasOne(p => p.Supplier).WithMany(s => s.Purchases).HasForeignKey(p => p.SupplierId);
        });

        modelBuilder.Entity<Supplier>(e =>
        {
            e.ToTable("Suppliers");
            e.HasIndex(s => s.Name);
        });

        modelBuilder.Entity<PurchaseItem>(e =>
        {
            e.ToTable("PurchaseItems");
            e.HasOne(i => i.Purchase).WithMany(p => p.Items).HasForeignKey(i => i.PurchaseId);
        });

        modelBuilder.Entity<RepairJob>(e =>
        {
            e.ToTable("RepairJobs");
            e.HasIndex(j => j.JobNumber).IsUnique();
            e.HasIndex(j => j.ReceivedDate);
            e.HasIndex(j => j.Status);
            e.HasOne(j => j.User).WithMany().HasForeignKey(j => j.UserId);
        });

        modelBuilder.Entity<RepairPart>(e =>
        {
            e.ToTable("RepairParts");
            e.HasOne(p => p.RepairJob).WithMany(j => j.Parts).HasForeignKey(p => p.RepairJobId);
        });

        modelBuilder.Entity<Voucher>(e =>
        {
            e.ToTable("Vouchers");
            e.HasIndex(v => v.VoucherNumber).IsUnique();
            e.HasIndex(v => v.Date);
            e.HasOne(v => v.Customer).WithMany().HasForeignKey(v => v.CustomerId);
            e.HasOne(v => v.User).WithMany().HasForeignKey(v => v.UserId);
        });

        modelBuilder.Entity<SyncLogEntry>(e =>
        {
            e.ToTable("SyncLogs");
            e.HasIndex(s => new { s.OriginDevice, s.OriginSeq }).IsUnique();
            e.HasIndex(s => s.Direction);
        });

        modelBuilder.Entity<SyncPeerState>(e =>
        {
            e.ToTable("SyncPeerState");
            e.HasIndex(p => new { p.OwnerDeviceId, p.OriginDevice }).IsUnique();
        });

        modelBuilder.Entity<SyncConflict>(e =>
        {
            e.ToTable("SyncConflicts");
            e.HasIndex(c => new { c.ConflictSyncId, c.IsResolved });
        });

        modelBuilder.Entity<ConflictAuditLog>(e =>
        {
            e.ToTable("ConflictAuditLogs");
            e.HasIndex(a => new { a.DeviceId, a.AttemptedAt });
        });
    }
}
