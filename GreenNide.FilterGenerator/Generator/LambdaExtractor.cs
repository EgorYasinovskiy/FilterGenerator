using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GreenNide.FilterGenerator.Generator;

public sealed partial class FilterGenerator
{
    // LambdaExtractor.cs (partial class FilterGenerator)

    /// <summary>
    /// Извлекает путь из лямбды в Expression-поле.
    /// o => o.Customer.Name → "Customer.Name"
    /// o => o.History.OrderByDescending(...).First().Status → вся цепочка вызовов
    /// </summary>
    private static string? ExtractPathFromLambda(SyntaxReference syntaxRef, CancellationToken ct)
    {
        var node = syntaxRef.GetSyntax(ct);

        var arrow = node.DescendantNodesAndSelf()
            .OfType<ArrowExpressionClauseSyntax>()
            .FirstOrDefault();

        LambdaExpressionSyntax? lambda = null;

        if (arrow?.Expression is LambdaExpressionSyntax al)
            lambda = al;
        else if (node is FieldDeclarationSyntax fieldDecl)
        {
            var variable = fieldDecl.Declaration.Variables.FirstOrDefault();
            lambda = variable?.Initializer?.Value as LambdaExpressionSyntax;
        }

        if (lambda is null)
        {
            // Ищем любую лямбду как fallback
            lambda = node.DescendantNodesAndSelf()
                .OfType<LambdaExpressionSyntax>()
                .FirstOrDefault();
        }

        return lambda is not null ? WalkTopLevelLambda(lambda) : null;
    }

    /// <summary>
    /// Извлекает raw expression для FullText поля
    /// </summary>
    private static string? ExtractRawExpression(SyntaxReference syntaxRef, CancellationToken ct)
    {
        var node = syntaxRef.GetSyntax(ct);

        var arrow = node.DescendantNodesAndSelf()
            .OfType<ArrowExpressionClauseSyntax>()
            .FirstOrDefault();

        if (arrow?.Expression is LambdaExpressionSyntax lambda)
        {
            var body = GetLambdaBodyExpression(lambda);
            if (body is null) return null;
            var paramName = GetParamName(lambda);
            return body.ToString().Replace($"{paramName}.", "e.");
        }

        return null;
    }
    
    /// <summary>
    /// Безопасно извлекает Expression из тела лямбды — 
    /// работает и с expression-body, и с block-body.
    /// </summary>
    private static ExpressionSyntax? GetLambdaBodyExpression(LambdaExpressionSyntax lambda)
    {
        if (lambda.Body is ExpressionSyntax expr)
            return expr;

        if (lambda.Body is BlockSyntax block)
            return block.Statements
                .OfType<ReturnStatementSyntax>()
                .FirstOrDefault()?.Expression;

        return null;
    }

    /// <summary>
    /// Извлекает элементы массива из Search-лямбды:
    /// o => new[] { o.Description, o.Customer.Name } → "Description|Customer.Name"
    /// </summary>
    private static string? ExtractArrayElementsFromLambda(SyntaxReference syntaxRef, CancellationToken ct)
    {
        var node = syntaxRef.GetSyntax(ct);

        var arrow = node.DescendantNodesAndSelf()
            .OfType<ArrowExpressionClauseSyntax>()
            .FirstOrDefault();

        LambdaExpressionSyntax? lambda = null;

        if (arrow?.Expression is LambdaExpressionSyntax al)
            lambda = al;
        else if (node is PropertyDeclarationSyntax propDecl)
            lambda = propDecl.Initializer?.Value as LambdaExpressionSyntax;
        else if (node is FieldDeclarationSyntax fieldDecl)
        {
            var variable = fieldDecl.Declaration.Variables.FirstOrDefault();
            lambda = variable?.Initializer?.Value as LambdaExpressionSyntax;
        }

        if (lambda is null)
            lambda = node.DescendantNodesAndSelf()
                .OfType<LambdaExpressionSyntax>()
                .FirstOrDefault();

        if (lambda is null) return null;

        var body = GetLambdaBodyExpression(lambda);
        if (body is null) return null;

        var paramName = GetParamName(lambda);

        var arrayCreation = body
                                .DescendantNodesAndSelf()
                                .OfType<ImplicitArrayCreationExpressionSyntax>()
                                .FirstOrDefault()
                            ?? body.DescendantNodesAndSelf()
                                .OfType<ArrayCreationExpressionSyntax>()
                                .FirstOrDefault() as ExpressionSyntax;

        if (arrayCreation is null) return null;

        var initializer = arrayCreation.DescendantNodesAndSelf()
            .OfType<InitializerExpressionSyntax>()
            .FirstOrDefault();
        if (initializer is null) return null;

        var paths = new List<string>();
        foreach (var expr in initializer.Expressions)
        {
            var path = WalkExpression(expr, paramName);
            if (!string.IsNullOrEmpty(path))
                paths.Add(path);
        }

        return paths.Count > 0 ? string.Join("|", paths) : null;
    }

    /// <summary>
    /// Обходит верхний уровень лямбды.
    /// o => o.Customer.Name → WalkExpression(body, "o")
    /// </summary>
    private static string? WalkTopLevelLambda(LambdaExpressionSyntax lambda)
    {
        var paramName = GetParamName(lambda);
        var body = GetLambdaBodyExpression(lambda);
        return body is not null ? WalkExpression(body, paramName) : null;
    }

    private static string GetParamName(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax s => s.Parameter.Identifier.Text,
            ParenthesizedLambdaExpressionSyntax p => p.ParameterList.Parameters
                .First().Identifier.Text,
            _ => "o"
        };
    }

    /// <summary>
    /// Рекурсивно обходит ExpressionSyntax и строит путь.
    /// </summary>
    private static string WalkExpression(ExpressionSyntax expr, string paramName)
    {
        switch (expr)
        {
            // o → "" (корень)
            case IdentifierNameSyntax id when id.Identifier.Text == paramName:
                return "";

            // o.Customer → "Customer"
            // o.Customer.Name → Walk(o.Customer) + ".Name" → "Customer.Name"
            case MemberAccessExpressionSyntax member:
                var left = WalkExpression(member.Expression, paramName);
                var right = member.Name.Identifier.Text;
                return string.IsNullOrEmpty(left) ? right : $"{left}.{right}";

            // o.History.OrderByDescending(...) → "History.OrderByDescending(...)"
            case InvocationExpressionSyntax inv:
                var invoked = WalkExpression(inv.Expression, paramName);
                var args = string.Join(", ", inv.ArgumentList.Arguments
                    .Select(a => a.ToString()));
                if (invoked.StartsWith("History.") || invoked.Contains("."))
                {
                    // Цепочка: History.OrderByDescending(h => ...).Select(...)
                    // Перестраиваем: берём исходный текст
                    return ReconstructChain(expr, paramName);
                }

                return string.IsNullOrEmpty(args)
                    ? $"{invoked}()"
                    : $"{invoked}({args})";

            // (OrderStatus?)h.Status → Walk(h.Status) + каст
            case CastExpressionSyntax cast:
                return WalkExpression(cast.Expression, paramName);

            // object Initializer или MemberInit
            default:
                return expr.ToString().Replace($"{paramName}.", "");
        }
    }

    /// <summary>
    /// Для сложных цепочек вызовов (LINQ) — берём исходный текст,
    /// заменяем параметр на "e".
    /// </summary>
    private static string ReconstructChain(ExpressionSyntax expr, string paramName)
    {
        return expr.ToString().Replace($"{paramName}.", "");
    }
}