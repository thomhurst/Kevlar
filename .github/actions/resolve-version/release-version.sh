#!/usr/bin/env bash
# Prints the package version for a release tag, or fails when the tag is not one.
#
# Grammar: an optional "v", then a SemVer 2.0 version without build metadata. Major, minor and
# patch have no leading zeros and are at most 65534, because they become AssemblyVersion and
# FileVersion parts. A prerelease is dot-separated, non-empty identifiers; numeric identifiers
# have no leading zeros. Accepted: v1.2.3, 1.2.3, v1.2.3-beta.1, v1.2.3-beta01, v1.2.3-0.
set -euo pipefail

tag="${1-}"
number='(0|[1-9][0-9]*)'
identifier='(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
pattern="^v?${number}\.${number}\.${number}(-${identifier}(\.${identifier})*)?$"

if [[ ! "$tag" =~ $pattern ]]; then
  echo "::error::Release tag '$tag' is not a version. Use a tag such as v1.2.3 or v1.2.3-beta.1." >&2
  exit 1
fi

for part in "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}" "${BASH_REMATCH[3]}"; do
  if [ "${#part}" -gt 5 ] || [ "$part" -gt 65534 ]; then
    echo "::error::Release tag '$tag' has a version part above 65534, the largest assembly version part." >&2
    exit 1
  fi
done

echo "${tag#v}"
