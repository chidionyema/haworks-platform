using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK078Tests
{
    [Fact]
    public async Task ContainsKey_Then_Indexer_Assignment_Reports()
    {
        const string source = """
            using System.Collections.Concurrent;
            public class Service
            {
                private readonly ConcurrentDictionary<string, int> _cache = new();
                public void Add(string key, int value)
                {
                    if (!{|#0:_cache.ContainsKey(key)|})
                    {
                        _cache[key] = value;
                    }
                }
            }
            """;

        var expected = Verifier<HWK078_NoConcurrentDictionaryCheckThenActAnalyzer>
            .Diagnostic(Diagnostics.NoConcurrentDictionaryCheckThenAct)
            .WithLocation(0)
            .WithArguments("ContainsKey");

        await Verifier<HWK078_NoConcurrentDictionaryCheckThenActAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task TryGetValue_Then_Indexer_In_Else_Reports()
    {
        const string source = """
            using System.Collections.Concurrent;
            public class Service
            {
                private readonly ConcurrentDictionary<string, int> _cache = new();
                public int GetOrSet(string key, int value)
                {
                    if ({|#0:_cache.TryGetValue(key, out var existing)|})
                    {
                        return existing;
                    }
                    else
                    {
                        _cache[key] = value;
                        return value;
                    }
                }
            }
            """;

        var expected = Verifier<HWK078_NoConcurrentDictionaryCheckThenActAnalyzer>
            .Diagnostic(Diagnostics.NoConcurrentDictionaryCheckThenAct)
            .WithLocation(0)
            .WithArguments("TryGetValue");

        await Verifier<HWK078_NoConcurrentDictionaryCheckThenActAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task GetOrAdd_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Concurrent;
            public class Service
            {
                private readonly ConcurrentDictionary<string, int> _cache = new();
                public int GetOrSet(string key, int value)
                {
                    return _cache.GetOrAdd(key, _ => value);
                }
            }
            """;

        await Verifier<HWK078_NoConcurrentDictionaryCheckThenActAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Regular_Dictionary_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            public class Service
            {
                private readonly Dictionary<string, int> _cache = new();
                public void Add(string key, int value)
                {
                    if (!_cache.ContainsKey(key))
                    {
                        _cache[key] = value;
                    }
                }
            }
            """;

        await Verifier<HWK078_NoConcurrentDictionaryCheckThenActAnalyzer>
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
                TestState.Sources.Add(Stubs.ConcurrentCollections);
            }
        }
    }
}
