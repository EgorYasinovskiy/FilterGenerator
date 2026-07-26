using Microsoft.CodeAnalysis;
using Xunit;

namespace GreenNide.ExpressionFilter.Tests.SourceGenerator;

/// <summary>
/// Тесты активации генератора фильтров.
/// Проверяют базовые сценарии: генерация при наличии атрибута,
/// отсутствие ошибок для валидного кода, и отсутствие генерации без атрибута.
/// </summary>
public class GeneratorActivationTests
{
    /// <summary>
    /// Проверяет, что генератор создаёт выходной файл, когда класс фильтра
    /// помечен атрибутом [GenerateFilter] и содержит корректное Expression-свойство.
    /// Исходный класс: OrderFilterDefinition → генерируется: OrderFilterParams.
    /// Это базовый smoke-тест — если он не проходит, генератор не работает в принципе.
    /// </summary>
    [Fact]
    public void Generator_ShouldProduceOutput_WhenFilterClassHasGenerateFilterAttribute()
    {
        // Arrange: создаём исходный код с сущностью Order и фильтром OrderFilterDefinition.
        // Генератор создаст класс OrderFilterParams (конвенция: EntityName + "FilterParams").
        var source = @"
using GreenNide.ExpressionFilter;

namespace TestNamespace;

public class Order
{
    public int Id { get; set; }
    public string Name { get; set; } = """";
}

[GenerateFilter(typeof(Order))]
public partial class OrderFilterDefinition
{
    public static System.Linq.Expressions.Expression<Func<Order, int>>? Id { get; } = o => o.Id;
}
";

        // Act: запускаем генератор через тестовый хелпер.
        var runResult = GeneratorTestHelper.RunGenerator(source);

        // Assert: генератор должен создать ровно 1 файл — OrderFilterParams.Filter.g.cs.
        Assert.Single(runResult.GeneratedTrees);
    }

    /// <summary>
    /// Проверяет, что для корректно определённого фильтра генератор не выдаёт
    /// ни одной ошибки (DiagnosticSeverity.Error).
    /// Это важно: генератор может создать файл, но при этом сообщить об ошибках
    /// в других частях кода. Этот тест гарантирует чистоту диагностики.
    /// </summary>
    [Fact]
    public void Generator_ShouldNotProduceDiagnostics_ForValidFilterClass()
    {
        // Arrange: фильтр для строки (string) — оператор по умолчанию Contains.
        var source = @"
using GreenNide.ExpressionFilter;

namespace TestNamespace;

public class Order
{
    public int Id { get; set; }
    public string Name { get; set; } = """";
}

[GenerateFilter(typeof(Order))]
public partial class OrderFilterDefinition
{
    public static System.Linq.Expressions.Expression<Func<Order, string>>? Name { get; } = o => o.Name;
}
";

        var runResult = GeneratorTestHelper.RunGenerator(source);

        // Assert: ровно 1 сгенерированный файл и ни одной ошибки.
        Assert.Single(runResult.GeneratedTrees);
        Assert.DoesNotContain(runResult.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// Проверяет, что генератор НЕ активируется, если на классе фильтра
    /// отсутствует атрибут [GenerateFilter], даже если класс — partial.
    /// Генератор должен полностью игнорировать классы без атрибута.
    /// </summary>
    [Fact]
    public void Generator_ShouldNotActivate_WhenAttributeNotFound()
    {
        // Arrange: partial class без атрибута [GenerateFilter].
        var source = @"
namespace TestNamespace;

public class Order
{
    public int Id { get; set; }
}

// Нет атрибута [GenerateFilter] — генератор не должен активироваться
public partial class OrderFilterDefinition
{
}
";

        var runResult = GeneratorTestHelper.RunGenerator(source);

        // Assert: нет атрибута → генератор не создаёт ни одного файла.
        Assert.Empty(runResult.GeneratedTrees);
    }

    /// <summary>
    /// Атрибут с ClassName: генерирует класс с указанным именем.
    /// [GenerateFilter(typeof(E), ClassName = "MyFilter")] → MyFilter.Filter.g.cs.
    /// </summary>
    [Fact]
    public void Naming_ExplicitClassName_UsesAttributeValue()
    {
        var source = @"
using GreenNide.ExpressionFilter;
namespace T;
public class E { public int Id { get; set; } }
[GenerateFilter(typeof(E), ClassName = ""MyFilter"")]
public partial class OrderFilterDefinition
{
    public static System.Linq.Expressions.Expression<Func<E, int>>? Id { get; } = o => o.Id;
}
";
        var result = GeneratorTestHelper.RunGenerator(source);
        Assert.Single(result.GeneratedTrees);
        Assert.Contains("MyFilter.Filter.g.cs", result.GeneratedTrees[0].FilePath);
    }
}
