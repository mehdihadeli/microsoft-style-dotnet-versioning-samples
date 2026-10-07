#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

output_mode="${1:-version}"
if [[ "$output_mode" == "github-output" ]]; then
  shift
fi

# A release tag on the current commit is the release identity. semantic-release has no
# release-candidate channel on a single release branch, so the tag is read directly and
# used exactly as written. A stable tag outranks a release candidate when both point at
# the same commit, which is how a validated candidate is promoted to stable.
release_tags="$(git tag --points-at HEAD | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$' || true)"
release_tag="$(printf '%s\n' "$release_tags" | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | sort -V | tail -n 1 || true)"
if [[ -z "$release_tag" ]]; then
  release_tag="$(printf '%s\n' "$release_tags" | sort -V | tail -n 1 || true)"
fi

if [[ -n "$release_tag" ]]; then
  version="${release_tag#v}"
else
  # The commit analyzer configured in release.config.cjs is the strategy. It returns the
  # next base version, and nothing at all while no commit warrants a release.
  base_version="$(node scripts/calculate-version.mjs | tr -d '\r')"

  # The nearest reachable release tag is the train baseline. A release candidate is a
  # candidate rather than a train, so an rc tag does not reset the preview counter.
  train_tag="$(git describe --tags --abbrev=0 --match 'v[0-9]*' --exclude '*-rc.*' HEAD 2>/dev/null || true)"
  if [[ -n "$train_tag" ]]; then
    height="$(git rev-list --count "$train_tag..HEAD")"
  else
    height="$(($(git rev-list --count HEAD) - 1))"
  fi

  # semantic-release declares no release for commits such as `chore:` or `docs:`, so the
  # shipped line is kept and only the preview ordinal moves. Before anything has shipped
  # the fallback is semantic-release's first release of 1.0.0.
  if [[ -z "$base_version" ]]; then
    base_version="${train_tag#v}"
    base_version="${base_version:-1.0.0}"
  fi

  version="$base_version-preview.$height"
fi

if [[ "$output_mode" != "github-output" ]]; then
  printf '%s\n' "$version"
  exit 0
fi

commit="$(git rev-parse --short HEAD)"
environment="none"

if [[ -n "$release_tag" ]]; then
  # A tag is the release identity and is published exactly as tagged, so no build suffix
  # is appended. Only the audience differs between a candidate and a stable release.
  if [[ "$version" == *-rc.* ]]; then
    environment="staging"
  else
    environment="production"
  fi
elif [[ "${GITHUB_REF:-}" == "refs/heads/main" && "$version" == *-preview.* ]]; then
  if [[ "$version" =~ -preview\.0$ ]]; then
    # The initialization build has no release behind it and belongs to no audience.
    environment="none"
  else
    # Previews carry the UTC date and the workflow run number so every build is unique,
    # which mirrors the other samples' CI build suffix.
    version="$version.$(date -u +%y%j).${GITHUB_RUN_NUMBER:?GITHUB_RUN_NUMBER is required}"
    environment="dev"
  fi
elif [[ "${GITHUB_REF:-}" == refs/tags/* ]]; then
  echo "Unsupported release tag: ${GITHUB_REF_NAME:-${GITHUB_REF#refs/tags/}}" >&2
  exit 1
fi

assembly_version="${version%%-*}"
informational_version="$version+$commit"
printf 'version=%s\n' "$version"
printf 'assembly_version=%s\n' "$assembly_version"
printf 'informational_version=%s\n' "$informational_version"
printf 'environment=%s\n' "$environment"
printf 'commit=%s\n' "$commit"
