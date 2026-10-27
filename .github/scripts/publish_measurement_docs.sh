#!/usr/bin/env bash
set -euo pipefail

case "${1:-}" in
  benchmark)
    paths=(docs/docs/benchmarks.md)
    message="docs: update benchmark results [skip ci]"
    ;;
  outage)
    # Publish the rendered page and its source measurement in one commit.
    paths=(docs/docs/outage-recovery.md docs/static/measurements/outage-results.json)
    message="docs: update outage measurements [skip ci]"
    ;;
  *)
    echo "Usage: $0 benchmark|outage" >&2
    exit 2
    ;;
esac

git add -- "${paths[@]}"
if git diff --cached --quiet -- "${paths[@]}"; then
  echo "Measurement docs unchanged; skipping commit and push."
else
  git config user.name "github-actions[bot]"
  git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
  git commit -m "$message" -- "${paths[@]}"
  # Same-page publishers are serialized by their job concurrency group. Rebase
  # only needs to retain unrelated main changes made since the fresh checkout.
  git pull --rebase origin main
  git push origin HEAD:main
fi

# A previous attempt may have committed the results but failed to deploy them.
# GITHUB_TOKEN pushes do not trigger docs.yml, so dispatch even without a commit.
run_url=$(gh workflow run docs.yml --ref main)
run_id="${run_url##*/}"
gh run watch "$run_id" --exit-status
