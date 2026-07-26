using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GreenNide.FilterGenerator.Generator;

public sealed partial class FilterGenerator
{
    // MethodParser.cs (partial class FilterGenerator)

    /// <summary>
    /// Проверяет, что метод имеет сигнатуру:
    /// Expression<Func<TEntity, bool>>? Method(FilterType filter)
    /// </summary>
    private static bool IsPredicateMethod(
        IMethodSymbol method, INamedTypeSymbol entityType,
        INamedTypeSymbol filterType, out ITypeSymbol returnType)
    {
        returnType = null!;

        // Параметры: ровно 1, типа FilterType
        if (method.Parameters.Length != 1) return false;
        if (!SymbolEqualityComparer.Default.Equals(
                method.Parameters[0].Type, filterType))
            return false;

        // Возвращаемый тип: Expression<Func<TEntity, bool>>?
        var retType = method.ReturnType;
        if (retType.NullableAnnotation != NullableAnnotation.Annotated) return false;
        if (retType is not INamedTypeSymbol namedRet) return false;
        if (namedRet.Name != "Expression" || namedRet.TypeArguments.Length != 1) return false;
        if (namedRet.TypeArguments[0] is not INamedTypeSymbol func) return false;
        if (func.Name != "Func" || func.TypeArguments.Length != 2) return false;
        if (!SymbolEqualityComparer.Default.Equals(func.TypeArguments[0], entityType)) return false;
        if (func.TypeArguments[1].SpecialType != SpecialType.System_Boolean) return false;

        returnType = func.TypeArguments[1];
        return true;
    }

    /// <summary>
    /// Извлекает замыкания на filter.X из тела метода.
    /// filter.ItemId.HasValue → свойство "ItemId", тип "int?"
    /// </summary>
    private static MethodDefinition? ParseMethodPredicate(
        IMethodSymbol method, INamedTypeSymbol filterType,
        CancellationToken ct, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef is null)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.PredicateMethodNotParsed,
                method.Locations.FirstOrDefault(), method.Name));
            return null;
        }

        var methodDecl = (MethodDeclarationSyntax)syntaxRef.GetSyntax(ct);
        var filterParamName = method.Parameters[0].Name;

        var closures = new List<ClosureProperty>();
        var seen = new HashSet<string>();

        foreach (var ma in methodDecl.DescendantNodes()
                     .OfType<MemberAccessExpressionSyntax>()
                     .Where(ma => ma.Expression is IdentifierNameSyntax id
                                  && id.Identifier.Text == filterParamName))
        {
            var propName = ma.Name.Identifier.Text;
            if (!seen.Add(propName)) continue;

            var propSymbol = filterType.GetMembers(propName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault();

            closures.Add(new ClosureProperty
            {
                PropertyName = propName,
                PropertyTypeCs = propSymbol is not null
                    ? ToNullableCsType(propSymbol.Type)
                    : "object?"
            });
        }

        if (closures.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.PredicateMethodNotParsed,
                method.Locations.FirstOrDefault(), method.Name));
            return null;
        }

        return new MethodDefinition
        {
            MethodName = method.Name,
            ClosureProperties = closures
        };
    }
}