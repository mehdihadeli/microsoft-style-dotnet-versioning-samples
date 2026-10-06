#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
# shellcheck source=../release.env
source release.env

output_mode="${1:-version}"
if [[ "$output_mode" == "github-output" ]]; then
  shift
fi

train_commit="$(git log -1 --format=%H -- release.env)"
initial_commit="$(git rev-list --max-parents=0 HEAD | head -n 1)"
height="$(git rev-list --count "$train_commit..HEAD")"
if [[ "$train_commit" != "$initial_commit" ]]; then height="$((height + 1))"; fi

release_tag="$(git tag --points-at HEAD | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$' | sort -V | tail -n 1 || true)"
if [[ "$release_tag" == "v0.0.0" && "$SEMANTIC_RELEASE_PHASE" == "preview" ]]; then
  # The bootstrap tag anchors the empty repository; the committed release intent
  # is the first real development line.
  version="$SEMANTIC_RELEASE_VERSION-preview.${GITHUB_RUN_NUMBER:-$height}"
elif [[ "${GITHUB_REF:-}" =~ ^refs/tags/v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$ ]]; then
  version="${GITHUB_REF_NAME#v}"
elif [[ -n "$release_tag" ]]; then
  version="${release_tag#v}"
else
  next_version="$(node scripts/calculate-version.mjs)"
  next_version="${next_version:-$SEMANTIC_RELEASE_VERSION}"
  if [[ "$next_version" != "$SEMANTIC_RELEASE_VERSION" ]]; then
    echo "semantic-release calculated $next_version but release intent is $SEMANTIC_RELEASE_VERSION." >&2
    exit 1
  fi
  case "$SEMANTIC_RELEASE_PHASE" in
    rc)
      highest=0
      while IFS= read -r tag; do
        [[ "$tag" =~ -rc\.([0-9]+)$ ]] || continue
        if (( BASH_REMATCH[1] > highest )); then highest="${BASH_REMATCH[1]}"; fi
      done < <(git tag --merged HEAD --list "v$next_version-rc.*")
      version="$next_version-rc.$((highest + 1))"
      ;;
    stable) version="$next_version" ;;
    preview) version="$next_version-preview.${GITHUB_RUN_NUMBER:-$height}" ;;
    *) echo "Unsupported release phase: $SEMANTIC_RELEASE_PHASE" >&2; exit 1 ;;
  esac
fi

version_core="${version%%-rc.*}"
version_core="${version_core%%-preview.*}"
if [[ "$version_core" != "$SEMANTIC_RELEASE_VERSION" ]]; then
  echo "Release version $version does not match release intent $SEMANTIC_RELEASE_VERSION." >&2
  exit 1
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
    echo "Release ref $ref_version does not match semantic-release $version." >&2
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
