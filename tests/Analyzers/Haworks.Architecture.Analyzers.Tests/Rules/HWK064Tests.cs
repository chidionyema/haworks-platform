using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK064Tests
{
    [Fact]
    public async Task Navigation_In_Loop_Reports()
    {
        const string source = """
            using System.Collections.Generic;
            public class Order
            {
                public List<string> Items { get; set; }
            }
            public class MyService
            {
                public void Process(List<Order> orders)
                {
                    foreach (var order in orders)
                    {
                        var items = order.{|#0:Items|};
                    }
                }
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK064_NoNavigationInLoopAnalyzer>
            .Diagnostic(Diagnostics.NoNavigationInLoop)
            .WithLocation(0)
            .WithArguments("Items");
        await CSharpAnalyzerVerifier<HWK064_NoNavigationInLoopAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Navigation_Outside_Loop_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            public class Order
            {
                public List<string> Items { get; set; }
            }
            public class MyService
            {
                public void Process(Order order)
                {
                    var items = order.Items;
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK064_NoNavigationInLoopAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
