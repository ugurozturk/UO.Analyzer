using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UO.Analyzers.CodeFixes.Logging;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseLoggerMessageCodeFixProvider)), Shared]
public sealed class UseLoggerMessageCodeFixProvider : CodeFixProvider
{
    public const string Title = "Convert to source-generated LoggerMessage";
    private const string EquivalenceKey = "UO.CA1848.SourceGeneratedLoggerMessage";

    public override ImmutableArray<string> FixableDiagnosticIds => ["CA1848"];

    // BatchFixer can race method-name allocation and containing-type edits. Single diagnostic only.
    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var cancellationToken = context.CancellationToken;
        var root = await context.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || model == null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var invocation = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation == null || LoggingInvocationAnalyzer.TryAnalyzeSupportedInvocation(invocation, model, cancellationToken) is not { } logging)
                continue;

            context.RegisterCodeFix(CodeAction.Create(Title,
                token => LoggerMessageDocumentRewriter.ReplaceExtensionCallWithSourceGeneratedLoggingAsync(
                    context.Document, logging, token), EquivalenceKey), diagnostic);
        }
    }
}
