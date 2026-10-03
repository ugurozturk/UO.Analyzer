using System;
using System.IO;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using UO.Analyzers.Tests.Infrastructure;
using Xunit;

namespace UO.Analyzers.Tests.CodeFixes;

public sealed class RuntimeSemanticsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreservesEvaluationOrderExceptionAndStructuredState(bool enabled)
    {
        using var host = new LoggingTestHost("""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Microsoft.Extensions.Logging;

            public class Scenario
            {
                private static readonly List<string> Events = new();
                private static bool Enabled;
                public static string Run(bool enabled)
                {
                    Enabled = enabled;
                    Events.Clear();
                    GetLogger().LogError(GetException(), "Order {OrderId} state {State}", GetOrderId(), GetState());
                    return string.Join("|", Events);
                }
                private static ILogger GetLogger() { Events.Add("logger"); return new Recorder(); }
                private static Exception GetException() { Events.Add("exception"); return new Exception("failure"); }
                private static int GetOrderId() { Events.Add("order"); return 42; }
                private static string GetState() { Events.Add("state"); return "pending"; }
                private class Recorder : ILogger
                {
                    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                    public bool IsEnabled(LogLevel level) { Events.Add("enabled?"); return Enabled; }
                    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                    {
                        Events.Add($"{level}:{exception?.Message}:{formatter(state, exception)}");
                        Events.Add(string.Join(",", ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).Select(p => $"{p.Key}={p.Value}")));
                    }
                }
            }
            """);
        var before = ExecuteCompiledLoggingScenario(await host.GenerateLoggingAndVerifyCompilationAsync(), enabled);
        await host.ApplyLoggingFixAndVerifyCompilationAsync();
        var after = ExecuteCompiledLoggingScenario(await host.GenerateLoggingAndVerifyCompilationAsync(), enabled);
        Assert.Equal(before, after);
        Assert.StartsWith("logger|exception|order|state|Error:failure:Order 42 state pending", after, StringComparison.Ordinal);
    }

    private static string ExecuteCompiledLoggingScenario(Compilation compilation, bool enabled)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("logging-test", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            return (string)assembly.GetType("Scenario")!.GetMethod("Run")!.Invoke(null, [enabled])!;
        }
        finally
        {
            context.Unload();
        }
    }
}
