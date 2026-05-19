using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK052Tests
{
    [Fact]
    public async Task Financial_Consumer_Without_Idempotency_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            using MassTransit;
            public class PaymentCompleted { }
            public class {|#0:PaymentCompletedConsumer|} : IConsumer<PaymentCompleted>
            {
                public Task Consume(ConsumeContext<PaymentCompleted> context) => Task.CompletedTask;
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK052_ConsumerMustCheckIdempotencyAnalyzer>
            .Diagnostic(Diagnostics.ConsumerMustCheckIdempotency)
            .WithLocation(0)
            .WithArguments("PaymentCompletedConsumer");
        await CSharpAnalyzerVerifier<HWK052_ConsumerMustCheckIdempotencyAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Financial_Consumer_With_IdempotencyJournal_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using MassTransit;
            public class PaymentCompleted { }
            public class PaymentCompletedConsumer : IConsumer<PaymentCompleted>
            {
                public async Task Consume(ConsumeContext<PaymentCompleted> context)
                {
                    // Check IdempotencyJournal before processing
                    var exists = false; // IdempotencyJournal check
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK052_ConsumerMustCheckIdempotencyAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NonFinancial_Consumer_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using MassTransit;
            public class UserRegistered { }
            public class UserRegisteredConsumer : IConsumer<UserRegistered>
            {
                public Task Consume(ConsumeContext<UserRegistered> context) => Task.CompletedTask;
            }
            """;
        await CSharpAnalyzerVerifier<HWK052_ConsumerMustCheckIdempotencyAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
