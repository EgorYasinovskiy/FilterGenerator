using Microsoft.CodeAnalysis;

namespace GreenNide.FilterGenerator.Generator;

internal static class DiagnosticDescriptors
{
    private const string Category = "FilterGenerator";

    public static readonly DiagnosticDescriptor ExpressionPathNotExtracted = new(
        "GFG001",
        "Could not extract expression path",
        "Could not extract expression path from '{0}'. The field will be skipped.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor LambdaNotExpressionBodied = new(
        "GFG002",
        "Expression must be expression-bodied",
        "Expression field '{0}' must use expression-bodied syntax (=> ...), not block syntax",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor PredicateMethodNotParsed = new(
        "GFG003",
        "Could not parse predicate method",
        "Could not parse predicate method '{0}'. The method will be skipped.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor EntityTypeNotFound = new(
        "GFG004",
        "Entity type not found",
        "Could not resolve entity type from [GenerateFilter] attribute on '{0}'",
        Category,
        DiagnosticSeverity.Error,
        true);
}