using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure;

public sealed class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppMeta> AppMeta => Set<AppMeta>();

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
    }
}
