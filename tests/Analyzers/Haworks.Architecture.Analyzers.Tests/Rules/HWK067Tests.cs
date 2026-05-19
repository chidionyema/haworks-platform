using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK067Tests
{
    [Fact]
    public async Task Record_Entity_Reports()
    {
        const string source = """
            public record BaseEntity { }
            public record {|#0:OrderEntity|} : BaseEntity;
            """;
        var expected = CSharpAnalyzerVerifier<HWK067_NoEntityRecordsWithEfAnalyzer>
            .Diagnostic(Diagnostics.NoEntityRecordsWithEf)
            .WithLocation(0)
            .WithArguments("OrderEntity");
        await CSharpAnalyzerVerifier<HWK067_NoEntityRecordsWithEfAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Class_Entity_NoDiagnostic()
    {
        const string source = """
            public class BaseEntity { }
            public class OrderEntity : BaseEntity { }
            """;
        await CSharpAnalyzerVerifier<HWK067_NoEntityRecordsWithEfAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Record_Event_NoDiagnostic()
    {
        const string source = """
            public record BaseEntity { }
            public record OrderCreatedEvent : BaseEntity;
            """;
        await CSharpAnalyzerVerifier<HWK067_NoEntityRecordsWithEfAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
