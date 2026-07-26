using System.Linq.Expressions;
using GreenNide.ExpressionFilter.Tests.Integration.Models;

namespace GreenNide.ExpressionFilter.Tests.Integration;

/// <summary>
/// Определение фильтра через Expression-свойства.
/// На этапе компиляции Source Generator создаёт отдельный класс OrderFilterParams
/// со свойствами и методом Apply().
///
/// Конвенция: {EntityName}FilterParams (Order → OrderFilterParams).
/// </summary>
[GenerateFilter(typeof(Order))]
public partial class OrderFilterDefinition
{
    // ─── Простые поля ─────────────────────────────────────

    /// <summary>Equal по умолчанию для Guid? — точное совпадение по CustomerId.</summary>
    public static Expression<Func<Order, Guid?>>? CustomerId { get; } = o => o.CustomerId;

    /// <summary>Contains по умолчанию для string — поиск подстроки в описании.</summary>
    public static Expression<Func<Order, string>>? Description { get; } = o => o.Description;

    // ─── Диапазон значений ────────────────────────────────

    /// <summary>GreaterThanOrEqual — нижняя граница суммы заказа.</summary>
    [Compare(CompareOperator.GreaterThanOrEqual)]
    public static Expression<Func<Order, decimal?>>? MinAmount { get; } = o => o.Amount;

    /// <summary>LessThanOrEqual — верхняя граница суммы заказа.</summary>
    [Compare(CompareOperator.LessThanOrEqual)]
    public static Expression<Func<Order, decimal?>>? MaxAmount { get; } = o => o.Amount;

    /// <summary>GreaterThanOrEqual — нижняя граница даты создания.</summary>
    [Compare(CompareOperator.GreaterThanOrEqual)]
    public static Expression<Func<Order, DateTime?>>? FromDate { get; } = o => o.CreatedAt;

    /// <summary>LessThanOrEqual — верхняя граница даты создания.</summary>
    [Compare(CompareOperator.LessThanOrEqual)]
    public static Expression<Func<Order, DateTime?>>? ToDate { get; } = o => o.CreatedAt;

    // ─── Навигация ────────────────────────────────────────

    /// <summary>
    /// Contains по навигации Customer.Name.
    /// Генератор автоматически добавит null-guard: e.Customer != null.
    /// </summary>
    public static Expression<Func<Order, string>>? CustomerName { get; } = o => o.Customer.Name;

    // ─── Подзапрос ────────────────────────────────────────

    /// <summary>
    /// Подзапрос: последний статус из истории заказа.
    /// Генератор передаст выражение как есть в Where(),
    /// EF Core транслирует его в SQL-подзапрос (correlated subquery).
    /// </summary>
    public static Expression<Func<Order, OrderStatus?>>? CurrentStatus { get; } =
        o => o.History
            .OrderByDescending(h => h.Timestamp)
            .Select(h => (OrderStatus?)h.Status)
            .FirstOrDefault();

    // ─── Multi-column search ──────────────────────────────

    /// <summary>
    /// Поиск по нескольким колонкам: Description, Customer.Name, Customer.Email.
    /// Тип string[] сигнализирует генератору о multi-column поиске.
    /// Сгенерируется OR-условие с null-guard для каждой навигации.
    /// </summary>
    public static Expression<Func<Order, string[]>>? Search { get; } =
        o => new[] { o.Description, o.Customer.Name, o.Customer.Email };

    // ─── Методы-предикаты ────────────────────────────────

    /// <summary>
    /// Предикат: заказ содержит товар с определённым Id.
    /// Использует коллекцию OrderItems через Any().
    /// </summary>
    public static Expression<Func<Order, bool>>? HasItem(OrderFilterDefinition filter) =>
        filter.ItemId.HasValue
            ? o => o.OrderItems.Any(i => i.Id == filter.ItemId.Value)
            : null;

    /// <summary>
    /// Предикат: количество товаров в заказе >= N.
    /// Использует Count() коллекции OrderItems.
    /// </summary>
    public static Expression<Func<Order, bool>>? HasMinItemCount(OrderFilterDefinition filter) =>
        filter.MinItemCount.HasValue
            ? o => o.OrderItems.Count >= filter.MinItemCount.Value
            : null;

    /// <summary>
    /// Предикат: все товары в заказе стоят >= N.
    /// Использует All() — для пустой коллекции возвращает true.
    /// </summary>
    public static Expression<Func<Order, bool>>? AllItemsExpensive(OrderFilterDefinition filter) =>
        filter.MinItemPrice.HasValue
            ? o => o.OrderItems.All(i => i.Price >= filter.MinItemPrice.Value)
            : null;

    // ─── Instance properties для методов-предикатов ────────
    // Эти свойства заполняются пользователем при создании фильтра.
    // Методы-предикаты ссылаются на filter.ItemId, filter.MinItemCount, filter.MinItemPrice.

    /// <summary>Id товара для фильтрации через HasItem.</summary>
    public Guid? ItemId { get; set; }

    /// <summary>Минимальное количество товаров для HasMinItemCount.</summary>
    public int? MinItemCount { get; set; }

    /// <summary>Минимальная цена товара для AllItemsExpensive.</summary>
    public decimal? MinItemPrice { get; set; }
}
