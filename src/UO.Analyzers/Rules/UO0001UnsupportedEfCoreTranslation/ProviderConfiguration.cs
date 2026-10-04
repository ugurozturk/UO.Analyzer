using System;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UO.Analyzers.Rules.UO0001UnsupportedEfCoreTranslation;

[Flags]
internal enum ProviderProfiles
{
    None = 0,
    Npgsql800 = 1,
    Sqlite800 = 2,
}

internal readonly struct ProviderConfiguration
{
    private const string ProvidersOption = "dotnet_code_quality.UO0001.ef_core_providers";
    private const string AssumeEfOption = "dotnet_code_quality.UO0001.assume_ef_core_queryable";

    private ProviderConfiguration(ProviderProfiles profiles, bool assumeEfQueryable)
    {
        Profiles = profiles;
        AssumeEfQueryable = assumeEfQueryable;
    }

    public ProviderProfiles Profiles { get; }
    public bool AssumeEfQueryable { get; }

    public static ProviderConfiguration Read(AnalyzerConfigOptions options)
    {
        var profiles = ProviderProfiles.None;
        if (options.TryGetValue(ProvidersOption, out var value))
        {
            foreach (var item in value.Split(','))
            {
                if (string.Equals(item.Trim(), "npgsql-8.0.0", StringComparison.OrdinalIgnoreCase))
                    profiles |= ProviderProfiles.Npgsql800;
                else if (string.Equals(item.Trim(), "sqlite-8.0.0", StringComparison.OrdinalIgnoreCase))
                    profiles |= ProviderProfiles.Sqlite800;
            }
        }

        var assumeEf = options.TryGetValue(AssumeEfOption, out var assume)
            && bool.TryParse(assume, out var enabled) && enabled;
        return new ProviderConfiguration(profiles, assumeEf);
    }

    public static string Display(ProviderProfiles profiles) => profiles switch
    {
        ProviderProfiles.Npgsql800 => "Npgsql 8.0.0",
        ProviderProfiles.Sqlite800 => "SQLite 8.0.0",
        ProviderProfiles.Npgsql800 | ProviderProfiles.Sqlite800 => "Npgsql 8.0.0, SQLite 8.0.0",
        _ => string.Empty,
    };
}
