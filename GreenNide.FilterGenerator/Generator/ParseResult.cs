using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace GreenNide.FilterGenerator.Generator;

internal sealed class ParseResult
{
    public FilterClassDefinition? Definition { get; set; }
    public ImmutableArray<Diagnostic> Diagnostics { get; set; }
}