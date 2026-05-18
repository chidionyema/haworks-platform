Check the latest CI run status.

Run: `gh run list --branch main --workflow CI --limit 3 --json databaseId,conclusion,status --jq '.[] | "#\(.databaseId) \(.conclusion // .status)"'`

Then for the latest run: `gh run view <id> --json jobs --jq '.jobs[] | "\(.name): \(.conclusion // .status)"'`
