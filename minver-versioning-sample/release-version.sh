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

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
version_file="$repository_root/version.env"

set_value() {
  local name="$1"
  local value="$2"
  local temporary_file
  temporary_file="$(mktemp)"
  awk -v name="$name" -v value="$value" '
    index($0, name "=") == 1 { print name "=" value; next }
    { print }
  ' "$version_file" > "$temporary_file"
  mv "$temporary_file" "$version_file"
}

set_release_intent() {
  local phase="$1"
  local version="$2"
  local major_minor="${version%.*}"
  set_value MINVER_MINIMUM_MAJOR_MINOR "$major_minor"
  set_value MINVER_RELEASE_VERSION "$version"
  set_value MINVER_RELEASE_PHASE "$phase"
}

next_rc_number() {
  local version="$1"
  local highest=0
  local tag
  while IFS= read -r tag; do
    [[ "$tag" =~ -rc\.([0-9]+)$ ]] || continue
    (( BASH_REMATCH[1] > highest )) && highest="${BASH_REMATCH[1]}"
  done < <(git tag --list "v$version-rc.*")
  printf '%s\n' "$((highest + 1))"
}

tag_release() {
  # shellcheck source=version.env
  source "$version_file"
  local tag
  case "$MINVER_RELEASE_PHASE" in
    rc) tag="v$MINVER_RELEASE_VERSION-rc.$(next_rc_number "$MINVER_RELEASE_VERSION")" ;;
    stable) tag="v$MINVER_RELEASE_VERSION" ;;
    *) echo "Cannot tag a $MINVER_RELEASE_PHASE release intent." >&2; exit 1 ;;
  esac
  git tag -a "$tag" -m "$tag"
  echo "Created $tag. Push it with: git push origin $tag"
}

case "${1:-}" in
  prepare-train)
    [[ $# -eq 2 ]] || { usage >&2; exit 1; }
    set_release_intent preview "$2"
    ;;
  prepare-rc)
    [[ $# -eq 2 ]] || { usage >&2; exit 1; }
    set_release_intent rc "$2"
    ;;
  prepare-stable)
    [[ $# -eq 2 ]] || { usage >&2; exit 1; }
    set_release_intent stable "$2"
    ;;
  tag)
    [[ $# -eq 1 ]] || { usage >&2; exit 1; }
    tag_release
    ;;
  *) usage >&2; exit 1 ;;
esac