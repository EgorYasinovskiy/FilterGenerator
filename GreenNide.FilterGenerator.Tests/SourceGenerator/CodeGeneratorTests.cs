using System.Reflection;
using GreenNide.FilterGenerator;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace GreenNide.ExpressionFilter.Tests.SourceGenerator;

/// <summary>
///     Тесты генерации кода: null-guard для навигаций, суффикс .Value для nullable value types,
///     проверка null для параметра filter, структура сгенерированного кода.
///     Все тесты вызывают CodeGenerator через рефлексию (internal API) и проверяют
///     текст сгенерированного кода на наличие/отсутствие нужных строк.
/// </summary>
public class CodeGeneratorTests
{
    private static readonly Type GeneratorType = typeof(FilterGenerator.Generator.FilterGenerator);

    private static readonly MethodInfo GenerateCodeMethod =
        GeneratorType.GetMethod("GenerateCode", BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    ///     Обёртка над приватным методом CodeGenerator.GenerateCode().
    ///     Автоматически вычисляет GeneratedClassName из EntityName по конвенции:
    ///     - Если не задан явно → {EntityName}FilterParams
    /// </summary>
    private static string GenerateCode(FilterClassDefinition def)
    {
        if (string.IsNullOrEmpty(def.GeneratedClassName))
            def.GeneratedClassName = def.EntityName + "FilterParams";
        return (string)GenerateCodeMethod.Invoke(null, [def])!;
    }

    /// <summary>
    ///     Обёртка над приватным методом BuildNullGuard().
    ///     Принимает строковый путь (например, "Customer.Name") и возвращает
    ///     строку null-guard (например, "e.Customer != null") или null для одноуровневых путей.
    /// </summary>
    private static string? BuildNullGuardViaReflection(string path)
    {
        var method = GeneratorType.GetMethod("BuildNullGuard",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (string?)method!.Invoke(null, [path]);
    }

    // ─── Null-guard для многоуровневых навигаций ──────────

    // ─── AST-based замена параметра: без ложных срабатываний ──────────

    /// <summary>
    ///     Проверяет, что AST-based замена параметра не ломает
    ///     идентификаторы, заканчивающиеся на букву параметра перед точкой.
    ///     При параметре "t" и пути "t.Project.Statuses" — метод string.Replace
    ///     найдёт "t." внутри "Project." и сломает результат в "e.Project.e.Statuses".
    ///     AST-подход заменяет ТОЛЬКО узлы IdentifierNameSyntax, совпадающие с именем параметра.
    /// </summary>
    [Fact]
    public void ReplaceParamWithEntity_ShouldNotBreakIdentifiersEndingWithParamChar()
    {
        // Arrange: выражение t.Project.Statuses, где параметр — "t".
        // Проблема: строка "t.Project.Statuses" содержит подстроку "t." в двух местах:
        // 1. "t." в начале (референс параметра)
        // 2. "t." внутри "Project.Statuses" (конец "Project" + точка)
        var body = SyntaxFactory.ParseExpression("t.Project.Statuses.FirstOrDefault()");
        var method = GeneratorType.GetMethod("ReplaceParamWithEntity",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        // Act: заменяем параметр "t" на "e" AST-обходом.
        var result = (string)method.Invoke(null, [body, "t"])!;

        // Assert: результат должен быть "e.Project.Statuses.FirstOrDefault()".
        // НЕ должно быть "e.Projece.Statuses" (как дал бы string.Replace).
        Assert.Equal("e.Project.Statuses.FirstOrDefault()", result);
    }

    /// <summary>
    ///     Проверяет, что для тернарного выражения с параметром "t" и идентификатором
    ///     "Project" (заканчивающимся на 't') все ветки корректно используют "e."
    ///     и не получают двойную замену внутри "Project.Statuses".
    /// </summary>
    [Fact]
    public void TernaryExpression_WithT_ParamAndProject_Termination_ShouldBeCorrect()
    {
        // Arrange: тернарное выражение, где параметр назван "t",
        // и есть обращение t.Project.Statuses (Project заканчивается на 't').
        // До фикса string.Replace давал "e.Projece.Statuses" — сломанный результат.
        var body = SyntaxFactory.ParseExpression(
            "t.History.Any() ? t.History.FirstOrDefault() : t.Project.Statuses.FirstOrDefault()");
        var method = GeneratorType.GetMethod("ReplaceParamWithEntity",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        // Act
        var result = (string)method.Invoke(null, [body, "t"])!;

        // Assert: все три ветки должны иметь "e." и не иметь двойной замены.
        Assert.Equal(
            "e.History.Any() ? e.History.FirstOrDefault() : e.Project.Statuses.FirstOrDefault()",
            result);
        Assert.DoesNotContain("e.Projece.Statuses", result);
    }

    /// <summary>
    ///     Проверяет, что при одинаковом имени параметра во внешней и вложенной лямбдах
    ///     заменяется только внешний параметр, а внутренний остаётся нетронутым.
    ///     t => t.History.FirstOrDefault(t => t.StatusId == 1)
    ///     → e.History.FirstOrDefault(t => t.StatusId == 1)
    ///     (внутренний t — это параметр вложенной лямбды, а не внешний)
    /// </summary>
    [Fact]
    public void ReplaceParamWithEntity_ShouldNotReplaceInnerLambdaParamWithSameName()
    {
        var body = SyntaxFactory.ParseExpression(
            "t.History.FirstOrDefault(t => t.StatusId == 1)");
        var method = GeneratorType.GetMethod("ReplaceParamWithEntity",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, [body, "t"])!;

        Assert.Equal("e.History.FirstOrDefault(t => t.StatusId == 1)", result);
    }

    /// <summary>
    ///     Проверяет, что при вложенной лямбе с другим именем параметра,
    ///     ссылки на внешний параметр внутри тела вложенной лямбы
    ///     всё равно корректно заменяются на "e".
    ///     t => t.History.FirstOrDefault(h => h.StatusId == t.Value)
    ///     → e.History.FirstOrDefault(h => h.StatusId == e.Value)
    /// </summary>
    [Fact]
    public void ReplaceParamWithEntity_ShouldReplaceOuterParamInsideInnerLambda()
    {
        var body = SyntaxFactory.ParseExpression(
            "t.History.FirstOrDefault(h => h.StatusId == t.Value)");
        var method = GeneratorType.GetMethod("ReplaceParamWithEntity",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, [body, "t"])!;

        Assert.Equal("e.History.FirstOrDefault(h => h.StatusId == e.Value)", result);
    }

    /// <summary>
    ///     Where + обращение к внешнему параметру из внутренней лямбы.
    ///     t.History.Where(x => x.StatusId == t.Status.Id)
    ///     → e.History.Where(x => x.StatusId == e.Status.Id)
    /// </summary>
    [Fact]
    public void ReplaceParamWithEntity_WhereWithOuterParamRef_ShouldReplace()
    {
        var body = SyntaxFactory.ParseExpression(
            "t.History.Where(x => x.StatusId == t.Status.Id)");
        var method = GeneratorType.GetMethod("ReplaceParamWithEntity",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, [body, "t"])!;

        Assert.Equal("e.History.Where(x => x.StatusId == e.Status.Id)", result);
    }

    /// <summary>
    ///     Select + обращение к внешнему параметру из внутренней лямбы.
    ///     t.History.Select(x => new { x.Id, t.Name })
    ///     → e.History.Select(x => new { x.Id, e.Name })
    /// </summary>
    [Fact]
    public void ReplaceParamWithEntity_SelectWithOuterParamRef_ShouldReplace()
    {
        var body = SyntaxFactory.ParseExpression(
            "t.History.Select(x => new { x.Id, t.Name })");
        var method = GeneratorType.GetMethod("ReplaceParamWithEntity",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, [body, "t"])!;

        Assert.Equal("e.History.Select(x => new { x.Id, e.Name })", result);
    }

    /// <summary>
    ///     GroupBy + обращение к внешнему параметру.
    ///     t.Items.GroupBy(x => x.Category, x => x.Price, (k, g) => new { k, Sum = g.Sum(s => s) + t.Bonus })
    ///     → e.Items.GroupBy(x => x.Category, x => x.Price, (k, g) => new { k, Sum = g.Sum(s => s) + e.Bonus })
    ///     Внутренняя лямба g.Sum(s => s) не должна быть затронута.
    /// </summary>
    [Fact]
    public void ReplaceParamWithEntity_GroupByWithNestedLambdaAndOuterRef_ShouldReplace()
    {
        var body = SyntaxFactory.ParseExpression(
            "t.Items.GroupBy(x => x.Category, x => x.Price, (k, g) => new { k, Sum = g.Sum(s => s) + t.Bonus })");
        var method = GeneratorType.GetMethod("ReplaceParamWithEntity",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, [body, "t"])!;

        Assert.Equal(
            "e.Items.GroupBy(x => x.Category, x => x.Price, (k, g) => new { k, Sum = g.Sum(s => s) + e.Bonus })",
            result);
    }

    /// <summary>
    ///     Any с тремя уровнями вложенности и обращением к внешнему параметру.
    ///     t.History.Any(x => x.Children.Any(y => y.Active && y.Id == t.PrimaryId))
    ///     → e.History.Any(x => x.Children.Any(y => y.Active && y.Id == e.PrimaryId))
    ///     Внутренние параметры x и y не должны заменяться.
    /// </summary>
    [Fact]
    public void ReplaceParamWithEntity_DeepNestedLambdaWithOuterRef_ShouldReplace()
    {
        var body = SyntaxFactory.ParseExpression(
            "t.History.Any(x => x.Children.Any(y => y.Active && y.Id == t.PrimaryId))");
        var method = GeneratorType.GetMethod("ReplaceParamWithEntity",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, [body, "t"])!;

        Assert.Equal(
            "e.History.Any(x => x.Children.Any(y => y.Active && y.Id == e.PrimaryId))",
            result);
    }

    /// <summary>
    ///     SelectMany + тернарник + обращение к внешнему параметру.
    ///     t.History.SelectMany(x => x.Tags, (x, tag) => x.IsActive ? t.DefaultTag : tag.Name)
    ///     → e.History.SelectMany(x => x.Tags, (x, tag) => x.IsActive ? e.DefaultTag : tag.Name)
    /// </summary>
    [Fact]
    public void ReplaceParamWithEntity_SelectManyWithTernaryAndOuterRef_ShouldReplace()
    {
        var body = SyntaxFactory.ParseExpression(
            "t.History.SelectMany(x => x.Tags, (x, tag) => x.IsActive ? t.DefaultTag : tag.Name)");
        var method = GeneratorType.GetMethod("ReplaceParamWithEntity",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, [body, "t"])!;

        Assert.Equal(
            "e.History.SelectMany(x => x.Tags, (x, tag) => x.IsActive ? e.DefaultTag : tag.Name)",
            result);
    }

    /// <summary>
    ///     Две вложенные лямбы с одинаковым именем (обе теневят параметр).
    ///     t.History.Where(t => t.StatusId == 1).Any(t => t.Id == t.History.Count)
    ///     → e.History.Where(t => t.StatusId == 1).Any(t => t.Id == t.History.Count)
    ///     Все теневящие лямбы защищены — ничего кроме внешних t не заменяется.
    /// </summary>
    [Fact]
    public void ReplaceParamWithEntity_MultipleShadowedLambdas_ShouldNotReplace()
    {
        var body = SyntaxFactory.ParseExpression(
            "t.History.Where(t => t.StatusId == 1).Any(t => t.Id == t.History.Count)");
        var method = GeneratorType.GetMethod("ReplaceParamWithEntity",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, [body, "t"])!;

        Assert.Equal(
            "e.History.Where(t => t.StatusId == 1).Any(t => t.Id == t.History.Count)",
            result);
    }

    /// <summary>
    ///     Тернарник + вложенные лямбы: внешний параметр в условии и в ветках.
    ///     t.History.Any() ? t.History.Where(x => x.Status == t.CurrentStatus).FirstOrDefault() : t.DefaultStatus
    ///     → e.History.Any() ? e.History.Where(x => x.Status == e.CurrentStatus).FirstOrDefault() : e.DefaultStatus
    /// </summary>
    [Fact]
    public void ReplaceParamWithEntity_TernaryWithNestedLambdasAndOuterRef_ShouldReplace()
    {
        var body = SyntaxFactory.ParseExpression(
            "t.History.Any() ? t.History.Where(x => x.Status == t.CurrentStatus).FirstOrDefault() : t.DefaultStatus");
        var method = GeneratorType.GetMethod("ReplaceParamWithEntity",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, [body, "t"])!;

        Assert.Equal(
            "e.History.Any() ? e.History.Where(x => x.Status == e.CurrentStatus).FirstOrDefault() : e.DefaultStatus",
            result);
    }
    [Fact]
    public void ReplaceParamWithEntity_SimpleMemberAccess_ShouldWork()
    {
        var body = SyntaxFactory.ParseExpression("o.Customer.Name");
        var method = GeneratorType.GetMethod("ReplaceParamWithEntity",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, [body, "o"])!;

        Assert.Equal("e.Customer.Name", result);
    }

    // ─── Null-guard с префиксом "e." ──────────

    /// <summary>
    ///     Проверяет, что BuildNullGuard корректно обрабатывает путь,
    ///     который начинается с "e." (как возвращает ReplaceParamWithEntity).
    ///     Должен вернуть "e.History != null" для пути
    ///     "e.History.OrderByDescending(...).Any() ? ... : ..."
    /// </summary>
    [Fact]
    public void BuildNullGuard_WithEPrefix_ShouldStripPrefixAndBuildGuard()
    {
        var guard = BuildNullGuardViaReflection(
            "e.History.OrderByDescending(h => h.CreatedAt).Select(x => (long?)x.StatusId).Any() ? e.History.OrderByDescending(h => h.CreatedAt).Select(x => (long?)x.StatusId).FirstOrDefault() : e.Project.Statuses.OrderBy(x=>x.Order).Select(x=>x.Id).FirstOrDefault()");

        Assert.NotNull(guard);
        Assert.Contains("e.History != null", guard);
        // Не должно быть "ee." или "e.e.History" (двойной префикс).
        Assert.DoesNotContain("ee.", guard!);
        Assert.DoesNotContain("e.e.", guard!);
    }

    /// <summary>
    ///     Проверяет, что BuildNullGuard для простого пути без "e."
    ///     по-прежнему работает (обратная совместимость).
    /// </summary>
    [Fact]
    public void BuildNullGuard_WithoutEPrefix_ShouldWorkAsBefore()
    {
        var guard = BuildNullGuardViaReflection("Customer.Name");

        Assert.NotNull(guard);
        Assert.Equal("e.Customer != null", guard);
    }

    // ─── Генерация кода с e.-префиксом в EntityPath ──────────

    /// <summary>
    ///     Проверяет, что при EntityPath, начинающемся с "e.",
    ///     код генератора НЕ добавляет повторный "e." (без "ee.").
    ///     Воспроизводит сценарий: сложный LINQ-выражение (ReconstructChain)
    ///     вернул путь с "e." префиксом, и CodeGenerator корректно его использует.
    /// </summary>
    [Fact]
    public void GenerateCode_WithEPrefixedEntityPath_ShouldNotDoublePrefix()
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
                    PropertyName = "CurrentStatus",
                    PropertyTypeCs = "OrderStatus?",
                    EntityPath = "e.History.OrderByDescending(h => h.CreatedAt).Select(x => (long?)x.StatusId).Any() ? e.History.OrderByDescending(h => h.CreatedAt).Select(x => (long?)x.StatusId).FirstOrDefault() : e.Project.Statuses.OrderBy(x=>x.Order).Select(x=>x.Id).FirstOrDefault()",
                    Operator = CompareOperator.Equal,
                    Kind = FilterKind.Simple,
                    NavigationNullGuard = BuildNullGuardViaReflection(
                        "e.History.OrderByDescending(h => h.CreatedAt).Select(x => (long?)x.StatusId).Any() ? e.History.OrderByDescending(h => h.CreatedAt).Select(x => (long?)x.StatusId).FirstOrDefault() : e.Project.Statuses.OrderBy(x=>x.Order).Select(x=>x.Id).FirstOrDefault()"),
                    IsNullableValueType = true
                }
            ]
        };

        var code = GenerateCode(def);

        // Все ветки должны иметь "e." и не иметь "ee.".
        Assert.Contains("e.History.OrderByDescending", code);
        Assert.Contains("e.Project.Statuses.OrderBy", code);
        Assert.DoesNotContain("ee.", code);
        // Проверяем, что в коде нет сломанных веток без префикса.
        Assert.DoesNotContain("? History.OrderByDescending", code);
        Assert.DoesNotContain(": Project.Statuses.OrderBy", code);
    }

    /// <summary>
    ///     Проверяет, что в сгенерированном коде для тернарного выражения
    ///     с несколькими ссылками на сущность (o.History и o.Project.X)
    ///     все ветки корректно получают префикс "e." — без дублирования и без пропусков.
    ///     Тест воспроизводит баг, когда ReconstructChain заменял все "t." на пустую строку,
    ///     в результате чего ветки тернарника после "?" и ":" теряли префикс "e.".
    /// </summary>
    [Fact]
    public void TernaryExpression_ShouldHaveEntityPrefixInAllBranches()
    {
        // Arrange: поле с тернарным выражением, ссылающимся на сущность в нескольких ветках.
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
                    PropertyName = "CurrentStatus",
                    PropertyTypeCs = "int?",
                    EntityPath = "e.History.OrderByDescending(h => h.CreatedAt).Select(x => (long?)x.StatusId).Any() ? e.History.OrderByDescending(h => h.CreatedAt).Select(x => (long?)x.StatusId).FirstOrDefault() : e.Project.TaskStatuses.OrderBy(x=>x.Order).Select(x=>x.Id).FirstOrDefault()",
                    Operator = CompareOperator.Equal,
                    Kind = FilterKind.Simple,
                    NavigationNullGuard = BuildNullGuardViaReflection("e.History.OrderByDescending(h => h.CreatedAt).Select(x => (long?)x.StatusId).Any() ? e.History.OrderByDescending(h => h.CreatedAt).Select(x => (long?)x.StatusId).FirstOrDefault() : e.Project.TaskStatuses.OrderBy(x=>x.Order).Select(x=>x.Id).FirstOrDefault()"),
                    IsNullableValueType = true
                }
            ]
        };

        // Act: генерируем код.
        var code = GenerateCode(def);

        // Assert: все ветки тернарника должны иметь префикс "e." — без дублирования ("ee.") и без пропусков.
        Assert.Contains("e.History.OrderByDescending", code);
        Assert.Contains("e.Project.TaskStatuses.OrderBy", code);
        Assert.DoesNotContain("ee.", code);
    }

    /// <summary>
    ///     Проверяет генерацию null-guard для 4-уровневой навигации: Order.Customer.Address.City.
    ///     Для каждого промежуточного звена (кроме последнего) должна быть сгенерирована
    ///     проверка на null: e.Order != null, e.Order.Customer != null, e.Order.Customer.Address != null.
    ///     Это предотвращает NullReferenceException при обращении через цепочку навигаций.
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
    ///     Проверяет генерацию null-guard для 2-уровневой навигации: Customer.Name.
    ///     Должна быть сгенерирована проверка e.Customer != null.
    ///     Само поле Name не требует null-guard, так как оно — конечное звено.
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
    ///     Проверяет, что для одноуровневого пути (Amount) null-guard НЕ генерируется.
    ///     Свойство Amount напрямую принадлежит сущности Order, навигация отсутствует.
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
    ///     Проверяет, что для ссылочных типов (Customer?) суффикс .Value НЕ добавляется.
    ///     Ссылочные типы не имеют .Value — для них используется только проверка != null.
    ///     В сгенерированном коде должно быть filter.Customer, но не filter.Customer.Value.
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
    ///     Проверяет, что для nullable value types (int?) суффикс .Value добавляется.
    ///     Nullable value types (.HasValue / .Value) требуют явного извлечения значения.
    ///     В сгенерированном коде должно быть filter.MinId.Value.
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
    ///     Проверяет, что для string? суффикс .Value НЕ добавляется.
    ///     string — ссылочный тип, хотя и NullableAnnotation.Annotated.
    ///     Для строк используется Contains/StartsWith/EndsWith, а не .Value.
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
    ///     Проверяет, что в начале метода Apply() генерируется проверка filter is null.
    ///     Если filter == null, метод должен вернуть исходный запрос без фильтрации.
    ///     Это защита от NullReferenceException при вызове Apply(null).
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
    ///     Проверяет, что сгенерированный код содержит метод Apply() с правильной сигнатурой:
    ///     - Расширяемый тип: IQueryable&lt;Order&gt;
    ///     - Параметр: OrderFilter filter
    ///     Даже если фильтр не содержит полей, метод Apply() всё равно должен быть сгенерирован.
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
    ///     Проверяет, что генератор создаёт два типа:
    ///     1. Class OrderFilter — отдельный класс со свойствами фильтра (НЕ partial).
    ///     2. Static class OrderFilterExtensions — с методом Apply().
    ///     Также проверяет правильность namespace.
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