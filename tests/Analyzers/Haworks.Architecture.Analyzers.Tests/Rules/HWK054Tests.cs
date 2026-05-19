using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK054Tests
{
    [Fact]
    public async Task No_HttpPost_NoDiagnostic()
    {
        const string source = """
            using System;
            public class HttpGetAttribute : Attribute { }
            public class OrderController
            {
                [HttpGet]
                public void Get(string userId) { }
            }
            """;
        await CSharpAnalyzerVerifier<HWK054_NoUserIdFromRequestBodyAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UserId_FromRoute_NoDiagnostic()
    {
        const string source = """
            using System;
            public class HttpPostAttribute : Attribute { }
            public class FromRouteAttribute : Attribute { }
            public class OrderController
            {
                [HttpPost]
                public void Create([FromRoute] string userId) { }
            }
            """;
        await CSharpAnalyzerVerifier<HWK054_NoUserIdFromRequestBodyAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
