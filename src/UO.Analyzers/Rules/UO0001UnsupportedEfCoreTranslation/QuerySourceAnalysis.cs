using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace UO.Analyzers.Rules.UO0001UnsupportedEfCoreTranslation;

internal sealed class QuerySourceAnalysis(QuerySymbols symbols,
    ImmutableDictionary<ILocalSymbol, IOperation> localSources, bool assumeEfQueryable)
{
    public bool IsEfSource(IOperation source, CancellationToken cancellationToken) =>
        IsEfSource(source, ImmutableHashSet.Create<ILocalSymbol>(SymbolEqualityComparer.Default), cancellationToken);

    private bool IsEfSource(IOperation source, ImmutableHashSet<ILocalSymbol> visited,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source is IConversionOperation conversion)
            return conversion.OperatorMethod is null && IsEfSource(conversion.Operand, visited, cancellationToken);
        if (source is IParenthesizedOperation parentheses)
            return IsEfSource(parentheses.Operand, visited, cancellationToken);
        if (symbols.IsDbSet(source.Type))
            return true;
        if (source is ILocalReferenceOperation local)
        {
            // Missing/invalidated locals remain unknown even in opt-in files; they may hold a materialized query.
            return !visited.Contains(local.Local) && localSources.TryGetValue(local.Local, out var initializer)
                && IsEfSource(initializer, visited.Add(local.Local), cancellationToken);
        }
        if (source is IInvocationOperation invocation)
        {
            if (SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.OriginalDefinition, symbols.DbSetAsQueryable))
                return invocation.Instance is not null && IsEfSource(invocation.Instance, visited, cancellationToken);
            var declaringType = invocation.TargetMethod.ContainingType;
            if (SymbolEqualityComparer.Default.Equals(declaringType, symbols.Enumerable))
                return false;
            if (SymbolEqualityComparer.Default.Equals(declaringType, symbols.Queryable)
                || SymbolEqualityComparer.Default.Equals(declaringType, symbols.EfExtensions))
                return symbols.IsQueryable(invocation.Type) && QuerySymbols.SourceArgument(invocation) is { } argument
                    && IsEfSource(argument, visited, cancellationToken);
        }
        // Only explicit abstraction boundaries are eligible for opt-in. Do not guess at conditional,
        // newly constructed or otherwise opaque query objects, even if they implement IQueryable.
        return assumeEfQueryable && symbols.IsQueryable(source.Type)
            && source is IParameterReferenceOperation or IPropertyReferenceOperation
                or IFieldReferenceOperation or IInvocationOperation or IAwaitOperation;
    }
}
