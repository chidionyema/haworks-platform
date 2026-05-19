using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK074Tests
{
    [Fact]
    public async Task BeginTransactionAsync_InConsumer_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            using MassTransit;
            public class MyEvent { }
            public class MyConsumer : IConsumer<MyEvent>
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task Consume(ConsumeContext<MyEvent> context)
                {
                    using var tx = await {|#0:_db.Database.BeginTransactionAsync()|};
                    await Task.CompletedTask;
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<HWK074_NoManualConsumerTransactionAnalyzer>
            .Diagnostic(Diagnostics.CompetingConsumerTransaction)
            .WithLocation(0)
            .WithArguments("BeginTransactionAsync");

        await CSharpAnalyzerVerifier<HWK074_NoManualConsumerTransactionAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoTransaction_InConsumer_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using MassTransit;
            public class MyEvent { }
            public class MyConsumer : IConsumer<MyEvent>
            {
                public async Task Consume(ConsumeContext<MyEvent> context)
                {
                    await Task.CompletedTask;
                }
            }
            """;

        await CSharpAnalyzerVerifier<HWK074_NoManualConsumerTransactionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task BeginTransactionAsync_InNonConsumer_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            public class MyService
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task DoWork()
                {
                    using var tx = await _db.Database.BeginTransactionAsync();
                    await Task.CompletedTask;
                }
            }
            """;

        await CSharpAnalyzerVerifier<HWK074_NoManualConsumerTransactionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
