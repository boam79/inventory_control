using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure;

public sealed record DemoSeedResult(
    bool Applied,
    int DocumentCount,
    int ItemCount,
    string Message);

public static class DemoSeedService
{
    public const int DefaultTargetDocuments = 20_000;
    public const int BusyThreshold = 50;

    private static readonly (string Code, string Name, string Category, string Spec, decimal Min, decimal Price, decimal DailyBase)[] Catalog =
    [
        ("M001", "주사기", "소모품", "1ml", 200m, 80m, 28m),
        ("M002", "수액세트", "소모품", "세트", 80m, 1_200m, 8m),
        ("M003", "PICC 관련 소모품", "시술재료", "세트", 15m, 35_000m, 2m),
        ("M004", "Chemoport 관련 소모품", "시술재료", "세트", 12m, 28_000m, 1m),
        ("M005", "초음파 젤", "소모품", "250ml", 20m, 4_500m, 3m),
        ("M006", "멸균장갑", "소모품", "M", 150m, 350m, 18m),
        ("M007", "거즈", "소모품", "4x4", 300m, 80m, 36m),
        ("M008", "알코올솜", "소모품", "통", 80m, 60m, 12m)
    ];

    private static readonly string[] DepartmentNames = ["외래", "시술실", "검사실", "간호"];
    private static readonly string[] SupplierNames = ["메디칼공급", "한국의료", "대한소모품"];

    public static int CountBusinessDocuments(InventoryDbContext db) =>
        db.Documents.Count(d =>
            d.Type == DocumentType.Receipt
            || d.Type == DocumentType.Issue
            || d.Type == DocumentType.Adjustment);

    public static DemoSeedResult Generate(
        InventoryDbContext db,
        DateTime today,
        bool force = false,
        int? targetDocuments = null,
        string actor = "demo-seed")
    {
        var target = targetDocuments ?? DefaultTargetDocuments;
        var existing = CountBusinessDocuments(db);
        if (existing >= BusyThreshold && !force)
        {
            return new DemoSeedResult(
                false,
                existing,
                db.Items.Count(),
                $"이미 거래 {existing}건이 있어 테스트 데이터를 만들지 않았습니다. 운영 DB를 덮어쓰지 않습니다. 그래도 추가하려면 확인 후 강제 생성을 선택하세요.");
        }

        var start = new DateTime(today.Year, today.Month, 1).AddMonths(-12);
        EnsureMasters(db, start, today);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.Database.ExecuteSqlRaw("PRAGMA synchronous = OFF");

        var items = db.Items.ToList();
        var departments = db.Departments.ToList();
        var suppliers = db.Suppliers.ToList();
        var lots = db.Lots.ToList();
        var remaining = lots.ToDictionary(l => l, l => l.Quantity);
        var lotSeq = 0;
        var documents = new List<StockDocument>(target + 8);
        var rng = new Random(20260820);
        var days = Workdays(start, today);
        var remainingTarget = target;

        for (var dayIndex = 0; dayIndex < days.Count; dayIndex++)
        {
            var date = days[dayIndex];
            var quota = dayIndex == days.Count - 1
                ? remainingTarget
                : Math.Max(1, remainingTarget / (days.Count - dayIndex));
            var emitted = EmitDay(
                date,
                today,
                quota,
                items,
                departments,
                suppliers,
                lots,
                remaining,
                documents,
                rng,
                actor,
                ref lotSeq);
            remainingTarget = Math.Max(0, remainingTarget - emitted);
        }

        FillToTarget(
            today,
            remainingTarget,
            items,
            departments,
            suppliers,
            lots,
            remaining,
            documents,
            rng,
            actor,
            ref lotSeq);
        IssueItemDownToZero(today, items, "M008", departments[0], lots, remaining, documents, actor, target >= DefaultTargetDocuments);
        AddReceipt(
            today,
            today,
            items[0],
            Catalog.First(c => c.Code == items[0].Code).Price,
            Catalog.First(c => c.Code == items[0].Code).DailyBase * 15,
            suppliers[0],
            lots,
            remaining,
            documents,
            actor,
            ref lotSeq);
        var gauze = items.FirstOrDefault(i => i.Code == "M007");
        if (gauze is not null)
        {
            gauze.MinStock = 10_000m;
        }

        foreach (var lot in lots)
        {
            lot.Quantity = remaining[lot];
        }

        db.ChangeTracker.AutoDetectChangesEnabled = true;
        db.Lots.AddRange(lots.Where(l => l.Id == 0));
        db.SaveChanges();

        foreach (var chunk in documents.Chunk(400))
        {
            foreach (var doc in chunk)
            {
                foreach (var line in doc.Lines)
                {
                    if (line.LotId is null or 0)
                    {
                        var lot = lots.First(l => l.LotNumber == line.LotNumber && l.ItemId == line.ItemId);
                        line.LotId = lot.Id;
                    }
                }
            }

            db.Documents.AddRange(chunk);
            db.SaveChanges();
        }

        db.SaveChanges();
        new AuditService(db).Write(
            actor,
            "DemoSeed.Generate",
            "Documents",
            documents.Count.ToString(),
            existing.ToString(),
            documents.Count.ToString(),
            "테스트 가상 거래. 운영 데이터가 아닙니다.");

        var count = CountBusinessDocuments(db);
        return new DemoSeedResult(
            true,
            count,
            items.Count,
            $"테스트 데이터 {count}건을 추가했습니다. 기존 거래는 삭제하지 않았습니다. 대시보드에서 KPI와 월별 그래프를 확인하세요.");
    }

    private static void FillToTarget(
        DateTime today,
        int remainingTarget,
        List<Item> items,
        List<Department> departments,
        List<Supplier> suppliers,
        List<Lot> lots,
        Dictionary<Lot, decimal> remaining,
        List<StockDocument> documents,
        Random rng,
        string actor,
        ref int lotSeq)
    {
        var guard = 0;
        while (remainingTarget > 0 && guard++ < remainingTarget + 8)
        {
            var item = items[guard % items.Count];
            var catalog = Catalog.First(c => c.Code == item.Code);
            if (AddIssue(today, item, 1m, departments[guard % departments.Count], lots, remaining, documents, actor))
            {
                remainingTarget--;
                continue;
            }

            remainingTarget -= AddReceipt(
                today,
                today,
                item,
                catalog.Price,
                catalog.DailyBase * 20,
                suppliers[rng.Next(suppliers.Count)],
                lots,
                remaining,
                documents,
                actor,
                ref lotSeq);
        }
    }

    private static void IssueItemDownToZero(
        DateTime today,
        List<Item> items,
        string code,
        Department department,
        List<Lot> lots,
        Dictionary<Lot, decimal> remaining,
        List<StockDocument> documents,
        string actor,
        bool enabled)
    {
        if (!enabled)
        {
            return;
        }
        var item = items.FirstOrDefault(i => i.Code == code);
        if (item is null)
        {
            return;
        }

        foreach (var lot in lots.Where(l => l.ItemId == item.Id && remaining[l] > 0).ToList())
        {
            var qty = remaining[lot];
            if (!AddIssue(today, item, qty, department, lots, remaining, documents, actor))
            {
                remaining[lot] = 0m;
            }
        }
    }

    private static int EmitDay(
        DateTime date,
        DateTime today,
        int quota,
        List<Item> items,
        List<Department> departments,
        List<Supplier> suppliers,
        List<Lot> lots,
        Dictionary<Lot, decimal> remaining,
        List<StockDocument> documents,
        Random rng,
        string actor,
        ref int lotSeq)
    {
        var emitted = 0;
        var seasonal = Seasonal(date.Month) * (1m + 0.012m * MonthsFrom(new DateTime(today.Year, today.Month, 1).AddMonths(-12), date));
        var weekWave = 1m + 0.12m * (decimal)Math.Sin(date.DayOfYear / 7.0);

        foreach (var item in items)
        {
            if (emitted >= quota)
            {
                break;
            }

            var catalog = Catalog.First(c => c.Code == item.Code);
            var onHand = lots.Where(l => l.ItemId == item.Id).Sum(l => remaining[l]);
            var want = Math.Max(1m, Math.Round(catalog.DailyBase * seasonal * weekWave, 0, MidpointRounding.AwayFromZero));
            if (onHand < want * 10)
            {
                emitted += AddReceipt(
                    date,
                    today,
                    item,
                    catalog.Price,
                    Math.Max(want * 25, catalog.DailyBase * 30),
                    suppliers[rng.Next(suppliers.Count)],
                    lots,
                    remaining,
                    documents,
                    actor,
                    ref lotSeq);
            }
        }

        var itemIndex = 0;
        while (emitted < quota)
        {
            var item = items[itemIndex % items.Count];
            itemIndex++;
            var catalog = Catalog.First(c => c.Code == item.Code);
            var want = Math.Max(1m, Math.Round(catalog.DailyBase * seasonal * weekWave, 0, MidpointRounding.AwayFromZero));
            var chunk = Math.Max(1m, Math.Min(want, 4m));
            if (AddIssue(date, item, chunk, departments[rng.Next(departments.Count)], lots, remaining, documents, actor))
            {
                emitted++;
            }
            else if (emitted < quota)
            {
                emitted += AddReceipt(
                    date,
                    today,
                    item,
                    catalog.Price,
                    catalog.DailyBase * 40,
                    suppliers[rng.Next(suppliers.Count)],
                    lots,
                    remaining,
                    documents,
                    actor,
                    ref lotSeq);
                if (emitted < quota && AddIssue(date, item, chunk, departments[rng.Next(departments.Count)], lots, remaining, documents, actor))
                {
                    emitted++;
                }
                else if (emitted >= quota)
                {
                    break;
                }
                else
                {
                    break;
                }
            }
            else
            {
                break;
            }

            if (itemIndex > items.Count * 80)
            {
                break;
            }
        }

        if (emitted < quota && date.Day % 17 == 0)
        {
            var item = items[rng.Next(items.Count)];
            var lot = lots.Where(l => l.ItemId == item.Id && remaining[l] > 1)
                .OrderBy(l => l.ExpiryDate)
                .FirstOrDefault();
            if (lot is not null)
            {
                remaining[lot] -= 1m;
                documents.Add(new StockDocument
                {
                    Type = DocumentType.Adjustment,
                    DocumentDate = date,
                    UserName = actor,
                    Reason = "테스트 폐기",
                    AdjustmentType = AdjustmentType.Disposal,
                    Lines =
                    {
                        new StockLine
                        {
                            ItemId = item.Id,
                            LotNumber = lot.LotNumber,
                            ExpiryDate = lot.ExpiryDate,
                            Quantity = 1m,
                            UnitPrice = lot.UnitCost,
                            Amount = lot.UnitCost,
                            UnitCostSnapshot = lot.UnitCost
                        }
                    }
                });
                emitted++;
            }
        }

        return emitted;
    }

    private static int AddReceipt(
        DateTime date,
        DateTime today,
        Item item,
        decimal price,
        decimal quantity,
        Supplier supplier,
        List<Lot> lots,
        Dictionary<Lot, decimal> remaining,
        List<StockDocument> documents,
        string actor,
        ref int lotSeq)
    {
        lotSeq++;
        var lotNumber = $"R{date:yyyyMMdd}-{item.Code}-{lotSeq:00000}";
        var expiry = date.AddMonths(10 + lotSeq % 8);
        if (lotSeq % 11 == 0)
        {
            expiry = today.AddDays(12 + lotSeq % 40);
        }

        var lot = new Lot
        {
            ItemId = item.Id,
            LotNumber = lotNumber,
            ReceivedDate = date,
            ExpiryDate = expiry,
            Quantity = quantity,
            UnitCost = price,
            SupplierId = supplier.Id
        };
        lots.Add(lot);
        remaining[lot] = quantity;
        documents.Add(new StockDocument
        {
            Type = DocumentType.Receipt,
            DocumentDate = date,
            SupplierId = supplier.Id,
            DocumentNo = lotNumber,
            UserName = actor,
            Lines =
            {
                new StockLine
                {
                    ItemId = item.Id,
                    LotNumber = lotNumber,
                    ExpiryDate = expiry,
                    Quantity = quantity,
                    UnitPrice = price,
                    Amount = quantity * price,
                    UnitCostSnapshot = price
                }
            }
        });
        return 1;
    }

    private static bool AddIssue(
        DateTime date,
        Item item,
        decimal quantity,
        Department department,
        List<Lot> lots,
        Dictionary<Lot, decimal> remaining,
        List<StockDocument> documents,
        string actor)
    {
        var lot = lots
            .Where(l => l.ItemId == item.Id
                        && remaining[l] >= quantity
                        && l.ReceivedDate.Date <= date.Date
                        && (l.ExpiryDate == null || l.ExpiryDate.Value.Date >= date.Date))
            .OrderBy(l => l.ExpiryDate ?? DateTime.MaxValue)
            .ThenBy(l => l.ReceivedDate)
            .FirstOrDefault();
        if (lot is null)
        {
            return false;
        }

        remaining[lot] -= quantity;
        var cost = lot.UnitCost;
        documents.Add(new StockDocument
        {
            Type = DocumentType.Issue,
            DocumentDate = date,
            DepartmentId = department.Id,
            UserName = actor,
            Lines =
            {
                new StockLine
                {
                    ItemId = item.Id,
                    LotNumber = lot.LotNumber,
                    ExpiryDate = lot.ExpiryDate,
                    Quantity = quantity,
                    UnitPrice = cost,
                    Amount = quantity * cost,
                    UnitCostSnapshot = cost
                }
            }
        });
        return true;
    }

    private static void EnsureMasters(InventoryDbContext db, DateTime start, DateTime today)
    {
        foreach (var name in DepartmentNames)
        {
            if (!db.Departments.Any(d => d.Name == name))
            {
                db.Departments.Add(new Department { Name = name });
            }
        }

        foreach (var name in SupplierNames)
        {
            if (!db.Suppliers.Any(s => s.Name == name))
            {
                db.Suppliers.Add(new Supplier { Name = name });
            }
        }

        db.SaveChanges();

        foreach (var row in Catalog)
        {
            var item = db.Items.SingleOrDefault(i => i.Code == row.Code);
            if (item is null)
            {
                item = new Item
                {
                    Code = row.Code,
                    Name = row.Name,
                    Category = row.Category,
                    Specification = row.Spec,
                    Unit = "개",
                    MinStock = row.Min,
                    TargetStock = row.Min * 4,
                    ReferencePrice = row.Price,
                    LotTracked = true,
                    ExpiryTracked = true,
                    OpeningStatus = OpeningStatus.Unset
                };
                db.Items.Add(item);
                db.SaveChanges();
            }
            else if (item.ReferencePrice == 0)
            {
                item.ReferencePrice = row.Price;
            }

            if (item.OpeningStatus != OpeningStatus.Confirmed)
            {
                for (var i = 0; i < 3; i++)
                {
                    var qty = row.DailyBase * 40;
                    db.Lots.Add(new Lot
                    {
                        ItemId = item.Id,
                        LotNumber = $"OPEN-{row.Code}-{i + 1}",
                        ReceivedDate = start.AddDays(-3 + i),
                        ExpiryDate = i == 0 ? today.AddDays(25) : start.AddMonths(8 + i),
                        Quantity = qty,
                        UnitCost = row.Price
                    });
                }

                item.OpeningStatus = OpeningStatus.Confirmed;
                item.MovingAverageCost = row.Price;
            }
        }

        db.SaveChanges();
    }

    private static List<DateTime> Workdays(DateTime start, DateTime end)
    {
        var list = new List<DateTime>();
        for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
        {
            if (d.DayOfWeek != DayOfWeek.Sunday)
            {
                list.Add(d);
            }
        }

        return list;
    }

    private static decimal Seasonal(int month) => month switch
    {
        1 => 0.82m,
        2 => 0.88m,
        3 => 1.05m,
        4 => 1.00m,
        5 => 1.08m,
        6 => 1.16m,
        7 => 1.24m,
        8 => 1.12m,
        9 => 0.98m,
        10 => 0.90m,
        11 => 1.08m,
        12 => 0.76m,
        _ => 1m
    };

    private static int MonthsFrom(DateTime start, DateTime date) =>
        (date.Year - start.Year) * 12 + date.Month - start.Month;
}
