# UO.Analyzers

A Roslyn Code Fix package for C# projects. Converts `ILogger` calls flagged by
**CA1848** into source-generated `LoggerMessage` methods.
Microsoft's .NET analyzer produces the CA1848 warning; this package provides the conversion action.

## Installation

Add the following package reference to your project, replacing `x.y.z` with the
package version you want to use:

```xml
<PackageReference Include="UO.Analyzers"
                  Version="x.y.z"
                  PrivateAssets="all" />
```

Your project must include `Microsoft.Extensions.Logging.Abstractions` and the logging
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
