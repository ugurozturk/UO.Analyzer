using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace UO.Analyzers.CodeFixes.Logging;

internal static class LoggerMessageDocumentRewriter
{
    public static async Task<Document> ReplaceExtensionCallWithSourceGeneratedLoggingAsync(
        Document document, LoggingInvocation logging, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var methodName = LoggerMessageNaming.CreateUniqueMethodName(logging, editor.SemanticModel, cancellationToken);
        var syntaxFactory = new LoggerMessageSyntaxFactory(editor.Generator, editor.SemanticModel, logging.Invocation.SpanStart);
        var method = syntaxFactory.CreatePartialLoggingMethod(logging, methodName, cancellationToken);
        var replacementCall = LoggerMessageSyntaxFactory.CreateLoggingMethodCallPreservingArgumentOrder(logging, methodName);

        ReplaceCallAndAddMethodToPartialClass(editor, logging, replacementCall, method);
        EnsureEnclosingClassesArePartial(editor, logging.ContainingType);

        return await AddImportsAndFormatGeneratedLoggingAsync(editor.GetChangedDocument(), cancellationToken).ConfigureAwait(false);
    }

    private static void ReplaceCallAndAddMethodToPartialClass(
        DocumentEditor editor, LoggingInvocation logging, InvocationExpressionSyntax replacementCall, MethodDeclarationSyntax method)
    {
        var updatedClass = AddPartialModifierIfMissing(logging.ContainingType.ReplaceNode(logging.Invocation, replacementCall))
            .AddMembers(method);
        editor.ReplaceNode(logging.ContainingType, updatedClass);
    }

    private static void EnsureEnclosingClassesArePartial(DocumentEditor editor, ClassDeclarationSyntax containingClass)
    {
        foreach (var ancestor in containingClass.Ancestors().OfType<ClassDeclarationSyntax>())
            editor.ReplaceNode(ancestor, (current, _) => AddPartialModifierIfMissing((ClassDeclarationSyntax)current));
    }

    private static ClassDeclarationSyntax AddPartialModifierIfMissing(ClassDeclarationSyntax declaration)
    {
        if (declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return declaration;

        // Move leading comments from the class keyword when it was the first token.
        if (declaration.Modifiers.Count == 0)
            return declaration.WithKeyword(declaration.Keyword.WithLeadingTrivia(default(SyntaxTriviaList)))
                .WithModifiers(TokenList(Token(SyntaxKind.PartialKeyword).WithLeadingTrivia(declaration.Keyword.LeadingTrivia).WithTrailingTrivia(Space)));
        return declaration.AddModifiers(Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(Space));
    }

    private static async Task<Document> AddImportsAndFormatGeneratedLoggingAsync(Document document, CancellationToken cancellationToken)
    {
        document = await ImportAdder.AddImportsAsync(document, Simplifier.AddImportsAnnotation, cancellationToken: cancellationToken).ConfigureAwait(false);
        document = await Simplifier.ReduceAsync(document, Simplifier.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await Formatter.FormatAsync(document, Formatter.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
