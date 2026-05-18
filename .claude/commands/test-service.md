Run unit tests for a specific service.

Usage: /test-service identity

Run: `dotnet test tests/$ARGUMENTS/$ARGUMENTS.Unit/$ARGUMENTS.Unit.csproj --configuration Release --logger "console;verbosity=minimal"`

Convert the argument to PascalCase first (e.g., checkout-orchestrator → CheckoutOrchestrator).
