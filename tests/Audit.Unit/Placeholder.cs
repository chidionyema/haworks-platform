// L0 ships an empty test project so the .sln + csproj build clean.
// L1.A populates `Extraction/` and `Redaction/` test directories;
// L1.C may add `Queries/` if any pure-unit tests fit (most query tests
// are integration). Do NOT add tests here at the project root — keep
// them under feature subfolders mirroring the production code.
namespace Haworks.Audit.Unit;

internal static class Placeholder
{
    // Intentionally empty. Delete this file once L1.A has landed real tests.
}
