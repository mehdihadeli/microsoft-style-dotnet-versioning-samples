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
intent="$root/release.env"

set_value() {
  local name="$1" value="$2" temporary_file
  temporary_file="$(mktemp)"
  awk -v name="$name" -v value="$value" '
    index($0, name "=") == 1 { print name "=" value; next }
    { print }
  ' "$intent" > "$temporary_file"
  mv "$temporary_file" "$intent"
}

prepare() {
  set_value SEMANTIC_RELEASE_VERSION "$2"
  set_value SEMANTIC_RELEASE_PHASE "$1"
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
  # shellcheck source=release.env
  source "$intent"
  local tag
  case "$SEMANTIC_RELEASE_PHASE" in
    rc) tag="v$SEMANTIC_RELEASE_VERSION-rc.$(next_rc_number "$SEMANTIC_RELEASE_VERSION")" ;;
    stable) tag="v$SEMANTIC_RELEASE_VERSION" ;;
    *) echo "Cannot tag a $SEMANTIC_RELEASE_PHASE release intent." >&2; exit 1 ;;
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