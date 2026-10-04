using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace UO.Analyzers.Rules.UO0001UnsupportedEfCoreTranslation;

internal sealed class QuerySymbols
{
    public QuerySymbols(Compilation compilation)
    {
        Queryable = compilation.GetTypeByMetadataName("System.Linq.Queryable");
        Enumerable = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        IQueryable = compilation.GetTypeByMetadataName("System.Linq.IQueryable`1");
        DbSet = GetEfType(compilation, "Microsoft.EntityFrameworkCore.DbSet`1");
        DbSetAsQueryable = DbSet?.GetMembers("AsQueryable").OfType<IMethodSymbol>().SingleOrDefault(method =>
            !method.IsStatic && method.Arity == 0 && method.Parameters.IsEmpty
            && method.ReturnType is INamedTypeSymbol result
            && SymbolEqualityComparer.Default.Equals(result.OriginalDefinition, IQueryable));
        EfExtensions = GetEfType(compilation, "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions");
        var expression = compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1");
        var func = compilation.GetTypeByMetadataName("System.Func`2");
        var predicates = ImmutableHashSet.CreateBuilder<IMethodSymbol>(SymbolEqualityComparer.Default);
        string[] names = ["Where", "Any", "All", "Count", "LongCount", "First", "FirstOrDefault",
            "Single", "SingleOrDefault", "Last", "LastOrDefault"];
        foreach (var name in names)
        {
            foreach (var method in Queryable?.GetMembers(name).OfType<IMethodSymbol>() ?? [])
            {
                if (method.Arity == 1 && method.Parameters.Length == 2
                    && method.Parameters[0].Type is INamedTypeSymbol source
                    && SymbolEqualityComparer.Default.Equals(source.OriginalDefinition, IQueryable)
                    && method.Parameters[1].Type is INamedTypeSymbol predicate
                    && SymbolEqualityComparer.Default.Equals(predicate.OriginalDefinition, expression)
                    && predicate.TypeArguments[0] is INamedTypeSymbol signature
                    && SymbolEqualityComparer.Default.Equals(signature.OriginalDefinition, func)
                    && SymbolEqualityComparer.Default.Equals(signature.TypeArguments[0], method.TypeParameters[0])
                    && signature.TypeArguments[1].SpecialType == SpecialType.System_Boolean)
                    predicates.Add(method);
            }
        }
        Predicates = predicates.ToImmutable();
    }

    public INamedTypeSymbol? Queryable { get; }
    public INamedTypeSymbol? Enumerable { get; }
    public INamedTypeSymbol? IQueryable { get; }
    public INamedTypeSymbol? DbSet { get; }
    public IMethodSymbol? DbSetAsQueryable { get; }
    public INamedTypeSymbol? EfExtensions { get; }
    public ImmutableHashSet<IMethodSymbol> Predicates { get; }

    public static IOperation? SourceArgument(IInvocationOperation invocation)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == 0)
                return argument.Value;
        }
        return null;
    }

    public bool IsQueryable(ITypeSymbol? type) => type is INamedTypeSymbol named
        && (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, IQueryable)
            || named.AllInterfaces.Any(item => SymbolEqualityComparer.Default.Equals(item.OriginalDefinition, IQueryable)));

    public bool IsDbSet(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, DbSet))
                return true;
        }
        return false;
    }

    private static INamedTypeSymbol? GetEfType(Compilation compilation, string metadataName)
    {
        var type = compilation.GetTypeByMetadataName(metadataName);
        // A source-defined lookalike is not evidence of an EF query provider.
        return type?.ContainingAssembly.Identity.Name == "Microsoft.EntityFrameworkCore"
            && !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly) ? type : null;
    }
}
