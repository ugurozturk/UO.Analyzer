using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UO.Analyzers.Tests.Infrastructure;
using Xunit;

namespace UO.Analyzers.Tests.CodeFixes;

public sealed class BoundaryTests
{
    [Theory]
    [InlineData("record")]
    [InlineData("struct")]
    [InlineData("record struct")]
    [InlineData("file class")]
    public async Task UnsupportedContainingTypesHaveNoAction(string declaration)
    {
        using var host = new LoggingTestHost($$"""
            using Microsoft.Extensions.Logging;
            {{declaration}} Service
            {
                public void Run(ILogger logger) { logger.LogInformation("Started"); }
            }
            """);
        Assert.Empty(await host.GetRegisteredCodeFixesAsync(await host.CreateSyntheticCA1848DiagnosticAsync()));
    }

    [Theory]
    [InlineData("public void Run<T>(ILogger logger, T value) { logger.LogInformation(\"Value {Value}\", value); }")]
    [InlineData("public void Run(ILogger logger, dynamic value) { logger.LogInformation(\"Value {Value}\", value); }")]
    [InlineData("public void Run(ILogger logger) => logger.LogInformation(\"Started\");")]
    [InlineData("public void Run(ILogger logger) { System.Linq.Expressions.Expression<System.Action> expression = () => logger.LogInformation(\"Started\"); _ = expression; }")]
    public async Task UnsupportedScopesAndTypesHaveNoAction(string member)
    {
        using var host = new LoggingTestHost($$"""
            using Microsoft.Extensions.Logging;
            class Service { {{member}} }
            """);
        Assert.Empty(await host.GetRegisteredCodeFixesAsync(await host.CreateSyntheticCA1848DiagnosticAsync()));
    }

    [Fact]
    public async Task TopLevelHasNoAction()
    {
        using var host = new LoggingTestHost("""
            using Microsoft.Extensions.Logging;
            ILogger logger = null!;
            logger.LogInformation("Started");
            """);
        Assert.Empty(await host.GetRegisteredCodeFixesAsync(await host.CreateSyntheticCA1848DiagnosticAsync()));
    }

    [Fact]
    public async Task OldLanguageVersionHasNoAction()
    {
        using var host = new LoggingTestHost("""
            using Microsoft.Extensions.Logging;
            class Service { public void Run(ILogger logger) { logger.LogInformation("Started"); } }
            """, LanguageVersion.CSharp8);
        Assert.Empty(await host.GetRegisteredCodeFixesAsync(await host.CreateSyntheticCA1848DiagnosticAsync()));
    }

    [Fact]
    public async Task PreservesTypeConflictsAndAddsRequiredImports()
    {
        using var host = new LoggingTestHost("""
            using Microsoft.Extensions.Logging;
            namespace Example;
            public class ILogger {}
            public class Guid {}
            public class Service
            {
                public void Run(Microsoft.Extensions.Logging.ILogger logger, System.Guid id)
                {
                    logger.LogInformation("User {UserId} logged in", id);
                }
            }
            """);
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        Assert.Contains("LogUserLoggedIn(logger, id)", result);
        Assert.Contains("Microsoft.Extensions.Logging.ILogger logger", result);
        Assert.Contains("System.Guid userId", result);
    }

    [Fact]
    public async Task UsesSymbolTypesForPropertiesTuplesArraysNullableAndGenericContainingClasses()
    {
        using var host = new LoggingTestHost("""
            using Microsoft.Extensions.Logging;
            public class Service<T>
            {
                public int Id { get; set; }
                public void Run(ILogger logger, T value, int? count, (int X, int Y) point, int[] values)
                {
                    logger.LogInformation("Values {Id} {Value} {Count} {Point} {Values}", Id, value, count, point, values);
                }
            }
            """);
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        Assert.Contains("int id, T? value, int? count, (int X, int Y) point, int[] values", result);
    }

    [Fact]
    public async Task KeepsLiteralContentEscapedBracesAndTrivia()
    {
        using var host = new LoggingTestHost("""
            using Microsoft.Extensions.Logging;
            // Keep type comment.
            class Service
            {
                public void Run(ILogger logger, int value)
                {
                    // Keep call comment.
                    logger.LogInformation(@"Literal {{braces}} and ""quotes"" {Value:N0}", value); // Keep suffix.
                }
            }
            """);
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        Assert.Contains("// Keep type comment.", result);
        Assert.Contains("// Keep call comment.", result);
        Assert.Contains("// Keep suffix.", result);
        Assert.Contains("@\"Literal {{braces}} and \"\"quotes\"\" {Value:N0}\"", result);
    }

    [Fact]
    public async Task SupportsMoreThanSixArguments()
    {
        using var host = new LoggingTestHost("""
            using Microsoft.Extensions.Logging;
            class Service
            {
                public void Run(ILogger logger)
                {
                    logger.LogInformation("Values {A} {B} {C} {D} {E} {F} {G}", 1, 2, 3, 4, 5, 6, 7);
                }
            }
            """);
        await host.ApplyLoggingFixAndVerifyCompilationAsync();
        var root = (await host.Document.GetSyntaxRootAsync())!;
        Assert.Equal(8, root.DescendantNodes().OfType<MethodDeclarationSyntax>().Last().ParameterList.Parameters.Count);
    }

    [Fact]
    public async Task AddsUsingAndSimplifiesUnambiguousTypes()
    {
        using var host = new LoggingTestHost("""
            using Microsoft.Extensions.Logging;
            public class Service
            {
                public void Run(ILogger logger, System.Guid id)
                {
                    logger.LogInformation("User {UserId} logged in", id);
                }
            }
            """);
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        Assert.Contains("using System;", result);
        Assert.Contains("Guid userId", result);
        Assert.DoesNotContain("global::", result);
    }

    [Fact]
    public async Task NullableDisabledConsumerCompilesWithoutWarnings()
    {
        using var host = new LoggingTestHost("""
            #nullable disable
            using System;
            using Microsoft.Extensions.Logging;
            class Service
            {
                public void Run(ILogger logger, Exception exception)
                {
                    logger.LogError(exception, "Failed");
                }
            }
            """);
        var result = await host.ApplyLoggingFixAndVerifyCompilationAsync();
        Assert.Contains("Exception exception", result);
        Assert.DoesNotContain("Exception?", result);
    }
}
