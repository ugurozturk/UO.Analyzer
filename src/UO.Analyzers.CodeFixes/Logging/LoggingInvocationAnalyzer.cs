using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace UO.Analyzers.CodeFixes.Logging;

internal static class LoggingInvocationAnalyzer
{
    public static LoggingInvocation? TryAnalyzeSupportedInvocation(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasSupportedInvocationSyntax(invocation) ||
            invocation.Expression is not MemberAccessExpressionSyntax member ||
            model.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
            return null;

        var method = operation.TargetMethod.ReducedFrom ?? operation.TargetMethod;
        var compilation = model.Compilation;
        var loggerType = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger");
        var extensionsType = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.LoggerExtensions");
        var attributeType = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.LoggerMessageAttribute");
        var exceptionType = compilation.GetTypeByMetadataName("System.Exception");
        if (loggerType == null || extensionsType == null || attributeType == null || exceptionType == null ||
            !SymbolEqualityComparer.Default.Equals(method.ContainingType, extensionsType) ||
            !method.IsExtensionMethod || method.Parameters.Length < 3 ||
            !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, loggerType) ||
            !attributeType.GetMembers("SkipEnabledCheck").Any())
            return null;

        var level = GetFixedLogLevel(method);
        if (level == null)
            return null;

        var hasException = method.Parameters.Length == 4 &&
            SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, exceptionType);
        var messageIndex = hasException ? 2 : 1;
        if (method.Parameters.Length != (hasException ? 4 : 3) ||
            method.Parameters[messageIndex].Type.SpecialType != SpecialType.System_String ||
            !method.Parameters[method.Parameters.Length - 1].IsParams)
            return null;

        // Restrict this version to reduced extension syntax. Static LoggerExtensions calls have a different argument layout.
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol { ReducedFrom: not null })
            return null;

        var containingType = FindClassSupportingPartialLoggingMethods(invocation);
        if (containingType == null || model.GetDeclaredSymbol(containingType, cancellationToken) is not INamedTypeSymbol typeSymbol)
            return null;

        if (model.GetDiagnostics(invocation.Span, cancellationToken).Any(d => d.Severity == DiagnosticSeverity.Error))
            return null;

        var arguments = invocation.ArgumentList.Arguments;
        var sourceMessageIndex = messageIndex - 1;
        if (arguments.Count <= sourceMessageIndex)
            return null;
        var messageExpression = arguments[sourceMessageIndex].Expression;
        var constant = model.GetConstantValue(messageExpression, cancellationToken);
        if (!constant.HasValue || constant.Value is not string message)
            return null;
        var template = MessageTemplate.TryParseSupportedTemplate(message);
        if (template == null || template.Placeholders.Length != arguments.Count - sourceMessageIndex - 1)
            return null;

        // Explicit params arrays change expansion/null semantics. Only individual object conversions are supported.
        var paramsOperation = operation.Arguments.LastOrDefault();
        if (paramsOperation == null || paramsOperation.ArgumentKind != ArgumentKind.ParamArray)
            return null;

        var mappedArguments = TryMapArgumentsToLoggingParameters(arguments, template, hasException, loggerType, exceptionType, model, cancellationToken);
        if (mappedArguments.IsDefault)
            return null;

        return new LoggingInvocation(invocation, containingType, typeSymbol, member.Expression, messageExpression,
            level, template, mappedArguments);
    }

    private static bool HasSupportedInvocationSyntax(InvocationExpressionSyntax invocation) =>
        invocation.SyntaxTree.Options is CSharpParseOptions { LanguageVersion: >= LanguageVersion.CSharp9 } &&
        invocation.Parent is ExpressionStatementSyntax &&
        !invocation.ContainsDirectives &&
        !invocation.ArgumentList.Arguments.Any(argument => argument.NameColon != null || !argument.RefKindKeyword.IsKind(SyntaxKind.None));

    private static string? GetFixedLogLevel(IMethodSymbol method) => method.Name switch
    {
        "LogTrace" => "Trace",
        "LogDebug" => "Debug",
        "LogInformation" => "Information",
        "LogWarning" => "Warning",
        "LogError" => "Error",
        "LogCritical" => "Critical",
        _ => null,
    };

    private static ClassDeclarationSyntax? FindClassSupportingPartialLoggingMethods(InvocationExpressionSyntax invocation)
    {
        var nearestType = invocation.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (nearestType is not ClassDeclarationSyntax containingClass)
            return null;

        foreach (var declaration in containingClass.AncestorsAndSelf().OfType<TypeDeclarationSyntax>())
        {
            if (declaration is not ClassDeclarationSyntax || declaration.Modifiers.Any(SyntaxKind.FileKeyword) || declaration.ContainsDirectives)
                return null;
        }

        return containingClass;
    }

    private static ImmutableArray<LoggingArgument> TryMapArgumentsToLoggingParameters(
        SeparatedSyntaxList<ArgumentSyntax> arguments, MessageTemplate template, bool hasException,
        INamedTypeSymbol loggerType, INamedTypeSymbol exceptionType, SemanticModel model, CancellationToken cancellationToken)
    {
        var sourceMessageIndex = hasException ? 1 : 0;
        var mappedArguments = ImmutableArray.CreateBuilder<LoggingArgument>();
        var usedNames = new HashSet<string>(template.Placeholders.Select(LoggerMessageNaming.CreateParameterNameFromPlaceholder), StringComparer.OrdinalIgnoreCase);
        if (hasException)
        {
            var exceptionName = LoggerMessageNaming.ReserveUniqueIdentifier("exception", usedNames);
            mappedArguments.Add(new LoggingArgument(exceptionName,
                exceptionType.WithNullableAnnotation(NullableAnnotation.Annotated), arguments[0].Expression));
        }

        for (var i = 0; i < template.Placeholders.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expression = arguments[sourceMessageIndex + 1 + i].Expression;
            var type = model.GetTypeInfo(expression, cancellationToken).Type;
            if (type == null || !CanDeclareLoggingParameterType(type) || IsLoggerTypeOrImplementation(type, loggerType) || IsExceptionTypeOrSubclass(type, exceptionType) ||
                type.ToDisplayString() == "Microsoft.Extensions.Logging.LogLevel")
                return default;

            // Preserve the original conversion to object. User-defined conversions to a primitive are not introduced.
            mappedArguments.Add(new LoggingArgument(LoggerMessageNaming.CreateParameterNameFromPlaceholder(template.Placeholders[i]), type, expression));
        }

        return mappedArguments.ToImmutable();
    }

    private static bool CanDeclareLoggingParameterType(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => CanDeclareLoggingParameterType(array.ElementType),
        ITypeParameterSymbol parameter => parameter.TypeParameterKind == TypeParameterKind.Type && !parameter.AllowsRefLikeType,
        INamedTypeSymbol named => !named.IsAnonymousType && !named.IsRefLikeType && !named.IsUnboundGenericType &&
            named.TypeKind != TypeKind.Error && named.TypeArguments.All(CanDeclareLoggingParameterType) &&
            (named.ContainingType == null || CanDeclareLoggingParameterType(named.ContainingType)),
        _ => false,
    };

    private static bool IsLoggerTypeOrImplementation(ITypeSymbol type, INamedTypeSymbol target) =>
        SymbolEqualityComparer.Default.Equals(type, target) ||
        type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, target));

    private static bool IsExceptionTypeOrSubclass(ITypeSymbol type, INamedTypeSymbol target)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, target))
                return true;
        }
        return false;
    }
}
