using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace UO.Analyzers.Rules.UO0001UnsupportedEfCoreTranslation;

// Only verified negative entries belong here. Absence means unknown, not supported.
internal static class TranslationRules
{
    public static ImmutableArray<UnsupportedMethodRule> Create(Compilation compilation)
    {
        var text = compilation.GetSpecialType(SpecialType.System_String);
        var boolean = compilation.GetSpecialType(SpecialType.System_Boolean);
        var culture = compilation.GetTypeByMetadataName("System.Globalization.CultureInfo");
        var comparison = compilation.GetTypeByMetadataName("System.StringComparison");
        var rules = ImmutableArray.CreateBuilder<UnsupportedMethodRule>();
        const ProviderProfiles profiles = ProviderProfiles.Npgsql800 | ProviderProfiles.Sqlite800
            | ProviderProfiles.Oracle102326000;

        // Case conversion and comparison are independent families sharing exact signature matching.
        Add("ToLowerInvariant", false, text);
        Add("ToUpperInvariant", false, text);
        if (culture is not null)
        {
            Add("ToLower", false, text, culture);
            Add("ToUpper", false, text, culture);
        }
        if (comparison is not null)
        {
            Add("Contains", false, boolean, text, comparison);
            Add("StartsWith", false, boolean, text, comparison);
            Add("EndsWith", false, boolean, text, comparison);
            Add("Equals", false, boolean, text, comparison);
            Add("Equals", true, boolean, text, text, comparison);
        }
        return rules.ToImmutable();

        void Add(string name, bool isStatic, ITypeSymbol returnType, params ITypeSymbol[] parameters)
        {
            foreach (var method in text.GetMembers(name).OfType<IMethodSymbol>())
            {
                if (MethodSignature.Matches(method, isStatic, returnType, parameters))
                    rules.Add(new UnsupportedMethodRule(method, profiles));
            }
        }
    }
}

internal sealed class UnsupportedMethodRule(IMethodSymbol method, ProviderProfiles unsupportedProfiles)
{
    public ProviderProfiles Evaluate(IMethodSymbol candidate, ProviderProfiles selectedProfiles) =>
        SymbolEqualityComparer.Default.Equals(method, candidate.OriginalDefinition)
            ? unsupportedProfiles & selectedProfiles
            : ProviderProfiles.None;
}

internal static class MethodSignature
{
    public static bool Matches(IMethodSymbol method, bool isStatic, ITypeSymbol returnType,
        ITypeSymbol[] parameterTypes)
    {
        if (method.IsStatic != isStatic || method.Arity != 0
            || !SymbolEqualityComparer.Default.Equals(method.ReturnType, returnType)
            || method.Parameters.Length != parameterTypes.Length)
            return false;

        for (var index = 0; index < parameterTypes.Length; index++)
        {
            if (method.Parameters[index].RefKind != RefKind.None
                || !SymbolEqualityComparer.Default.Equals(method.Parameters[index].Type, parameterTypes[index]))
                return false;
        }
        return true;
    }
}
