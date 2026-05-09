#!/usr/bin/env bash
# Installs the project's git hooks. Run once after cloning or after a
# fresh worktree. Idempotent — safe to re-run.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
HOOKS_DIR="$REPO_ROOT/.git/hooks"
[ -d "$HOOKS_DIR" ] || HOOKS_DIR="$(git -C "$REPO_ROOT" rev-parse --git-path hooks)"

cat > "$HOOKS_DIR/pre-push" <<'HOOK'
#!/usr/bin/env bash
# Pre-push gate. Runs the fast CI checks locally before allowing a push.
# Bypass once with `git push --no-verify` if you're absolutely sure.
set -e
REPO_ROOT="$(git rev-parse --show-toplevel)"

# Only run if pushing to origin/main (the deploy branch). Branch pushes
# don't gate so feature work isn't blocked.
while read -r local_ref local_sha remote_ref remote_sha; do
    if [ "$remote_ref" = "refs/heads/main" ]; then
        echo "[pre-push] pushing to main — running ci-check fast"
        if ! "$REPO_ROOT/scripts/stack.sh" ci-check fast; then
            echo "[pre-push] ci-check FAILED — push aborted. Fix locally or use --no-verify if you're sure."
            exit 1
        fi
    fi
done

exit 0
HOOK
chmod +x "$HOOKS_DIR/pre-push"
echo "Installed pre-push hook → $HOOKS_DIR/pre-push"
echo "  Will run 'stack.sh ci-check fast' before any push to origin/main."
echo "  Bypass with: git push --no-verify"
