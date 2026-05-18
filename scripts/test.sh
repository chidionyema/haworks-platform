#!/usr/bin/env bash
# Quick local test runner for a single service.
# Usage: ./scripts/test.sh identity       # unit + integration
#        ./scripts/test.sh identity unit  # unit only
set -euo pipefail

SERVICE="${1:?Usage: $0 <service> [unit|integration|all]}"
MODE="${2:-all}"

# Normalize to PascalCase for filter path matching
SVC_PASCAL=$(echo "$SERVICE" | sed -r 's/(^|-)(\w)/\U\2/g')

case "$MODE" in
  unit)
    echo ">>> Running unit tests for $SVC_PASCAL"
    dotnet test "tests/$SVC_PASCAL/$SVC_PASCAL.Unit/$SVC_PASCAL.Unit.csproj" --configuration Release --logger "console;verbosity=minimal"
    ;;
  integration)
    echo ">>> Running integration tests for $SVC_PASCAL (requires Docker)"
    dotnet test "tests/$SVC_PASCAL/$SVC_PASCAL.Integration/$SVC_PASCAL.Integration.csproj" --configuration Release --logger "console;verbosity=minimal"
    ;;
  all|*)
    echo ">>> Building filter for $SVC_PASCAL"
    FILTER="filters/$SVC_PASCAL.slnf"
    if [[ -f "$FILTER" ]]; then
      dotnet build "$FILTER" --configuration Release
    else
      echo "No solution filter found at $FILTER — building full solution"
      dotnet build HaworksPlatform.sln --configuration Release
    fi
    echo ">>> Running all tests for $SVC_PASCAL"
    for proj in tests/$SVC_PASCAL/$SVC_PASCAL.*/*.csproj tests/$SVC_PASCAL.*/*.csproj; do
      [[ -f "$proj" ]] && dotnet test "$proj" --no-build --configuration Release --logger "console;verbosity=minimal" || true
    done
    ;;
esac
