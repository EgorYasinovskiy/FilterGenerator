using GreenNide.ExpressionFilter.Tests.Integration.Models;
using Microsoft.EntityFrameworkCore;

namespace GreenNide.ExpressionFilter.Tests.Integration;

public class TestDbContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderHistory> OrderHistories => Set<OrderHistory>();
    public DbSet<Customer> Customers => Set<Customer>();

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);
            e.HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId);
            e.HasMany(o => o.OrderItems).WithOne(i => i.Order).HasForeignKey(i => i.OrderId);
            e.HasMany(o => o.History).WithOne(h => h.Order).HasForeignKey(h => h.OrderId);
        });

        modelBuilder.Entity<OrderItem>(e => e.HasKey(i => i.Id));
        modelBuilder.Entity<OrderHistory>(e => e.HasKey(h => h.Id));
        modelBuilder.Entity<Customer>(e => e.HasKey(c => c.Id));
    }
}
