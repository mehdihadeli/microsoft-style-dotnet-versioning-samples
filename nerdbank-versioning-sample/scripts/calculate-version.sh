#!/usr/bin/env bash
set -euo pipefail

repository_path="${1:-.}"
nbgv_path="${2:-nbgv}"

cd "$repository_path"

"$nbgv_path" get-version -v SemVer2 >/dev/null

mapfile -t release_tags < <(
  git tag --points-at HEAD | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$' || true
)

if ((${#release_tags[@]} > 0)); then
  stable_tag="$(printf '%s\n' "${release_tags[@]}" | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | head -n 1 || true)"
  if [[ -n "$stable_tag" ]]; then
    printf '%s\n' "${stable_tag#v}"
    exit 0
  fi

  printf '%s\n' "${release_tags[@]}" | sort -V | tail -n 1 | sed 's/^v//'
  exit 0
fi

nearest_stable=""
nearest_distance=""
while IFS= read -r tag; do
  [[ "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]] || continue
  distance="$(git rev-list --first-parent --count "$tag..HEAD")"
  if [[ -z "$nearest_distance" || "$distance" -lt "$nearest_distance" ]]; then
    nearest_stable="$tag"
    nearest_distance="$distance"
  fi
done < <(git tag --merged HEAD)

if [[ -n "$nearest_stable" ]]; then
  stable_version="${nearest_stable#v}"
  IFS=. read -r major minor patch <<<"$stable_version"
  printf '%s.%s.%s-preview.%s\n' "$major" "$minor" "$((patch + 1))" "$nearest_distance"
  exit 0
fi

configured_version="$(sed -nE 's/.*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/p' version.json)"
if [[ ! "$configured_version" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)-preview\.\{height\}$ ]]; then
  echo "version.json must use '<major>.<minor>.<patch>-preview.{height}'." >&2
  exit 1
fi

major="${BASH_REMATCH[1]}"
minor="${BASH_REMATCH[2]}"
patch="${BASH_REMATCH[3]}"
offset="$(sed -nE 's/.*"versionHeightOffset"[[:space:]]*:[[:space:]]*(-?[0-9]+).*/\1/p' version.json)"
preview_number="$(( $(git rev-list --first-parent --count HEAD) + offset ))"
if ((preview_number < 1)); then
  echo "Calculated preview number must be at least 1." >&2
  exit 1
fi

printf '%s.%s.%s-preview.%s\n' "$major" "$minor" "$patch" "$preview_number"
