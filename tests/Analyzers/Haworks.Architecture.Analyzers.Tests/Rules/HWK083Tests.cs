using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK083Tests
{
    [Fact]
    public async Task Command_Without_Validator_Reports()
    {
        const string source = """
            using MediatR;
            public record {|#0:CreateOrderCommand|}(string UserId) : IRequest<System.Guid>;
            """;

        var expected = Verifier<HWK083_NoCommandWithoutValidatorAnalyzer>
            .Diagnostic(Diagnostics.NoCommandWithoutValidator)
            .WithLocation(0)
            .WithArguments("CreateOrderCommand");

        await Verifier<HWK083_NoCommandWithoutValidatorAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Command_With_Validator_NoDiagnostic()
    {
        const string source = """
            using MediatR;
            using FluentValidation;
            public record CreateOrderCommand(string UserId) : IRequest<System.Guid>;
            public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand> { }
            """;

        await Verifier<HWK083_NoCommandWithoutValidatorAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Query_Command_Without_Validator_NoDiagnostic()
    {
        const string source = """
            using MediatR;
            public record GetOrderCommand(System.Guid Id) : IRequest<string>;
            """;

        await Verifier<HWK083_NoCommandWithoutValidatorAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NonCommand_Class_NoDiagnostic()
    {
        const string source = """
            public class OrderService
            {
                public void DoWork() { }
            }
            """;

        await Verifier<HWK083_NoCommandWithoutValidatorAnalyzer>
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
                TestState.Sources.Add(Stubs.MediatR);
                TestState.Sources.Add(Stubs.FluentValidation);
            }
        }
    }
}
