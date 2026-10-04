using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace UO.Analyzers.Rules.UO0001UnsupportedEfCoreTranslation;

internal sealed class QueryContextAnalysis(QuerySymbols symbols, QuerySourceAnalysis sources)
{
    public QueryContext? Find(IOperation operation, CancellationToken cancellationToken)
    {
        var rows = ImmutableArray.CreateBuilder<IParameterSymbol>();
        string? contextName = null;
        for (var ancestor = operation.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ancestor is not IAnonymousFunctionOperation lambda)
                continue;

            var argument = FindArgument(lambda);
            if (argument?.Parent is not IInvocationOperation query
                || argument.Parameter?.Ordinal != 1
                || !symbols.Predicates.Contains(query.TargetMethod.OriginalDefinition)
                || QuerySymbols.SourceArgument(query) is not { } source
                || !sources.IsEfSource(source, cancellationToken))
                break;

            contextName ??= "Queryable." + query.TargetMethod.Name;
            rows.AddRange(lambda.Symbol.Parameters);
        }
        return contextName is null ? null : new QueryContext(contextName, rows.ToImmutable());
    }

    private static IArgumentOperation? FindArgument(IAnonymousFunctionOperation lambda)
    {
        IOperation current = lambda;
        while (current.Parent is IConversionOperation or IDelegateCreationOperation or IParenthesizedOperation)
            current = current.Parent;
        return current.Parent as IArgumentOperation;
    }
}

internal sealed class QueryContext(string name, ImmutableArray<IParameterSymbol> rows)
{
    public string Name { get; } = name;
    public ImmutableArray<IParameterSymbol> Rows { get; } = rows;
}
