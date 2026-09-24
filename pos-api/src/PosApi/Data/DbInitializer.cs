using Microsoft.EntityFrameworkCore;
using PosApi.Domain;
using PosApi.Services;

namespace PosApi.Data;

/// Chạy sau khi migrate: tạo users demo + 200 sản phẩm giả + surcharges + customer mẫu.
public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        // ── Users ──
        if (!await db.Users.AnyAsync())
        {
            db.Users.AddRange(
                new User { Username = "admin", FullName = "Quản Lý", Email = "admin@digisme.local", PasswordHash = Hashing.Hash("admin123"), Role = UserRole.Manager },
                new User { Username = "cau1", FullName = "Cẩm Tú", Email = "cau1@digisme.local", PasswordHash = Hashing.Hash("cau11234"), Role = UserRole.Cashier });
            await db.SaveChangesAsync();
            log.LogInformation("Seeded users: admin/admin123 (Manager), cau1/cau11234 (Cashier)");
        }

        // ── Units ──
        if (!await db.Units.AnyAsync())
        {
            db.Units.AddRange(
                new Unit { Name = "cái" }, new Unit { Name = "lon" }, new Unit { Name = "chai" },
                new Unit { Name = "lốc" }, new Unit { Name = "thùng" }, new Unit { Name = "gói" },
                new Unit { Name = "túi" }, new Unit { Name = "kg" });
            await db.SaveChangesAsync();
        }

        // ── Categories ──
        if (!await db.Categories.AnyAsync())
        {
            string[] cats = { "Nước giải khát", "Bia & Rượu", "Bánh kẹo", "Gói bánh ăn sáng", "Sữa & Đồ uống dinh dưỡng", "Hóa mỹ phẩm", "Thực phẩm khô", "Gia vị" };
            db.Categories.AddRange(cats.Select(c => new Category { Name = c }));
            await db.SaveChangesAsync();
        }

        // ── Surcharges ──
        if (!await db.Surcharges.AnyAsync())
        {
            db.Surcharges.AddRange(
                new Surcharge { Name = "Thuế VAT 10%", Type = SurchargeType.Percent, Value = 10, ApplyMode = SurchargeApply.Auto },
                new Surcharge { Name = "Phí đóng gói",     Type = SurchargeType.Fixed,     Value = 5000m,   ApplyMode = SurchargeApply.Auto   },
                new Surcharge { Name = "Phụ thu Tết",      Type = SurchargeType.Fixed,     Value = 10000m,  ApplyMode = SurchargeApply.Manual,
                    ActiveFrom = new DateTime(2026, 1, 28, 0, 0, 0, DateTimeKind.Utc), ActiveUntil = new DateTime(2026, 2, 4, 23, 59, 59, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
        }

        // ── Products: 200 sản phẩm giả ──
        if (!await db.Products.AnyAsync())
        {
            var rng = new Random(20260828);
            var catIds = await db.Categories.Select(c => c.Id).ToListAsync();
            var unitLon = await db.Units.FirstAsync(u => u.Name == "lon");
            var unitLoc = await db.Units.FirstAsync(u => u.Name == "lốc");
            var unitThung = await db.Units.FirstAsync(u => u.Name == "thùng");
            var unitCai = await db.Units.FirstAsync(u => u.Name == "cái");

            string[] names = {
                "Coca-Cola 330ml", "Pepsi 330ml", "Nước suối Lavie 500ml", "Nước mía 450ml", "Red Bull 250ml",
                "Sữa tươi Vinamilk 1L", "Sữa chua TH true", "Bánh Kinh Đô Socola", "Bánh quy Oreo", "Socola Nutella",
                "Nước mắm Nam Ngư 500ml", "Dầu ăn Neptune 1L", "Muối hạt 250g", "Đường cát 1kg", "Bánh tráng rế 300g",
                "Xà phòng Lifebuoy", "Nước rửa chén Sunlight", "Bao nilon 40x50", "Kem đánh răng P/S", "Chai nước 2L"
            };

            var products = new List<Product>();
            var productUnits = new List<ProductUnit>();
            for (int i = 1; i <= 200; i++)
            {
                string baseName = names[(i - 1) % names.Length];
                string name = i <= names.Length ? baseName : $"{baseName} - SP{i}";
                int cost = rng.Next(2000, 40000);
                int retail = cost + cost * rng.Next(30, 80) / 100;
                int stock = rng.Next(0, 150);
                var p = new Product
                {
                    Name = name,
                    CategoryId = catIds[rng.Next(catIds.Count)],
                    Barcode = i.ToString("D9"),          // 000000001 ... 000000200
                    CostPrice = cost,
                    RetailPrice = retail,
                    WholesalePrice = retail - retail / 6,
                    MemberPrice = retail - retail / 10,
                    MinStock = 10,
                    CurrentStock = stock,
                    // SP 1..3 là hàng cân (kg) để test decimal qty
                    IsWeighed = i <= 3
                };
                products.Add(p);

                int defaultUnit = catIds.Contains(p.CategoryId) && (name.Contains("Coca") || name.Contains("Pepsi") || name.Contains("Red Bull")) ? unitLon.Id : unitCai.Id;
                productUnits.Add(new ProductUnit { Product = p, UnitId = defaultUnit, IsBase = true, ConversionRate = 1 });
                if (defaultUnit == unitLon.Id)
                {
                    productUnits.Add(new ProductUnit { Product = p, UnitId = unitLoc.Id, IsBase = false, ConversionRate = 6 });
                    productUnits.Add(new ProductUnit { Product = p, UnitId = unitThung.Id, IsBase = false, ConversionRate = 24 });
                }
            }

            db.Products.AddRange(products);
            db.ProductUnits.AddRange(productUnits);

            // Ledger entry ban đầu cho mỗi sản phẩm (dùng navigation để EF gán FK sau SaveChanges)
            db.StockLedger.AddRange(products.Select(p => new StockLedgerEntry
            {
                Product = p,
                Qty = p.CurrentStock,
                Balance = p.CurrentStock,
                RefType = LedgerRefType.Adjustment,
                Note = "Seeding ban đầu"
            }));

            await db.SaveChangesAsync();
            log.LogInformation("Seeded 200 products with barcodes 000000001..000000200");
        }

        // ── StoreSetting (dòng duy nhất) ──
        if (!await db.StoreSettings.AnyAsync())
        {
            db.StoreSettings.Add(new StoreSetting
            {
                StoreName = "DigiSME Store Demo",
                Address = "123 Đường Demo, Quận 1, TP.HCM",
                VietQrBankCode = "MB",
                VietQrAccountNo = "0987654321",
                VietQrAccountName = "DIGISME DEMO",
                VatRatePercent = 10m
            });
            await db.SaveChangesAsync();
            log.LogInformation("Seeded StoreSetting");
        }

        // ── Customer mẫu ──
        if (!await db.Customers.AnyAsync())
        {
            var khach = new Customer { Phone = "0901234567", Name = "Khách VIP Minh" };
            khach.Loyalty = new LoyaltyAccount { Points = 1250, Tier = "Gold", LifetimeSpend = 1_250_000 };
            db.Customers.Add(khach);
            await db.SaveChangesAsync();
        }
    }
}
