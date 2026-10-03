using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UO.Analyzers.CodeFixes.Logging;

internal sealed class LoggingInvocation
{
    internal LoggingInvocation(InvocationExpressionSyntax invocation, ClassDeclarationSyntax containingType,
        INamedTypeSymbol typeSymbol, ExpressionSyntax logger, ExpressionSyntax messageExpression,
        string level, MessageTemplate template, ImmutableArray<LoggingArgument> arguments)
    {
        Invocation = invocation;
        ContainingType = containingType;
        TypeSymbol = typeSymbol;
        Logger = logger;
        MessageExpression = messageExpression;
        Level = level;
        Template = template;
        Arguments = arguments;
    }

    public InvocationExpressionSyntax Invocation { get; }
    public ClassDeclarationSyntax ContainingType { get; }
    public INamedTypeSymbol TypeSymbol { get; }
    public ExpressionSyntax Logger { get; }
    public ExpressionSyntax MessageExpression { get; }
    public string Level { get; }
    public MessageTemplate Template { get; }
    public ImmutableArray<LoggingArgument> Arguments { get; }

}

internal sealed class LoggingArgument(string name, ITypeSymbol type, ExpressionSyntax expression)
{
    public string Name { get; } = name;
    public ITypeSymbol Type { get; } = type;
    public ExpressionSyntax Expression { get; } = expression;
}
