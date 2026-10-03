using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using UO.Analyzers.CodeFixes.Logging;
using Xunit;

namespace UO.Analyzers.Tests.Infrastructure;

internal sealed class LoggingTestHost : IDisposable
{
    private static readonly Lazy<DiagnosticAnalyzer> MicrosoftAnalyzer = new(LoadMicrosoftCA1848Analyzer);
    private static readonly Lazy<Type> GeneratorType = new(() => LoadTestAssetAssembly("Microsoft.Extensions.Logging.Generators.dll")
        .GetTypes().Single(t => typeof(IIncrementalGenerator).IsAssignableFrom(t) && !t.IsAbstract));
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(ILogger).Assembly.Location).Distinct()
            .Select(p => MetadataReference.CreateFromFile(p)).ToImmutableArray<MetadataReference>());

    private readonly AdhocWorkspace workspace = new();

    public LoggingTestHost(string source, LanguageVersion languageVersion = LanguageVersion.CSharp13)
    {
        var project = workspace.AddProject("Consumer", LanguageNames.CSharp)
            .WithMetadataReferences(References.Value)
            .WithParseOptions(new CSharpParseOptions(languageVersion))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["CA1848"] = ReportDiagnostic.Warn }));
        Document = project.AddDocument("Consumer.cs", SourceText.From(source));
    }

    public Document Document { get; private set; }
    public static Type LoggingGeneratorType => GeneratorType.Value;
    public static ImmutableArray<MetadataReference> MetadataReferences => References.Value;

    public async Task<ImmutableArray<Diagnostic>> GetMicrosoftCA1848DiagnosticsAsync()
    {
        var compilation = (await Document.Project.GetCompilationAsync())!;
        var diagnostics = await compilation.WithAnalyzers([MicrosoftAnalyzer.Value]).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        return diagnostics.Where(d => d.Id == "CA1848").ToImmutableArray();
    }

    public async Task<List<CodeAction>> GetRegisteredCodeFixesAsync(Diagnostic? diagnostic = null, CancellationToken cancellationToken = default)
    {
        diagnostic ??= Assert.Single(await GetMicrosoftCA1848DiagnosticsAsync());
        var actions = new List<CodeAction>();
        await new UseLoggerMessageCodeFixProvider().RegisterCodeFixesAsync(
            new CodeFixContext(Document, diagnostic, (action, _) => actions.Add(action), cancellationToken));
        return actions;
    }

    public async Task<string> ApplyLoggingFixAndVerifyCompilationAsync(bool expectRemainingDiagnostics = false)
    {
        var diagnostics = await GetMicrosoftCA1848DiagnosticsAsync();
        Assert.NotEmpty(diagnostics);
        var action = Assert.Single(await GetRegisteredCodeFixesAsync(diagnostics[0]));
        Assert.Equal(UseLoggerMessageCodeFixProvider.Title, action.Title);
        var changes = Assert.Single((await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        Document = changes.ChangedSolution.GetDocument(Document.Id)!;
        await GenerateLoggingAndVerifyCompilationAsync();
        if (!expectRemainingDiagnostics)
            Assert.Empty(await GetMicrosoftCA1848DiagnosticsAsync());
        return (await Document.GetTextAsync()).ToString();
    }

    public async Task<Compilation> GenerateLoggingAndVerifyCompilationAsync()
    {
        var compilation = (await Document.Project.GetCompilationAsync())!;
        var generator = ((IIncrementalGenerator)Activator.CreateInstance(GeneratorType.Value)!).AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create([generator], parseOptions: (CSharpParseOptions)Document.Project.ParseOptions!);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        Assert.Empty(generated.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        return generated;
    }

    public async Task<Diagnostic> CreateSyntheticCA1848DiagnosticAsync(string methodName = "LogInformation", bool wholeInvocation = false)
    {
        var root = (await Document.GetSyntaxRootAsync())!;
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .First(i => i.Expression.ToString().EndsWith(methodName, StringComparison.Ordinal));
        var location = wholeInvocation ? invocation.GetLocation() : invocation.Expression.GetLastToken().GetLocation();
        return Diagnostic.Create(new DiagnosticDescriptor("CA1848", "Test", "Test", "Test", DiagnosticSeverity.Warning, true), location);
    }

    public static DiagnosticAnalyzer GetMicrosoftCA1848Analyzer() => MicrosoftAnalyzer.Value;

    public void AddSourceDocument(string name, string source)
    {
        Document = Document.Project.AddDocument(name, SourceText.From(source)).Project.GetDocument(Document.Id)!;
    }

    private static DiagnosticAnalyzer LoadMicrosoftCA1848Analyzer()
    {
        LoadTestAssetAssembly("Microsoft.CodeAnalysis.NetAnalyzers.dll");
        var assembly = LoadTestAssetAssembly("Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll");
        // CA1848's language-neutral analyzer lives in the shared assembly in current .NET analyzers.
        return assembly.GetTypes().Concat(LoadTestAssetAssembly("Microsoft.CodeAnalysis.NetAnalyzers.dll").GetTypes())
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .First(a => a.SupportedDiagnostics.Any(d => d.Id == "CA1848"));
    }

    private static Assembly LoadTestAssetAssembly(string name) => Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "TestAssets", name));
    public void Dispose() => workspace.Dispose();
}
