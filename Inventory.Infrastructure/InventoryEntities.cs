namespace Inventory.Infrastructure;

public enum OpeningStatus
{
    Unset = 0,
    InProgress = 1,
    Confirmed = 2,
    RecountNeeded = 3
}

public enum DocumentType
{
    Receipt = 0,
    Issue = 1,
    Adjustment = 2,
    Opening = 3,
    Reversal = 4
}

public enum AdjustmentType
{
    ReturnToSupplier = 0,
    CancelIssue = 1,
    Damage = 2,
    Disposal = 3,
    Expired = 4,
    CountUp = 5,
    CountDown = 6,
    Other = 7
}

public sealed class Item
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal MinStock { get; set; }
    public decimal TargetStock { get; set; }
    public decimal ReferencePrice { get; set; }
    public decimal MovingAverageCost { get; set; }
    public bool IsActive { get; set; } = true;
    public bool LotTracked { get; set; } = true;
    public bool ExpiryTracked { get; set; } = true;
    public OpeningStatus OpeningStatus { get; set; } = OpeningStatus.Unset;
    /// <summary>기본 사용부서: 출고 시 이 품목을 보통 쓰는 부서(품목마스터 선택 필드, PRD 7.2). 출고 등록에서 기본값으로 제안된다.</summary>
    public int? DefaultDepartmentId { get; set; }
}

public sealed class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class Lot
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public string LotNumber { get; set; } = string.Empty;
    public DateTime ReceivedDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public int? SupplierId { get; set; }
}

public sealed class StockDocument
{
    public int Id { get; set; }
    public DocumentType Type { get; set; }
    public DateTime DocumentDate { get; set; }
    public int? SupplierId { get; set; }
    public int? DepartmentId { get; set; }
    public string? DocumentNo { get; set; }
    public string UserName { get; set; } = string.Empty;
    public bool IsCancelled { get; set; }
    public int? ReversesDocumentId { get; set; }
    public string? Reason { get; set; }
    public AdjustmentType? AdjustmentType { get; set; }
    public bool DuplicateWarning { get; set; }
    public List<StockLine> Lines { get; set; } = new();
}

public sealed class StockLine
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public StockDocument Document { get; set; } = null!;
    public int ItemId { get; set; }
    public int? LotId { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public decimal UnitCostSnapshot { get; set; }
}

public sealed class MonthClose
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime ClosedAtUtc { get; set; }
    public string ClosedBy { get; set; } = string.Empty;
    public bool IsClosed { get; set; } = true;
    public string? ReopenReason { get; set; }
}

public sealed class ReceiptLineRequest
{
    public string ItemCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
}

public sealed class IssueLineRequest
{
    public string ItemCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? LotNumber { get; set; }
}

public sealed class ImportResult
{
    public int ImportedItems { get; set; }
    public int SkippedRows { get; set; }
    public int TransactionRows { get; set; }
    public int OpeningConfirmed { get; set; }
    public bool DoubleCountWarning { get; set; }
    public string Warning { get; set; } = string.Empty;
}

public enum ImportMode
{
    MasterOnly = 0,
    MasterAndOpening = 1,
    FullHistory = 2
}

public enum StockStatusKind
{
    Unset = 0,
    Normal = 1,
    Reorder = 2,
    OutOfStock = 3,
    Expiring = 4,
    Inactive = 5
}

public sealed record StockSnapshot(
    string Code,
    string Name,
    decimal? OnHand,
    StockStatusKind Status,
    decimal MinStock = 0);

public sealed record DocumentSummary(
    int Id,
    DocumentType Type,
    DateTime DocumentDate,
    string? DocumentNo,
    bool IsCancelled,
    int LineCount,
    string? FirstItemName = null,
    decimal TotalAmount = 0);

public sealed record DocumentLineDetail(
    string ItemCode,
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    string? LotNumber);

public sealed record DocumentDetail(
    int Id,
    DocumentType Type,
    DateTime DocumentDate,
    bool IsCancelled,
    string? SupplierName,
    string? DepartmentName,
    IReadOnlyList<DocumentLineDetail> Lines);

public sealed class ImportPreview
{
    public IReadOnlyList<string> ItemCodes { get; init; } = Array.Empty<string>();
    public int EmptyRowsSkipped { get; init; }
}
