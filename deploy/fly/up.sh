#!/usr/bin/env bash
# One-command deploy: checks prerequisites, copies .env.example if missing,
# prompts for the few required values, then runs bootstrap + deploy in sequence.
#
# Usage: deploy/fly/up.sh

set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ENV_DIR="$ROOT_DIR/deploy/fly"
ENV_FILE="$ENV_DIR/.env.local"
EXAMPLE="$ENV_DIR/.env.example"

bold()  { printf '\033[1m%s\033[0m\n' "$*"; }
warn()  { printf '\033[33m%s\033[0m\n' "$*" >&2; }
fatal() { printf '\033[31m%s\033[0m\n' "$*" >&2; exit 1; }

bold "==> 1/5  Checking prerequisites"
command -v flyctl  >/dev/null || fatal "flyctl not installed. https://fly.io/docs/flyctl/install/"
command -v openssl >/dev/null || fatal "openssl not installed (needed to generate JWT key)."
flyctl auth whoami >/dev/null 2>&1 || fatal "Not logged in to fly. Run: flyctl auth login"

bold "==> 2/5  Preparing .env.local"
if [[ ! -f "$ENV_FILE" ]]; then
  cp "$EXAMPLE" "$ENV_FILE"
  warn "Created $ENV_FILE from template."
  warn "Open it now and fill in: RABBITMQ_URL, REDIS_URL, POSTGRES_BASE."
  warn "JWT_SIGNING_KEY_PEM auto-generates on first bootstrap run."
  warn ""
  warn "Press Enter once filled in (Ctrl-C to abort)."
  read -r _
fi

# Sanity-check the three things bootstrap needs.
# shellcheck disable=SC1090
set -a; source "$ENV_FILE"; set +a
missing=()
[[ -z "${RABBITMQ_URL:-}"  || "$RABBITMQ_URL"  == amqps://USER:PASS@*       ]] && missing+=(RABBITMQ_URL)
[[ -z "${REDIS_URL:-}"     || "$REDIS_URL"     == rediss://default:TOKEN@*  ]] && missing+=(REDIS_URL)
[[ -z "${POSTGRES_BASE:-}" || "$POSTGRES_BASE" == postgres://default:PASS@* ]] && missing+=(POSTGRES_BASE)
if [[ ${#missing[@]} -gt 0 ]]; then
  fatal "Still placeholder values in $ENV_FILE: ${missing[*]}"
fi

bold "==> 3/5  Bootstrapping Fly apps + secrets"
"$ENV_DIR/bootstrap.sh" "$ENV_FILE"

bold "==> 4/5  Deploying services"
"$ENV_DIR/deploy.sh"

bold "==> 5/5  Post-deploy summary"
echo
for svc in identity catalog orders payments checkout bffweb; do
  app="ritualworks-$svc"
  status=$(flyctl status -a "$app" --json 2>/dev/null | grep -o '"Status":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "unknown")
  printf '  %-28s %s\n' "$app" "$status"
done

if [[ "${DEPLOY_CONTENT:-false}" == "true" ]]; then
  status=$(flyctl status -a ritualworks-content --json 2>/dev/null | grep -o '"Status":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "unknown")
  printf '  %-28s %s\n' "ritualworks-content" "$status"
fi

echo
bold "Public URL: https://ritualworks-bffweb.fly.dev"
echo "Logs:   flyctl logs -a ritualworks-<svc>"
echo "Roll back a service: flyctl releases rollback -a ritualworks-<svc>"
