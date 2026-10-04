using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UO.Analyzers.Tests.Infrastructure;
using Xunit;

namespace UO.Analyzers.Tests.CodeFixes;

public sealed class InterpolatedMessageTests
{
    [Theory]
    [InlineData("Logger.LogInformation($\"Successfully completed {tenant.Name} tenant database migrations.\");",
        "Successfully completed {TenantName} tenant database migrations.", "string tenantName")]
    [InlineData("Logger.LogInformation($\"Executing {(tenant == null ? \"host\" : tenant.Name + \" tenant\")} database seed...\");",
        "Executing {Value} database seed...", "string value")]
    [InlineData("Logger.LogInformation(($\"Value {count}\"));", "Value {Count}", "string count")]
    [InlineData("Logger.LogInformation($\"Values {count} {count} {Count}\");", "Values {Count} {Count2} {Count3}", "string count, string count2, string count3")]
    [InlineData("Logger.LogError(exception, $\"Failed {exception.Message}\");", "Failed {ExceptionMessage}", "Exception? exception, string exceptionMessage")]
    [InlineData("Logger.LogError(exception, $\"Failed {exception}\");", "Failed {Exception}", "Exception? exception2, string exception")]
    [InlineData("Logger.LogInformation($\"Value {count,10:N2}\");", "Value {Count}", "string count")]
    [InlineData("Logger.LogInformation($@\"Path C:\\temp\\{count} and \"\"quote\"\"\");", "{Message}", "string message")]
    [InlineData("Logger.LogInformation($\"Literal {{{count}}}\");", "Literal {{{Count}}}", "string count")]
    [InlineData("Logger.LogInformation($$\"\"\"Literal {braces} {{count}}\"\"\");", "Literal {{braces}} {Count}", "string count")]
    [InlineData("Logger.LogInformation($$\"\"\"\n    Literal {braces}\n    {{count,10:N2}}\n    \"\"\");", "Literal {{braces}}\n{Count}", "string count")]
    public async Task ConvertsInterpolationsWithRealDiagnosticAndGenerator(string call, string template, string parameters)
    {
        using var host = new LoggingTestHost($$"""
            using System;
            using Microsoft.Extensions.Logging;
            public class Service
            {
                private ILogger Logger { get; }
                public Service(ILogger logger) => Logger = logger;
                public void Run(Tenant tenant, int count, int Count, Exception exception)
                {
                    {{call}}
                }
            }
            public class Tenant { public string Name => "Example"; }
            """);
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        var root = (await host.Document.GetSyntaxRootAsync())!;
        var message = root.DescendantNodes().OfType<AttributeArgumentSyntax>()
            .Single(argument => argument.NameEquals?.Name.Identifier.ValueText == "Message");
        Assert.Equal(template, Assert.IsType<LiteralExpressionSyntax>(message.Expression).Token.ValueText);
        Assert.Contains(parameters, result);
    }

    [Fact]
    public async Task InterpolationWithAdditionalTemplateArgumentsHasNoAction()
    {
        using var host = new LoggingTestHost("""
            using Microsoft.Extensions.Logging;
            class Service
            {
                public void Run(ILogger logger, string name, int value)
                {
                    logger.LogInformation($"{name}: {{Value}}", value);
                }
            }
            """);
        Assert.Empty(await host.GetRegisteredCodeFixesAsync());
    }

    [Theory]
    [InlineData("dynamic value", "$\"Value {value}\"")]
    [InlineData("int value", "$\"Value {await Task.FromResult(value)}\"")]
    public async Task KeepsWholeMessageForDynamicOrAwaitHoles(string parameter, string message)
    {
        using var host = new LoggingTestHost($$"""
            using System.Threading.Tasks;
            using Microsoft.Extensions.Logging;
            class Service
            {
                public async Task Run(ILogger logger, {{parameter}})
                {
                    logger.LogInformation({{message}});
                    await Task.CompletedTask;
                }
            }
            """);
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        Assert.Contains("Message = \"{Message}\"", result);
        Assert.Contains("LogInformation(logger, " + message + ")", result);
    }
}
