#!/usr/bin/env bash
set -euo pipefail

if git diff --quiet docs/docs/stress-tests.md; then
  echo "Stress page unchanged; skipping commit."
else
  git config user.name "github-actions[bot]"
  git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
  git add docs/docs/stress-tests.md
  git commit -m "docs: update stress results [skip ci]"
  git pull --rebase origin main
  git push origin HEAD:main
fi

# A previous attempt may have committed the page but failed during deployment.
# GITHUB_TOKEN pushes do not trigger docs.yml, so dispatch even for an unchanged page.
run_url=$(gh workflow run docs.yml --ref main)
run_id="${run_url##*/}"
gh run watch "$run_id" --exit-status
