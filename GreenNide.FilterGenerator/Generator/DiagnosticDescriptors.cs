using Microsoft.CodeAnalysis;

namespace GreenNide.FilterGenerator.Generator;

internal static class DiagnosticDescriptors
{
    public const string Category = "FilterGenerator";

    public static readonly DiagnosticDescriptor ExpressionPathNotExtracted = new(
        id: "GFG001",
        title: "Could not extract expression path",
        messageFormat: "Could not extract expression path from '{0}'. The field will be skipped.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor LambdaNotExpressionBodied = new(
        id: "GFG002",
        title: "Expression must be expression-bodied",
        messageFormat: "Expression field '{0}' must use expression-bodied syntax (=> ...), not block syntax.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PredicateMethodNotParsed = new(
        id: "GFG003",
        title: "Could not parse predicate method",
        messageFormat: "Could not parse predicate method '{0}'. The method will be skipped.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EntityTypeNotFound = new(
        id: "GFG004",
        title: "Entity type not found",
        messageFormat: "Could not resolve entity type from [GenerateFilter] attribute on '{0}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}