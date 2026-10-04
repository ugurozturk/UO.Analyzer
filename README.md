# UO.Analyzers

A Roslyn analyzer and Code Fix package for C# projects. Converts `ILogger` calls flagged by
**CA1848** into source-generated `LoggerMessage` methods.
Microsoft's .NET analyzer produces the CA1848 warning; this package provides the conversion action.
The opt-in **UO0001** rule also warns about verified unsupported method overloads in EF Core SQL predicates.

## Installation

Add the following package reference to your project, replacing `x.y.z` with the
package version you want to use:

```xml
<PackageReference Include="UO.Analyzers"
                  Version="x.y.z"
                  PrivateAssets="all" />
```

For the CA1848 conversion, your project must include `Microsoft.Extensions.Logging.Abstractions` and the logging
source generator. If these are already provided by your framework or another package,
you do not need to add them again.

Enable .NET analyzers (`EnableNETAnalyzers=true`) and enable the CA1848 warning
in your `.editorconfig` file:

```ini
[*.cs]
dotnet_diagnostic.CA1848.severity = warning
```

### Requirements

- C# 9 or later.
- An IDE that uses Roslyn 4.14 or later and supports discovering Code Fix providers from NuGet packages.

The package is used only during development and adds no runtime dependency to your application.

## Usage

### EF Core SQL translation: UO0001

Select a verified provider profile explicitly:

```ini
[*.cs]
dotnet_code_quality.UO0001.ef_core_providers = npgsql-8.0.0, sqlite-8.0.0
dotnet_diagnostic.UO0001.severity = warning
```

With a known `DbSet<Customer>` source, this marks the complete `Contains` invocation:

```csharp
db.Customers.Where(customer => customer.NameSurname.Contains(
    searchTerm, StringComparison.CurrentCultureIgnoreCase));
```

Profiles cover the stock Npgsql EF provider **8.0.0** and SQLite EF provider **8.0.0** only.
No profile is inferred from installed packages or runtime configuration. Helpers taking an opaque
`IQueryable<T>` (including ABP repository queries) require a separate, preferably file-scoped opt-in:

```ini
[**/CustomerQueryHelpers.cs]
dotnet_code_quality.UO0001.assume_ef_core_queryable = true
```

This asserts that otherwise unknown queryable abstraction boundaries in that file are EF queries.
Known in-memory chains still stop analysis. Predicates, row dependencies, and exact method symbols
are checked separately. Captured values and final `Select` transformations are not flagged.
There is no Code Fix: changing culture/comparison semantics requires a deliberate choice.
The rule does not emit or suppress CA1862 or any other Microsoft diagnostic.

See [UO0001 configuration, support matrix, evidence and limits](docs/rules/UO0001.md)
and [adding translation support](docs/adding-ef-translation-support.md).

### CA1848 conversion

For a supported logging call with a CA1848 warning, select
**Convert to source-generated LoggerMessage** from your IDE's Quick Fix menu.
The required `partial` declarations and logging method are added automatically.

Before:

```csharp
public class OrderService
{
    private readonly ILogger<OrderService> _logger;

    public OrderService(ILogger<OrderService> logger) => _logger = logger;

    public void Process(int orderId)
    {
        _logger.LogInformation(
            "Processing order {OrderId}",
            orderId);
    }
}
```

After:

```csharp
public partial class OrderService
{
    private readonly ILogger<OrderService> _logger;

    public OrderService(ILogger<OrderService> logger) => _logger = logger;

    public void Process(int orderId)
    {
        LogProcessingOrder(_logger, orderId);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Processing order {OrderId}",
        SkipEnabledCheck = true)]
    private static partial void LogProcessingOrder(ILogger logger, int orderId);
}
```

### Supported calls

- `LogTrace`, `LogDebug`, `LogInformation`, `LogWarning`, `LogError`, and `LogCritical`.
- Calls through `ILogger` and `ILogger<T>`.
- Constant message templates, structured logging arguments, and exception overloads.
- Interpolated messages, including verbatim and raw string forms.
- Nested classes and existing partial classes.

### Conversion behavior

- The logger, exception, and message arguments are evaluated once each, in their original order.
- `SkipEnabledCheck = true` preserves the existing call's log level checking behavior.
- For constant templates, the message, placeholder names, formatting/alignment, and exception are preserved.
- The generator derives EventId/EventName from the method name; the previous default `0`/unnamed event
  is not preserved. Review the conversion if your application filters logs by event metadata.
- Interpolated message values become formatted `string` parameters, and `{OriginalFormat}`
  becomes a constant template. In some cases, the entire message is preserved as a single
  `{Message}` parameter. This conversion does not eliminate all interpolation costs.

### Limitations

The action is not offered when a call cannot be converted safely. The main unsupported cases are:

- Nonconstant messages other than interpolated strings, and dynamic calls.
- `EventId` overloads, static `LoggerExtensions` calls, and named arguments.
- Explicit `params` arrays/null, or additional `params` arguments with an interpolated message.
- Malformed templates, argument count mismatches, and repeated or unsupported placeholder names.
- `Exception`, `ILogger`, or `LogLevel` as structured payloads; anonymous, dynamic,
  ref-like, pointer, or generic method type parameters.
- Conditional access, expression-bodied calls, expression trees, and top-level code.
- Structs, records, interfaces, file-local types, or preprocessor directives in the containing type.

**Fix All is not supported;** apply the conversion to each call individually.

## Local validation

Use the same checks as CI (substitute the version from `Directory.Build.props` when it changes):

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
dotnet pack -c Release --no-build
python3 scripts/verify-package.py artifacts/packages/UO.Analyzers.1.0.1.nupkg
dotnet restore tests/PackageConsumer/PackageConsumer.csproj -p:AnalyzerPackageVersion=1.0.1 --source ./artifacts/packages --source https://api.nuget.org/v3/index.json --packages ./artifacts/consumer-packages
dotnet build tests/PackageConsumer/PackageConsumer.csproj -c Release --no-restore -p:AnalyzerPackageVersion=1.0.1
python3 scripts/verify-package.py artifacts/packages/UO.Analyzers.1.0.1.nupkg --consumer tests/PackageConsumer
```

Analyzer tests verify compiler/editor diagnostics against real EF metadata, not database translation acceptance.
Manual IDE discovery and actual application/provider translation remain separate acceptance checks.
