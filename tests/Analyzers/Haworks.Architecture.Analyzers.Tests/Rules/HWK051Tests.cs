using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK051Tests
{
    [Fact]
    public async Task SaveChanges_Then_Publish_Without_Transaction_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            public class MyService
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task DoWork()
                {
                    await {|#0:_db.SaveChangesAsync()|};
                    await Publish(new object());
                }
                private Task Publish(object msg) => Task.CompletedTask;
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK051_NoSaveChangesBeforePublishWithoutTransactionAnalyzer>
            .Diagnostic(Diagnostics.NoSaveChangesBeforePublishWithoutTransaction)
            .WithLocation(0);
        await CSharpAnalyzerVerifier<HWK051_NoSaveChangesBeforePublishWithoutTransactionAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Publish_Then_SaveChanges_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            public class MyService
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task DoWork()
                {
                    await Publish(new object());
                    await _db.SaveChangesAsync();
                }
                private Task Publish(object msg) => Task.CompletedTask;
            }
            """;
        await CSharpAnalyzerVerifier<HWK051_NoSaveChangesBeforePublishWithoutTransactionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SaveChanges_Then_Publish_With_Transaction_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            public class MyService
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task DoWork()
                {
                    await _db.Database.BeginTransactionAsync();
                    await _db.SaveChangesAsync();
                    await Publish(new object());
                }
                private Task Publish(object msg) => Task.CompletedTask;
            }
            """;
        await CSharpAnalyzerVerifier<HWK051_NoSaveChangesBeforePublishWithoutTransactionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
