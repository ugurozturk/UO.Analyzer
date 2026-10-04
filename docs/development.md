# Development

## Adding rules and fixes

- [Adding an analyzer, Code Fix or refactoring](adding-a-rule.md)
- [Extending the EF Core translation catalog](adding-ef-translation-support.md)

## Local validation

Use the same checks as [CI](../.github/workflows/ci.yml), from the repository root:

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
dotnet pack -c Release --no-build
package_version="$(dotnet msbuild packaging/UO.Analyzers.Package/UO.Analyzers.Package.csproj -nologo -getProperty:PackageVersion)"
python3 scripts/verify-package.py "artifacts/packages/UO.Analyzers.${package_version}.nupkg"
dotnet restore tests/PackageConsumer/PackageConsumer.csproj -p:AnalyzerPackageVersion="$package_version" --source ./artifacts/packages --source https://api.nuget.org/v3/index.json --packages ./artifacts/consumer-packages
dotnet build tests/PackageConsumer/PackageConsumer.csproj -c Release --no-restore -p:AnalyzerPackageVersion="$package_version"
python3 scripts/verify-package.py "artifacts/packages/UO.Analyzers.${package_version}.nupkg" --consumer tests/PackageConsumer
```

Analyzer tests verify compiler/editor diagnostics against real EF metadata, not database translation acceptance.
Manual IDE discovery and actual application/provider translation remain separate acceptance checks.

[Back to README](../README.md)
