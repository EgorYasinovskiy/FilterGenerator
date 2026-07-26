# GreenNide.FilterGenerator

[![NuGet Version](https://img.shields.io/nuget/v/GreenNide.FilterGenerator?label=nuget&color=blue)](https://www.nuget.org/packages/GreenNide.FilterGenerator/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/GreenNide.FilterGenerator?color=green)](https://www.nuget.org/packages/GreenNide.FilterGenerator/)

Roslyn source generator, который автоматически генерирует фильтры для запросов EF Core.

Вы описываете фильтр через Expression-свойства — генератор создаёт **отдельный POCO-класс** со свойствами и методом
`Apply()`, который конструирует `IQueryable<T>.Where(...)` на основе заполненных свойств.

## Установка

### NuGet (рекомендуется)

```bash
dotnet add package GreenNide.FilterGenerator --prerelease
```

Или в `.csproj`:

```xml
<PackageReference Include="GreenNide.FilterGenerator" Version="0.0.1-alpha.5" />
```

### Из исходников (для разработки)

```xml
<ProjectReference Include="..\GreenNide.FilterGenerator\GreenNide.FilterGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

## Быстрый старт

### 1. Определите сущность

```csharp
public class Order
{
    public Guid Id { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public List<OrderItem> OrderItems { get; set; } = new();
    public List<OrderHistory> History { get; set; } = new();
}
```

### 2. Создайте определение фильтра

Описываем фильтр через **статические Expression-свойства** и (опционально) **методы-предикаты**:

```csharp
using GreenNide.ExpressionFilter;

[GenerateFilter(typeof(Order))]
public partial class OrderFilterDefinition
{
    // Equal (по умолчанию для Guid?)
    public static Expression<Func<Order, Guid?>>? CustomerId { get; } = o => o.CustomerId;

    // Contains (по умолчанию для string)
    public static Expression<Func<Order, string>>? Description { get; } = o => o.Description;

    // GreaterThanOrEqual
    [Compare(CompareOperator.GreaterThanOrEqual)]
    public static Expression<Func<Order, decimal?>>? MinAmount { get; } = o => o.Amount;

    // LessThanOrEqual
    [Compare(CompareOperator.LessThanOrEqual)]
    public static Expression<Func<Order, decimal?>>? MaxAmount { get; } = o => o.Amount;

    // Навигация с null-guard
    public static Expression<Func<Order, string>>? CustomerName { get; } = o => o.Customer.Name;

    // Метод-предикат
    public static Expression<Func<Order, bool>>? HasItem(OrderFilterDefinition filter) =>
        filter.ItemId.HasValue
            ? o => o.OrderItems.Any(i => i.Id == filter.ItemId.Value)
            : null;

    // Instance-свойства для параметров методов-предикатов
    public Guid? ItemId { get; set; }
}
```

### 3. Используйте

Генератор создаёт **отдельный POCO-класс** `OrderFilterParams` (не наследуется от Definition) и расширение
`OrderFilterParamsExtensions`:

```csharp
var filter = new OrderFilterParams
{
    CustomerId = someId,
    MinAmount = 100m,
    Description = "Premium",
    ItemId = itemId
};

var results = await dbContext.Orders
    .Apply(filter)
    .ToListAsync();
```

Ключевое отличие: `OrderFilterParams` — standalone POCO, его можно свободно маппить в DTO, сериализовать, передавать на
фронтенд.

## Именование сгенерированного класса

Генератор использует простую конвенцию (2 варианта):

| Исходный код                                              | Сгенерированный класс |
|-----------------------------------------------------------|-----------------------|
| `[GenerateFilter(typeof(Order))]`                         | `OrderFilterParams`   |
| `[GenerateFilter(typeof(Order), ClassName = "MyFilter")]` | `MyFilter`            |

Правило: если `ClassName` не задан, используется `{EntityName}FilterParams`.

## Типы фильтров

### Простые поля

Каждое Expression-свойство описывает один фильтр:

```csharp
// Equal (по умолчанию для числовых типов)
public static Expression<Func<Order, Guid?>>? CustomerId { get; } = o => o.CustomerId;

// Contains (по умолчанию для string)
public static Expression<Func<Order, string>>? Description { get; } = o => o.Description;

// GreaterThanOrEqual — через атрибут [Compare]
[Compare(CompareOperator.GreaterThanOrEqual)]
public static Expression<Func<Order, decimal?>>? MinAmount { get; } = o => o.Amount;
```

**Поддерживаемые операторы:**

| Оператор             | Описание        |
|----------------------|-----------------|
| `Equal`              | `==`            |
| `NotEqual`           | `!=`            |
| `GreaterThan`        | `>`             |
| `GreaterThanOrEqual` | `>=`            |
| `LessThan`           | `<`             |
| `LessThanOrEqual`    | `<=`            |
| `Contains`           | `.Contains()`   |
| `StartsWith`         | `.StartsWith()` |
| `EndsWith`           | `.EndsWith()`   |

**Авто-определение оператора:**

- `string` → `Contains`
- Все остальные типы → `Equal`
- Можно переопределить через `[Compare(...)]`

### Навигационные свойства

Для обращений через навигации генератор автоматически добавляет null-guard:

```csharp
// o => o.Customer.Name
// Сгенерируется:
// if (!string.IsNullOrWhiteSpace(filter.CustomerName))
//     query = query.Where(e => e.Customer != null && e.Customer.Name.Contains(filter.CustomerName));
```

Многоуровневые навигации тоже работают:

```csharp
// o => o.Order.Customer.Address.City
// Сгенерируется:
// e.Order != null && e.Order.Customer != null && e.Order.Customer.Address != null
```

### Subquery (подзапросы)

Expression с вызовами LINQ-методов передаются в SQL как подзапросы:

```csharp
public static Expression<Func<Order, OrderStatus?>>? CurrentStatus { get; } =
    o => o.History
        .OrderByDescending(h => h.Timestamp)
        .Select(h => (OrderStatus?)h.Status)
        .FirstOrDefault();
```

### Multi-column Search

Массив `string[]` в Expression задаёт поиск по нескольким колонкам через `||`:

```csharp
[Search]
public static Expression<Func<Order, string[]>>? Search { get; } =
    o => new[] { o.Description, o.Customer.Name, o.Customer.Email };
```

Сгенерируется:

```csharp
if (!string.IsNullOrWhiteSpace(filter.Search))
{
    query = query.Where(e =>
        e.Description.Contains(filter.Search) ||
        (e.Customer != null && e.Customer.Name.Contains(filter.Search)) ||
        (e.Customer != null && e.Customer.Email.Contains(filter.Search)));
}
```

### Методы-предикаты

Статические методы с сигнатурой `Expression<Func<TEntity, bool>>? Method(DefinitionType filter)` позволяют писать
сложные предикаты:

```csharp
[GenerateFilter(typeof(Order))]
public partial class OrderFilterDefinition
{
    // Closure-свойства (instance)
    public Guid? ItemId { get; set; }
    public int? MinItemCount { get; set; }
    public decimal? MinItemPrice { get; set; }

    // Предикаты (статические, принимают определение фильтра)
    public static Expression<Func<Order, bool>>? HasItem(OrderFilterDefinition filter) =>
        filter.ItemId.HasValue
            ? o => o.OrderItems.Any(i => i.Id == filter.ItemId.Value)
            : null;

    public static Expression<Func<Order, bool>>? HasMinItemCount(OrderFilterDefinition filter) =>
        filter.MinItemCount.HasValue
            ? o => o.OrderItems.Count >= filter.MinItemCount.Value
            : null;

    public static Expression<Func<Order, bool>>? AllItemsExpensive(OrderFilterDefinition filter) =>
        filter.MinItemPrice.HasValue
            ? o => o.OrderItems.All(i => i.Price >= filter.MinItemPrice.Value)
            : null;
}
```

Генератор автоматически:

- Копирует closure-свойства в сгенерированный POCO-класс
- В `Apply()` создаёт промежуточный объект Definition, копирует closure-свойства и вызывает методы-предикаты (
  bridge-паттерн)

```csharp
var filter = new OrderFilterParams { MinItemCount = 2, MinItemPrice = 100m };
var results = await dbContext.Orders.Apply(filter).ToListAsync();
```

### Архитектура: Definition vs Generated

```
OrderFilterDefinition                    OrderFilterParams (генерируется)
┌─────────────────────────────┐         ┌─────────────────────────────┐
│ static Expression-свойства  │         │ public свойства             │
│ static методы-предикаты     │         │   (Expression + closure)    │
│ instance closure-свойства   │         │                             │
└─────────────────────────────┘         └─────────────────────────────┘
        ▲                                          │
        │         new Definition()                 │
        └──────────────────────────────────────────┘
              Apply() создаёт мост: копирует
              closure-свойства, вызывает предикаты
```

- `OrderFilterDefinition` — определение фильтра с Expression-свойствами и методами-предикатами
- `OrderFilterParams` — standalone POCO со всеми свойствами (можно маппить в DTO)
- Между ними **нет наследования** — связь через bridge-паттерн в `Apply()`

## Поддерживаемые nullable-типы

Генератор корректно работает с nullable value types:

- `int?`, `decimal?`, `Guid?`, `DateTime?` и т.д. — используется `.HasValue` для проверки + `.Value` для доступа к
  значению
- `string?` — используется `string.IsNullOrWhiteSpace()`
- Ссылочные типы (классы) — используется `!= null`

## Тесты

```bash
dotnet test
```

Тесты включают:

- **Unit-тесты генератора** — проверка генерации кода через Roslyn API
- **Интеграционные тесты EF Core** — проверка трансляции в SQL через Testcontainers (PostgreSQL)
