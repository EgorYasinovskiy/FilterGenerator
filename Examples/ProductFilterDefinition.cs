using System.Linq.Expressions;
using GreenNide.ExpressionFilter;

namespace Examples;

[GenerateFilter(typeof(Product))]
public partial class ProductFilterDefinition
{
    public static Expression<Func<Product, Guid?>>? CategoryId { get; } = o => o.CategoryId;

    public static Expression<Func<Product, string>>? Name { get; } = o => o.Name;

    public static Expression<Func<Product, string>>? Description { get; } = o => o.Description;

    [Compare(CompareOperator.GreaterThanOrEqual)]
    public static Expression<Func<Product, decimal?>>? MinPrice { get; } = o => o.Price;

    [Compare(CompareOperator.LessThanOrEqual)]
    public static Expression<Func<Product, decimal?>>? MaxPrice { get; } = o => o.Price;

    [Compare(CompareOperator.GreaterThanOrEqual)]
    public static Expression<Func<Product, DateTime?>>? FromDate { get; } = o => o.CreatedAt;

    [Compare(CompareOperator.LessThanOrEqual)]
    public static Expression<Func<Product, DateTime?>>? ToDate { get; } = o => o.CreatedAt;

    public static Expression<Func<Product, string>>? CategoryName { get; } = o => o.Category.Name;

    public static Expression<Func<Product, string[]>>? Search { get; } =
        o => new[] { o.Name, o.Description, o.Category.Name };

    public string? Tag { get; set; }
    public List<Guid>? CategoryIds { get; set; }

    public static Expression<Func<Product, bool>>? HasTag(ProductFilterDefinition filter)
    {
        return string.IsNullOrEmpty(filter.Tag)
            ? null
            : o => o.Tags.Any(t => t.Tag == filter.Tag);
    }

    public static Expression<Func<Product, bool>>? InCategories(ProductFilterDefinition filter)
    {
        return filter.CategoryIds is { Count: > 0 }
            ? o => filter.CategoryIds.Contains(o.CategoryId!.Value)
            : null;
    }
}
