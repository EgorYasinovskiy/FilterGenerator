using System.Linq.Expressions;
using GreenNide.ExpressionFilter;

namespace Examples;

[GenerateFilter(typeof(Category), ClassName = "CategorySearchParams")]
public partial class CategoryFilterDefinition
{
    public static Expression<Func<Category, string>>? Name { get; } = o => o.Name;

    public static Expression<Func<Category, string[]>>? Search { get; } =
        o => new[] { o.Name };
}
