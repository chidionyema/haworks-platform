using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK060Tests
{
    [Fact]
    public async Task Console_WriteLine_Reports()
    {
        const string source = """
            public class MyService
            {
                public void DoWork()
                {
                    {|#0:System.Console.WriteLine("hello")|};
                }
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK060_NoConsoleWriteAnalyzer>
            .Diagnostic(Diagnostics.NoConsoleWrite)
            .WithLocation(0)
            .WithArguments("Console.WriteLine");
        await CSharpAnalyzerVerifier<HWK060_NoConsoleWriteAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Logger_Call_NoDiagnostic()
    {
        const string source = """
            public interface ILogger { void LogInformation(string msg); }
            public class MyService
            {
                private readonly ILogger _logger;
                public void DoWork()
                {
                    _logger.LogInformation("hello");
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK060_NoConsoleWriteAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
