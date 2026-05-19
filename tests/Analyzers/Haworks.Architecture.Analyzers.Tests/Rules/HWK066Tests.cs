using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK066Tests
{
    [Fact]
    public async Task String_Property_Without_MaxLength_Reports()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            public class Order
            {
                public string Name { get; set; }
            }
            public class OrderConfig
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    {|#0:builder.Property(x => x.Name)|}.IsRequired();
                }
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK066_EfStringMustHaveMaxLengthAnalyzer>
            .Diagnostic(Diagnostics.EfStringMustHaveMaxLength)
            .WithLocation(0)
            .WithArguments("Name");
        await CSharpAnalyzerVerifier<HWK066_EfStringMustHaveMaxLengthAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task String_Property_With_MaxLength_NoDiagnostic()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            public class Order
            {
                public string Name { get; set; }
            }
            public class OrderConfig
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK066_EfStringMustHaveMaxLengthAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
