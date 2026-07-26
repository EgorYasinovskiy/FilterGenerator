using System.Reflection;
using GreenNide.FilterGenerator;
using Xunit;

namespace GreenNide.ExpressionFilter.Tests.SourceGenerator;

/// <summary>
/// Тесты генерации кода: null-guard для навигаций, суффикс .Value для nullable value types,
/// проверка null для параметра filter, структура сгенерированного кода.
/// Все тесты вызывают CodeGenerator через рефлексию (internal API) и проверяют
/// текст сгенерированного кода на наличие/отсутствие нужных строк.
/// </summary>
public class CodeGeneratorTests
{
    private static readonly Type GeneratorType = typeof(GreenNide.FilterGenerator.Generator.FilterGenerator);
    private static readonly MethodInfo GenerateCodeMethod =
        GeneratorType.GetMethod("GenerateCode", BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Обёртка над приватным методом CodeGenerator.GenerateCode().
    /// Автоматически вычисляет GeneratedClassName из EntityName по конвенции:
    ///   - Если не задан явно → {EntityName}FilterParams
    /// </summary>
    private static string GenerateCode(FilterClassDefinition def)
    {
        if (string.IsNullOrEmpty(def.GeneratedClassName))
            def.GeneratedClassName = def.EntityName + "FilterParams";
        return (string)GenerateCodeMethod.Invoke(null, [def])!;
    }

    /// <summary>
    /// Обёртка над приватным методом BuildNullGuard().
    /// Принимает строковый путь (например, "Customer.Name") и возвращает
    /// строку null-guard (например, "e.Customer != null") или null для одноуровневых путей.
    /// </summary>
    private static string? BuildNullGuardViaReflection(string path)
    {
        var method = GeneratorType.GetMethod("BuildNullGuard",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (string?)method!.Invoke(null, [path]);
    }

    // ─── Null-guard для многоуровневых навигаций ──────────

    /// <summary>
    /// Проверяет генерацию null-guard для 4-уровневой навигации: Order.Customer.Address.City.
    /// Для каждого промежуточного звена (кроме последнего) должна быть сгенерирована
    /// проверка на null: e.Order != null, e.Order.Customer != null, e.Order.Customer.Address != null.
    /// Это предотвращает NullReferenceException при обращении через цепочку навигаций.
    /// </summary>
    [Fact]
    public void MultiLevelNavigation_ShouldCheckAllIntermediatePaths()
    {
        // Arrange: поле фильтра City ссылается на путь Order.Customer.Address.City.
        var def = new FilterClassDefinition
        {
            Namespace = "Test",
            ClassName = "OrderFilterDefinition",
            EntityName = "Order",
            EntityFullName = "Test.Order",
            Fields =
            [
                new FilterFieldDefinition
                {
                    PropertyName = "City",
                    PropertyTypeCs = "string?",
                    EntityPath = "Order.Customer.Address.City",
                    Operator = CompareOperator.Equal,
                    Kind = FilterKind.Simple,
                    NavigationNullGuard = BuildNullGuardViaReflection("Order.Customer.Address.City")
                }
            ]
        };

        // Act: генерируем код.
        var code = GenerateCode(def);

        // Assert: в сгенерированном коде должны быть проверки для каждого промежуточного звена.
        // Последний фрагмент ("City") — это целевое поле, проверка на null для него не нужна.
        Assert.Contains("e.Order != null", code);
        Assert.Contains("e.Order.Customer != null", code);
        Assert.Contains("e.Order.Customer.Address != null", code);
    }

    /// <summary>
    /// Проверяет генерацию null-guard для 2-уровневой навигации: Customer.Name.
    /// Должна быть сгенерирована проверка e.Customer != null.
    /// Само поле Name не требует null-guard, так как оно — конечное звено.
    /// </summary>
    [Fact]
    public void TwoLevelNavigation_ShouldCheckIntermediatePath()
    {
        // Arrange: поле CustomerName ссылается на Customer.Name.
        var def = new FilterClassDefinition
        {
            Namespace = "Test",
            ClassName = "OrderFilterDefinition",
            EntityName = "Order",
            EntityFullName = "Test.Order",
            Fields =
            [
                new FilterFieldDefinition
                {
                    PropertyName = "CustomerName",
                    PropertyTypeCs = "string?",
                    EntityPath = "Customer.Name",
                    Operator = CompareOperator.Equal,
                    Kind = FilterKind.Simple,
                    NavigationNullGuard = BuildNullGuardViaReflection("Customer.Name")
                }
            ]
        };

        var code = GenerateCode(def);

        // Assert: только одна проверка — e.Customer != null.
        Assert.Contains("e.Customer != null", code);
    }

    /// <summary>
    /// Проверяет, что для одноуровневого пути (Amount) null-guard НЕ генерируется.
    /// Свойство Amount напрямую принадлежит сущности Order, навигация отсутствует.
    /// </summary>
    [Fact]
    public void SingleLevelPath_ShouldHaveNoNullGuard()
    {
        // Act: вызываем BuildNullGuard для простого пути без навигации.
        var guard = BuildNullGuardViaReflection("Amount");

        // Assert: null-guard не нужен — возвращается null.
        Assert.Null(guard);
    }

    // ─── Суффикс .Value для nullable value types ──────────

    /// <summary>
    /// Проверяет, что для ссылочных типов (Customer?) суффикс .Value НЕ добавляется.
    /// Ссылочные типы не имеют .Value — для них используется только проверка != null.
    /// В сгенерированном коде должно быть filter.Customer, но не filter.Customer.Value.
    /// </summary>
    [Fact]
    public void ReferenceType_ShouldNotUseValueSuffix()
    {
        var def = new FilterClassDefinition
        {
            Namespace = "Test",
            ClassName = "OrderFilterDefinition",
            EntityName = "Order",
            EntityFullName = "Test.Order",
            Fields =
            [
                new FilterFieldDefinition
                {
                    PropertyName = "Customer",
                    PropertyTypeCs = "Customer?",
                    EntityPath = "Customer",
                    Operator = CompareOperator.Equal,
                    Kind = FilterKind.Simple,
                    NavigationNullGuard = null,
                    IsNullableValueType = false
                }
            ]
        };

        var code = GenerateCode(def);

        // Assert: .Value не должен присутствовать для ссылочных типов.
        Assert.DoesNotContain("filter.Customer.Value", code);
        Assert.Contains("filter.Customer", code);
    }

    /// <summary>
    /// Проверяет, что для nullable value types (int?) суффикс .Value добавляется.
    /// Nullable value types (.HasValue / .Value) требуют явного извлечения значения.
    /// В сгенерированном коде должно быть filter.MinId.Value.
    /// </summary>
    [Fact]
    public void ValueType_ShouldUseValueSuffix()
    {
        var def = new FilterClassDefinition
        {
            Namespace = "Test",
            ClassName = "OrderFilterDefinition",
            EntityName = "Order",
            EntityFullName = "Test.Order",
            Fields =
            [
                new FilterFieldDefinition
                {
                    PropertyName = "MinId",
                    PropertyTypeCs = "int?",
                    EntityPath = "Id",
                    Operator = CompareOperator.GreaterThanOrEqual,
                    Kind = FilterKind.Simple,
                    NavigationNullGuard = null,
                    IsNullableValueType = true
                }
            ]
        };

        var code = GenerateCode(def);

        // Assert: для int? должен быть суффикс .Value.
        Assert.Contains("filter.MinId.Value", code);
    }

    /// <summary>
    /// Проверяет, что для string? суффикс .Value НЕ добавляется.
    /// string — ссылочный тип, хотя и NullableAnnotation.Annotated.
    /// Для строк используется Contains/StartsWith/EndsWith, а не .Value.
    /// </summary>
    [Fact]
    public void StringType_ShouldNotUseValueSuffix()
    {
        var def = new FilterClassDefinition
        {
            Namespace = "Test",
            ClassName = "OrderFilterDefinition",
            EntityName = "Order",
            EntityFullName = "Test.Order",
            Fields =
            [
                new FilterFieldDefinition
                {
                    PropertyName = "Name",
                    PropertyTypeCs = "string?",
                    EntityPath = "Name",
                    Operator = CompareOperator.Contains,
                    Kind = FilterKind.Simple,
                    NavigationNullGuard = null,
                    IsNullableValueType = false
                }
            ]
        };

        var code = GenerateCode(def);

        // Assert: .Value не должен присутствовать для строк.
        Assert.DoesNotContain("filter.Name.Value", code);
    }

    // ─── Проверка null для параметра filter ────────────────

    /// <summary>
    /// Проверяет, что в начале метода Apply() генерируется проверка filter is null.
    /// Если filter == null, метод должен вернуть исходный запрос без фильтрации.
    /// Это защита от NullReferenceException при вызове Apply(null).
    /// </summary>
    [Fact]
    public void ShouldCheckFilterForNull()
    {
        var def = new FilterClassDefinition
        {
            Namespace = "Test",
            ClassName = "OrderFilterDefinition",
            EntityName = "Order",
            EntityFullName = "Test.Order",
            Fields =
            [
                new FilterFieldDefinition
                {
                    PropertyName = "Name",
                    PropertyTypeCs = "string?",
                    EntityPath = "Name",
                    Operator = CompareOperator.Contains,
                    Kind = FilterKind.Simple,
                    NavigationNullGuard = null
                }
            ]
        };

        var code = GenerateCode(def);

        // Assert: в сгенерированном коде должна быть строка "if (filter is null) return query;"
        Assert.Contains("if (filter is null) return query;", code);
    }

    // ─── Структура сгенерированного кода ───────────────────

    /// <summary>
    /// Проверяет, что сгенерированный код содержит метод Apply() с правильной сигнатурой:
    /// - Расширяемый тип: IQueryable&lt;Order&gt;
    /// - Параметр: OrderFilter filter
    /// Даже если фильтр не содержит полей, метод Apply() всё равно должен быть сгенерирован.
    /// </summary>
    [Fact]
    public void GeneratedCode_ShouldContainApplyMethod()
    {
        // Arrange: фильтр без полей — проверяем только структуру метода.
        var def = new FilterClassDefinition
        {
            Namespace = "Test",
            ClassName = "OrderFilterDefinition",
            EntityName = "Order",
            EntityFullName = "Test.Order",
            Fields = []
        };

        var code = GenerateCode(def);

        // Assert: метод Apply() с правильной сигнатурой должен присутствовать.
        Assert.Contains("public static IQueryable<Order> Apply(", code);
        Assert.Contains("OrderFilterParams filter", code);
    }

    /// <summary>
    /// Проверяет, что генератор создаёт два типа:
    /// 1. Class OrderFilter — отдельный класс со свойствами фильтра (НЕ partial).
    /// 2. Static class OrderFilterExtensions — с методом Apply().
    /// Также проверяет правильность namespace.
    /// </summary>
    [Fact]
    public void GeneratedCode_ShouldContainClassAndExtensions()
    {
        var def = new FilterClassDefinition
        {
            Namespace = "Test",
            ClassName = "OrderFilterDefinition",
            EntityName = "Order",
            EntityFullName = "Test.Order",
            Fields = []
        };

        var code = GenerateCode(def);

        // Assert: отдельный class + static extensions class + namespace.
        Assert.Contains("public class OrderFilterParams", code);
        Assert.Contains("public static class OrderFilterParamsExtensions", code);
        Assert.Contains("namespace Test;", code);
    }
}
