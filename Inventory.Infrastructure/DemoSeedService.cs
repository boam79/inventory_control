using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure;

public sealed record DemoSeedResult(
    bool Applied,
    int DocumentCount,
    int ItemCount,
    string Message);

public static class DemoSeedService
{
    public const int DefaultItemCount = 1_000;
    public const int DefaultTargetDocuments = 50_000;
    public const int BusyThreshold = 50;
    public const int LotTrackedLimit = 300;

    private static readonly (string Prefix, string Category, string Spec, decimal Price, decimal MonthlyBase, bool PreferLot)[] Templates =
    [
        ("주사기 1ml", "주사", "1ml", 80m, 4m, true),
        ("수액세트", "수액", "세트", 1_200m, 3m, false),
        ("PICC 소모품", "시술재료", "세트", 35_000m, 1m, true),
        ("Chemoport 소모품", "시술재료", "세트", 28_000m, 1m, true),
        ("초음파 젤", "시술재료", "250ml", 4_500m, 2m, false),
        ("멸균장갑", "소독", "M", 350m, 5m, false),
        ("거즈", "드레싱", "4x4", 80m, 6m, false),
        ("알코올솜", "소독", "통", 60m, 4m, false),
        ("생리식염수", "수액", "1L", 900m, 3m, false),
        ("반창고", "드레싱", "롤", 150m, 3m, false)
    ];

    private static readonly string[] DepartmentNames = ["외래", "시술실", "검사실", "간호"];
    private static readonly string[] SupplierNames = ["메디칼공급", "한국의료", "대한소모품"];

    public static int CountBusinessDocuments(InventoryDbContext db) =>
        db.Documents.Count(d =>
            d.Type == DocumentType.Receipt
            || d.Type == DocumentType.Issue
            || d.Type == DocumentType.Adjustment);

    public static bool ShouldAutoSeed(InventoryDbContext db) =>
        CountBusinessDocuments(db) == 0;

    public static decimal SeasonalFactor(string category, int month)
    {
        var season = month switch
        {
            12 or 1 or 2 => 0,
            3 or 4 or 5 => 1,
            6 or 7 or 8 => 2,
            _ => 3
        };
        return category switch
        {
            "주사" => new[] { 1.35m, 1.05m, 0.85m, 1.00m }[season],
            "수액" => new[] { 0.80m, 1.00m, 1.40m, 0.95m }[season],
            "시술재료" => new[] { 0.75m, 1.10m, 1.45m, 0.90m }[season],
            "소독" => new[] { 1.50m, 1.00m, 0.70m, 1.15m }[season],
            "드레싱" => new[] { 1.25m, 0.95m, 0.80m, 1.20m }[season],
            _ => 1m
        };
    }

    public static DemoSeedResult TryAutoSeed(
        InventoryDbContext db,
        DateTime today,
        string actor = "demo-seed",
        int? itemCount = null)
    {
        if (!ShouldAutoSeed(db))
        {
            var existing = CountBusinessDocuments(db);
            return new DemoSeedResult(
                false,
                existing,
                db.Items.Count(),
                $"거래 {existing}건이 있어 자동 시드하지 않았습니다. 기존 입고·출고는 그대로입니다.");
        }

        return Generate(db, today, force: false, actor: actor, itemCount: itemCount);
    }

    /// <summary>
    /// Fully wipes sample items and transactions (not just transactions), then regenerates
    /// exactly <paramref name="itemCount"/> items. Unlike <see cref="Generate"/>'s master-creation
    /// step, which only ever adds items to reach a target, this guarantees the item count shrinks
    /// too when a smaller <paramref name="itemCount"/> is requested (e.g. going from 10,000 to 1,000).
    /// </summary>
    public static DemoSeedResult ReplaceSample(
        InventoryDbContext db,
        DateTime today,
        string actor = "demo-seed",
        int? itemCount = null)
    {
        db.ChangeTracker.Clear();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF");
        db.Database.ExecuteSqlRaw("DELETE FROM StockLines");
        db.Database.ExecuteSqlRaw("DELETE FROM Documents");
        db.Database.ExecuteSqlRaw("DELETE FROM Lots");
        db.Database.ExecuteSqlRaw("DELETE FROM MonthCloses");
        db.Database.ExecuteSqlRaw("DELETE FROM Items");
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON");
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return Generate(db, today, force: true, actor: actor, itemCount: itemCount);
    }

    public static DemoSeedResult Generate(
        InventoryDbContext db,
        DateTime today,
        bool force = false,
        int? targetDocuments = null,
        string actor = "demo-seed",
        int? itemCount = null)
    {
        _ = targetDocuments;
        var wantedItems = itemCount ?? DefaultItemCount;
        var existing = CountBusinessDocuments(db);
        if (existing >= BusyThreshold && !force)
        {
            return new DemoSeedResult(
                false,
                existing,
                db.Items.Count(),
                $"이미 거래 {existing}건이 있어 테스트 데이터를 만들지 않았습니다. 운영 DB를 덮어쓰지 않습니다. 그래도 추가하려면 확인 후 강제 생성을 선택하세요.");
        }

        if (db.Items.Count() >= DefaultItemCount && !force && wantedItems >= DefaultItemCount)
        {
            if (existing > 0)
            {
                return new DemoSeedResult(
                    false,
                    existing,
                    db.Items.Count(),
                    $"품목이 이미 {db.Items.Count()}개입니다. 중복 생성하지 않았습니다.");
            }
        }

        var start = new DateTime(today.Year, today.Month, 1).AddMonths(-12);
        EnsureMasters(db, start, today, wantedItems);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.Database.ExecuteSqlRaw("PRAGMA synchronous = OFF");
        using var tx = db.Database.BeginTransaction();

        var items = db.Items.OrderBy(i => i.Code).ToList();
        var departments = db.Departments.ToList();
        var suppliers = db.Suppliers.ToList();
        var lots = db.Lots.ToList();
        var remaining = lots.ToDictionary(l => l, l => l.Quantity);
        var lotsByItem = lots.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.ToList());
        var documents = new List<StockDocument>(items.Count * 20);
        var lotSeq = 0;
        foreach (var item in items)
        {
            var template = TemplateFor(item);
            var rhythm = Rhythm(item.Code, item.Category);
            for (var offset = 0; offset < 13; offset++)
            {
                var monthStart = start.AddMonths(offset);
                var days = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
                var monthRng = new Random(StableHash(item.Code) + offset * 997 + monthStart.Year);
                if (ShouldSkipMonth(rhythm, offset) && offset is not 0 and not 12)
                {
                    continue;
                }

                var qty = MonthIssueQty(rhythm, item.Category, monthStart.Month, offset, monthRng);
                if (qty < 1m)
                {
                    continue;
                }

                var issueDays = PickDays(monthRng, days, rhythm.Band);
                var remainingQty = qty;
                var firstIssueDay = issueDays[0];
                var receiveLead = 2 + monthRng.Next(9);
                var receiveDate = new DateTime(monthStart.Year, monthStart.Month, Math.Max(1, firstIssueDay - receiveLead));
                if (receiveDate > today)
                {
                    continue;
                }

                var price = JitterPrice(template.Price, item.Code, monthRng);
                var receiveQty = Math.Max(qty, Math.Round(qty * (1.25m + (decimal)monthRng.NextDouble()), 0, MidpointRounding.AwayFromZero));
                AddReceipt(
                    receiveDate,
                    today,
                    item,
                    price,
                    receiveQty,
                    suppliers[monthRng.Next(suppliers.Count)],
                    lots,
                    lotsByItem,
                    remaining,
                    documents,
                    actor,
                    ref lotSeq);
                if (rhythm.Band == VolumeBand.Heavy && monthRng.Next(3) == 0)
                {
                    var extraDay = Math.Min(days, 14 + monthRng.Next(10));
                    var extraDate = new DateTime(monthStart.Year, monthStart.Month, extraDay);
                    if (extraDate <= today && extraDate != receiveDate)
                    {
                        AddReceipt(
                            extraDate,
                            today,
                            item,
                            JitterPrice(template.Price, item.Code + extraDay, monthRng),
                            Math.Max(4m, Math.Round(qty * 0.35m, 0, MidpointRounding.AwayFromZero)),
                            suppliers[monthRng.Next(suppliers.Count)],
                            lots,
                            lotsByItem,
                            remaining,
                            documents,
                            actor,
                            ref lotSeq);
                    }
                }

                for (var d = 0; d < issueDays.Count; d++)
                {
                    var date = new DateTime(monthStart.Year, monthStart.Month, issueDays[d]);
                    if (date > today)
                    {
                        continue;
                    }

                    var leftSlots = issueDays.Count - d;
                    var each = d == issueDays.Count - 1
                        ? remainingQty
                        : Math.Max(1m, Math.Round(remainingQty / leftSlots, 0, MidpointRounding.AwayFromZero));
                    each = Math.Min(each, remainingQty);
                    if (each < 1m)
                    {
                        continue;
                    }

                    remainingQty -= each;
                    EnsureStock(
                        date,
                        today,
                        item,
                        price,
                        each * 2,
                        suppliers[monthRng.Next(suppliers.Count)],
                        lots,
                        lotsByItem,
                        remaining,
                        documents,
                        actor,
                        ref lotSeq);
                    AddIssue(
                        date,
                        item,
                        each,
                        departments[monthRng.Next(departments.Count)],
                        lotsByItem,
                        remaining,
                        documents,
                        actor);
                }
            }
        }

        var first = items[0];
        var firstRhythm = Rhythm(first.Code, first.Category);
        AddReceipt(
            today,
            today,
            first,
            first.ReferencePrice > 0 ? first.ReferencePrice : TemplateFor(first).Price,
            Math.Max(12m, firstRhythm.MonthlyBase),
            suppliers[0],
            lots,
            lotsByItem,
            remaining,
            documents,
            actor,
            ref lotSeq);

        foreach (var lot in lots)
        {
            lot.Quantity = remaining[lot];
        }

        db.Lots.AddRange(lots.Where(l => l.Id == 0));
        db.SaveChanges();

        foreach (var chunk in documents.Chunk(800))
        {
            foreach (var doc in chunk)
            {
                foreach (var line in doc.Lines)
                {
                    if (line.LotId is null or 0)
                    {
                        var lot = lotsByItem[line.ItemId].First(l => l.LotNumber == line.LotNumber);
                        line.LotId = lot.Id;
                    }
                }
            }

            db.Documents.AddRange(chunk);
            db.SaveChanges();
        }

        db.ChangeTracker.AutoDetectChangesEnabled = true;
        tx.Commit();
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
            $"테스트 데이터 품목 {items.Count}개, 거래 {count}건을 추가했습니다. 기존 거래는 삭제하지 않았습니다.");
    }

    public static decimal SeasonalWinterVsSummer(string category) =>
        SeasonalFactor(category, 1) - SeasonalFactor(category, 7);

    private enum VolumeBand
    {
        Heavy,
        Regular,
        Occasional,
        Rare
    }

    private readonly record struct ItemRhythm(
        VolumeBand Band,
        decimal MonthlyBase,
        decimal Slope,
        int Phase,
        int SkipMod);

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var ch in value)
            {
                hash = (hash * 31) + ch;
            }

            return hash & 0x7fffffff;
        }
    }

    private static (string Prefix, string Category, string Spec, decimal Price, decimal MonthlyBase, bool PreferLot) TemplateFor(Item item)
    {
        if (!string.IsNullOrWhiteSpace(item.Category))
        {
            var byCategory = Templates.FirstOrDefault(t => t.Category == item.Category);
            if (byCategory.Prefix is not null)
            {
                return byCategory;
            }
        }

        return Templates[StableHash(item.Code) % Templates.Length];
    }

    private static ItemRhythm Rhythm(string code, string category)
    {
        var hash = StableHash(code + "|" + category);
        var roll = hash % 100;
        var band = roll switch
        {
            < 5 => VolumeBand.Heavy,
            < 22 => VolumeBand.Regular,
            < 65 => VolumeBand.Occasional,
            _ => VolumeBand.Rare
        };
        var monthlyBase = band switch
        {
            VolumeBand.Heavy => 80 + (hash % 240),
            VolumeBand.Regular => 14 + (hash % 52),
            VolumeBand.Occasional => 3 + (hash % 11),
            _ => 1 + (hash % 4)
        };
        var slope = ((hash / 97) % 17 - 8) * 0.014m;
        var phase = (int)(hash % 12);
        var skipMod = band switch
        {
            VolumeBand.Rare => 2 + (int)(hash % 3),
            VolumeBand.Occasional => 6 + (int)(hash % 5),
            _ => 0
        };
        return new ItemRhythm(band, monthlyBase, slope, phase, skipMod);
    }

    private static bool ShouldSkipMonth(ItemRhythm rhythm, int offset) =>
        rhythm.SkipMod > 0 && (offset + 3) % rhythm.SkipMod == 0;

    private static decimal MonthIssueQty(ItemRhythm rhythm, string category, int month, int offset, Random rng)
    {
        var factor = SeasonalFactor(category, month);
        var trend = 1m + (offset - 6) * rhythm.Slope;
        var wave = 1m + ((month + rhythm.Phase) % 6 - 2) * 0.11m;
        var noise = 0.68m + (decimal)rng.NextDouble() * 0.64m;
        var spike = (StableHash(category) + offset) % 11 == 0 && rhythm.Band != VolumeBand.Rare
            ? 1.55m + (rhythm.Phase % 5) * 0.12m
            : 1m;
        var qty = Math.Round(rhythm.MonthlyBase * factor * trend * wave * noise * spike, 0, MidpointRounding.AwayFromZero);
        if (qty < 1m && rhythm.Band != VolumeBand.Rare)
        {
            qty = 1m;
        }

        return Math.Max(0m, qty);
    }

    private static List<int> PickDays(Random rng, int daysInMonth, VolumeBand band)
    {
        var count = band switch
        {
            VolumeBand.Heavy => 4 + rng.Next(4),
            VolumeBand.Regular => 2 + rng.Next(3),
            VolumeBand.Occasional => 1 + rng.Next(2),
            _ => 1
        };
        count = Math.Clamp(count, 1, Math.Min(8, daysInMonth));
        var chosen = new SortedSet<int>();
        var guard = 0;
        while (chosen.Count < count && guard++ < 40)
        {
            chosen.Add(1 + rng.Next(daysInMonth));
        }

        if (chosen.Count == 0)
        {
            chosen.Add(Math.Min(12, daysInMonth));
        }

        return chosen.ToList();
    }

    private static decimal JitterPrice(decimal price, string salt, Random rng)
    {
        var basePrice = price <= 0 ? 100m : price;
        var factor = 0.88m + (StableHash(salt) % 25) * 0.01m + (decimal)rng.NextDouble() * 0.06m;
        return Math.Round(basePrice * factor, 0, MidpointRounding.AwayFromZero);
    }

    private static void EnsureStock(
        DateTime date,
        DateTime today,
        Item item,
        decimal price,
        decimal need,
        Supplier supplier,
        List<Lot> lots,
        Dictionary<int, List<Lot>> lotsByItem,
        Dictionary<Lot, decimal> remaining,
        List<StockDocument> documents,
        string actor,
        ref int lotSeq)
    {
        var onHand = lotsByItem.TryGetValue(item.Id, out var list)
            ? list.Sum(l => remaining[l])
            : 0m;
        if (onHand >= need)
        {
            return;
        }

        var add = need - onHand + (item.LotTracked ? need : need * 3);
        AddReceipt(date, today, item, price, add, supplier, lots, lotsByItem, remaining, documents, actor, ref lotSeq);
    }

    private static void AddReceipt(
        DateTime date,
        DateTime today,
        Item item,
        decimal price,
        decimal quantity,
        Supplier supplier,
        List<Lot> lots,
        Dictionary<int, List<Lot>> lotsByItem,
        Dictionary<Lot, decimal> remaining,
        List<StockDocument> documents,
        string actor,
        ref int lotSeq)
    {
        lotSeq++;
        Lot lot;
        if (!item.LotTracked && lotsByItem.TryGetValue(item.Id, out var existing) && existing.Count > 0)
        {
            lot = existing[0];
            remaining[lot] += quantity;
        }
        else
        {
            var lotNumber = item.LotTracked ? $"R{date:yyyyMM}-{item.Code}-{lotSeq:00000}" : "OPEN";
            var found = lotsByItem.TryGetValue(item.Id, out var same)
                ? same.FirstOrDefault(l => l.LotNumber == lotNumber)
                : null;
            if (found is not null)
            {
                lot = found;
                remaining[lot] += quantity;
            }
            else
            {
                lot = new Lot
                {
                    ItemId = item.Id,
                    LotNumber = lotNumber,
                    ReceivedDate = date,
                    ExpiryDate = item.ExpiryTracked ? date.AddMonths(10 + lotSeq % 6) : null,
                    Quantity = quantity,
                    UnitCost = price,
                    SupplierId = supplier.Id
                };
                if (lotSeq % 17 == 0 && item.ExpiryTracked)
                {
                    lot.ExpiryDate = today.AddDays(20 + lotSeq % 40);
                }

                lots.Add(lot);
                remaining[lot] = quantity;
                if (!lotsByItem.TryGetValue(item.Id, out var bag))
                {
                    bag = [];
                    lotsByItem[item.Id] = bag;
                }

                bag.Add(lot);
            }
        }

        documents.Add(new StockDocument
        {
            Type = DocumentType.Receipt,
            DocumentDate = date,
            SupplierId = supplier.Id,
            DocumentNo = $"{item.Code}-{date:yyyyMMdd}-{lotSeq}",
            UserName = actor,
            Lines =
            {
                new StockLine
                {
                    ItemId = item.Id,
                    LotNumber = lot.LotNumber,
                    ExpiryDate = lot.ExpiryDate,
                    Quantity = quantity,
                    UnitPrice = price,
                    Amount = quantity * price,
                    UnitCostSnapshot = price
                }
            }
        });
    }

    private static void AddIssue(
        DateTime date,
        Item item,
        decimal quantity,
        Department department,
        Dictionary<int, List<Lot>> lotsByItem,
        Dictionary<Lot, decimal> remaining,
        List<StockDocument> documents,
        string actor)
    {
        if (!lotsByItem.TryGetValue(item.Id, out var bag))
        {
            return;
        }

        var left = quantity;
        foreach (var lot in bag
                     .Where(l => remaining[l] > 0
                                 && l.ReceivedDate.Date <= date.Date
                                 && (l.ExpiryDate == null || l.ExpiryDate.Value.Date >= date.Date))
                     .OrderBy(l => l.ExpiryDate ?? DateTime.MaxValue))
        {
            if (left <= 0)
            {
                break;
            }

            var take = Math.Min(left, remaining[lot]);
            remaining[lot] -= take;
            left -= take;
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
                        Quantity = take,
                        UnitPrice = lot.UnitCost,
                        Amount = take * lot.UnitCost,
                        UnitCostSnapshot = lot.UnitCost
                    }
                }
            });
        }
    }

    private static void EnsureMasters(InventoryDbContext db, DateTime start, DateTime today, int wantedItems)
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

        var existingCodes = db.Items.Select(i => i.Code).ToHashSet();
        var toCreate = Math.Max(0, wantedItems - existingCodes.Count);
        var created = 0;
        var lotAssigned = db.Items.Count(i => i.LotTracked);
        var batch = new List<Item>();
        for (var n = 1; created < toCreate; n++)
        {
            var code = $"P{n:00000}";
            if (!existingCodes.Add(code))
            {
                continue;
            }

            var template = Templates[(n - 1) % Templates.Length];
            var lot = lotAssigned < LotTrackedLimit && template.PreferLot;
            if (lot)
            {
                lotAssigned++;
            }

            batch.Add(new Item
            {
                Code = code,
                Name = $"{template.Prefix} #{n:0000}",
                Category = template.Category,
                Specification = template.Spec,
                Unit = "개",
                MinStock = lot ? 20m : 5m,
                TargetStock = 80m,
                ReferencePrice = template.Price,
                LotTracked = lot,
                ExpiryTracked = lot,
                OpeningStatus = OpeningStatus.Unset
            });
            created++;
            if (n > wantedItems + 100_000)
            {
                break;
            }
            if (batch.Count >= 400)
            {
                db.Items.AddRange(batch);
                db.SaveChanges();
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            db.Items.AddRange(batch);
            db.SaveChanges();
        }

        var items = db.Items.ToList();
        var openingLots = new List<Lot>();
        foreach (var item in items)
        {
            if (item.OpeningStatus == OpeningStatus.Confirmed)
            {
                continue;
            }

            var template = TemplateFor(item);
            var rhythm = Rhythm(item.Code, item.Category);
            var qty = Math.Max(24m, rhythm.MonthlyBase * (rhythm.Band == VolumeBand.Heavy ? 36 : 28));
            var lots = item.LotTracked ? 2 : 1;
            for (var i = 0; i < lots; i++)
            {
                openingLots.Add(new Lot
                {
                    ItemId = item.Id,
                    LotNumber = item.LotTracked ? $"OPEN-{item.Code}-{i + 1}" : "OPEN",
                    ReceivedDate = start.AddDays(-2 + i),
                    ExpiryDate = item.ExpiryTracked ? (i == 0 ? today.AddDays(40) : start.AddMonths(10)) : null,
                    Quantity = qty,
                    UnitCost = item.ReferencePrice > 0 ? item.ReferencePrice : template.Price
                });
            }

            item.OpeningStatus = OpeningStatus.Confirmed;
            item.MovingAverageCost = item.ReferencePrice;
            if (item.ReferencePrice == 0)
            {
                item.ReferencePrice = template.Price;
            }
        }

        foreach (var chunk in openingLots.Chunk(400))
        {
            db.Lots.AddRange(chunk);
            db.SaveChanges();
        }

        db.SaveChanges();
    }
}
