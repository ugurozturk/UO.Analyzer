using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace UO.Analyzers.CodeFixes.Logging;

internal sealed class LoggerMessageSyntaxFactory
{
    private readonly SyntaxGenerator generator;
    private readonly SemanticModel semanticModel;
    private readonly bool nullableAnnotationsEnabled;

    public LoggerMessageSyntaxFactory(SyntaxGenerator generator, SemanticModel semanticModel, int invocationPosition)
    {
        this.generator = generator;
        this.semanticModel = semanticModel;
        nullableAnnotationsEnabled = semanticModel.GetNullableContext(invocationPosition).AnnotationsEnabled();
    }

    public MethodDeclarationSyntax CreatePartialLoggingMethod(LoggingInvocation logging, string methodName, CancellationToken cancellationToken) =>
        MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), Identifier(methodName))
            .WithAttributeLists(SingletonList(AttributeList(SingletonSeparatedList(CreateLoggerMessageAttribute(logging, cancellationToken)))))
            .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.PartialKeyword)))
            .WithParameterList(CreateLoggingMethodParameters(logging))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
            .WithAdditionalAnnotations(Formatter.Annotation);

    private ParameterListSyntax CreateLoggingMethodParameters(LoggingInvocation logging)
    {
        var parameterNames = new HashSet<string>(logging.Arguments.Select(argument => argument.Name), StringComparer.OrdinalIgnoreCase);
        var loggerName = LoggerMessageNaming.ReserveUniqueIdentifier("logger", parameterNames);
        var loggerType = semanticModel.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger")!;
        var parameters = new List<ParameterSyntax>
        {
            Parameter(Identifier(loggerName)).WithType(CreateSimplifiableTypeSyntax(loggerType)),
        };
        parameters.AddRange(logging.Arguments.Select(argument =>
            Parameter(CreateKeywordSafeIdentifier(argument.Name)).WithType(CreateSimplifiableTypeSyntax(argument.Type))));
        return ParameterList(SeparatedList(parameters));
    }

    private AttributeSyntax CreateLoggerMessageAttribute(LoggingInvocation logging, CancellationToken cancellationToken)
    {
        var attributeType = semanticModel.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.LoggerMessageAttribute")!;
        var logLevelType = semanticModel.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.LogLevel")!;
        return Attribute((NameSyntax)CreateSimplifiableTypeSyntax(attributeType))
            .WithArgumentList(AttributeArgumentList(SeparatedList(new[]
            {
                CreateNamedAttributeArgument("Level", MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    CreateSimplifiableTypeSyntax(logLevelType), IdentifierName(logging.Level))),
                CreateNamedAttributeArgument("Message", CreateConstantMessageLiteral(logging.MessageExpression, cancellationToken)),
                // LoggerExtensions always invokes ILogger.Log, even when disabled. Preserve this behavior.
                CreateNamedAttributeArgument("SkipEnabledCheck", LiteralExpression(SyntaxKind.TrueLiteralExpression)),
            })));
    }

    private LiteralExpressionSyntax CreateConstantMessageLiteral(ExpressionSyntax messageExpression, CancellationToken cancellationToken)
    {
        var messageConstant = (string)semanticModel.GetConstantValue(messageExpression, cancellationToken).Value!;
        return messageExpression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.WithoutTrivia()
            : LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(messageConstant));
    }

    private TypeSyntax CreateSimplifiableTypeSyntax(ITypeSymbol symbol) =>
        (TypeSyntax)generator.TypeExpression(nullableAnnotationsEnabled ? symbol : symbol.WithNullableAnnotation(NullableAnnotation.None))
            .WithAdditionalAnnotations(Simplifier.Annotation, Simplifier.AddImportsAnnotation);

    public static InvocationExpressionSyntax CreateLoggingMethodCallPreservingArgumentOrder(LoggingInvocation logging, string methodName)
    {
        // Keep logger, exception, and payloads in their original evaluation order, each exactly once.
        var arguments = new List<ArgumentSyntax> { Argument(logging.Logger.WithoutTrivia()) };
        arguments.AddRange(logging.Arguments.Select(argument => (ArgumentSyntax)argument.Expression.Parent!));
        return InvocationExpression(IdentifierName(methodName), ArgumentList(SeparatedList(arguments)))
            .WithTriviaFrom(logging.Invocation)
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static AttributeArgumentSyntax CreateNamedAttributeArgument(string name, ExpressionSyntax expression) =>
        AttributeArgument(expression).WithNameEquals(NameEquals(IdentifierName(name)));

    private static SyntaxToken CreateKeywordSafeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
            ? Identifier(default, SyntaxKind.IdentifierToken, "@" + name, name, default)
            : Identifier(name);
}
