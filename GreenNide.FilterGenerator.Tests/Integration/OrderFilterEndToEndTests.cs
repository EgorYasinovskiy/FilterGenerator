using GreenNide.ExpressionFilter.Tests.Integration.Models;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreenNide.ExpressionFilter.Tests.Integration;

/// <summary>
/// End-to-end тесты OrderFilterParams — проверяют полный пайплайн фильтрации
/// через реальный PostgreSQL (Testcontainers).
/// Тесты покрывают: простые поля, навигационные свойства, подзапросы,
/// multi-column поиск и методы-предикаты.
/// </summary>
public sealed class OrderFilterParamsEndToEndTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .Build();

    private TestDbContext _ctx = null!;
    private List<Order> _orders = null!;

    /// <summary>
    /// Поднимает контейнер PostgreSQL, создаёт DbContext и заполняет тестовыми данными.
    /// Вызывается один раз перед каждым тестом.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _ctx = new TestDbContext(options);
        await _ctx.Database.EnsureCreatedAsync();
        await SeedData();
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Создаёт 3 тестовых заказа с разными характеристиками:
    /// - Order 1: Alice, 100$, Processing, 3 товара (Widget x3 @50), дата 2026-01-15
    /// - Order 2: Bob, 500$, Shipped, 3 товара (Gadget + Widget), дата 2026-03-20
    /// - Order 3: без клиента (null), 25$, Pending, 0 товаров, дата 2026-06-01
    /// Это покрывает все граничные случаи: навигации с null, диапазоны, подзапросы.
    /// </summary>
    private async Task SeedData()
    {
        var alice = new Customer { Id = Guid.NewGuid(), Name = "Alice Johnson", Email = "alice@example.com" };
        var bob = new Customer { Id = Guid.NewGuid(), Name = "Bob Smith", Email = "bob@test.org" };

        var widgetId = Guid.NewGuid();
        var gadgetId = Guid.NewGuid();

        _orders = new List<Order>
        {
            // Order 1: Alice, 100$, Processing, 3 items (Widget x3 @50)
            new()
            {
                Id = Guid.NewGuid(),
                Description = "First order",
                Amount = 100m,
                ItemCount = 3,
                CreatedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                CustomerId = alice.Id,
                Customer = alice,
                OrderItems = new List<OrderItem>
                {
                    new() { Id = widgetId, ProductName = "Widget", Quantity = 3, Price = 50m }
                },
                History = new List<OrderHistory>
                {
                    new() { Status = OrderStatus.Pending, Timestamp = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc) },
                    new() { Status = OrderStatus.Processing, Timestamp = new DateTime(2026, 1, 16, 12, 0, 0, DateTimeKind.Utc) }
                }
            },
            // Order 2: Bob, 500$, Shipped, 3 items (Gadget x1 @200, Widget x2 @150)
            new()
            {
                Id = Guid.NewGuid(),
                Description = "Premium order",
                Amount = 500m,
                ItemCount = 3,
                CreatedAt = new DateTime(2026, 3, 20, 14, 0, 0, DateTimeKind.Utc),
                CustomerId = bob.Id,
                Customer = bob,
                OrderItems = new List<OrderItem>
                {
                    new() { Id = gadgetId, ProductName = "Gadget", Quantity = 1, Price = 200m },
                    new() { Id = Guid.NewGuid(), ProductName = "Widget", Quantity = 2, Price = 150m }
                },
                History = new List<OrderHistory>
                {
                    new() { Status = OrderStatus.Pending, Timestamp = new DateTime(2026, 3, 20, 14, 0, 0, DateTimeKind.Utc) },
                    new() { Status = OrderStatus.Shipped, Timestamp = new DateTime(2026, 3, 22, 9, 0, 0, DateTimeKind.Utc) }
                }
            },
            // Order 3: No customer, 25$, Pending, 0 items
            new()
            {
                Id = Guid.NewGuid(),
                Description = "Guest order",
                Amount = 25m,
                ItemCount = 0,
                CreatedAt = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc),
                CustomerId = null,
                Customer = null,
                OrderItems = new List<OrderItem>(),
                History = new List<OrderHistory>
                {
                    new() { Status = OrderStatus.Pending, Timestamp = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc) }
                }
            }
        };

        _ctx.Orders.AddRange(_orders);
        await _ctx.SaveChangesAsync();
    }

    // ─── Null filter ───────────────────────────────────────

    /// <summary>
    /// Проверяет, что при передаче null-фильтра метод Apply() возвращает
    /// все заказы без фильтрации. Это базовый guard — метод не должен
    /// выбрасывать NullReferenceException.
    /// </summary>
    [Fact]
    public async Task NullFilter_ShouldReturnAllOrders()
    {
        var result = await _ctx.Orders.Apply(null!).ToListAsync();

        Assert.Equal(3, result.Count);
    }

    // ─── CustomerId — Equal ────────────────────────────────

    /// <summary>
    /// Проверяет фильтрацию по CustomerId (Equal, Guid?).
    /// Устанавливаем Id клиента из первого заказа — должен вернуться только Order 1.
    /// Проверяет, что оператор Equal корректно работает с nullable Guid.
    /// </summary>
    [Fact]
    public async Task CustomerId_Equal_ShouldFilterCorrectly()
    {
        var filter = new OrderFilterParams { CustomerId = _orders[0].CustomerId };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Single(result);
        Assert.Equal("First order", result[0].Description);
    }

    /// <summary>
    /// Проверяет, что фильтрация по несуществующему CustomerId возвращает пустой результат.
    /// Генерирует GUID, которого нет в базе — ни один заказ не должен совпасть.
    /// </summary>
    [Fact]
    public async Task CustomerId_Equal_NoMatch_ShouldReturnEmpty()
    {
        var filter = new OrderFilterParams { CustomerId = Guid.NewGuid() };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Empty(result);
    }

    // ─── CustomerName — Contains on navigation ─────────────

    /// <summary>
    /// Проверяет фильтрацию по навигационному свойству Customer.Name (Contains).
    /// Ищем "Alice" — должен вернуться Order 1 (Customer = Alice Johnson).
    /// Генератор должен добавить null-guard: e.Customer != null && e.Customer.Name.Contains(...).
    /// </summary>
    [Fact]
    public async Task CustomerName_Contains_ShouldFilterByNavigation()
    {
        var filter = new OrderFilterParams { CustomerName = "Alice" };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Single(result);
        Assert.Equal("First order", result[0].Description);
    }

    /// <summary>
    /// Проверяет, что фильтрация по Customer.Name при null-клиенте (Order 3)
    /// не вызывает NullReferenceException. Null-guard (e.Customer != null) должен
    /// защитить от обращения к Name у null-клиента.
    /// </summary>
    [Fact]
    public async Task CustomerName_Contains_NullCustomer_ShouldNotThrow()
    {
        var filter = new OrderFilterParams { CustomerName = "Guest" };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        // Order 3 имеет null Customer — null guard предотвращает NRE.
        Assert.Empty(result);
    }

    // ─── Amount range — GreaterThanOrEqual / LessThanOrEqual

    /// <summary>
    /// Проверяет фильтрацию по нижней границе суммы (GreaterThanOrEqual).
    /// MinAmount = 100: Order 1 (100$) и Order 2 (500$) проходят, Order 3 (25$) — нет.
    /// </summary>
    [Fact]
    public async Task MinAmount_Gte_ShouldFilterCorrectly()
    {
        var filter = new OrderFilterParams { MinAmount = 100m };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Equal(2, result.Count);
        Assert.All(result, o => Assert.True(o.Amount >= 100m));
    }

    /// <summary>
    /// Проверяет фильтрацию по верхней границе суммы (LessThanOrEqual).
    /// MaxAmount = 100: Order 1 (100$) и Order 3 (25$) проходят, Order 2 (500$) — нет.
    /// </summary>
    [Fact]
    public async Task MaxAmount_Lte_ShouldFilterCorrectly()
    {
        var filter = new OrderFilterParams { MaxAmount = 100m };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Equal(2, result.Count);
        Assert.All(result, o => Assert.True(o.Amount <= 100m));
    }

    /// <summary>
    /// Проверяет комбинированную фильтрацию по диапазону суммы (Min + Max).
    /// MinAmount = 50, MaxAmount = 200: только Order 1 (100$) попадает в диапазон.
    /// Order 2 (500$) — больше максимума, Order 3 (25$) — меньше минимума.
    /// </summary>
    [Fact]
    public async Task AmountRange_Combined_ShouldFilterCorrectly()
    {
        var filter = new OrderFilterParams { MinAmount = 50m, MaxAmount = 200m };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        // Order 1: 100m ✓ (50 ≤ 100 ≤ 200), Order 2: 500m ✗, Order 3: 25m ✗
        Assert.Single(result);
        Assert.All(result, o => Assert.True(o.Amount >= 50m && o.Amount <= 200m));
    }

    // ─── Date range ────────────────────────────────────────

    /// <summary>
    /// Проверяет фильтрацию по нижней границе даты (GreaterThanOrEqual).
    /// FromDate = 2026-03-01: Order 2 (2026-03-20) и Order 3 (2026-06-01) проходят.
    /// Order 1 (2026-01-15) — раньше.
    /// </summary>
    [Fact]
    public async Task FromDate_Gte_ShouldFilterCorrectly()
    {
        var filter = new OrderFilterParams { FromDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc) };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Equal(2, result.Count);
        Assert.All(result, o => Assert.True(o.CreatedAt >= filter.FromDate!.Value));
    }

    /// <summary>
    /// Проверяет фильтрацию по верхней границе даты (LessThanOrEqual).
    /// ToDate = 2026-02-01: только Order 1 (2026-01-15) попадает.
    /// Order 2 (2026-03-20) и Order 3 (2026-06-01) — позже.
    /// </summary>
    [Fact]
    public async Task ToDate_Lte_ShouldFilterCorrectly()
    {
        var filter = new OrderFilterParams { ToDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Single(result);
        Assert.Equal("First order", result[0].Description);
    }

    /// <summary>
    /// Проверяет комбинированную фильтрацию по диапазону дат.
    /// FromDate = 2026-01-01, ToDate = 2026-06-30: все 3 заказа попадают.
    /// Это проверяет, что два Where-условия (>= и <=) корректно компонуются.
    /// </summary>
    [Fact]
    public async Task DateRange_Combined_ShouldFilterCorrectly()
    {
        var filter = new OrderFilterParams
        {
            FromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToDate = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc)
        };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Equal(3, result.Count);
    }

    // ─── CurrentStatus — subquery ──────────────────────────

    /// <summary>
    /// Проверяет фильтрацию по подзапросу: текущий статус заказа (последняя запись из History).
    /// CurrentStatus = Processing: Order 1 (последняя история = Processing).
    /// Подзапрос OrderByDescending(Timestamp).Select(Status).FirstOrDefault() должен
    /// корректно транслироваться в SQL.
    /// </summary>
    [Fact]
    public async Task CurrentStatus_Subquery_ShouldFilterByLatestHistory()
    {
        var filter = new OrderFilterParams { CurrentStatus = OrderStatus.Processing };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Single(result);
        Assert.Equal("First order", result[0].Description);
    }

    /// <summary>
    /// Проверяет фильтрацию по подзапросу со статусом Shipped.
    /// Order 2: последняя история = Shipped → должен быть найден.
    /// </summary>
    [Fact]
    public async Task CurrentStatus_Subquery_Shipped_ShouldFilterCorrectly()
    {
        var filter = new OrderFilterParams { CurrentStatus = OrderStatus.Shipped };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Single(result);
        Assert.Equal("Premium order", result[0].Description);
    }

    /// <summary>
    /// Проверяет фильтрацию по подзапросу со статусом Pending.
    /// Order 3: единственная история = Pending → должен быть найден.
    /// Order 1 тоже имеет Pending в истории, но последний статус — Processing.
    /// </summary>
    [Fact]
    public async Task CurrentStatus_Subquery_Pending_ShouldReturnOrder3()
    {
        var filter = new OrderFilterParams { CurrentStatus = OrderStatus.Pending };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Single(result);
        Assert.Equal("Guest order", result[0].Description);
    }

    // ─── Search — multi-column ─────────────────────────────

    /// <summary>
    /// Проверяет multi-column поиск по Description.
    /// Search = "Premium" — находится в Description второго заказа ("Premium order").
    /// </summary>
    [Fact]
    public async Task Search_FindsInDescription()
    {
        var filter = new OrderFilterParams { Search = "Premium" };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Single(result);
        Assert.Equal("Premium order", result[0].Description);
    }

    /// <summary>
    /// Проверяет multi-column поиск по Customer.Name (навигация).
    /// Search = "Bob" — находится в имени клиента второго заказа ("Bob Smith").
    /// </summary>
    [Fact]
    public async Task Search_FindsInCustomerName()
    {
        var filter = new OrderFilterParams { Search = "Bob" };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Single(result);
        Assert.Equal("Premium order", result[0].Description);
    }

    /// <summary>
    /// Проверяет multi-column поиск по Customer.Email (навигация).
    /// Search = "example.com" — находится в email клиента первого заказа.
    /// </summary>
    [Fact]
    public async Task Search_FindsInCustomerEmail()
    {
        var filter = new OrderFilterParams { Search = "example.com" };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Single(result);
        Assert.Equal("First order", result[0].Description);
    }

    /// <summary>
    /// Проверяет, что поиск по несуществующему значению возвращает пустой результат
    /// и не вызывает ошибок при наличии null-навигаций (Order 3 имеет null Customer).
    /// </summary>
    [Fact]
    public async Task Search_NullCustomer_ShouldNotThrow()
    {
        var filter = new OrderFilterParams { Search = "nonexistent" };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Empty(result);
    }

    // ─── Predicate methods ─────────────────────────────────

    /// <summary>
    /// Проверяет метод-предикат HasItem: фильтрация заказов, содержащих товар с определённым Id.
    /// Передаём Id товара из первого заказа (Widget) — должен вернуться только Order 1.
    /// </summary>
    [Fact]
    public async Task HasItem_ShouldFilterByOrderItemId()
    {
        var itemId = _orders[0].OrderItems[0].Id;
        var filter = new OrderFilterParams { ItemId = itemId };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Single(result);
        Assert.Equal("First order", result[0].Description);
    }

    /// <summary>
    /// Проверяет, что метод HasItem при null ItemId не фильтрует — возвращаются все заказы.
    /// Когда ItemId не задан, предикат возвращает null, и Where() не применяется.
    /// </summary>
    [Fact]
    public async Task HasItem_Null_ShouldNotFilter()
    {
        var filter = new OrderFilterParams { ItemId = null };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Equal(3, result.Count);
    }

    /// <summary>
    /// Проверяет метод-предикат HasMinItemCount: фильтрация заказов с количеством товаров >= N.
    /// MinItemCount = 2: Order 1 (1 товар ✗), Order 2 (2 товара ✓), Order 3 (0 товаров ✗).
    /// </summary>
    [Fact]
    public async Task HasMinItemCount_ShouldFilterCorrectly()
    {
        var filter = new OrderFilterParams { MinItemCount = 2 };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        // Order 1: 1 item ✗, Order 2: 2 items ✓, Order 3: 0 items ✗
        Assert.Single(result);
        Assert.Contains(result, o => o.Description == "Premium order");
    }

    /// <summary>
    /// Проверяет метод-предикат AllItemsExpensive: все товары в заказе стоят >= N.
    /// MinItemPrice = 100:
    /// - Order 1: Widget @50 < 100 → исключён
    /// - Order 2: Gadget @200, Widget @150, Widget @150 → все >= 100 ✓
    /// - Order 3: нет товаров → All() для пустой коллекции = true ✓
    /// </summary>
    [Fact]
    public async Task AllItemsExpensive_ShouldFilterCorrectly()
    {
        var filter = new OrderFilterParams { MinItemPrice = 100m };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        // Order 2: все товары >= 100 (200, 150, 150)
        // Order 1: Widget @50 < 100 — исключён
        // Order 3: нет товаров — All() возвращает true для пустой коллекции
        Assert.Equal(2, result.Count);
        Assert.Contains(result, o => o.Description == "Premium order");
        Assert.Contains(result, o => o.Description == "Guest order");
    }

    // ─── Combined filters ──────────────────────────────────

    /// <summary>
    /// Проверяет комбинацию нескольких фильтров одновременно:
    /// MinAmount=50, MaxAmount=200, FromDate=2026-01-01, ToDate=2026-06-30.
    /// Только Order 1 (100$, 2026-01-15) удовлетворяет всем условиям.
    /// Проверяет, что все Where-условия корректно компонуются через AND.
    /// </summary>
    [Fact]
    public async Task CombinedFilters_ShouldApplyAll()
    {
        var filter = new OrderFilterParams
        {
            MinAmount = 50m,
            MaxAmount = 200m,
            FromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToDate = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc)
        };

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        // Только Order 1 (100$) удовлетворяет всем критериям
        Assert.Single(result);
        Assert.Equal("First order", result[0].Description);
    }

    /// <summary>
    /// Проверяет, что пустой фильтр (все поля null) возвращает все заказы.
    /// Ни одно Where-условие не должно быть добавлено — Apply() должен
    /// вернуть исходный IQueryable без модификаций.
    /// </summary>
    [Fact]
    public async Task EmptyFilter_ShouldReturnAllOrders()
    {
        var filter = new OrderFilterParams();

        var result = await _ctx.Orders.Apply(filter).ToListAsync();

        Assert.Equal(3, result.Count);
    }
}
