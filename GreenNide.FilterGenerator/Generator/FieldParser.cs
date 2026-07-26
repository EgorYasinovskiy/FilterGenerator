using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using GreenNide.ExpressionFilter;
using Microsoft.CodeAnalysis;

namespace GreenNide.FilterGenerator.Generator;

public sealed partial class FilterGenerator
{
    private static FilterFieldDefinition? ParseSimpleField(
        ISymbol member, string name, ITypeSymbol returnType,
        CancellationToken ct, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var syntaxRef = member.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef is null)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.ExpressionPathNotExtracted,
                member.Locations.FirstOrDefault(),
                name));
            return null;
        }

        var path = ExtractPathFromLambda(syntaxRef, ct);
        if (path is null)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.ExpressionPathNotExtracted,
                member.Locations.FirstOrDefault(),
                name));
            return null;
        }

        var compareAttr = member.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "CompareAttribute");
        var op = compareAttr is not null
            ? (CompareOperator)(int)compareAttr.ConstructorArguments[0].Value!
            : InferDefaultOperator(returnType);

        var isNullableValueType = returnType.IsValueType
            && returnType.NullableAnnotation == NullableAnnotation.Annotated;

        return new FilterFieldDefinition
        {
            PropertyName = name,
            PropertyTypeCs = ToNullableCsType(returnType),
            EntityPath = path,
            Operator = op,
            Kind = FilterKind.Simple,
            NavigationNullGuard = BuildNullGuard(path),
            IsNullableValueType = isNullableValueType
        };
    }

    private static FilterFieldDefinition? ParseSearchField(
        ISymbol member, string name,
        CancellationToken ct, ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>.Builder diagnostics)
    {
        var syntaxRef = member.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef is null)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.ExpressionPathNotExtracted,
                member.Locations.FirstOrDefault(), name));
            return null;
        }

        var path = ExtractArrayElementsFromLambda(syntaxRef, ct);
        if (path is null)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.ExpressionPathNotExtracted,
                member.Locations.FirstOrDefault(), name));
            return null;
        }

        return new FilterFieldDefinition
        {
            PropertyName = name,
            PropertyTypeCs = "string?",
            EntityPath = path,
            Operator = CompareOperator.Contains,
            Kind = FilterKind.Search
        };
    }


    private static CompareOperator InferDefaultOperator(ITypeSymbol returnType)
    {
        if (returnType.SpecialType == SpecialType.System_String)
            return CompareOperator.Contains;
        return CompareOperator.Equal;
    }

    private static string ToNullableCsType(ITypeSymbol type)
    {
        if (type.NullableAnnotation == NullableAnnotation.Annotated)
            return type.ToDisplayString();

        if (type.IsValueType)
            return type.ToDisplayString() + "?";

        // reference type — и так nullable
        return type.ToDisplayString() + "?";
    }

    private static string? BuildNullGuard(string path)
    {
        // Простые навигации: "Customer.Name" → "e.Customer != null"
        // Многоуровневые: "Order.Customer.Address.City" →
        //   "e.Order != null && e.Order.Customer != null && e.Order.Customer.Address != null"
        // Сложные LINQ: "History.OrderByDescending(...).Select(...).FirstOrDefault()" →
        //   только первый сегмент: "e.History != null"

        var openParen = path.IndexOf('(');
        var navPath = openParen >= 0 ? path.Substring(0, openParen) : path;

        var parts = navPath.Split('.');
        if (parts.Length <= 1) return null;

        var guards = new List<string>();
        var current = "e";
        for (var i = 0; i < parts.Length - 1; i++)
        {
            current += $".{parts[i]}";
            guards.Add($"{current} != null");
        }

        return string.Join(" && ", guards);
    }
}