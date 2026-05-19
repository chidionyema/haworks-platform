using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK077Tests
{
    [Fact]
    public async Task ToListAsync_On_Queryable_Without_Take_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            using System.Collections.Generic;
            using System.Linq;
            public static class QueryExtensions
            {
                public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => Task.FromResult(new List<T>());
            }
            public class Order { }
            public class Service
            {
                private readonly IQueryable<Order> _orders;
                public async Task<List<Order>> GetAll()
                {
                    return await {|#0:_orders.ToListAsync()|};
                }
            }
            """;

        var expected = Verifier<HWK077_NoUnboundedToListAnalyzer>
            .Diagnostic(Diagnostics.NoUnboundedToList)
            .WithLocation(0)
            .WithArguments("ToListAsync");

        await Verifier<HWK077_NoUnboundedToListAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task ToList_On_InMemory_List_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            public class Service
            {
                public List<int> GetAll()
                {
                    var items = new List<int> { 1, 2, 3 };
                    return items.Where(x => x > 1).ToList();
                }
            }
            """;

        await Verifier<HWK077_NoUnboundedToListAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    private static class Verifier<TAnalyzer> where TAnalyzer : DiagnosticAnalyzer, new()
    {
        public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor) =>
            CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic(descriptor);

        public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
        {
            var test = new Test { TestCode = source };
            test.ExpectedDiagnostics.AddRange(expected);
            await test.RunAsync(CancellationToken.None);
        }

        public static async Task VerifyNoDiagnosticsAsync(string source)
        {
            var test = new Test { TestCode = source };
            await test.RunAsync(CancellationToken.None);
        }

        private sealed class Test : CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            public Test()
            {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
                TestState.Sources.Add(Stubs.EfCore);
            }
        }
    }
}
