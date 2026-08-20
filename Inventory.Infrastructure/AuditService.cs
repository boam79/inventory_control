using Inventory.Core;

namespace Inventory.Infrastructure;

public sealed class AuditService
{
    private readonly InventoryDbContext _db;

    public AuditService(InventoryDbContext db)
    {
        _db = db;
    }

    public void Write(
        string userName,
        string action,
        string entityType,
        string entityId,
        string? beforeValue,
        string? afterValue,
        string? reason)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserName = userName,
            OccurredAtUtc = DateTime.UtcNow,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BeforeValue = beforeValue,
            AfterValue = afterValue,
            Reason = reason,
            AppVersion = ProductInfo.Name
        });
        _db.SaveChanges();
    }
}
