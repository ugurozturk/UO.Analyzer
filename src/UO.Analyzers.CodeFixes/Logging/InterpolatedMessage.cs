using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace UO.Analyzers.CodeFixes.Logging;

internal static class InterpolatedMessage
{
    public static MessageTemplate? TryCreate(InterpolatedStringExpressionSyntax expression, SemanticModel model,
        CancellationToken cancellationToken, out ImmutableArray<LoggingArgument> arguments)
    {
        arguments = default;
        var message = new StringBuilder();
        var mapped = ImmutableArray.CreateBuilder<LoggingArgument>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stringType = model.Compilation.GetSpecialType(SpecialType.System_String);
        // Older compilers/runtimes and await/dynamic holes can use string.Format,
        // which evaluates all holes before formatting. Keep the complete expression
        // in those cases so custom formatting side effects retain their order.
        if (expression.SyntaxTree.Options is not CSharpParseOptions { LanguageVersion: >= LanguageVersion.CSharp10 } ||
            model.Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.DefaultInterpolatedStringHandler") == null ||
            expression.DescendantNodes().Any(node => node is AwaitExpressionSyntax) ||
            // Logging generators can emit literal backslashes without escaping them.
            // Keep such text in the payload instead of embedding it in their template.
            expression.Contents.OfType<InterpolatedStringTextSyntax>().Any(text => text.TextToken.ValueText.IndexOf('\\') >= 0) ||
            expression.Contents.OfType<InterpolationSyntax>().Any(hole =>
                model.GetTypeInfo(hole.Expression, cancellationToken).Type?.TypeKind == TypeKind.Dynamic))
        {
            arguments = ImmutableArray.Create(new LoggingArgument("message", stringType, expression));
            return MessageTemplate.TryParseSupportedTemplate("{Message}");
        }
        var isRaw = expression.StringStartToken.IsKind(SyntaxKind.InterpolatedSingleLineRawStringStartToken) ||
            expression.StringStartToken.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken);
        foreach (var content in expression.Contents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (content is InterpolatedStringTextSyntax text)
            {
                var value = text.TextToken.ValueText;
                if (!isRaw)
                    value = value.Replace("{{", "{").Replace("}}", "}");
                message.Append(value.Replace("{", "{{").Replace("}", "}}"));
                continue;
            }

            if (content is not InterpolationSyntax interpolation)
                return null;
            var name = LoggerMessageNaming.ReserveUniqueIdentifier(GetName(interpolation.Expression), names);
            message.Append('{').Append(name).Append('}');

            // Format each hole before evaluating the next one, as the original string does.
            // Keeping interpolation also preserves current culture, null-as-empty, custom
            // formatting and enumerable ToString behavior instead of logging's formatting rules.
            var hole = interpolation.WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken).WithTriviaFrom(interpolation.OpenBraceToken))
                .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken).WithTriviaFrom(interpolation.CloseBraceToken));
            if (hole.FormatClause is { } format)
            {
                var value = format.FormatStringToken.ValueText;
                var escaped = Literal(value).Text;
                hole = hole.WithFormatClause(format.WithFormatStringToken(Token(default,
                    SyntaxKind.InterpolatedStringTextToken, escaped.Substring(1, escaped.Length - 2), value, default)));
            }
            var formatted = InterpolatedStringExpression(Token(SyntaxKind.InterpolatedStringStartToken))
                .WithContents(SingletonList<InterpolatedStringContentSyntax>(hole))
                .WithStringEndToken(Token(SyntaxKind.InterpolatedStringEndToken));
            mapped.Add(new LoggingArgument(LoggerMessageNaming.CreateParameterNameFromPlaceholder(name), stringType, formatted));
        }

        if (mapped.Count == 0)
            return null;
        var template = MessageTemplate.TryParseSupportedTemplate(message.ToString());
        if (template != null)
            arguments = mapped.ToImmutable();
        return template;
    }

    private static string GetName(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;
        var name = expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => GetName(member.Expression) + member.Name.Identifier.ValueText,
            _ => "Value",
        };
        if (name.Length == 0 || name[0] == '_' || !SyntaxFacts.IsValidIdentifier(name))
            return "Value";
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}
