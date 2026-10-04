using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace UO.Analyzers.Rules.UO0001UnsupportedEfCoreTranslation;

internal static class RowDependency
{
    public static bool DependsOn(IOperation operation, ImmutableArray<IParameterSymbol> rows,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.ConstantValue.HasValue || operation is IAnonymousFunctionOperation)
            return false;
        if (operation is IParameterReferenceOperation reference)
        {
            foreach (var row in rows)
            {
                if (SymbolEqualityComparer.Default.Equals(row, reference.Parameter))
                    return true;
            }
        }
        foreach (var child in operation.ChildOperations)
        {
            if (DependsOn(child, rows, cancellationToken))
                return true;
        }
        return false;
    }
}
