using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace UO.Analyzers.CodeFixes.Logging;

internal static class LoggerMessageNaming
{
    public static string CreateUniqueMethodName(LoggingInvocation logging, SemanticModel model, CancellationToken cancellationToken)
    {
        var reservedNames = CollectConflictingMemberAndEventNames(logging, model, cancellationToken);
        var messageStem = CreateMethodNameStemFromLiteralText(logging.Template.LiteralText);
        var methodStem = "Log" + (messageStem.Length is > 0 and <= 80 ? messageStem : logging.Level);
        var methodName = ReserveUniqueIdentifier(methodStem, reservedNames);
        while (reservedNames.Contains("__" + methodName + "Callback") || reservedNames.Contains("__" + methodName + "Struct"))
            methodName = ReserveUniqueIdentifier(methodStem, reservedNames);
        return methodName;
    }

    private static HashSet<string> CollectConflictingMemberAndEventNames(
        LoggingInvocation logging, SemanticModel model, CancellationToken cancellationToken)
    {
        var attributeType = model.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.LoggerMessageAttribute")!;
        var reservedNames = new HashSet<string>(StringComparer.Ordinal);
        for (var type = logging.TypeSymbol; type != null; type = type.BaseType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reservedNames.UnionWith(type.GetMembers().Select(member => member.Name));
        }

        foreach (var member in logging.TypeSymbol.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var attribute in member.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                    continue;
                foreach (var argument in attribute.NamedArguments)
                {
                    if (argument.Key == "EventName" && argument.Value.Value is string eventName)
                        reservedNames.Add(eventName);
                }
            }
        }

        reservedNames.UnionWith(model.LookupSymbols(logging.Invocation.SpanStart).Select(symbol => symbol.Name));
        return reservedNames;
    }

    public static string ReserveUniqueIdentifier(string preferredName, ISet<string> reservedNames)
    {
        var candidate = preferredName;
        for (var suffix = 2; !reservedNames.Add(candidate); suffix++)
            candidate = preferredName + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return candidate;
    }

    public static string CreateParameterNameFromPlaceholder(string placeholder)
    {
        var characters = placeholder.ToCharArray();
        for (var index = 0; index < characters.Length && char.IsUpper(characters[index]); index++)
        {
            if (index > 0 && index + 1 < characters.Length && char.IsLower(characters[index + 1]))
                break;
            characters[index] = char.ToLowerInvariant(characters[index]);
        }

        return new string(characters);
    }

    private static string CreateMethodNameStemFromLiteralText(string literalText)
    {
        var methodStem = new StringBuilder();
        var startsWord = true;
        foreach (var character in literalText)
        {
            if (!char.IsLetterOrDigit(character))
            {
                startsWord = true;
                continue;
            }

            methodStem.Append(startsWord ? char.ToUpperInvariant(character) : character);
            startsWord = false;
        }

        return methodStem.ToString();
    }
}
