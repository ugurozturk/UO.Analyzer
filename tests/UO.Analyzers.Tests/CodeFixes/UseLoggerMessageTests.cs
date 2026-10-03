using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UO.Analyzers.CodeFixes.Logging;
using UO.Analyzers.Tests.Infrastructure;
using Xunit;

namespace UO.Analyzers.Tests.CodeFixes;

public sealed class UseLoggerMessageTests
{
    private static string CreateServiceSource(string call, string members = "", string declaration = "public class Service", string loggerType = "ILogger") => $$"""
        using System;
        using Microsoft.Extensions.Logging;
        namespace Example;
        {{declaration}}
        {
            private readonly {{loggerType}} _logger;
            public Service({{loggerType}} logger) => _logger = logger;
            public void Run(int orderId, Guid userId, string state, Exception exception)
            {
                {{call}}
            }
            {{members}}
        }
        """;

    [Theory]
    [InlineData("Trace")]
    [InlineData("Debug")]
    [InlineData("Information")]
    [InlineData("Warning")]
    [InlineData("Error")]
    [InlineData("Critical")]
    public async Task ConvertsAllLevelsUsingRealMicrosoftDiagnosticAndGenerator(string level)
    {
        using var host = new LoggingTestHost(CreateServiceSource($"_logger.Log{level}(\"Application started\");"));
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        Assert.Contains("public partial class Service", result);
        Assert.Contains("LogApplicationStarted(_logger);", result);
        Assert.Contains("Level = LogLevel." + level, result);
        Assert.Contains("private static partial void LogApplicationStarted(ILogger logger)", result);
        Assert.DoesNotContain("global::", result);
    }

    [Theory]
    [InlineData("ILogger")]
    [InlineData("ILogger<Service>")]
    public async Task InfersStructuredTypes(string loggerType)
    {
        using var host = new LoggingTestHost(CreateServiceSource("_logger.LogWarning(\"User {UserId} has invalid state {State}\", userId, state);", loggerType: loggerType));
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        Assert.Contains("LogUserHasInvalidState(_logger, userId, state)", result);
        Assert.Contains("Guid userId, string state", result);
    }

    [Fact]
    public async Task ConvertsExceptionOverloadWithoutReordering()
    {
        using var host = new LoggingTestHost(CreateServiceSource("_logger.LogError(exception, \"Processing {OrderId} failed\", orderId);"));
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        Assert.Contains("LogProcessingFailed(_logger, exception, orderId)", result);
        Assert.Contains("Exception? exception, int orderId", result);
    }

    [Theory]
    [InlineData("public class Service")]
    [InlineData("public partial class Service")]
    public async Task HandlesPartialAndOverloadCollisions(string declaration)
    {
        using var host = new LoggingTestHost(CreateServiceSource("_logger.LogInformation(\"Processing order {OrderId}\", orderId);",
            "private void LogProcessingOrder() {} private void LogProcessingOrder2(int value) {}", declaration));
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        Assert.Contains("LogProcessingOrder3(_logger, orderId)", result);
        Assert.DoesNotContain("partial partial", result);
    }

    [Theory]
    [InlineData("OrderId", "orderId")]
    [InlineData("URL", "url")]
    [InlineData("URLValue", "urlValue")]
    [InlineData("class", "@class")]
    [InlineData("ElapsedMilliseconds:N0", "elapsedMilliseconds")]
    [InlineData("ElapsedMilliseconds,10:N0", "elapsedMilliseconds")]
    [InlineData("logger", "logger")]
    public async Task ConvertsPlaceholderNamesAndPreservesTemplate(string placeholder, string parameter)
    {
        using var host = new LoggingTestHost(CreateServiceSource($"_logger.LogInformation(\"Value {{{placeholder}}}\", orderId);"));
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        Assert.Contains("int " + parameter, result);
        Assert.Contains("Value {" + placeholder + "}", result);
    }

    [Fact]
    public async Task SupportsNestedClassesAndBlockNamespace()
    {
        using var host = new LoggingTestHost("""
            using Microsoft.Extensions.Logging;
            namespace Example
            {
                public class Outer
                {
                    public class Inner
                    {
                        public void Run(ILogger logger) { logger.LogInformation("Application started"); }
                    }
                }
            }
            """);
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        Assert.Contains("public partial class Outer", result);
        Assert.Contains("public partial class Inner", result);
        var root = (await host.Document.GetSyntaxRootAsync())!;
        Assert.Single(root.DescendantNodes().OfType<MethodDeclarationSyntax>(), m => m.Identifier.ValueText == "LogApplicationStarted");
        Assert.Equal("Inner", root.DescendantNodes().OfType<MethodDeclarationSyntax>().Last().Parent is ClassDeclarationSyntax type ? type.Identifier.ValueText : "");
    }

    [Fact]
    public async Task SupportsPropertyReceiverAndConstantMessage()
    {
        using var host = new LoggingTestHost(CreateServiceSource("const string text = \"Application\" + \" started\"; Logger.LogInformation(text); _ = text;",
            "private ILogger Logger => _logger;"));
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        Assert.Contains("LogApplicationStarted(Logger)", result);
        Assert.Contains("Message = \"Application started\"", result);
    }

    [Fact]
    public async Task HandlesLocalNameCollision()
    {
        using var host = new LoggingTestHost(CreateServiceSource("Action LogApplicationStarted = () => {}; _logger.LogInformation(\"Application started\"); LogApplicationStarted();"));
        Assert.Contains("LogApplicationStarted2(_logger)", await host.ApplyLoggingFixAndVerifyCompilationAsync());
    }

    [Fact]
    public async Task UsesFallbackForPlaceholderOnlyMessage()
    {
        using var host = new LoggingTestHost(CreateServiceSource("_logger.LogInformation(\"{OrderId}\", orderId);"));
        Assert.Contains("LogInformation(_logger, orderId)", await host.ApplyLoggingFixAndVerifyCompilationAsync());
    }

    [Theory]
    [InlineData("_logger.LogInformation(state);")]
    [InlineData("_logger.LogInformation(new EventId(5), \"Started\");")]
    [InlineData("_logger.LogInformation(\"{Value} {Value}\", orderId, orderId + 1);")]
    [InlineData("_logger.LogInformation(\"{Value} {value}\", orderId, orderId);")]
    [InlineData("_logger.LogInformation(\"{Value}\", new object[] { orderId });")]
    [InlineData("_logger.LogInformation(\"{Value}\", null);")]
    [InlineData("_logger.LogInformation(\"{Value}\", new { Value = 1 });")]
    [InlineData("_logger.LogInformation(\"{Value}\", exception);")]
    [InlineData("_logger.LogInformation(\"{Logger}\", _logger);")]
    [InlineData("_logger.LogInformation(\"{Level}\", LogLevel.Debug);")]
    [InlineData("_logger.LogInformation(\"{Value}\");")]
    [InlineData("_logger.LogInformation(\"Started\", orderId);")]
    [InlineData("_logger.LogInformation(\"{Broken\", orderId);")]
    [InlineData("_logger.LogInformation(\"{0}\", orderId);")]
    [InlineData("_logger.LogInformation(message: \"Started\");")]
    [InlineData("_logger?.LogInformation(\"Started\");")]
    [InlineData("LoggerExtensions.LogInformation(_logger, \"Started\");")]
    public async Task UnsafeCallsHaveNoAction(string call)
    {
        using var host = new LoggingTestHost(CreateServiceSource(call));
        var diagnostic = await host.CreateSyntheticCA1848DiagnosticAsync();
        Assert.Empty(await host.GetRegisteredCodeFixesAsync(diagnostic));
    }

    [Fact]
    public async Task OtherMethodsWithSameNameHaveNoAction()
    {
        using var host = new LoggingTestHost("""
            public class Service
            {
                public void LogInformation(string value) {}
                public void Run() { this.LogInformation("Started"); }
            }
            """);
        Assert.Empty(await host.GetRegisteredCodeFixesAsync(await host.CreateSyntheticCA1848DiagnosticAsync()));
    }

    [Fact]
    public async Task SupportsDiagnosticOnWholeInvocation()
    {
        using var host = new LoggingTestHost(CreateServiceSource("_logger.LogInformation(\"Started\");"));
        Assert.Single(await host.GetRegisteredCodeFixesAsync(await host.CreateSyntheticCA1848DiagnosticAsync(wholeInvocation: true)));
    }

    [Fact]
    public async Task PropagatesCancellation()
    {
        using var host = new LoggingTestHost(CreateServiceSource("_logger.LogInformation(\"Started\");"));
        var diagnostic = await host.CreateSyntheticCA1848DiagnosticAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.GetRegisteredCodeFixesAsync(diagnostic, cancellation.Token));
    }

    [Fact]
    public void DoesNotOfferUnsafeBatchFixAll() => Assert.Null(new UseLoggerMessageCodeFixProvider().GetFixAllProvider());

    [Fact]
    public async Task SequentialFixesDoNotCollideOrIntroduceDuplicateEventIds()
    {
        using var host = new LoggingTestHost(CreateServiceSource("_logger.LogInformation(\"Started\"); _logger.LogInformation(\"Started\");"));
        await host.ApplyLoggingFixAndVerifyCompilationAsync(expectRemainingDiagnostics: true);
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        Assert.Contains("LogStarted(_logger)", result);
        Assert.Contains("LogStarted2(_logger)", result);
    }

    [Fact]
    public async Task FindsMemberAndGeneratorBackingFieldCollisionsInOtherPartialFiles()
    {
        using var host = new LoggingTestHost(CreateServiceSource("_logger.LogInformation(\"Started\");", declaration: "public partial class Service"));
        host.AddSourceDocument("Other.cs", """
            namespace Example;
            public partial class Service
            {
                public void LogStarted(int value) {}
                public int __LogStarted2Callback;
                public int __LogStarted3Struct;
            }
            """);
        Assert.Contains("LogStarted4(_logger)", await host.ApplyLoggingFixAndVerifyCompilationAsync());
    }

    [Fact]
    public async Task AvoidsExplicitEventNameCollision()
    {
        using var host = new LoggingTestHost(CreateServiceSource("_logger.LogInformation(\"Started\");", """
            [LoggerMessage(EventName = "LogStarted", Level = LogLevel.Warning, Message = "Existing")]
            private static partial void Existing(ILogger logger);
            """, declaration: "public partial class Service"));
        Assert.Contains("LogStarted2(_logger)", await host.ApplyLoggingFixAndVerifyCompilationAsync());
    }
}
