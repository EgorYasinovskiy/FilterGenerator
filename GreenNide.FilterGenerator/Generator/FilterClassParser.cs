using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace GreenNide.FilterGenerator.Generator;

public sealed partial class FilterGenerator
{
    private static ParseResult? ParseFilterClass(
        GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var classSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        // 1. Извлекаем тип сущности
        var genAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == GenerateFilterAttrFqn);

        if (genAttr?.ConstructorArguments.Length < 1 ||
            genAttr?.ConstructorArguments[0].Value is not INamedTypeSymbol entityType)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.EntityTypeNotFound,
                classSymbol.Locations.FirstOrDefault(),
                classSymbol.Name));

            return new ParseResult { Definition = null, Diagnostics = diagnostics.ToImmutable() };
        }

        var def = new FilterClassDefinition
        {
            Namespace = classSymbol.ContainingNamespace.ToDisplayString(),
            ClassName = classSymbol.Name,
            GeneratedClassName = ResolveGeneratedClassName(classSymbol, genAttr, entityType.Name),
            EntityName = entityType.Name,
            EntityFullName = entityType.ToDisplayString()
        };

        // 2. Expression-поля
        foreach (var member in classSymbol.GetMembers())
        {
            if (!member.IsStatic || member.IsImplicitlyDeclared) continue;

            ITypeSymbol typeSymbol;
            if (member is IFieldSymbol f)
                typeSymbol = f.Type;
            else if (member is IPropertySymbol p)
                typeSymbol = p.Type;
            else
                continue;

            if (!TryParseExpressionType(typeSymbol, entityType, out var returnTypeSymbol))
                continue;

            var memberName = member.Name;
            var hasSearch = HasAttribute(member, "SearchAttribute");

            FilterFieldDefinition? field = null;


            if (hasSearch || IsStringArray(returnTypeSymbol))
                field = ParseSearchField(member, memberName, ct, diagnostics);
            else
                field = ParseSimpleField(member, memberName, returnTypeSymbol, ct, diagnostics);

            if (field is not null)
                def.Fields.Add(field);
        }

        // 3. Методы-предикаты
        foreach (var member in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (!member.IsStatic || member.MethodKind != MethodKind.Ordinary) continue;
            if (member.DeclaredAccessibility == Accessibility.Private) continue;

            if (!IsPredicateMethod(member, entityType, classSymbol, out _))
                continue;

            var methodDef = ParseMethodPredicate(member, classSymbol, ct, diagnostics);
            if (methodDef is not null)
                def.Methods.Add(methodDef);
        }

        return new ParseResult { Definition = def, Diagnostics = diagnostics.ToImmutable() };
    }

// Проверяет, что тип — Expression<Func<TEntity, TReturn>>
    private static bool TryParseExpressionType(
        ITypeSymbol type, INamedTypeSymbol expectedEntity, out ITypeSymbol returnType)
    {
        returnType = null!;

        if (type is not INamedTypeSymbol named) return false;
        if (named.Name != "Expression" || named.TypeArguments.Length != 1) return false;
        if (named.TypeArguments[0] is not INamedTypeSymbol func) return false;
        if (func.Name != "Func" || func.TypeArguments.Length < 2) return false;

        var entityArg = func.TypeArguments[0];
        if (!SymbolEqualityComparer.Default.Equals(entityArg, expectedEntity)) return false;

        returnType = func.TypeArguments[func.TypeArguments.Length - 1];
        return true;
    }

    private static bool IsStringArray(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arr)
            return arr.ElementType.SpecialType == SpecialType.System_String;
        return false;
    }

    private static bool HasAttribute(ISymbol member, string shortName)
    {
        return member.GetAttributes()
            .Any(a => a.AttributeClass?.Name == shortName);
    }

    /// <summary>
    ///     Вычисляет имя генерируемого класса по конвенции или атрибуту.
    ///     Приоритет:
    ///     1. ClassName из атрибута [GenerateFilter(ClassName = "...")]
    ///     2. Название сущности + FilterParams
    /// </summary>
    private static string ResolveGeneratedClassName(
        INamedTypeSymbol classSymbol, AttributeData genAttr, string entityName)
    {
        // 1. Явное имя из атрибута
        var classNameArg = genAttr.NamedArguments
            .FirstOrDefault(a => a.Key == "ClassName");
        if (classNameArg.Value.Value is string explicitName && !string.IsNullOrWhiteSpace(explicitName))
            return explicitName;

        return entityName + "FilterParams";
    }
}