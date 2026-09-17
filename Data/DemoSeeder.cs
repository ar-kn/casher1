using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Infrastructure;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;

namespace PhoneAccounting.App.Data;

public static class DemoSeeder
{
    private sealed record Brand(
        string ArName,
        string EnName,
        decimal CaseBuy, decimal CaseSell,
        decimal ProtBuy, decimal ProtSell,
        decimal FlexBuy, decimal FlexSell,
        decimal BackBuy, decimal BackSell);

    private sealed record PhoneModel(
        Brand Brand,
        string ArModel,
        string EnModel,
        decimal PhoneBuy, decimal PhoneSell,
        decimal ScreenBuy, decimal ScreenSell,
        int StockWeight);

    public static void Seed(AppDbContext db)
    {
        if (db.Products.Any()) return;

        var random = new Random(20260809);
        var today = DateTime.Now.Date;

        // ===== 1) التصنيفات الهرمية =====
        var phonesCat = new Category { Name = "هواتف" };
        var accessoriesCat = new Category { Name = "إكسسوارات" };
        var partsCat = new Category { Name = "قطع غيار الصيانة" };

        var casesCat = new Category { Name = "كفرات حماية", Parent = accessoriesCat };
        var protectorsCat = new Category { Name = "لاصق شاشة", Parent = accessoriesCat };
        var chargersCat = new Category { Name = "شواحن", Parent = accessoriesCat };
        var cablesCat = new Category { Name = "كيبلات", Parent = accessoriesCat };
        var earphonesCat = new Category { Name = "سماعات", Parent = accessoriesCat };

        var screensCat = new Category { Name = "شاشات", Parent = partsCat };
        var flexCat = new Category { Name = "فلات شحن", Parent = partsCat };
        var backCat = new Category { Name = "ظهر هواتف", Parent = partsCat };

        var iphoneCat = new Category { Name = "آيفون", Parent = phonesCat };
        var samsungCat = new Category { Name = "سامسونج", Parent = phonesCat };
        var realmeCat = new Category { Name = "ريلمي", Parent = phonesCat };
        var redmiCat = new Category { Name = "ريدمي", Parent = phonesCat };
        var tecnoCat = new Category { Name = "تكنو", Parent = phonesCat };

        db.Categories.AddRange(
            phonesCat, accessoriesCat, partsCat,
            casesCat, protectorsCat, chargersCat, cablesCat, earphonesCat,
            screensCat, flexCat, backCat,
            iphoneCat, samsungCat, realmeCat, redmiCat, tecnoCat);
        db.SaveChanges();

        // ===== 2) البراندات وأسعار إكسسواراتها =====
        var iphone = new Brand("آيفون", "Apple", 7000, 14000, 2500, 6500, 45000, 85000, 120000, 190000);
        var samsung = new Brand("سامسونج", "Samsung", 5500, 11000, 2200, 5500, 28000, 52000, 75000, 125000);
        var realme = new Brand("ريلمي", "Realme", 4500, 9500, 1800, 4500, 22000, 40000, 45000, 80000);
        var redmi = new Brand("ريدمي", "Redmi", 4500, 9500, 1800, 4500, 22000, 40000, 45000, 80000);
        var tecno = new Brand("تكنو", "Tecno", 4000, 8500, 1500, 4000, 20000, 36000, 40000, 70000);

        // ===== 3) موديلات الهواتف (أسماء حقيقية) =====
        var phones = new PhoneModel[]
        {
            // آيفون
            new(iphone, "آيفون 17 برو ماكس 256GB", "iPhone 17 Pro Max 256GB", 2750000, 3050000, 480000, 750000, 12),
            new(iphone, "آيفون 17 برو 256GB", "iPhone 17 Pro 256GB", 2380000, 2650000, 450000, 700000, 14),
            new(iphone, "آيفون 17 256GB", "iPhone 17 256GB", 1840000, 2050000, 400000, 620000, 18),
            new(iphone, "آيفون 16e 256GB", "iPhone 16e 256GB", 1120000, 1250000, 300000, 470000, 20),
            new(iphone, "آيفون 15 128GB", "iPhone 15 128GB", 890000, 1000000, 260000, 410000, 16),
            // سامسونج
            new(samsung, "جالكسي S26 ألترا 256GB", "Galaxy S26 Ultra 256GB", 2120000, 2350000, 340000, 540000, 15),
            new(samsung, "جالكسي S26 128GB", "Galaxy S26 128GB", 1580000, 1750000, 280000, 450000, 16),
            new(samsung, "جالكسي A56 5G 128GB", "Galaxy A56 5G 128GB", 640000, 720000, 130000, 210000, 22),
            new(samsung, "جالكسي A36 5G 128GB", "Galaxy A36 5G 128GB", 480000, 540000, 110000, 180000, 24),
            new(samsung, "جالكسي A26 5G 128GB", "Galaxy A26 5G 128GB", 385000, 430000, 90000, 150000, 25),
            // ريلمي
            new(realme, "ريلمي GT 7 256GB", "Realme GT 7 256GB", 860000, 950000, 240000, 380000, 18),
            new(realme, "ريلمي GT 7T 256GB", "Realme GT 7T 256GB", 790000, 880000, 200000, 320000, 16),
            new(realme, "ريلمي C77 5G 128GB", "Realme C77 5G 128GB", 430000, 480000, 110000, 180000, 22),
            new(realme, "ريلمي C75 5G 128GB", "Realme C75 5G 128GB", 395000, 440000, 100000, 165000, 24),
            new(realme, "ريلمي C75 128GB", "Realme C75 128GB", 375000, 420000, 95000, 155000, 22),
            // ريدمي
            new(redmi, "ريدمي نوت 15 برو+ 5G 256GB", "Redmi Note 15 Pro+ 5G 256GB", 710000, 790000, 160000, 260000, 20),
            new(redmi, "ريدمي نوت 15 برو 5G 256GB", "Redmi Note 15 Pro 5G 256GB", 575000, 640000, 140000, 230000, 22),
            new(redmi, "ريدمي نوت 15 128GB", "Redmi Note 15 128GB", 420000, 470000, 120000, 195000, 26),
            new(redmi, "ريدمي 14C 128GB", "Redmi 14C 128GB", 300000, 340000, 85000, 140000, 28),
            // تكنو
            new(tecno, "تكنو بوفا 8 برو 5G 128GB", "Tecno Pova 8 Pro 5G 128GB", 520000, 580000, 120000, 195000, 20),
            new(tecno, "تكنو بوفا 8 5G 128GB", "Tecno Pova 8 5G 128GB", 450000, 500000, 105000, 175000, 24),
            new(tecno, "تكنو كامون 50 ألترا 256GB", "Tecno Camon 50 Ultra 256GB", 505000, 560000, 115000, 185000, 22),
            new(tecno, "تكنو سبارك 50 5G 128GB", "Tecno Spark 50 5G 128GB", 285000, 320000, 80000, 135000, 26),
        };

        // توزيع الأرصدة حسب الأوزان بحيث يكون المجموع تماماً كما هو مطلوب
        var phoneStock = Distribute(500, phones.Select(p => p.StockWeight).ToArray());
        var caseStock = Distribute(5000, phones.Select(p => p.StockWeight).ToArray());
        var protectorStock = Distribute(7000, phones.Select(p => p.StockWeight).ToArray());
        var screenStock = Distribute(3500, phones.Select(p => p.StockWeight).ToArray());
        var flexStock = Distribute(7000, phones.Select(p => p.StockWeight).ToArray());
        var backStock = Distribute(8000, phones.Select(p => p.StockWeight).ToArray());

        // ===== 4) بناء المنتجات =====
        var products = new List<Product>();
        var initialStock = new List<int>();
        var barcodeCounter = 1000000000;

        string NextBarcode() => (barcodeCounter++).ToString();

        for (var i = 0; i < phones.Length; i++)
        {
            var p = phones[i];
            var brand = p.Brand;
            var category = brand switch
            {
                _ when brand == iphone => iphoneCat,
                _ when brand == samsung => samsungCat,
                _ when brand == realme => realmeCat,
                _ when brand == redmi => redmiCat,
                _ => tecnoCat
            };
            var created = today.AddDays(-random.Next(0, 120));

            AddProduct(products, initialStock,
                p.ArModel, p.EnModel, category,
                p.PhoneBuy, p.PhoneSell, phoneStock[i], 3, DeviceRole.Cashier, NextBarcode(), created);

            AddProduct(products, initialStock,
                $"كفر {p.ArModel}", $"{p.EnModel} Case", casesCat,
                brand.CaseBuy, brand.CaseSell, caseStock[i], 50, DeviceRole.Accessories, NextBarcode(), created);

            AddProduct(products, initialStock,
                $"لاصق شاشة {p.ArModel}", $"{p.EnModel} Screen Protector", protectorsCat,
                brand.ProtBuy, brand.ProtSell, protectorStock[i], 100, DeviceRole.Accessories, NextBarcode(), created);

            AddProduct(products, initialStock,
                $"شاشة {p.ArModel} أصلية", $"{p.EnModel} Original Screen", screensCat,
                p.ScreenBuy, p.ScreenSell, screenStock[i], 5, DeviceRole.Repair, NextBarcode(), created);

            AddProduct(products, initialStock,
                $"فلت شحن {p.ArModel}", $"{p.EnModel} Charging Port Flex", flexCat,
                brand.FlexBuy, brand.FlexSell, flexStock[i], 20, DeviceRole.Repair, NextBarcode(), created);

            AddProduct(products, initialStock,
                $"ظهر {p.ArModel} زجاجي", $"{p.EnModel} Back Glass", backCat,
                brand.BackBuy, brand.BackSell, backStock[i], 10, DeviceRole.Repair, NextBarcode(), created);
        }

        // الشواحن (المجموع 500)
        AddProduct(products, initialStock, "شاحن آبل 20W USB-C", "Apple 20W USB-C Adapter", chargersCat,
            18000, 35000, 60, 15, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "شاحن سامسونج 45W سوبر فاست", "Samsung 45W Super Fast Charger", chargersCat,
            20000, 38000, 55, 15, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "شاحن سامسونج 25W", "Samsung 25W Adapter", chargersCat,
            15000, 28000, 65, 15, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "شاحن ريدمي 67W توربو", "Redmi 67W Turbo Charger", chargersCat,
            16000, 30000, 70, 15, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "شاحن ريدمي 120W هايبرتشارج", "Redmi 120W HyperCharge", chargersCat,
            25000, 45000, 45, 10, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "شاحن ريلمي 80W سوبرفووك", "Realme 80W SuperVOOC Charger", chargersCat,
            18000, 34000, 60, 15, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "شاحن تكنو 45W", "Tecno 45W Charger", chargersCat,
            12000, 24000, 65, 15, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "شاحن لاسلكي 15W", "15W Wireless Charger", chargersCat,
            20000, 40000, 40, 10, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "شاحن سيارة 30W", "30W Car Charger", chargersCat,
            10000, 20000, 40, 10, DeviceRole.Accessories, NextBarcode(), today);

        // الكيبلات (المجموع 2000)
        AddProduct(products, initialStock, "كيبل Type-C إلى Type-C 100W 1م", "USB-C to USB-C 100W Cable 1m", cablesCat,
            3000, 7000, 300, 50, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "كيبل Type-C 3A 1م", "USB-C 3A Cable 1m", cablesCat,
            2500, 5500, 350, 50, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "كيبل لايتنينغ لأيفون", "Apple Lightning Cable", cablesCat,
            3500, 8000, 250, 50, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "كيبل مايكرو USB", "Micro USB Cable", cablesCat,
            2000, 4500, 200, 40, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "كيبل Type-C مضفر 2م", "Braided USB-C Cable 2m", cablesCat,
            4000, 9000, 300, 50, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "كيبل Type-C 100W 2م", "USB-C 100W Cable 2m", cablesCat,
            5000, 11000, 250, 40, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "كيبل Type-C 60W 1م", "USB-C 60W Cable 1m", cablesCat,
            2800, 6000, 200, 40, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "كيبل سوبرفووك ريلمي 100W", "Realme SuperVOOC 100W Cable", cablesCat,
            4500, 9500, 150, 30, DeviceRole.Accessories, NextBarcode(), today);

        // السماعات (المجموع 4000)
        AddProduct(products, initialStock, "سماعة آبل إيربودز برو 3", "Apple AirPods Pro 3", earphonesCat,
            350000, 480000, 150, 10, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "سماعة سامسونج جالاكسي بادز 3", "Samsung Galaxy Buds 3", earphonesCat,
            250000, 350000, 200, 10, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "سماعة ريدمي بادز 6", "Redmi Buds 6", earphonesCat,
            35000, 60000, 600, 30, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "سماعة ريلمي بادز إير 6", "Realme Buds Air 6", earphonesCat,
            40000, 70000, 550, 30, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "سماعة تكنو بادز", "Tecno Buds", earphonesCat,
            25000, 45000, 650, 30, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "سماعة سلكية 3.5مم", "Wired Earphones 3.5mm", earphonesCat,
            5000, 10000, 800, 50, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "سماعة سلكية Type-C", "Wired USB-C Earphones", earphonesCat,
            7000, 14000, 650, 50, DeviceRole.Accessories, NextBarcode(), today);
        AddProduct(products, initialStock, "سماعة رأس بلوتوث", "Bluetooth Headset", earphonesCat,
            30000, 55000, 400, 20, DeviceRole.Accessories, NextBarcode(), today);

        db.Products.AddRange(products);
        db.SaveChanges();

        // ===== 5) العملاء =====
        var customerNames = new[]
        {
            "أحمد عبدالله", "مصطفى كريم", "يوسف سعيد", "علي رضا",
            "محمد صالح", "كرار جبار", "حيدر عباس", "سجاد عبد",
            "منتظر حسين", "عمر فاروق"
        };
        var customers = customerNames
            .Select(n => new Customer { Name = n, Phone = $"07{random.Next(7, 9)}0{random.Next(10000000, 99999999)}" })
            .ToList();
        db.Customers.AddRange(customers);
        db.SaveChanges();

        // ===== 6) الموظفون =====
        var employeeDefs = new[]
        {
            ("ali", "علي محمد", UserRole.Accountant),
            ("hussein", "حسين علي", UserRole.Cashier),
            ("zainab", "زينب جاسم", UserRole.Cashier),
            ("ahmed", "أحمد كريم", UserRole.RepairTech),
            ("fatima", "فاطمة حسن", UserRole.Inventory),
        };
        var cashiers = new List<User>();
        foreach (var (username, display, role) in employeeDefs)
        {
            var user = new User
            {
                Username = username,
                PasswordHash = PasswordHasher.Hash("1234"),
                DisplayName = display,
                Role = role,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = today.AddDays(-random.Next(0, 110))
            };
            db.Users.Add(user);
            if (role == UserRole.Cashier) cashiers.Add(user);
        }
        var admin = db.Users.First(u => u.Role == UserRole.Admin);
        cashiers.Add(admin);
        db.SaveChanges();

        // ===== 7) مبيعات ومشتريات على مدى 4 أشهر (سجل تاريخي) =====
        var suppliers = new[]
        {
            "مورد الهواتف الحديثة", "شركة الإكسسوارات البغدادية",
            "مورد قطع الغيار المركزي", "توكيل أجهزة السامسونج"
        };

        var phoneCategoryIds = new[] { iphoneCat, samsungCat, realmeCat, redmiCat, tecnoCat }
            .Select(c => c.Id).ToHashSet();

        var stockOf = products.ToDictionary(p => p.Id, p => p.Stock);
        var saleSeq = 0;
        var purchaseSeq = 0;
        var lastSaleDay = (DateTime?)null;
        var lastPurchaseDay = (DateTime?)null;
        var start = today.AddDays(-119);

        for (var dayOffset = 0; dayOffset < 120; dayOffset++)
        {
            var day = start.AddDays(dayOffset);
            if (day > today) break;

            // مشتريات كل 3 أيام
            if (dayOffset % 3 == 0)
            {
                if (lastPurchaseDay != day.Date) { purchaseSeq = 0; lastPurchaseDay = day.Date; }
                purchaseSeq++;
                var purchase = new Purchase
                {
                    InvoiceNumber = InvoiceService.PurchaseInvoiceNumber(purchaseSeq, day.Date),
                    Date = day.AddHours(random.Next(9, 12)),
                    User = cashiers[random.Next(cashiers.Count)],
                    Device = (DeviceRole)random.Next(0, 4),
                    SupplierName = suppliers[random.Next(suppliers.Length)],
                    Total = 0
                };
                var purchaseTotal = 0m;
                var chosen = products.OrderBy(_ => random.Next()).Take(random.Next(4, 8)).ToList();
                foreach (var product in chosen)
                {
                    var isPhone = phoneCategoryIds.Contains(product.CategoryId);
                    var qty = isPhone ? random.Next(3, 8) : random.Next(15, 45);
                    var item = new PurchaseItem
                    {
                        ProductId = product.Id,
                        ProductName = product.Name,
                        Quantity = qty,
                        UnitPrice = product.BuyPrice
                    };
                    purchase.Items.Add(item);
                    purchaseTotal += qty * product.BuyPrice;
                    stockOf[product.Id] += qty;
                }
                purchase.Total = purchaseTotal;
                db.Purchases.Add(purchase);
            }

            // مبيعات اليوم
            var salesToday = random.Next(2, 9);
            for (var s = 0; s < salesToday; s++)
            {
                var saleTime = day.Date.AddHours(random.Next(10, 21)).AddMinutes(random.Next(0, 59));
                if (saleTime > today.AddHours(23)) continue;

                if (lastSaleDay != saleTime.Date) { saleSeq = 0; lastSaleDay = saleTime.Date; }
                saleSeq++;
                var sale = new Sale
                {
                    InvoiceNumber = InvoiceService.SaleInvoiceNumber(saleSeq, saleTime.Date),
                    Date = saleTime,
                    User = cashiers[random.Next(cashiers.Count)],
                    Device = (DeviceRole)random.Next(0, 4),
                    PaymentMethod = RandomPayment(random),
                    Discount = 0,
                    Total = 0,
                    Profit = 0
                };

                if (random.Next(3) == 0)
                    sale.Customer = customers[random.Next(customers.Count)];

                var itemCount = random.Next(1, 5);
                decimal total = 0;
                decimal cost = 0;

                for (var i = 0; i < itemCount; i++)
                {
                    var product = products[random.Next(products.Count)];
                    var available = stockOf[product.Id];
                    if (available <= 0) continue;

                    var isPhone = phoneCategoryIds.Contains(product.CategoryId);
                    var maxQty = Math.Min(available, isPhone ? 2 : 5);
                    var qty = random.Next(1, maxQty + 1);

                    var item = new SaleItem
                    {
                        ProductId = product.Id,
                        ProductName = product.Name,
                        Quantity = qty,
                        UnitPrice = product.SellPrice,
                        BuyPrice = product.BuyPrice
                    };
                    sale.Items.Add(item);
                    total += qty * product.SellPrice;
                    cost += qty * product.BuyPrice;
                    stockOf[product.Id] -= qty;
                }

                if (sale.Items.Count == 0) continue;

                if (random.Next(10) == 0)
                {
                    sale.Discount = Math.Round(total * 0.05m, 0);
                }

                sale.Total = total - sale.Discount;
                sale.Profit = sale.Total - cost;
                db.Sales.Add(sale);
            }
        }

        // استعادة المخزون إلى الأرصدة المطلوبة تماماً
        for (var i = 0; i < products.Count; i++)
        {
            products[i].Stock = initialStock[i];
            products[i].UpdatedAt = today;
        }

        SeedRepairJobs(db, random, today, customers, products, phoneCategoryIds, stockOf);

        db.SaveChanges();
    }

    /// <summary>قطع صيانة على مدى الأسابيع الماضية بحالات مختلفة.</summary>
    private static void SeedRepairJobs(
        AppDbContext db,
        Random random,
        DateTime today,
        List<Customer> customers,
        List<Product> products,
        HashSet<int> phoneCategoryIds,
        Dictionary<int, int> stockOf)
    {
        var repairParts = products.Where(p => p.Device == DeviceRole.Repair).ToList();
        var phoneProducts = products.Where(p => phoneCategoryIds.Contains(p.CategoryId)).ToList();
        if (repairParts.Count == 0 || phoneProducts.Count == 0) return;

        var techs = db.Users.Where(u => u.Role == UserRole.RepairTech).ToList();
        if (techs.Count == 0) return;

        var issues = new[]
        {
            "كسر شاشة اللمس", "عدم شحن البطارية", "توقف الجهاز عن الإقلاع",
            "منفذ الشحن متضرر", "عطل في السماعة", "تسريب ماء",
            "بطارية سريعة التفريغ", "الجهاز لا يتعرف على الشريحة", "ظهور خطوط على الشاشة",
            "عطل في زر الطاقة", "اللوحة الخلفية مكسورة", "مشكلة في الكاميرا"
        };

        var repairSeq = 0;
        var lastRepairDay = (DateTime?)null;
        var start = today.AddDays(-44);

        for (var dayOffset = 0; dayOffset < 45; dayOffset++)
        {
            var day = start.AddDays(dayOffset);
            if (day > today) break;

            var jobsToday = random.Next(1, 4);
            for (var j = 0; j < jobsToday; j++)
            {
                var phone = phoneProducts[random.Next(phoneProducts.Count)];
                var customer = customers[random.Next(customers.Count)];
                var statusRoll = random.Next(100);
                var status = statusRoll < 15 ? RepairStatus.Received
                    : statusRoll < 45 ? RepairStatus.InProgress
                    : statusRoll < 75 ? RepairStatus.Completed
                    : RepairStatus.Delivered;

                var laborFee = new[] { 20000m, 25000m, 25000m, 30000m, 40000m }[random.Next(5)];
                var paymentMethod = RandomPayment(random);
                var paid = status == RepairStatus.Delivered
                    ? paymentMethod != PaymentMethod.Credit
                    : random.Next(2) == 0;

                var receivedDate = day.AddHours(random.Next(9, 19)).AddMinutes(random.Next(0, 59));
                if (lastRepairDay != receivedDate.Date) { repairSeq = 0; lastRepairDay = receivedDate.Date; }
                repairSeq++;
                var job = new RepairJob
                {
                    JobNumber = InvoiceService.RepairJobNumber(repairSeq, receivedDate.Date),
                    ReceivedDate = receivedDate,
                    CompletedDate = null,
                    DeliveredDate = null,
                    CustomerName = customer.Name,
                    CustomerPhone = customer.Phone,
                    DeviceName = phone.Name,
                    Issue = issues[random.Next(issues.Length)],
                    Notes = random.Next(4) == 0 ? "الزبون يريد الجهاز في أسرع وقت ممكن" : null,
                    Status = status,
                    User = techs[random.Next(techs.Count)],
                    Device = DeviceRole.Repair,
                    LaborFee = laborFee,
                    PaymentMethod = paymentMethod,
                    IsPaid = paid,
                    PartsTotal = 0,
                    Total = 0
                };

                if (status == RepairStatus.Completed)
                    job.CompletedDate = job.ReceivedDate.AddDays(random.Next(1, 4));
                if (status == RepairStatus.Delivered)
                {
                    job.CompletedDate = job.ReceivedDate.AddDays(random.Next(1, 4));
                    job.DeliveredDate = job.CompletedDate.Value.AddDays(random.Next(0, 3));
                }

                var partsCount = random.Next(0, 3);
                var partsTotal = 0m;
                for (var p = 0; p < partsCount; p++)
                {
                    var part = repairParts[random.Next(repairParts.Count)];
                    var qty = random.Next(1, 3);
                    job.Parts.Add(new RepairPart
                    {
                        ProductId = part.Id,
                        ProductName = part.Name,
                        Quantity = qty,
                        UnitPrice = part.SellPrice
                    });
                    partsTotal += qty * part.SellPrice;
                    stockOf[part.Id] -= qty;
                }

                job.PartsTotal = partsTotal;
                job.Total = laborFee + partsTotal;
                db.RepairJobs.Add(job);
            }
        }
    }

    private static void AddProduct(
        List<Product> products,
        List<int> initialStock,
        string name,
        string englishName,
        Category category,
        decimal buyPrice,
        decimal sellPrice,
        int stock,
        int minStock,
        DeviceRole device,
        string barcode,
        DateTime createdAt)
    {
        products.Add(new Product
        {
            Name = name,
            EnglishName = englishName,
            Category = category,
            SubType = SubTypeHelper.Detect(name, category.Name),
            BuyPrice = buyPrice,
            SellPrice = sellPrice,
            Stock = stock,
            MinStock = minStock,
            Barcode = barcode,
            Device = device,
            IsActive = true,
            CreatedAt = createdAt,
            UpdatedAt = DateTime.Now.Date
        });
        initialStock.Add(stock);
    }

    private static int[] Distribute(int total, int[] weights)
    {
        var result = new int[weights.Length];
        var weightSum = weights.Sum();
        var allocated = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            var share = (int)((long)total * weights[i] / weightSum);
            result[i] = share;
            allocated += share;
        }
        for (var i = 0; i < total - allocated; i++)
            result[i]++;
        return result;
    }

    private static PaymentMethod RandomPayment(Random random)
    {
        var roll = random.Next(10);
        if (roll < 7) return PaymentMethod.Cash;
        if (roll < 9) return PaymentMethod.Card;
        return PaymentMethod.Credit;
    }
}
