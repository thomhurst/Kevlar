#!/usr/bin/env bash
# Release tag helpers.
#
#   release-version.sh <tag>                     Prints the package version for a release tag, or
#                                                fails when the tag is not one.
#   release-version.sh --greater <tag> <tag2>    Succeeds when both are release tags and the first
#                                                has higher SemVer 2.0 precedence; fails otherwise
#                                                (lower, equal, or not a release tag).
#
# Grammar: an optional "v", then a SemVer 2.0 version without build metadata. Major, minor and
# patch have no leading zeros and are at most 65534, because they become AssemblyVersion and
# FileVersion parts. A prerelease is dot-separated, non-empty identifiers; numeric identifiers
# have no leading zeros. Accepted: v1.2.3, 1.2.3, v1.2.3-beta.1, v1.2.3-beta01, v1.2.3-0.
set -euo pipefail
export LC_ALL=C  # ASCII ordering for alphanumeric prerelease identifiers

number='(0|[1-9][0-9]*)'
identifier='(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
pattern="^v?${number}\.${number}\.${number}(-${identifier}(\.${identifier})*)?$"

# Validates $1 and sets major, minor, patch and prerelease (without the leading "-").
parse() {
  local tag="$1"
  if [[ ! "$tag" =~ $pattern ]]; then
    echo "::error::Release tag '$tag' is not a version. Use a tag such as v1.2.3 or v1.2.3-beta.1." >&2
    return 1
  fi

  major="${BASH_REMATCH[1]}"
  minor="${BASH_REMATCH[2]}"
  patch="${BASH_REMATCH[3]}"
  prerelease="${BASH_REMATCH[4]#-}"
  local part
  for part in "$major" "$minor" "$patch"; do
    if [ "${#part}" -gt 5 ] || [ "$part" -gt 65534 ]; then
      echo "::error::Release tag '$tag' has a version part above 65534, the largest assembly version part." >&2
      return 1
    fi
  done
}

# Prints -1, 0 or 1 comparing two prerelease identifiers by SemVer 2.0 precedence.
compare_identifier() {
  local a="$1" b="$2"
  local a_numeric=false b_numeric=false
  [[ "$a" =~ ^[0-9]+$ ]] && a_numeric=true
  [[ "$b" =~ ^[0-9]+$ ]] && b_numeric=true
  if $a_numeric && $b_numeric; then
    # No leading zeros, so a longer number is larger; equal lengths compare as text.
    if [ "${#a}" -ne "${#b}" ]; then
      [ "${#a}" -lt "${#b}" ] && echo -1 || echo 1
    elif [ "$a" = "$b" ]; then
      echo 0
    else
      [[ "$a" < "$b" ]] && echo -1 || echo 1
    fi
  elif $a_numeric; then
    echo -1
  elif $b_numeric; then
    echo 1
  elif [ "$a" = "$b" ]; then
    echo 0
  else
    [[ "$a" < "$b" ]] && echo -1 || echo 1
  fi
}

# Prints -1, 0 or 1 comparing two release tags by SemVer 2.0 precedence.
compare() {
  parse "$1"
  local a_major="$major" a_minor="$minor" a_patch="$patch" a_pre="$prerelease"
  parse "$2"
  local b_major="$major" b_minor="$minor" b_patch="$patch" b_pre="$prerelease"

  local i
  for i in 0 1 2; do
    local -a a_core=("$a_major" "$a_minor" "$a_patch") b_core=("$b_major" "$b_minor" "$b_patch")
    if [ "${a_core[$i]}" -lt "${b_core[$i]}" ]; then echo -1; return; fi
    if [ "${a_core[$i]}" -gt "${b_core[$i]}" ]; then echo 1; return; fi
  done

  # A version without a prerelease ranks above the same version with one.
  if [ -z "$a_pre" ] && [ -z "$b_pre" ]; then echo 0; return; fi
  if [ -z "$a_pre" ]; then echo 1; return; fi
  if [ -z "$b_pre" ]; then echo -1; return; fi

  local -a a_ids b_ids
  IFS=. read -r -a a_ids <<< "$a_pre"
  IFS=. read -r -a b_ids <<< "$b_pre"
  local count=${#a_ids[@]}
  [ "${#b_ids[@]}" -lt "$count" ] && count=${#b_ids[@]}
  for ((i = 0; i < count; i++)); do
    local result
    result=$(compare_identifier "${a_ids[$i]}" "${b_ids[$i]}")
    if [ "$result" != 0 ]; then echo "$result"; return; fi
  done

  # All shared identifiers are equal: the longer list ranks higher.
  if [ "${#a_ids[@]}" -lt "${#b_ids[@]}" ]; then echo -1
  elif [ "${#a_ids[@]}" -gt "${#b_ids[@]}" ]; then echo 1
  else echo 0
  fi
}

if [ "${1-}" = "--greater" ]; then
  result=$(compare "${2-}" "${3-}") || exit 1
  [ "$result" = 1 ]
  exit
fi

tag="${1-}"
parse "$tag"
echo "${tag#v}"
