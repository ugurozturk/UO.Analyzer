using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using UO.Analyzers.CodeFixes.Logging;

namespace UO.Analyzers.Tests.Infrastructure;

// Framework-neutral DefaultVerifier reports failures correctly through xUnit without the obsolete XUnit adapter.
internal class AnalyzerTest<TAnalyzer> : CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public AnalyzerTest()
    {
        ReferenceAssemblies = new ReferenceAssemblies("net10.0");
        TestState.AdditionalReferences.AddRange(LoggingTestHost.MetadataReferences);
        SolutionTransforms.Add((solution, projectId) => solution.WithProjectParseOptions(projectId,
            new CSharpParseOptions(LanguageVersion.CSharp13)));
    }
}

internal class CodeFixTest<TAnalyzer, TCodeFix> : CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : Microsoft.CodeAnalysis.CodeFixes.CodeFixProvider, new()
{
    public CodeFixTest()
    {
        ReferenceAssemblies = new ReferenceAssemblies("net10.0");
        TestState.AdditionalReferences.AddRange(LoggingTestHost.MetadataReferences);
        SolutionTransforms.Add((solution, projectId) => solution
            .WithProjectParseOptions(projectId, new CSharpParseOptions(LanguageVersion.CSharp13))
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable)));
    }
}

internal sealed class MicrosoftLoggingCodeFixTest : CodeFixTest<EmptyAnalyzer, UseLoggerMessageCodeFixProvider>
{
    public MicrosoftLoggingCodeFixTest()
    {
        TestBehaviors |= TestBehaviors.SkipGeneratedSourcesCheck;
        CodeActionEquivalenceKey = "UO.CA1848.SourceGeneratedLoggerMessage";
        SolutionTransforms.Add((solution, id) => solution.WithProjectCompilationOptions(id,
            solution.GetProject(id)!.CompilationOptions!.WithSpecificDiagnosticOptions(
                new Dictionary<string, ReportDiagnostic> { ["CA1848"] = ReportDiagnostic.Warn })));
    }

    protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers() => [LoggingTestHost.GetMicrosoftCA1848Analyzer()];
    protected override IEnumerable<Type> GetSourceGenerators() => [LoggingTestHost.LoggingGeneratorType];
}

// Only a generic type placeholder; the integration verifier above substitutes Microsoft's actual analyzer.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EmptyAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [];
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
    }
}
