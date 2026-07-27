# GreenNide.FilterGenerator

[![NuGet Version](https://img.shields.io/nuget/v/GreenNide.FilterGenerator?label=nuget&color=blue)](https://www.nuget.org/packages/GreenNide.FilterGenerator/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/GreenNide.FilterGenerator?color=green)](https://www.nuget.org/packages/GreenNide.FilterGenerator/)

A Roslyn source generator that auto-generates EF Core filter classes from expression tree definitions. You describe filters with static expression properties — the generator creates a standalone POCO class with an `Apply()` extension method that builds `IQueryable<T>.Where(...)`.

## Install

```bash
dotnet add package GreenNide.FilterGenerator --prerelease
```

Or in `.csproj`:

```xml
<PackageReference Include="GreenNide.FilterGenerator" Version="0.0.1-alpha.5" />
```

## Quick Start

### 1. Define a filter

```csharp
using GreenNide.ExpressionFilter;

[GenerateFilter(typeof(Order))]
public partial class OrderFilterDefinition
{
    // Equal (default for Guid?)
    public static Expression<Func<Order, Guid?>>? CustomerId { get; } = o => o.CustomerId;

    // Contains (default for string)
    public static Expression<Func<Order, string>>? Description { get; } = o => o.Description;

    // GreaterThanOrEqual
    [Compare(CompareOperator.GreaterThanOrEqual)]
    public static Expression<Func<Order, decimal?>>? MinAmount { get; } = o => o.Amount;

    // LessThanOrEqual
    [Compare(CompareOperator.LessThanOrEqual)]
    public static Expression<Func<Order, decimal?>>? MaxAmount { get; } = o => o.Amount;

    // Navigation with automatic null-guard
    public static Expression<Func<Order, string>>? CustomerName { get; } = o => o.Customer.Name;

    // Multi-column search
    public static Expression<Func<Order, string[]>>? Search { get; } =
        o => new[] { o.Description, o.Customer.Name, o.Customer.Email };
}
```

### 2. Use it

The generator produces a standalone `OrderFilterParams` POCO class (no inheritance from the definition):

```csharp
var filter = new OrderFilterParams
{
    CustomerId = someId,
    MinAmount = 100m,
    Description = "Premium"
};

var results = await dbContext.Orders
    .Apply(filter)
    .ToListAsync();
```

### 3. Bind from HTTP requests

Since `OrderFilterParams` is a plain POCO, it works directly with model binding. Use `[FromQuery]` for GET or `[FromBody]` for POST:

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    public OrdersController(AppDbContext db) => _db = db;

    // GET /api/orders?customerId=...&minAmount=100&description=premium
    [HttpGet]
    public IActionResult Get([FromQuery] OrderFilterParams filter)
    {
        var results = _db.Orders.Apply(filter).ToList();
        return Ok(results);
    }

    // POST /api/orders/filter  { "minAmount": 100, "description": "premium" }
    [HttpPost("filter")]
    public IActionResult Filter([FromBody] OrderFilterParams filter)
    {
        var results = _db.Orders.Apply(filter).ToList();
        return Ok(results);
    }
}
```

A working example project is in [`Examples/`](Examples/).

## Supported Operators

| Operator            | Description        |
|---------------------|--------------------|
| `Equal`             | `==`               |
| `NotEqual`          | `!=`               |
| `GreaterThan`       | `>`                |
| `GreaterThanOrEqual`| `>=`               |
| `LessThan`          | `<`                |
| `LessThanOrEqual`   | `<=`               |
| `Contains`          | `.Contains()`      |
| `StartsWith`        | `.StartsWith()`    |
| `EndsWith`          | `.EndsWith()`      |

**Auto-detection:** `string` → `Contains`, everything else → `Equal`. Override with `[Compare(...)]`.

## Predicate Methods

Static methods with signature `Expression<Func<TEntity, bool>>? Method(DefinitionType filter)` let you write complex predicates:

```csharp
[GenerateFilter(typeof(Order))]
public partial class OrderFilterDefinition
{
    public Guid? ItemId { get; set; }
    public int? MinItemCount { get; set; }

    public static Expression<Func<Order, bool>>? HasItem(OrderFilterDefinition filter) =>
        filter.ItemId.HasValue
            ? o => o.OrderItems.Any(i => i.Id == filter.ItemId.Value)
            : null;

    public static Expression<Func<Order, bool>>? HasMinItemCount(OrderFilterDefinition filter) =>
        filter.MinItemCount.HasValue
            ? o => o.OrderItems.Count >= filter.MinItemCount.Value
            : null;
}
```

Closure properties are automatically copied to the generated POCO class. This also works for collection types — e.g., filtering by multiple IDs:

```csharp
[GenerateFilter(typeof(Product))]
public partial class ProductFilterDefinition
{
    // Closure property: a list of category IDs
    public List<Guid>? CategoryIds { get; set; }

    // Predicate: products whose CategoryId is in the list
    public static Expression<Func<Product, bool>>? InCategories(ProductFilterDefinition filter) =>
        filter.CategoryIds is { Count: > 0 }
            ? o => filter.CategoryIds.Contains(o.CategoryId!.Value)
            : null;
}

// Usage — the list binds from JSON body automatically:
// POST /api/products/filter  { "categoryIds": ["guid1", "guid2"] }
var filter = new ProductFilterParams
{
    CategoryIds = new List<Guid> { electronicsId, booksId }
};
var results = _db.Products.Apply(filter).ToList();
```

## Tests

```bash
dotnet test
```
