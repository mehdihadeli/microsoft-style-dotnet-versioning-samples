#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
props="Directory.Build.props"
version_prefix="$(sed -n 's:.*<VersionPrefix[^>]*>\([^<]*\)</VersionPrefix>.*:\1:p' "$props" | head -n 1)"
release_phase="$(sed -n 's:.*<ReleasePhase[^>]*>\([^<]*\)</ReleasePhase>.*:\1:p' "$props" | head -n 1)"

output_mode="${1:-version}"
if [[ "$output_mode" == "github-output" ]]; then
  shift
fi

release_tag="$(git tag --points-at HEAD | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$' | sort -V | tail -n 1 || true)"
if [[ -n "$release_tag" ]]; then
  version="${release_tag#v}"
  tag_core="${version%%-rc.*}"
  if [[ "$tag_core" != "$version_prefix" ]]; then
    echo "Release tag $version does not match Arcade VersionPrefix $version_prefix." >&2
    exit 1
  fi
  if [[ "$release_phase" == "rc" && "$version" != *-rc.* ]]; then
    echo "Release tag $version does not match RC release intent." >&2
    exit 1
  fi
  if [[ "$release_phase" == "stable" && "$version" == *-rc.* ]]; then
    echo "Release tag $version does not match stable release intent." >&2
    exit 1
  fi
elif [[ "$release_phase" == "rc" ]]; then
  highest=0
  while IFS= read -r tag; do
    [[ "$tag" =~ -rc\.([0-9]+)$ ]] || continue
    if (( BASH_REMATCH[1] > highest )); then highest="${BASH_REMATCH[1]}"; fi
  done < <(git tag --merged HEAD --list "v$version_prefix-rc.*")
  version="$version_prefix-rc.$((highest + 1))"
elif [[ "$release_phase" == "stable" ]]; then
  version="$version_prefix"
else
  train_commit="$(git log -1 --format=%H -- "$props")"
  initial_commit="$(git rev-list --max-parents=0 HEAD | head -n 1)"
  height="$(git rev-list --count "$train_commit..HEAD")"
  if [[ "$train_commit" != "$initial_commit" ]]; then height="$((height + 1))"; fi
  version="$version_prefix-preview.${GITHUB_RUN_NUMBER:-$height}"
fi

if [[ "$output_mode" != "github-output" ]]; then
  printf '%s\n' "$version"
  exit 0
fi

commit="$(git rev-parse --short HEAD)"
assembly_version="${version%%-*}"
environment="none"
if [[ "${GITHUB_REF:-}" =~ ^refs/tags/v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$ ]]; then
  ref_version="${GITHUB_REF_NAME#v}"
  if [[ "$ref_version" != "$version" ]]; then
    echo "Release ref $ref_version does not match Arcade version $version." >&2
    exit 1
  fi
  if [[ "$ref_version" == *-rc.* ]]; then
    environment="staging"
  else
    environment="production"
  fi
elif [[ "${GITHUB_REF:-}" == "refs/heads/main" && "$version" == *-preview.* ]]; then
  environment="dev"
elif [[ "${GITHUB_REF:-}" == refs/tags/* ]]; then
  echo "Unsupported release tag: ${GITHUB_REF_NAME:-${GITHUB_REF#refs/tags/}}" >&2
  exit 1
fi

printf 'version=%s\n' "$version"
printf 'assembly_version=%s\n' "$assembly_version"
printf 'informational_version=%s+%s\n' "$version" "$commit"
printf 'environment=%s\n' "$environment"
printf 'commit=%s\n' "$commit"
