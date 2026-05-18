using Xunit;
namespace Haworks.Analyzers.Unit;
public class DiagnosticsTests
{
    [Fact]
    public void All_diagnostic_descriptors_have_unique_ids()
    {
        var type = typeof(Haworks.Architecture.Analyzers.Diagnostics);
        var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Microsoft.CodeAnalysis.DiagnosticDescriptor))
            .Select(f => ((Microsoft.CodeAnalysis.DiagnosticDescriptor)f.GetValue(null)!).Id)
            .ToList();
        Assert.Equal(fields.Count, fields.Distinct().Count());
        Assert.True(fields.Count > 10, $"Expected >10 analyzers, found {fields.Count}");
    }
}
