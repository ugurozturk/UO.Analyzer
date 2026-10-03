using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using UO.Analyzers.Tests.Infrastructure;
using Xunit;

namespace UO.Analyzers.Tests.CodeFixes;

public sealed class FrameworkIntegrationTests
{
    [Theory]
    [InlineData("ILogger")]
    [InlineData("ILogger<Service>")]
    public async Task MicrosoftCA1848ToGeneratedMethod(string loggerType)
    {
        var test = new MicrosoftLoggingCodeFixTest
        {
            TestCode = $$"""
                using Microsoft.Extensions.Logging;

                public class Service
                {
                    public void Run({{loggerType}} logger, int orderId)
                    {
                        {|#0:logger.LogInformation("Processing order {OrderId}", orderId)|};
                    }
                }
                """,
            FixedCode = $$"""
                using Microsoft.Extensions.Logging;

                public partial class Service
                {
                    public void Run({{loggerType}} logger, int orderId)
                    {
                        LogProcessingOrder(logger, orderId);
                    }

                    [LoggerMessage(Level = LogLevel.Information, Message = "Processing order {OrderId}", SkipEnabledCheck = true)]
                    private static partial void LogProcessingOrder(ILogger logger, int orderId);
                }
                """,
        };
        test.ExpectedDiagnostics.Add(new DiagnosticResult("CA1848", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("LoggerExtensions.LogInformation(ILogger, string?, params object?[])"));
        await test.RunAsync();
    }
}
