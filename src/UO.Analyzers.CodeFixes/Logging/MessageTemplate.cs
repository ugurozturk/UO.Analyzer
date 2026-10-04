using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace UO.Analyzers.CodeFixes.Logging;

internal sealed class MessageTemplate
{
    private MessageTemplate(ImmutableArray<string> placeholders, string literalText, string message)
    {
        Placeholders = placeholders;
        LiteralText = literalText;
        Message = message;
    }

    public ImmutableArray<string> Placeholders { get; }
    public string LiteralText { get; }
    public string Message { get; }

    public static MessageTemplate? TryParseSupportedTemplate(string message)
    {
        var placeholders = ImmutableArray.CreateBuilder<string>();
        var placeholderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var literalText = new StringBuilder();
        for (var index = 0; index < message.Length; index++)
        {
            var character = message[index];
            if (character is not ('{' or '}'))
            {
                literalText.Append(character);
                continue;
            }

            if (index + 1 < message.Length && message[index + 1] == character)
            {
                literalText.Append(' ');
                index++;
                continue;
            }

            if (character == '}')
                return null;

            var closingBraceIndex = message.IndexOf('}', index + 1);
            if (closingBraceIndex < 0)
                return null;

            var placeholderContent = message.Substring(index + 1, closingBraceIndex - index - 1);
            var placeholderName = TryExtractSupportedPlaceholderName(placeholderContent);
            // Each occurrence needs a distinct parameter; merging duplicate placeholders changes logging semantics.
            if (placeholderName == null || !placeholderNames.Add(placeholderName))
                return null;

            placeholders.Add(placeholderName);
            literalText.Append(' ');
            index = closingBraceIndex;
        }

        return new MessageTemplate(placeholders.ToImmutable(), literalText.ToString(), message);
    }

    private static string? TryExtractSupportedPlaceholderName(string placeholderContent)
    {
        if (placeholderContent.IndexOf('{') >= 0)
            return null;

        var separatorIndex = placeholderContent.IndexOfAny(new[] { ',', ':' });
        var name = separatorIndex < 0 ? placeholderContent : placeholderContent.Substring(0, separatorIndex);
        if (!CanMapPlaceholderToParameter(name))
            return null;

        if (separatorIndex >= 0 && placeholderContent[separatorIndex] == ',' &&
            !HasValidAlignmentWidth(placeholderContent, separatorIndex))
            return null;

        return name;
    }

    private static bool HasValidAlignmentWidth(string placeholderContent, int commaIndex)
    {
        var colonIndex = placeholderContent.IndexOf(':', commaIndex);
        var alignmentEnd = colonIndex < 0 ? placeholderContent.Length : colonIndex;
        var alignment = placeholderContent.Substring(commaIndex + 1, alignmentEnd - commaIndex - 1);
        return int.TryParse(alignment, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static bool CanMapPlaceholderToParameter(string name) =>
        !name.StartsWith("_", StringComparison.Ordinal) &&
        (SyntaxFacts.IsValidIdentifier(name) || SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None);
}
