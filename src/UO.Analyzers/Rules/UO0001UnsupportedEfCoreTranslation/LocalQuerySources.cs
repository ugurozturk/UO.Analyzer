using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace UO.Analyzers.Rules.UO0001UnsupportedEfCoreTranslation;

internal static class LocalQuerySources
{
    public static ImmutableDictionary<ILocalSymbol, IOperation> Collect(
        ImmutableArray<IOperation> blocks, QuerySymbols symbols, CancellationToken cancellationToken)
    {
        var collector = new Collector(symbols, cancellationToken);
        foreach (var block in blocks)
            collector.Visit(block);
        foreach (var written in collector.Written)
            collector.Initializers.Remove(written);
        return collector.Initializers.ToImmutable();
    }

    // Scan each operation block once, including closures that may reassign a captured query.
    // Self-composition preserves the initializer's provider across branches and loops. Any other
    // write still invalidates the local for the entire block, including earlier query uses.
    private sealed class Collector(QuerySymbols symbols, CancellationToken cancellationToken) : OperationWalker
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
            if (operation.IsRef || operation.Target is not ILocalReferenceOperation local
                || !PreservesSource(operation.Value, local.Local))
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

        private bool PreservesSource(IOperation value, ILocalSymbol local)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (value)
            {
                case ILocalReferenceOperation reference:
                    return SymbolEqualityComparer.Default.Equals(reference.Local, local);
                case IConversionOperation conversion when conversion.OperatorMethod is null:
                    return PreservesSource(conversion.Operand, local);
                case IParenthesizedOperation parentheses:
                    return PreservesSource(parentheses.Operand, local);
                case IInvocationOperation invocation when symbols.IsQueryable(invocation.Type):
                    var declaringType = invocation.TargetMethod.ContainingType;
                    return (SymbolEqualityComparer.Default.Equals(declaringType, symbols.Queryable)
                            || SymbolEqualityComparer.Default.Equals(declaringType, symbols.EfExtensions))
                        && QuerySymbols.SourceArgument(invocation) is { } source
                        && PreservesSource(source, local);
                default:
                    return false;
            }
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
