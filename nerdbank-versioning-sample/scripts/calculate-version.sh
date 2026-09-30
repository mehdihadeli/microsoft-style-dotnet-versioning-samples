#!/usr/bin/env bash
set -euo pipefail

repository_path="${1:-.}"
nbgv_path="${2:-nbgv}"
output_mode="${3:-version}"

cd "$repository_path"

nbgv_version="$("$nbgv_path" get-version -v SemVer2)"
effective_version="${nbgv_version%%.g*}"

release_tag="$(git tag --points-at HEAD | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$' | sort -V | tail -n 1 || true)"

if [[ -n "$release_tag" && "${release_tag#v}" != "$effective_version" ]]; then
  echo "Release tag ${release_tag#v} does not match NBGV version $effective_version." >&2
  exit 1
fi

if [[ "$output_mode" != "github-output" ]]; then
  printf '%s\n' "$effective_version"
  exit 0
fi

commit="$(git rev-parse --short HEAD)"
version="$effective_version"
environment="none"

if [[ "${GITHUB_REF:-}" =~ ^refs/tags/v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$ ]]; then
  version="${GITHUB_REF_NAME#v}"
  if [[ "$version" != "$effective_version" ]]; then
    echo "Release ref $version does not match NBGV version $effective_version." >&2
    exit 1
  fi
  if [[ "$version" == *-rc.* ]]; then
    version="$version.$(date -u +%y%j).${GITHUB_RUN_NUMBER:?GITHUB_RUN_NUMBER is required}"
    environment="staging"
  else
    environment="production"
  fi
elif [[ "${GITHUB_REF:-}" == "refs/heads/main" && "$effective_version" == *-preview.* ]]; then
  if [[ "$effective_version" =~ -preview\.0(\.|$) ]]; then
    environment="none"
  else
    version="$effective_version.$(date -u +%y%j).${GITHUB_RUN_NUMBER:?GITHUB_RUN_NUMBER is required}"
    environment="dev"
  fi
elif [[ "${GITHUB_REF:-}" == refs/tags/* ]]; then
  echo "Unsupported release tag: ${GITHUB_REF_NAME:-${GITHUB_REF#refs/tags/}}" >&2
  exit 1
fi

assembly_version="$($nbgv_path get-version -v AssemblyVersion)"
informational_version="$version+$commit"
printf 'version=%s\n' "$version"
printf 'assembly_version=%s\n' "$assembly_version"
printf 'informational_version=%s\n' "$informational_version"
printf 'environment=%s\n' "$environment"
printf 'commit=%s\n' "$commit"
