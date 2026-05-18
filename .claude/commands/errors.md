Find all build errors in the solution. Fast diagnostic.

Run: `dotnet build HaworksPlatform.sln -v q --nologo 2>&1 | grep "error " | sed 's/.*error //' | sort | uniq -c | sort -rn | head -20`

Report the count and unique error types.
