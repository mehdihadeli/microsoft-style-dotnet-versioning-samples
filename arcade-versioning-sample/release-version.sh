#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  ./release-version.sh prepare-train <major.minor.patch>
  ./release-version.sh prepare-rc <major.minor.patch>
  ./release-version.sh prepare-stable <major.minor.patch>
  ./release-version.sh tag
EOF
}

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
props="$root/Directory.Build.props"

property() {
  sed -n "s:.*<$1[^>]*>\([^<]*\)</$1>.*:\1:p" "$props" | head -n 1
}

set_property() {
  local name="$1" value="$2" temporary_file
  temporary_file="$(mktemp)"
  sed -E "s:<${name}([^>]*)>[^<]*</${name}>:<${name}\\1>${value}</${name}>:" "$props" > "$temporary_file"
  mv "$temporary_file" "$props"
}

prepare() {
  set_property VersionPrefix "$2"
  set_property ReleasePhase "$1"
}

next_rc_number() {
  local version="$1" highest=0 tag
  while IFS= read -r tag; do
    [[ "$tag" =~ -rc\.([0-9]+)$ ]] || continue
    if (( BASH_REMATCH[1] > highest )); then highest="${BASH_REMATCH[1]}"; fi
  done < <(git tag --list "v$version-rc.*")
  printf '%s\n' "$((highest + 1))"
}

tag_release() {
  local version phase tag
  version="$(property VersionPrefix)"
  phase="$(property ReleasePhase)"
  case "$phase" in
    rc) tag="v$version-rc.$(next_rc_number "$version")" ;;
    stable) tag="v$version" ;;
    *) echo "Cannot tag a $phase release intent." >&2; exit 1 ;;
  esac
  git tag -a "$tag" -m "$tag"
  echo "Created $tag. Push it with: git push origin $tag"
}

case "${1:-}" in
  prepare-train) [[ $# -eq 2 ]] || { usage >&2; exit 1; }; prepare preview "$2" ;;
  prepare-rc) [[ $# -eq 2 ]] || { usage >&2; exit 1; }; prepare rc "$2" ;;
  prepare-stable) [[ $# -eq 2 ]] || { usage >&2; exit 1; }; prepare stable "$2" ;;
  tag) [[ $# -eq 1 ]] || { usage >&2; exit 1; }; tag_release ;;
  *) usage >&2; exit 1 ;;
esac