#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

gitversion_cli="${GITVERSION_CLI:-dotnet-gitversion}"
if [[ "$gitversion_cli" == "dotnet-gitversion" && ! -e "$gitversion_cli" && -f ".tools/gitversion/dotnet-gitversion" ]]; then
  gitversion_cli=".tools/gitversion/dotnet-gitversion"
elif [[ "$gitversion_cli" == "dotnet-gitversion" && ! -e "$gitversion_cli" && -f ".tools/gitversion/dotnet-gitversion.exe" ]]; then
  gitversion_cli=".tools/gitversion/dotnet-gitversion.exe"
elif [[ ! -e "$gitversion_cli" && -f "${gitversion_cli}.exe" ]]; then
  gitversion_cli="${gitversion_cli}.exe"
fi

output_mode="${1:-version}"
if [[ "$output_mode" == "github-output" ]]; then
  shift
fi

# A release tag on the current main commit is the release identity. GitVersion
# always labels untagged main commits as previews and never lets a prerelease
# tag's label win on a main branch, so the tag is read directly here.
release_tags="$(git tag --points-at HEAD | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$' || true)"
# A stable tag outranks a prerelease tag when both point at the same commit,
# which is how a validated release candidate is promoted to stable.
release_tag="$(printf '%s\n' "$release_tags" | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | sort -V | tail -n 1 || true)"
if [[ -z "$release_tag" ]]; then
  release_tag="$(printf '%s\n' "$release_tags" | sort -V | tail -n 1 || true)"
fi
if [[ -n "$release_tag" ]]; then
  version="${release_tag#v}"
else
  version="$(MSYS_NO_PATHCONV=1 "$gitversion_cli" /showvariable SemVer)"
fi

if [[ "$output_mode" != "github-output" ]]; then
  printf '%s\n' "$version"
  exit 0
fi

commit="$(git rev-parse --short HEAD)"
environment="none"
build_suffix="$(date -u +%y%j).${GITHUB_RUN_NUMBER:?GITHUB_RUN_NUMBER is required}"

if [[ -n "$release_tag" ]]; then
  if [[ "$version" == *-rc.* ]]; then
    version="$version.$build_suffix"
    environment="staging"
  else
    environment="production"
  fi
elif [[ "${GITHUB_REF:-}" == "refs/heads/main" && "$version" == *-preview.* ]]; then
  if [[ "$version" =~ -preview\.0(\.|$) ]]; then
    environment="none"
  else
    version="$version.$build_suffix"
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
