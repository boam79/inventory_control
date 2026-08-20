using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure;

public sealed class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppMeta> AppMeta => Set<AppMeta>();
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<StockDocument> Documents => Set<StockDocument>();
    public DbSet<StockLine> StockLines => Set<StockLine>();
    public DbSet<MonthClose> MonthCloses => Set<MonthClose>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppMeta>(entity =>
        {
            entity.ToTable("AppMeta");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Key).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Value).HasMaxLength(256).IsRequired();
            entity.HasIndex(row => row.Key).IsUnique();
        });

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.UserName).HasMaxLength(64).IsRequired();
            entity.Property(row => row.PasswordHash).HasMaxLength(256).IsRequired();
            entity.Property(row => row.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(row => row.UserName).IsUnique();
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.UserName).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Action).HasMaxLength(64).IsRequired();
            entity.Property(row => row.EntityType).HasMaxLength(64).IsRequired();
            entity.Property(row => row.EntityId).HasMaxLength(64).IsRequired();
            entity.Property(row => row.AppVersion).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.ToTable("Settings");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Key).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Value).HasMaxLength(1024).IsRequired();
            entity.HasIndex(row => row.Key).IsUnique();
        });

        modelBuilder.Entity<Item>(entity =>
        {
            entity.ToTable("Items");
            entity.HasIndex(row => row.Code).IsUnique();
            entity.Property(row => row.Code).HasMaxLength(32).IsRequired();
            entity.Property(row => row.Name).HasMaxLength(128).IsRequired();
            entity.Property(row => row.MinStock).HasPrecision(18, 3);
            entity.Property(row => row.TargetStock).HasPrecision(18, 3);
            entity.Property(row => row.ReferencePrice).HasPrecision(18, 4);
            entity.Property(row => row.MovingAverageCost).HasPrecision(18, 4);
            entity.Property(row => row.OpeningStatus).HasConversion<string>().HasMaxLength(24);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("Departments");
            entity.Property(row => row.Name).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.ToTable("Suppliers");
            entity.Property(row => row.Name).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<Lot>(entity =>
        {
            entity.ToTable("Lots");
            entity.Property(row => row.LotNumber).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Quantity).HasPrecision(18, 3);
            entity.Property(row => row.UnitCost).HasPrecision(18, 4);
            entity.HasIndex(row => new { row.ItemId, row.LotNumber }).IsUnique();
        });

        modelBuilder.Entity<StockDocument>(entity =>
        {
            entity.ToTable("Documents");
            entity.HasMany(row => row.Lines).WithOne(row => row.Document).HasForeignKey(row => row.DocumentId);
            entity.Property(row => row.Type).HasConversion<string>().HasMaxLength(24);
            entity.Property(row => row.AdjustmentType).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<StockLine>(entity =>
        {
            entity.ToTable("StockLines");
            entity.Property(row => row.Quantity).HasPrecision(18, 3);
            entity.Property(row => row.UnitPrice).HasPrecision(18, 4);
            entity.Property(row => row.Amount).HasPrecision(18, 4);
            entity.Property(row => row.UnitCostSnapshot).HasPrecision(18, 4);
        });

        modelBuilder.Entity<MonthClose>(entity =>
        {
            entity.ToTable("MonthCloses");
            entity.HasIndex(row => new { row.Year, row.Month });
        });
    }
}
