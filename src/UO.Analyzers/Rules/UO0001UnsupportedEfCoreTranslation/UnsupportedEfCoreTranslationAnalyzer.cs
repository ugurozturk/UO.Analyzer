using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using UO.Analyzers.Diagnostics;

namespace UO.Analyzers.Rules.UO0001UnsupportedEfCoreTranslation;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsupportedEfCoreTranslationAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UnsupportedEfCoreTranslation,
        "Use a method supported by the EF Core provider",
        "'{0}' has no SQL translation in the configured '{1}' EF Core profile(s) for the '{2}' predicate; review provider-supported operations and collation semantics",
        DiagnosticCategories.Reliability,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Row-dependent calls in SQL predicates must use overloads supported by the selected provider. "
            + "Choose an operation or database collation with the required comparison semantics. No automatic fix is safe.");

    private static readonly SymbolDisplayFormat MethodDisplay = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(compilationContext =>
        {
            var symbols = new QuerySymbols(compilationContext.Compilation);
            var rules = TranslationRules.Create(compilationContext.Compilation);
            compilationContext.RegisterOperationBlockStartAction(blockContext =>
            {
                if (blockContext.OperationBlocks.IsEmpty)
                    return;
                var options = blockContext.Options.AnalyzerConfigOptionsProvider.GetOptions(
                    blockContext.OperationBlocks[0].Syntax.SyntaxTree);
                var configuration = ProviderConfiguration.Read(options);
                if (configuration.Profiles == ProviderProfiles.None)
                    return;

                var locals = LocalQuerySources.Collect(blockContext.OperationBlocks, symbols, blockContext.CancellationToken);
                var sources = new QuerySourceAnalysis(symbols, locals, configuration.AssumeEfQueryable);
                var queryContexts = new QueryContextAnalysis(symbols, sources);
                blockContext.RegisterOperationAction(operationContext =>
                    AnalyzeInvocation(operationContext, rules, configuration.Profiles, queryContexts), OperationKind.Invocation);
            });
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context,
        ImmutableArray<UnsupportedMethodRule> rules, ProviderProfiles selectedProfiles, QueryContextAnalysis queryContexts)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.Syntax.ContainsDiagnostics)
            return;
        var unsupportedProfiles = ProviderProfiles.None;
        foreach (var rule in rules)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            unsupportedProfiles |= rule.Evaluate(invocation.TargetMethod, selectedProfiles);
        }
        if (unsupportedProfiles == ProviderProfiles.None)
            return;
        var queryContext = queryContexts.Find(invocation, context.CancellationToken);
        if (queryContext is null || !RowDependency.DependsOn(invocation, queryContext.Rows, context.CancellationToken))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(),
            invocation.TargetMethod.ToDisplayString(MethodDisplay), ProviderConfiguration.Display(unsupportedProfiles), queryContext.Name));
    }
}
