using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GreenNide.ExpressionFilter.Tests.SourceGenerator;

public static class GeneratorTestHelper
{
    public static GeneratorDriverRunResult RunGenerator(
        string userSource,
        params string[] additionalSources)
    {
        var syntaxTrees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(userSource)
        };

        foreach (var src in additionalSources)
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(src));

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        };
        
        var abstractionsDll = typeof(GreenNide.ExpressionFilter.GenerateFilterAttribute).Assembly.Location;
        references.Add(MetadataReference.CreateFromFile(abstractionsDll));

      
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var netstandard = Path.Combine(runtimeDir, "netstandard.dll");
        if (File.Exists(netstandard))
            references.Add(MetadataReference.CreateFromFile(netstandard));

        var sysRuntime = Path.Combine(runtimeDir, "System.Runtime.dll");
        if (File.Exists(sysRuntime))
            references.Add(MetadataReference.CreateFromFile(sysRuntime));

       
        foreach (var dll in Directory.GetFiles(runtimeDir, "System.*.dll")
                     .Where(d => !d.Contains("System.Numerics") && !d.Contains("System.Drawing")))
        {
            try { references.Add(MetadataReference.CreateFromFile(dll)); }
            catch {/*SKIP*/}
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        
        var generator = new GreenNide.FilterGenerator.Generator.FilterGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        return runResult;
    }
}
