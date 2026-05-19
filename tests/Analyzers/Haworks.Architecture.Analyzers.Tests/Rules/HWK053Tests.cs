using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK053Tests
{
    [Fact]
    public async Task Financial_Service_SaveChanges_Without_Transaction_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            public class PaymentService
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task {|#0:ProcessPayment|}()
                {
                    await _db.SaveChangesAsync();
                }
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK053_FinancialWritesMustUseTransactionAnalyzer>
            .Diagnostic(Diagnostics.FinancialWritesMustUseTransaction)
            .WithLocation(0)
            .WithArguments("ProcessPayment");
        await CSharpAnalyzerVerifier<HWK053_FinancialWritesMustUseTransactionAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Financial_Service_With_Transaction_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            public class PaymentService
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task ProcessPayment()
                {
                    await _db.Database.BeginTransactionAsync();
                    await _db.SaveChangesAsync();
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK053_FinancialWritesMustUseTransactionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NonFinancial_Service_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            public class NotificationService
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task Send()
                {
                    await _db.SaveChangesAsync();
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK053_FinancialWritesMustUseTransactionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
