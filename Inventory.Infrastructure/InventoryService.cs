using Inventory.Core;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure;

public sealed class InventoryService
{
    private readonly InventoryDbContext _db;
    private readonly AuditService _audit;
    private readonly string _actor;

    public InventoryService(InventoryDbContext db, string actor)
    {
        _db = db;
        _actor = actor;
        _audit = new AuditService(db);
    }

    public Item CreateItem(
        string code,
        string name,
        string category,
        string specification,
        string unit,
        decimal minStock,
        bool lotTracked = true,
        bool expiryTracked = true,
        int? defaultDepartmentId = null)
    {
        if (_db.Items.Any(i => i.Code == code))
        {
            throw new InvalidOperationException("품목코드가 이미 있습니다.");
        }

        var item = new Item
        {
            Code = code.Trim(),
            Name = name.Trim(),
            Category = category,
            Specification = specification,
            Unit = unit,
            MinStock = minStock,
            LotTracked = lotTracked,
            ExpiryTracked = expiryTracked,
            OpeningStatus = OpeningStatus.Unset,
            DefaultDepartmentId = defaultDepartmentId
        };
        _db.Items.Add(item);
        _db.SaveChanges();
        return item;
    }

    /// <summary>
    /// 입고 화면에서 자유 입력한 품목명을 코드/이름 정확 일치로 찾거나, 없으면 간단 품목으로 등록한다.
    /// LOT·유효기간 비관리·기초재고 Confirmed로 만들어 바로 입고·재고 반영이 되게 한다.
    /// </summary>
    public Item FindOrCreateItemByName(string nameOrCode)
    {
        var q = (nameOrCode ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            throw new InvalidOperationException("품목명은 필수입니다.");
        }

        var byCode = _db.Items.FirstOrDefault(i => i.Code == q);
        if (byCode is not null)
        {
            return byCode;
        }

        var byName = _db.Items.AsEnumerable()
            .FirstOrDefault(i => i.IsActive && string.Equals(i.Name, q, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName;
        }

        var code = NextGeneratedItemCode();
        var item = CreateItem(code, q, "기타", "개", "개", 0m, lotTracked: false, expiryTracked: false);
        item.OpeningStatus = OpeningStatus.Confirmed;
        _db.SaveChanges();
        return item;
    }

    private string NextGeneratedItemCode()
    {
        const string prefix = "N";
        var used = _db.Items.AsNoTracking()
            .Where(i => i.Code.StartsWith(prefix))
            .Select(i => i.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var n = 1; n < 100_000; n++)
        {
            var code = $"{prefix}{n:D4}";
            if (!used.Contains(code))
            {
                return code;
            }
        }

        return prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }

    public void RenameItem(string code, string newName)
    {
        var item = RequireItem(code);
        var before = item.Name;
        item.Name = newName;
        _db.SaveChanges();
        _audit.Write(_actor, "Item.Rename", "Item", code, before, newName, "품목명 변경");
    }

    public void DeactivateItem(string code)
    {
        var item = RequireItem(code);
        item.IsActive = false;
        _db.SaveChanges();
    }

    public void DeleteItem(string code)
    {
        var item = RequireItem(code);
        if (_db.StockLines.Any(l => l.ItemId == item.Id) || _db.Lots.Any(l => l.ItemId == item.Id))
        {
            throw new InvalidOperationException("거래가 있는 품목은 삭제할 수 없습니다. 사용중지하십시오.");
        }

        _db.Items.Remove(item);
        _db.SaveChanges();
    }

    /// <summary>
    /// 재고 목록에서 품목을 숨긴다(사용중지). 전표 이력은 남긴다.
    /// 현재고가 남아 있으면 거부한다.
    /// </summary>
    public void HideItemFromStock(string code)
    {
        var item = RequireItem(code);
        if (!item.IsActive)
        {
            return;
        }

        if ((GetOnHand(code) ?? 0m) > 0m)
        {
            throw new InvalidOperationException("재고가 남아 있는 품목은 삭제할 수 없습니다. 입출고 전표를 먼저 삭제하세요.");
        }

        item.IsActive = false;
        _db.SaveChanges();
    }

    public IReadOnlyList<Item> ItemsAvailableForIssue() =>
        _db.Items.Where(i => i.IsActive && i.OpeningStatus == OpeningStatus.Confirmed).ToList();

    public Department CreateDepartment(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("부서 이름은 필수입니다.");
        }

        var row = new Department { Name = name.Trim() };
        _db.Departments.Add(row);
        _db.SaveChanges();
        return row;
    }

    public void DeactivateDepartment(int id)
    {
        var row = _db.Departments.Single(d => d.Id == id);
        row.IsActive = false;
        _db.SaveChanges();
    }

    public IReadOnlyList<Department> SearchDepartments(string query)
    {
        query ??= string.Empty;
        return _db.Departments.Where(d => d.Name.Contains(query)).ToList();
    }

    public Supplier CreateSupplier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("공급업체 이름은 필수입니다.");
        }

        var row = new Supplier { Name = name.Trim() };
        _db.Suppliers.Add(row);
        _db.SaveChanges();
        return row;
    }

    public void DeactivateSupplier(int id)
    {
        var row = _db.Suppliers.Single(s => s.Id == id);
        row.IsActive = false;
        _db.SaveChanges();
    }

    public IReadOnlyList<Supplier> SearchSuppliers(string query)
    {
        query ??= string.Empty;
        return _db.Suppliers.Where(s => s.Name.Contains(query)).ToList();
    }

    public decimal? GetOnHand(string code)
    {
        var item = RequireItem(code);
        if (item.OpeningStatus == OpeningStatus.Unset)
        {
            return null;
        }

        return _db.Lots.Where(l => l.ItemId == item.Id).Sum(l => (decimal?)l.Quantity) ?? 0m;
    }

    public OpeningStatus GetOpeningStatus(string code) => RequireItem(code).OpeningStatus;

    public void SaveOpeningDraft(string code, string lotNumber, decimal quantity, DateTime receivedDate, DateTime? expiry)
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException("초기재고 수량은 0보다 커야 합니다.");
        }

        var item = RequireItem(code);
        item.OpeningStatus = OpeningStatus.InProgress;
        var openingCost = item.MovingAverageCost != 0 ? item.MovingAverageCost : item.ReferencePrice;
        UpsertLot(item, lotNumber, receivedDate, expiry, quantity, openingCost, null);
        _db.SaveChanges();
    }

    public void ConfirmOpening(string code)
    {
        var item = RequireItem(code);
        if (!_db.Lots.Any(l => l.ItemId == item.Id))
        {
            throw new InvalidOperationException("LOT 수량을 입력한 뒤 확정하세요.");
        }

        item.OpeningStatus = OpeningStatus.Confirmed;
        RecalcAverage(item);
        _db.SaveChanges();
    }

    public StockDocument Receive(
        DateTime date,
        int? supplierId,
        string? documentNo,
        IReadOnlyList<ReceiptLineRequest> lines)
    {
        EnsurePeriodOpen(date);
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("입고 품목이 없습니다.");
        }

        var duplicate = supplierId is not null
            && !string.IsNullOrWhiteSpace(documentNo)
            && _db.Documents.Any(d =>
                d.Type == DocumentType.Receipt
                && !d.IsCancelled
                && d.SupplierId == supplierId
                && d.DocumentNo == documentNo);

        var doc = new StockDocument
        {
            Type = DocumentType.Receipt,
            DocumentDate = date,
            SupplierId = supplierId,
            DocumentNo = documentNo,
            UserName = _actor,
            DuplicateWarning = duplicate
        };

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new InvalidOperationException("수량은 0보다 커야 합니다.");
            }

            if (line.UnitPrice < 0)
            {
                throw new InvalidOperationException("단가는 음수일 수 없습니다.");
            }

            var item = RequireItem(line.ItemCode);
            if (item.LotTracked && string.IsNullOrWhiteSpace(line.LotNumber))
            {
                throw new InvalidOperationException("LOT 관리 품목은 LOT가 필요합니다.");
            }

            if (item.ExpiryTracked && line.ExpiryDate is null)
            {
                throw new InvalidOperationException("유효기간 관리 품목은 유효기간이 필요합니다.");
            }

            var lotNumber = string.IsNullOrWhiteSpace(line.LotNumber) ? "NONE" : line.LotNumber.Trim();
            var lot = UpsertLot(item, lotNumber, date, line.ExpiryDate, line.Quantity, line.UnitPrice, supplierId);
            RecalcAverage(item);
            doc.Lines.Add(new StockLine
            {
                ItemId = item.Id,
                LotId = lot.Id,
                LotNumber = lotNumber,
                ExpiryDate = line.ExpiryDate,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Amount = MoneyFormulas.LineAmount(line.Quantity, line.UnitPrice),
                UnitCostSnapshot = line.UnitPrice
            });
        }

        _db.Documents.Add(doc);
        _db.SaveChanges();
        return doc;
    }

    public StockDocument Issue(
        DateTime date,
        int? departmentId,
        IReadOnlyList<IssueLineRequest> lines,
        string? issuedTo = null)
    {
        EnsurePeriodOpen(date);
        var doc = new StockDocument
        {
            Type = DocumentType.Issue,
            DocumentDate = date,
            DepartmentId = departmentId,
            UserName = _actor,
            IssuedTo = string.IsNullOrWhiteSpace(issuedTo) ? null : issuedTo.Trim()
        };

        foreach (var line in lines)
        {
            var item = RequireItem(line.ItemCode);
            if (!item.IsActive)
            {
                throw new InvalidOperationException("사용중지 품목은 출고할 수 없습니다.");
            }

            if (item.OpeningStatus != OpeningStatus.Confirmed)
            {
                throw new InvalidOperationException("초기재고가 확정되지 않은 품목은 출고할 수 없습니다.");
            }

            if (line.Quantity <= 0)
            {
                throw new InvalidOperationException("수량은 0보다 커야 합니다.");
            }

            var lot = SelectLot(item, line.LotNumber, date, line.Quantity);
            RecalcAverage(item);
            var costSnapshot = CurrentUnitCost(item, lot);
            lot.Quantity -= line.Quantity;
            RecalcAverage(item);
            doc.Lines.Add(new StockLine
            {
                ItemId = item.Id,
                LotId = lot.Id,
                LotNumber = lot.LotNumber,
                ExpiryDate = lot.ExpiryDate,
                Quantity = line.Quantity,
                UnitPrice = costSnapshot,
                Amount = MoneyFormulas.LineAmount(line.Quantity, costSnapshot),
                UnitCostSnapshot = costSnapshot
            });
        }

        _db.Documents.Add(doc);
        _db.SaveChanges();
        return doc;
    }

    public StockDocument Adjust(
        DateTime date,
        string itemCode,
        string lotNumber,
        decimal delta,
        AdjustmentType type,
        string reason)
    {
        EnsurePeriodOpen(date);
        var item = RequireItem(itemCode);
        var lot = _db.Lots.Single(l => l.ItemId == item.Id && l.LotNumber == lotNumber);
        var before = lot.Quantity;
        lot.Quantity += delta;
        if (lot.Quantity < 0)
        {
            throw new InvalidOperationException("조정 후 수량이 음수가 됩니다.");
        }

        var doc = new StockDocument
        {
            Type = DocumentType.Adjustment,
            DocumentDate = date,
            UserName = _actor,
            Reason = reason,
            AdjustmentType = type,
            Lines =
            {
                new StockLine
                {
                    ItemId = item.Id,
                    LotId = lot.Id,
                    LotNumber = lot.LotNumber,
                    Quantity = delta,
                    UnitPrice = lot.UnitCost,
                    Amount = MoneyFormulas.LineAmount(Math.Abs(delta), lot.UnitCost),
                    UnitCostSnapshot = lot.UnitCost
                }
            }
        };
        _db.Documents.Add(doc);
        RecalcAverage(item);
        _db.SaveChanges();
        _ = before;
        return doc;
    }

    public StockDocument CancelDocument(int documentId, string reason)
    {
        var original = _db.Documents.Include(d => d.Lines).Single(d => d.Id == documentId);
        if (original.IsCancelled)
        {
            throw new InvalidOperationException("이미 취소된 거래입니다.");
        }

        EnsurePeriodOpen(original.DocumentDate);
        original.IsCancelled = true;
        foreach (var line in original.Lines)
        {
            if (line.LotId is null)
            {
                continue;
            }

            var lot = _db.Lots.Single(l => l.Id == line.LotId.Value);
            if (original.Type == DocumentType.Receipt || original.Type == DocumentType.Opening)
            {
                lot.Quantity -= line.Quantity;
            }
            else if (original.Type == DocumentType.Issue)
            {
                lot.Quantity += line.Quantity;
            }
        }

        var reversal = new StockDocument
        {
            Type = DocumentType.Reversal,
            DocumentDate = original.DocumentDate,
            UserName = _actor,
            Reason = reason,
            ReversesDocumentId = original.Id
        };
        _db.Documents.Add(reversal);
        var itemIds = original.Lines.Select(l => l.ItemId).Distinct().ToList();
        _db.SaveChanges();
        HideUnusedItemsFromStock(itemIds);
        _db.SaveChanges();
        return reversal;
    }

    public void CloseMonth(int year, int month)
    {
        if (_db.MonthCloses.Any(c => c.Year == year && c.Month == month && c.IsClosed))
        {
            throw new InvalidOperationException("이미 마감된 월입니다.");
        }

        _db.MonthCloses.Add(new MonthClose
        {
            Year = year,
            Month = month,
            ClosedAtUtc = DateTime.UtcNow,
            ClosedBy = _actor,
            IsClosed = true
        });
        _db.SaveChanges();
    }

    public void ReopenMonth(int year, int month, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("마감 해제 사유는 필수입니다.");
        }

        var row = _db.MonthCloses.Single(c => c.Year == year && c.Month == month && c.IsClosed);
        row.IsClosed = false;
        row.ReopenReason = reason;
        _db.SaveChanges();
    }

    public decimal UsageQuantity(int year, int month) =>
        _db.Documents
            .Where(d => d.Type == DocumentType.Issue && !d.IsCancelled
                        && d.DocumentDate.Year == year && d.DocumentDate.Month == month)
            .SelectMany(d => d.Lines)
            .Sum(l => (decimal?)l.Quantity) ?? 0m;

    public IReadOnlyList<Lot> LotsForItem(string code)
    {
        var item = RequireItem(code);
        return _db.Lots.Where(l => l.ItemId == item.Id).OrderBy(l => l.ExpiryDate).ToList();
    }

    public IReadOnlyList<Item> SearchStock(string query)
    {
        query ??= string.Empty;
        return _db.Items.Where(i => i.Code.Contains(query) || i.Name.Contains(query)).ToList();
    }

    public IReadOnlyList<StockSnapshot> SearchStockSnapshots(string query, int expiryWarningDays = 90, int? take = null)
    {
        var today = DateTime.Today;
        var warnUntil = today.AddDays(expiryWarningDays);
        query ??= string.Empty;
        var itemQuery = _db.Items.AsNoTracking().Where(i => i.IsActive);
        if (!string.IsNullOrWhiteSpace(query))
        {
            itemQuery = itemQuery.Where(i =>
                i.Code.Contains(query) || i.Name.Contains(query) || i.Category.Contains(query));
        }

        var items = itemQuery
            .OrderBy(i => i.Code)
            .Select(i => new { i.Id, i.Code, i.Name, i.IsActive, i.OpeningStatus, i.MinStock, i.MovingAverageCost, i.ReferencePrice })
            .ToList();
        var qty = _db.Lots.AsNoTracking()
            .GroupBy(l => l.ItemId)
            .Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToList()
            .ToDictionary(x => x.Key, x => x.Qty);
        var expiring = _db.Lots.AsNoTracking()
            .Where(l => l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate.Value.Date <= warnUntil)
            .Select(l => l.ItemId)
            .Distinct()
            .ToHashSet();

        IEnumerable<StockSnapshot> snaps = items.Select(item =>
        {
            decimal? onHand = item.OpeningStatus == OpeningStatus.Unset
                ? null
                : qty.GetValueOrDefault(item.Id);
            var status = Classify(item.IsActive, item.OpeningStatus, item.MinStock, onHand, expiring.Contains(item.Id));
            var unitCost = item.MovingAverageCost != 0 ? item.MovingAverageCost : item.ReferencePrice;
            decimal? stockValue = onHand is { } hand
                ? MoneyFormulas.LineAmount(hand, unitCost)
                : null;
            return new StockSnapshot(item.Code, item.Name, onHand, status, item.MinStock, unitCost, stockValue);
        });
        if (take is not null)
        {
            snaps = snaps.Take(take.Value);
        }

        return snaps.ToList();
    }

    public IReadOnlyList<Item> ReorderItems()
    {
        var codes = SearchStockSnapshots(string.Empty)
            .Where(row => row.Status is StockStatusKind.Reorder or StockStatusKind.OutOfStock)
            .Select(row => row.Code)
            .ToList();
        return _db.Items.AsNoTracking().Where(i => codes.Contains(i.Code)).ToList();
    }

    public IReadOnlyList<StockDocument> ListDocuments(int? take = null)
    {
        var query = _db.Documents.AsNoTracking().Include(d => d.Lines).OrderByDescending(d => d.Id);
        return (take is null ? query : query.Take(take.Value)).ToList();
    }

    /// <param name="type">
    /// When set, only that document type is returned (e.g. Issue for the 출고 chip).
    /// When null and <paramref name="voucherTypesOnly"/> is true, Receipt + Issue only
    /// (excludes Reversal / Adjustment / Opening which used to appear as fake 「출고」).
    /// </param>
    /// <param name="includeCancelled">
    /// When false (default), cancelled/deleted slips are omitted so 「최근 전표」목록에서 사라진다.
    /// Soft-cancel + stock reversal still runs; pass true only when audit/history needs them.
    /// </param>
    public IReadOnlyList<DocumentSummary> ListDocumentSummaries(
        int take,
        DocumentType? type = null,
        bool voucherTypesOnly = false,
        bool includeCancelled = false)
    {
        var query = _db.Documents.AsNoTracking().AsQueryable();
        if (type is { } typed)
        {
            query = query.Where(d => d.Type == typed);
        }
        else if (voucherTypesOnly)
        {
            query = query.Where(d => d.Type == DocumentType.Receipt || d.Type == DocumentType.Issue);
        }

        if (!includeCancelled)
        {
            query = query.Where(d => !d.IsCancelled);
        }

        var summaries = query
            .OrderByDescending(d => d.Id)
            .Take(take)
            .Select(d => new
            {
                d.Id,
                d.Type,
                d.DocumentDate,
                d.DocumentNo,
                d.IsCancelled,
                LineCount = d.Lines.Count,
                TotalQuantity = d.Lines.Sum(l => l.Quantity),
                TotalAmount = d.Lines.Sum(l => l.Amount),
                FirstItemId = d.Lines.OrderBy(l => l.Id).Select(l => (int?)l.ItemId).FirstOrDefault(),
                FirstUnitPrice = d.Lines.OrderBy(l => l.Id).Select(l => (decimal?)l.UnitPrice).FirstOrDefault()
            })
            .ToList();

        var itemIds = summaries.Where(s => s.FirstItemId.HasValue).Select(s => s.FirstItemId!.Value).Distinct().ToList();
        var names = _db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionary(i => i.Id, i => i.Name);

        return summaries.Select(s => new DocumentSummary(
                s.Id,
                s.Type,
                s.DocumentDate,
                s.DocumentNo,
                s.IsCancelled,
                s.LineCount,
                s.FirstItemId.HasValue && names.TryGetValue(s.FirstItemId.Value, out var name) ? name : null,
                s.TotalQuantity,
                s.TotalAmount,
                s.LineCount == 1 ? s.FirstUnitPrice : null))
            .ToList();
    }

    public DocumentDetail GetDocumentDetail(int documentId)
    {
        var doc = _db.Documents.AsNoTracking()
            .Include(d => d.Lines)
            .SingleOrDefault(d => d.Id == documentId)
            ?? throw new InvalidOperationException("전표를 찾을 수 없습니다.");

        var itemIds = doc.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = _db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionary(i => i.Id, i => i);
        string? supplierName = null;
        if (doc.SupplierId is { } sid)
        {
            supplierName = _db.Suppliers.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefault();
        }

        string? departmentName = null;
        if (doc.DepartmentId is { } did)
        {
            departmentName = _db.Departments.AsNoTracking().Where(d => d.Id == did).Select(d => d.Name).FirstOrDefault();
        }

        var lines = doc.Lines.OrderBy(l => l.Id).Select(l =>
        {
            items.TryGetValue(l.ItemId, out var item);
            var lot = string.IsNullOrWhiteSpace(l.LotNumber) || l.LotNumber == "NONE" ? null : l.LotNumber;
            return new DocumentLineDetail(
                item?.Code ?? "",
                item?.Name ?? "—",
                l.Quantity,
                l.UnitPrice,
                lot);
        }).ToList();

        return new DocumentDetail(
            doc.Id,
            doc.Type,
            doc.DocumentDate,
            doc.IsCancelled,
            supplierName,
            departmentName,
            lines,
            doc.IssuedTo);
    }

    /// <summary>전표 삭제(재고 반대 처리). 원본은 취소 표시로 남긴다.</summary>
    public StockDocument DeleteDocument(int documentId) => CancelDocument(documentId, "삭제");

    private static StockStatusKind Classify(Item item, decimal? onHand, bool expiring) =>
        Classify(item.IsActive, item.OpeningStatus, item.MinStock, onHand, expiring);

    private static StockStatusKind Classify(
        bool isActive,
        OpeningStatus opening,
        decimal minStock,
        decimal? onHand,
        bool expiring)
    {
        if (!isActive)
        {
            return StockStatusKind.Inactive;
        }

        if (opening == OpeningStatus.Unset || onHand is null)
        {
            return StockStatusKind.Unset;
        }

        if (onHand == 0)
        {
            return StockStatusKind.OutOfStock;
        }

        if (onHand <= minStock)
        {
            return StockStatusKind.Reorder;
        }

        return expiring ? StockStatusKind.Expiring : StockStatusKind.Normal;
    }

    private Lot SelectLot(Item item, string? lotNumber, DateTime issueDate, decimal quantity)
    {
        Lot lot;
        if (!string.IsNullOrWhiteSpace(lotNumber))
        {
            lot = _db.Lots.SingleOrDefault(l => l.ItemId == item.Id && l.LotNumber == lotNumber)
                  ?? throw new InvalidOperationException("LOT를 찾을 수 없습니다.");
        }
        else
        {
            lot = _db.Lots
                      .Where(l => l.ItemId == item.Id && l.Quantity > 0)
                      .Where(l => !item.ExpiryTracked || l.ExpiryDate == null || l.ExpiryDate.Value.Date >= issueDate.Date)
                      .OrderBy(l => l.ExpiryDate ?? DateTime.MaxValue)
                      .ThenBy(l => l.ReceivedDate)
                      .FirstOrDefault()
                  ?? throw new InvalidOperationException("사용 가능 LOT가 없습니다.");
        }

        if (lot.Quantity < quantity)
        {
            throw new InvalidOperationException("사용 가능 재고보다 많은 수량은 저장할 수 없습니다.");
        }

        if (item.ExpiryTracked && lot.ExpiryDate is { } expiry && expiry.Date < issueDate.Date)
        {
            throw new InvalidOperationException("유효기간이 지난 LOT는 사용할 수 없습니다.");
        }

        if (issueDate.Date < lot.ReceivedDate.Date)
        {
            throw new InvalidOperationException("사용일이 입고일보다 빠를 수 없습니다.");
        }

        return lot;
    }

    private Lot UpsertLot(
        Item item,
        string lotNumber,
        DateTime receivedDate,
        DateTime? expiry,
        decimal addQuantity,
        decimal unitCost,
        int? supplierId)
    {
        var lot = _db.Lots.SingleOrDefault(l => l.ItemId == item.Id && l.LotNumber == lotNumber);
        if (lot is null)
        {
            lot = new Lot
            {
                ItemId = item.Id,
                LotNumber = lotNumber,
                ReceivedDate = receivedDate,
                ExpiryDate = expiry,
                Quantity = addQuantity,
                UnitCost = unitCost,
                SupplierId = supplierId
            };
            _db.Lots.Add(lot);
            _db.SaveChanges();
            return lot;
        }

        var totalQty = lot.Quantity + addQuantity;
        if (totalQty > 0)
        {
            lot.UnitCost = ((lot.Quantity * lot.UnitCost) + (addQuantity * unitCost)) / totalQty;
        }

        lot.Quantity = totalQty;
        return lot;
    }

    private void RecalcAverage(Item item)
    {
        var lots = _db.Lots.Where(l => l.ItemId == item.Id && l.Quantity > 0).ToList();
        var qty = lots.Sum(l => l.Quantity);
        item.MovingAverageCost = qty == 0 ? 0 : lots.Sum(l => l.Quantity * l.UnitCost) / qty;
    }

    private static decimal CurrentUnitCost(Item item, Lot lot)
    {
        if (item.MovingAverageCost != 0)
        {
            return item.MovingAverageCost;
        }

        return lot.UnitCost != 0 ? lot.UnitCost : item.ReferencePrice;
    }

    private void EnsurePeriodOpen(DateTime date)
    {
        if (_db.MonthCloses.Any(c => c.Year == date.Year && c.Month == date.Month && c.IsClosed))
        {
            throw new InvalidOperationException("마감된 기간의 거래는 저장할 수 없습니다.");
        }
    }

    /// <summary>
    /// 전표 취소 후 현재고 0이고 다른 유효 전표가 없는 품목을 재고 목록에서 숨긴다.
    /// </summary>
    private void HideUnusedItemsFromStock(IReadOnlyCollection<int> itemIds)
    {
        foreach (var itemId in itemIds)
        {
            var item = _db.Items.SingleOrDefault(i => i.Id == itemId);
            if (item is null || !item.IsActive)
            {
                continue;
            }

            var onHand = _db.Lots.Where(l => l.ItemId == itemId).Sum(l => (decimal?)l.Quantity) ?? 0m;
            if (onHand > 0m)
            {
                continue;
            }

            var hasLive = _db.StockLines.Any(l =>
                l.ItemId == itemId
                && !l.Document.IsCancelled
                && l.Document.Type != DocumentType.Reversal);
            if (hasLive)
            {
                continue;
            }

            item.IsActive = false;
        }
    }

    private Item RequireItem(string code) =>
        _db.Items.SingleOrDefault(i => i.Code == code)
        ?? throw new InvalidOperationException("미등록 품목입니다.");
}
