# UO.Analyzers

Roslyn analyzers and Code Fixes for C# projects, including EF Core SQL translation checks and
source-generated logging conversions. The package adds no runtime dependency to your application.

[NuGet](https://www.nuget.org/packages/UO.Analyzers) · [Rules and Code Fixes](#rules-and-code-fixes) · [Configuration](#configuration) · [Development](https://github.com/ugurozturk/UO.Analyzer/blob/main/docs/development.md)

## Installation

Add the package to each project you want to analyze, replacing `x.y.z` with the package version:

```xml
<PackageReference Include="UO.Analyzers"
                  Version="x.y.z"
                  PrivateAssets="all" />
```

Use C# 9 or later and a compiler/IDE based on Roslyn 4.14 or later. The IDE must support discovering
Code Fix providers from NuGet packages to offer fixes. Feature-specific dependencies are documented
on each rule's page.

## Configuration

**Installing the package alone does not activate rules that require configuration.**
UO0001 requires an explicit EF provider profile. Helpers receiving an `IQueryable<T>` also require
an opt-in because their query provider cannot be determined from the parameter type.

Add the following to the consuming project's `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.UO0001.severity = warning
# Select the verified provider profile(s) used by your application.
dotnet_code_quality.UO0001.ef_core_providers = npgsql-8.0.0, sqlite-8.0.0, oracle-10.23.26000

# Replace this filename with the file containing your EF IQueryable helpers.
[**/CustomerQueryHelpers.cs]
dotnet_code_quality.UO0001.assume_ef_core_queryable = true
```

`assume_ef_core_queryable` asserts that otherwise unknown queryable sources in the matching file
are EF queries; scope it to the relevant files. The profile suffix (such as `8.0.0` or `10.23.26000`) is the verified **EF provider
version**, not the UO.Analyzers package version. Provider selection is not inferred from installed packages.

See [UO0001 configuration and troubleshooting](https://github.com/ugurozturk/UO.Analyzer/blob/main/docs/rules/UO0001.md#configuration)
for defaults, examples and cases where no warning is expected. Severity can be configured with
`dotnet_diagnostic.<ID>.severity`; setting severity alone does not supply required rule options.

## Rules and Code Fixes

Select an ID for setup instructions, examples, supported cases and limitations.

| ID | Description | Diagnostic owner | Severity / activation | Code Fix |
| --- | --- | --- | --- | --- |
| [UO0001](https://github.com/ugurozturk/UO.Analyzer/blob/main/docs/rules/UO0001.md) | Detect unsupported method overloads in EF Core SQL predicates | UO.Analyzers | Warning; provider configuration required | No |
| [CA1848](https://github.com/ugurozturk/UO.Analyzer/blob/main/docs/code-fixes/CA1848.md) | Convert logging calls to source-generated `LoggerMessage` methods | Microsoft .NET analyzers | Enable Microsoft's CA1848 diagnostic | Yes |

UO.Analyzers supplies the Code Fix for CA1848; it does not produce that diagnostic.

## Development

See the [development guide](https://github.com/ugurozturk/UO.Analyzer/blob/main/docs/development.md)
for build, test and package validation commands, and the
[contributor guide](https://github.com/ugurozturk/UO.Analyzer/blob/main/docs/adding-a-rule.md)
for adding rules and fixes. New EF overloads and provider profiles are covered by the
[translation catalog guide](https://github.com/ugurozturk/UO.Analyzer/blob/main/docs/adding-ef-translation-support.md).
