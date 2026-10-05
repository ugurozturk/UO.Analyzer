using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UO.Analyzers.Rules.UO0001UnsupportedEfCoreTranslation;

[Flags]
internal enum ProviderProfiles
{
    None = 0,
    Npgsql800 = 1,
    Sqlite800 = 2,
    Oracle102326000 = 4,
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
                else if (string.Equals(item.Trim(), "oracle-10.23.26000", StringComparison.OrdinalIgnoreCase))
                    profiles |= ProviderProfiles.Oracle102326000;
            }
        }

        var assumeEf = options.TryGetValue(AssumeEfOption, out var assume)
            && bool.TryParse(assume, out var enabled) && enabled;
        return new ProviderConfiguration(profiles, assumeEf);
    }

    public static string Display(ProviderProfiles profiles)
    {
        var labels = new List<string>(3);
        if ((profiles & ProviderProfiles.Npgsql800) != 0)
            labels.Add("Npgsql 8.0.0");
        if ((profiles & ProviderProfiles.Sqlite800) != 0)
            labels.Add("SQLite 8.0.0");
        if ((profiles & ProviderProfiles.Oracle102326000) != 0)
            labels.Add("Oracle 10.23.26000");
        return string.Join(", ", labels);
    }
}
