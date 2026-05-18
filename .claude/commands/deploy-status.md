Check the latest Deploy workflow status.

Run: `gh run list --branch main --workflow Deploy --limit 3 --json databaseId,conclusion,status --jq '.[] | "#\(.databaseId) \(.conclusion // .status)"'`

Then show job details for the latest run.
