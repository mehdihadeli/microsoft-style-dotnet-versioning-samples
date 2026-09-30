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

if [[ $# -lt 1 ]] || ! command -v dotnet >/dev/null 2>&1; then
  usage >&2
  exit 1
fi

nbgv() {
  if [[ -n "${NBGV_PATH:-}" ]]; then
    "$NBGV_PATH" "$@"
  else
    dotnet nbgv "$@"
  fi
}

clear_version_height_offset() {
  local temporary_file

  temporary_file="$(mktemp)"
  awk '
    /"versionHeightOffset":/ { next }
    /"versionHeightOffsetAppliesTo":/ { next }
    { print }
  ' version.json > "$temporary_file"
  mv "$temporary_file" version.json
}

prepare_train() {
  nbgv set-version "$1-preview.{height}"
  clear_version_height_offset
  echo "Updated version.json to $1-preview.{height}. Commit and push this release-train change."
}

prepare_rc() {
  nbgv set-version "$1-rc.{height}"
  echo "Updated version.json to $1-rc.{height}. Commit and push this RC change."
}

prepare_stable() {
  nbgv set-version "$1"
  clear_version_height_offset
  echo "Updated version.json to $1. Commit and push this stable release change."
}

tag_release() {
  nbgv tag
  echo "Created the NBGV tag. Push it with: git push origin <tag-name>"
}

case "$1" in
  prepare-train|prepare-rc|prepare-stable)
    [[ $# -eq 2 ]] || { usage >&2; exit 1; }
    "${1//-/_}" "$2"
    ;;
  tag)
    [[ $# -eq 1 ]] || { usage >&2; exit 1; }
    tag_release
    ;;
  *)
    usage >&2
    exit 1
    ;;
esac