using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace UO.Analyzers.Rules.UO0001UnsupportedEfCoreTranslation;

internal static class LocalQuerySources
{
    public static ImmutableDictionary<ILocalSymbol, IOperation> Collect(
        ImmutableArray<IOperation> blocks, CancellationToken cancellationToken)
    {
        var collector = new Collector(cancellationToken);
        foreach (var block in blocks)
            collector.Visit(block);
        foreach (var written in collector.Written)
            collector.Initializers.Remove(written);
        return collector.Initializers.ToImmutable();
    }

    // Scan each operation block once, including closures that may reassign a captured query.
    // This deliberately rejects even assignments after the query: no speculative control-flow proof.
    private sealed class Collector(CancellationToken cancellationToken) : OperationWalker
    {
        public ImmutableDictionary<ILocalSymbol, IOperation>.Builder Initializers { get; } =
            ImmutableDictionary.CreateBuilder<ILocalSymbol, IOperation>(SymbolEqualityComparer.Default);
        public ImmutableHashSet<ILocalSymbol>.Builder Written { get; } =
            ImmutableHashSet.CreateBuilder<ILocalSymbol>(SymbolEqualityComparer.Default);

        public override void Visit(IOperation? operation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            base.Visit(operation);
        }

        public override void VisitVariableDeclarator(IVariableDeclaratorOperation operation)
        {
            if (operation.Initializer is not null)
            {
                Initializers[operation.Symbol] = operation.Initializer.Value;
                if (operation.Symbol.RefKind != RefKind.None)
                    MarkWritten(operation.Initializer.Value);
            }
            base.VisitVariableDeclarator(operation);
        }

        public override void VisitSimpleAssignment(ISimpleAssignmentOperation operation)
        {
            MarkWritten(operation.Target);
            if (operation.IsRef)
                MarkWritten(operation.Value);
            base.VisitSimpleAssignment(operation);
        }

        public override void VisitDeconstructionAssignment(IDeconstructionAssignmentOperation operation)
        {
            MarkWritten(operation.Target);
            base.VisitDeconstructionAssignment(operation);
        }

        public override void VisitArgument(IArgumentOperation operation)
        {
            if (operation.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
                MarkWritten(operation.Value);
            base.VisitArgument(operation);
        }

        private void MarkWritten(IOperation operation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation is ILocalReferenceOperation local)
                Written.Add(local.Local);
            foreach (var child in operation.ChildOperations)
                MarkWritten(child);
        }
    }
}
