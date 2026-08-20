namespace Inventory.Infrastructure;

public sealed class AuditLog
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? BeforeValue { get; set; }
    public string? AfterValue { get; set; }
    public string? Reason { get; set; }
    public string AppVersion { get; set; } = string.Empty;
}
