#!/usr/bin/env bash
# Migration drift check — catches DbContext model properties not reflected in migrations.
#
# Runs `dotnet ef migrations has-pending-model-changes` for every service that
# has an EF Core DbContext. Prevents the class of bug where a property is added
# to the model but no migration is generated, causing runtime 42703/42P01 errors
# in production.
#
# Exit codes:
#   0   all services in sync
#   1   at least one service has pending model changes

set -euo pipefail

REPO_ROOT=${1:-$(git rev-parse --show-toplevel)}
cd "$REPO_ROOT"

# Each entry: "startup-project|infrastructure-project|context-name"
SERVICES=(
  "src/Audit/Audit.Api|src/Audit/Audit.Infrastructure|AuditDbContext"
  "src/Catalog/Catalog.Api|src/Catalog/Catalog.Infrastructure|CatalogDbContext"
  "src/CheckoutOrchestrator/CheckoutOrchestrator.Api|src/CheckoutOrchestrator/CheckoutOrchestrator.Infrastructure|CheckoutDbContext"
  "src/Identity/Identity.Api|src/Identity/Identity.Infrastructure|AppIdentityDbContext"
  "src/Location/Location.Api|src/Location/Location.Infrastructure|LocationDbContext"
  "src/Media/Media.Api|src/Media/Media.Api|MediaDbContext"
  "src/Merchant/Merchant.Api|src/Merchant/Merchant.Infrastructure|MerchantDbContext"
  "src/Notifications/Notifications.Api|src/Notifications/Notifications.Infrastructure|NotificationsDbContext"
  "src/Orders/Orders.Api|src/Orders/Orders.Infrastructure|OrderDbContext"
  "src/Payments/Payments.Api|src/Payments/Payments.Infrastructure|PaymentDbContext"
  "src/Payouts/Payouts.Api|src/Payouts/Payouts.Infrastructure|PayoutsDbContext"
  "src/Pricing/Pricing.Api|src/Pricing/Pricing.Infrastructure|PricingDbContext"
  "src/Privacy/Privacy.Api|src/Privacy/Privacy.Infrastructure|PrivacyDbContext"
  "src/RulesEngine/RulesEngine.Api|src/RulesEngine/RulesEngine.Api|RulesDbContext"
  "src/Scheduler/Scheduler.Api|src/Scheduler/Scheduler.Infrastructure|SchedulerDbContext"
  "src/Shipping/Shipping.Api|src/Shipping/Shipping.Api|ShippingDbContext"
  "src/Webhooks/Webhooks.Api|src/Webhooks/Webhooks.Infrastructure|WebhooksDbContext"
)

FAILED=()
PASSED=0

for entry in "${SERVICES[@]}"; do
  IFS='|' read -r STARTUP INFRA CONTEXT <<< "$entry"
  SVC_NAME=$(echo "$STARTUP" | sed 's|src/||;s|/.*||')

  if ! dotnet ef migrations has-pending-model-changes \
      --startup-project "$STARTUP" \
      --project "$INFRA" \
      --context "$CONTEXT" \
      -- --environment Test > /dev/null 2>&1; then
    echo "DRIFT: $SVC_NAME ($CONTEXT) — model has changes not reflected in migrations"
    FAILED+=("$SVC_NAME")
  else
    PASSED=$((PASSED + 1))
  fi
done

echo ""
echo "Migration drift check: ${PASSED} OK, ${#FAILED[@]} DRIFT"

if [[ ${#FAILED[@]} -gt 0 ]]; then
  echo ""
  echo "Services with pending model changes:"
  for svc in "${FAILED[@]}"; do
    echo "  - $svc"
  done
  echo ""
  echo "Fix: run 'dotnet ef migrations add <Name> --startup-project <Api> --project <Infra> -- --environment Test'"

  # Hard-fail mode: set MIGRATION_DRIFT_STRICT=1 to block CI.
  # Default: warn-only until all existing drift is resolved.
  if [[ "${MIGRATION_DRIFT_STRICT:-1}" == "1" ]]; then
    exit 1
  else
    echo ""
    echo "⚠ Running in warn-only mode (set MIGRATION_DRIFT_STRICT=1 to hard-fail)"
    exit 0
  fi
fi
