using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GreenNide.FilterGenerator.Generator;

[Generator]
public sealed partial class FilterGenerator : IIncrementalGenerator
{
    private const string GenerateFilterAttrFqn = "GreenNide.ExpressionFilter.GenerateFilterAttribute";


    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GenerateFilterAttrFqn,
                predicate: static (node, ct) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => ParseFilterClass(ctx, ct))
            .Where(static r => r is not null);

        context.RegisterSourceOutput(
            results.Collect(),
            static (spc, results) =>
            {
                foreach (var result in results)
                {
                    if (result is null) continue;

                    // Выводим диагностики
                    foreach (var diag in result.Diagnostics)
                        spc.ReportDiagnostic(diag);

                    // Генерируем код, только если определение успешно
                    if (result.Definition is not null)
                    {
                        var code = GenerateCode(result.Definition);
                        spc.AddSource($"{result.Definition.GeneratedClassName}.Filter.g.cs", code);
                    }
                }
            });
    }
}