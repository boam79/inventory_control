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
    }
}
