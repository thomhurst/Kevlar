#!/usr/bin/env bash
# Runs the "Update draft release" step of .github/workflows/release-draft.yml against stubbed gh
# and git commands, and checks which release writes it makes. Needs bash >= 4.4.
set -euo pipefail
shopt -s inherit_errexit

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin" "$work/repo/.github/actions/resolve-version"
cp "$repository_root/.github/actions/resolve-version/release-version.sh" "$work/repo/.github/actions/resolve-version/"

# The step's script is the block under the only `run: |` in the workflow.
awk '/^        run: \|/{f=1;next} f{sub(/^          /,""); print}' \
  "$repository_root/.github/workflows/release-draft.yml" > "$work/draft.sh"
if ! grep -q 'generate-notes' "$work/draft.sh"; then
  echo "Could not extract the draft step from release-draft.yml." >&2
  exit 1
fi

cat > "$work/bin/git" <<'EOF'
#!/usr/bin/env bash
ref="${*: -1}"
if [ "$ref" = refs/heads/main ]; then
  if [ -n "${MAIN_FAIL-}" ]; then echo "fatal: unable to access origin" >&2; exit 128; fi
  printf '%s\trefs/heads/main\n' "$MAIN_TIP"
  exit 0
fi
tag="${ref#refs/tags/}"
if grep -qxF "$tag" "$SIM/tags" || [ "$tag" = "${EXTRA_TAG-}" ]; then exit 0; fi
exit 2
EOF

cat > "$work/bin/gh" <<'EOF'
#!/usr/bin/env bash
echo "gh $*" >> "$SIM/calls"
case "$*" in
  *"releases?per_page=100"*) if [ -n "${DRAFT_TAG-}" ]; then echo 42; fi ;;
  *"releases/42 --jq .tag_name"*) echo "$DRAFT_TAG" ;;
  *"releases/42 --jq .draft"*) echo true ;;
  *"releases/latest"*) echo v1.5.1 ;;
  *generate-notes*)
    if [ -n "${NOTES_FAIL-}" ]; then echo "gh: Server Error (HTTP 502)" >&2; exit 1; fi
    echo notes ;;
  *"-X PATCH"*|*"-X POST repos/o/r/releases "*) echo https://example.invalid/release ;;
  *) echo "unexpected gh call: $*" >&2; exit 1 ;;
esac
EOF
chmod +x "$work/bin/git" "$work/bin/gh"

failures=0

# scenario <name> <expected exit> <expected write: none|create|update-suggested|update-kept> [VAR=value...]
scenario() {
  local name="$1" expected_exit="$2" expected_write="$3"
  shift 3
  printf '%s\n' v1.5.0 v1.5.1 > "$work/tags"
  : > "$work/calls"
  : > "$work/summary"
  local exit_code=0
  (cd "$work/repo" && env SIM="$work" MAIN_TIP=abc DRAFT_TAG= "$@" PATH="$work/bin:$PATH" \
    GITHUB_REPOSITORY=o/r GITHUB_SHA=abc VERSION=1.5.2 DRAFT_MARKER='<!-- m -->' \
    GITHUB_STEP_SUMMARY="$work/summary" bash "$work/draft.sh" > /dev/null 2>&1) || exit_code=$?

  local write=none
  if grep -q -- '-X POST repos/o/r/releases ' "$work/calls"; then
    write=create
  elif grep -q -- '-X PATCH .*-f tag_name=v1.5.2' "$work/calls"; then
    write=update-suggested
  elif grep -q -- '-X PATCH' "$work/calls"; then
    write=update-kept
  fi

  local exit_ok=true
  if [ "$expected_exit" = 0 ]; then [ "$exit_code" -eq 0 ] || exit_ok=false; else [ "$exit_code" -ne 0 ] || exit_ok=false; fi
  if $exit_ok && [ "$write" = "$expected_write" ]; then
    echo "ok: $name"
  else
    echo "FAIL: $name (exit $exit_code, write $write; expected exit $expected_exit, write $expected_write)" >&2
    failures=$((failures + 1))
  fi
}

scenario 'creates a draft' 0 create
scenario 'updates the draft to the suggestion' 0 update-suggested DRAFT_TAG=v1.5.2
scenario 'resets a lower tag' 0 update-suggested DRAFT_TAG=v1.5.0
scenario 'resets an invalid tag' 0 update-suggested DRAFT_TAG=release-1
scenario 'keeps a higher tag' 0 update-kept DRAFT_TAG=v1.6.0
scenario 'keeps a higher prerelease tag' 0 update-kept DRAFT_TAG=v2.0.0-beta.1
scenario 'resets a higher tag that is already released' 0 update-suggested DRAFT_TAG=1.6.0 EXTRA_TAG=1.6.0
scenario 'notes failure writes nothing on create' 1 none NOTES_FAIL=1
scenario 'notes failure writes nothing on update' 1 none NOTES_FAIL=1 DRAFT_TAG=v1.5.2
scenario 're-run of an older main commit writes nothing' 0 none MAIN_TIP=def DRAFT_TAG=v1.5.2
scenario 'main lookup failure writes nothing' 1 none MAIN_FAIL=1 DRAFT_TAG=v1.5.2

if [ "$failures" -gt 0 ]; then
  exit 1
fi
echo "Draft release step scenarios passed."
