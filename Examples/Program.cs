using Examples;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("examples"));

var app = builder.Build();

SeedData(app.Services.GetRequiredService<AppDbContext>());

app.MapControllers();
app.Run();

static void SeedData(AppDbContext db)
{
    var electronics = new Category { Id = Guid.NewGuid(), Name = "Electronics" };
    var books = new Category { Id = Guid.NewGuid(), Name = "Books" };
    db.Categories.AddRange(electronics, books);

    db.Products.AddRange(
        new Product
        {
            Id = Guid.NewGuid(),
            Name = "Laptop",
            Description = "High-performance laptop",
            Price = 1200m,
            CreatedAt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            CategoryId = electronics.Id,
            Category = electronics,
            Tags = new List<ProductTag> { new() { Id = Guid.NewGuid(), Tag = "sale" } }
        },
        new Product
        {
            Id = Guid.NewGuid(),
            Name = "Mouse",
            Description = "Wireless ergonomic mouse",
            Price = 45m,
            CreatedAt = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            CategoryId = electronics.Id,
            Category = electronics,
            Tags = new List<ProductTag>()
        },
        new Product
        {
            Id = Guid.NewGuid(),
            Name = "C# in Depth",
            Description = "Advanced C# programming book",
            Price = 55m,
            CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            CategoryId = books.Id,
            Category = books,
            Tags = new List<ProductTag> { new() { Id = Guid.NewGuid(), Tag = "sale" } }
        }
    );

    db.SaveChanges();
}
