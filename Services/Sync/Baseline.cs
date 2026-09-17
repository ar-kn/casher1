using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;

namespace PhoneAccounting.App.Services.Sync;

/// <summary>
/// لقطة البداية (Baseline) لجهاز جديد: صورة كاملة للجداول المرجعية (تصنيفات، منتجات، موردين، زبائن).
/// تُرسل مرة واحدة عند انضمام جهاز جديد؛ والأجهزة القائمة تأخذ الباقي عبر الدلتا المتراكمة.
/// التطبيق: إنشاء-إن-لَم-يوجد فقط (لا يستبدل البيانات القائمة — لا يتجاوز ما هو محلي).
/// </summary>
public static class Baseline
{
    public static List<DeltaRow> Export(AppDbContext db)
    {
        var rows = new List<DeltaRow>();

        foreach (var category in db.Categories.AsNoTracking().ToList())
            rows.Add(RowOf("Categories", category));

        foreach (var product in db.Products.AsNoTracking().Include(p => p.Category).ToList())
            rows.Add(RowOf("Products", product, ("CategoryId", product.Category?.SyncId)));

        foreach (var supplier in db.Suppliers.AsNoTracking().ToList())
            rows.Add(RowOf("Suppliers", supplier));

        foreach (var customer in db.Customers.AsNoTracking().ToList())
            rows.Add(RowOf("Customers", customer));

        return rows;
    }

    public static int Apply(AppDbContext db, IEnumerable<DeltaRow> rows)
    {
        using var tx = db.Database.BeginTransaction();
        var created = 0;

        foreach (var row in rows.Where(r => r.T == "Categories"))
            if (ApplyIfMissing(CreateCategory(db, row))) created++;
        foreach (var row in rows.Where(r => r.T == "Products"))
            if (ApplyIfMissing(CreateProduct(db, row))) created++;
        foreach (var row in rows.Where(r => r.T == "Suppliers"))
            if (ApplyIfMissing(CreateSupplier(db, row))) created++;
        foreach (var row in rows.Where(r => r.T == "Customers"))
            if (ApplyIfMissing(CreateCustomer(db, row))) created++;

        db.SaveChanges();
        tx.Commit();
        return created;
    }

    private static bool ApplyIfMissing(object? entity) => entity is not null;

    private static Category? CreateCategory(AppDbContext db, DeltaRow row)
    {
        if (db.Categories.Any(x => x.SyncId == row.S)) return null;
        var cat = new Category { SyncId = row.S, OriginDevice = row.Origin, Name = row.F.GetValueOrDefault("Name")?.Str ?? "" };
        db.Categories.Add(cat);
        db.SaveChanges();
        return cat;
    }

    private static Product? CreateProduct(AppDbContext db, DeltaRow row)
    {
        if (db.Products.Any(x => x.SyncId == row.S)) return null;
        var existingSyncId = db.Categories.FirstOrDefault(c => c.SyncId == row.R.GetValueOrDefault("CategoryId"));
        var product = new Product
        {
            SyncId = row.S,
            OriginDevice = row.Origin,
            Name = row.F.GetValueOrDefault("Name")?.Str ?? "",
            EnglishName = row.F.GetValueOrDefault("EnglishName")?.Str,
            SubType = row.F.GetValueOrDefault("SubType")?.Str,
            Barcode = row.F.GetValueOrDefault("Barcode")?.Str,
            Imei = row.F.GetValueOrDefault("Imei")?.Str,
            CategoryId = existingSyncId?.Id ?? 0,
            BuyPrice = row.F.GetValueOrDefault("BuyPrice")?.Dec ?? 0,
            SellPrice = row.F.GetValueOrDefault("SellPrice")?.Dec ?? 0,
            Stock = (int)(row.F.GetValueOrDefault("Stock")?.Num ?? 0),
            MinStock = (int)(row.F.GetValueOrDefault("MinStock")?.Num ?? 5),
            IsUsed = row.F.GetValueOrDefault("IsUsed")?.Bool ?? false,
            Device = (DeviceRole)(row.F.GetValueOrDefault("Device")?.Num ?? 0),
            IsActive = row.F.GetValueOrDefault("IsActive")?.Bool ?? true,
            DeletedAt = row.F.GetValueOrDefault("DeletedAt")?.Num,
        };
        db.Products.Add(product);
        db.SaveChanges();
        return product;
    }

    private static Supplier? CreateSupplier(AppDbContext db, DeltaRow row)
    {
        if (db.Suppliers.Any(x => x.SyncId == row.S)) return null;
        var supplier = new Supplier
        {
            SyncId = row.S,
            OriginDevice = row.Origin,
            Name = row.F.GetValueOrDefault("Name")?.Str ?? "",
            Phone = row.F.GetValueOrDefault("Phone")?.Str,
            Balance = row.F.GetValueOrDefault("Balance")?.Dec ?? 0
        };
        db.Suppliers.Add(supplier);
        db.SaveChanges();
        return supplier;
    }

    private static Customer? CreateCustomer(AppDbContext db, DeltaRow row)
    {
        if (db.Customers.Any(x => x.SyncId == row.S)) return null;
        var customer = new Customer
        {
            SyncId = row.S,
            OriginDevice = row.Origin,
            Name = row.F.GetValueOrDefault("Name")?.Str ?? "",
            Phone = row.F.GetValueOrDefault("Phone")?.Str,
            Balance = row.F.GetValueOrDefault("Balance")?.Dec ?? 0
        };
        db.Customers.Add(customer);
        db.SaveChanges();
        return customer;
    }

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

            fields[p.Name] = ToScalar(p.GetValue(entity));
        }

        var refsDict = new Dictionary<string, string?>();
        foreach (var (name, refSyncId) in refs)
            refsDict[name] = refSyncId is null ? null : refSyncId.ToString();

        return new DeltaRow { T = table, S = syncId, Origin = origin, F = fields, R = refsDict };
    }

    private static JsonScalar ToScalar(object? raw)
    {
        if (raw is null) return JsonScalar.OfNull();
        return raw switch
        {
            string s => JsonScalar.Of(s),
            bool b => JsonScalar.Of(b),
            DateTime dt => JsonScalar.Of(dt),
            decimal d => JsonScalar.Of(d),
            long l => JsonScalar.Of(l),
            int i => JsonScalar.Of(i),
            short sh => JsonScalar.Of(sh),
            byte by => JsonScalar.Of(by),
            float f => JsonScalar.Of((decimal)f),
            double dbl => JsonScalar.Of((decimal)dbl),
            Enum e => JsonScalar.Of(Convert.ToInt64(e)),
            _ => JsonScalar.Of(raw.ToString() ?? "")
        };
    }
}