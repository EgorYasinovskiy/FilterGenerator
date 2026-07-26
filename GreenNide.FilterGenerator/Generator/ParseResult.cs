using System.Collections.Immutable;

namespace GreenNide.FilterGenerator.Generator;

internal sealed class ParseResult
{
    public FilterClassDefinition? Definition { get; set; }
    public ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> Diagnostics { get; set; }
}