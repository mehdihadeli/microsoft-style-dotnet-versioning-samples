#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
# shellcheck source=../release.env
source release.env

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

release_tag="$(git tag --points-at HEAD | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$' | sort -V | tail -n 1 || true)"
native_version="$(MSYS_NO_PATHCONV=1 "$gitversion_cli" /showvariable SemVer)"
if [[ -n "$release_tag" ]]; then
  version="${release_tag#v}"
  tag_core="${version%%-rc.*}"
  if [[ "$tag_core" != "$GITVERSION_RELEASE_VERSION" ]]; then
    echo "Release tag $version does not match release intent $GITVERSION_RELEASE_VERSION." >&2
    exit 1
  fi
elif [[ "$GITVERSION_RELEASE_PHASE" == "rc" ]]; then
  highest=0
  while IFS= read -r tag; do
    [[ "$tag" =~ -rc\.([0-9]+)$ ]] || continue
    if (( BASH_REMATCH[1] > highest )); then highest="${BASH_REMATCH[1]}"; fi
  done < <(git tag --merged HEAD --list "v$GITVERSION_RELEASE_VERSION-rc.*")
  version="$GITVERSION_RELEASE_VERSION-rc.$((highest + 1))"
elif [[ "$GITVERSION_RELEASE_PHASE" == "stable" ]]; then
  version="$GITVERSION_RELEASE_VERSION"
else
  version="$native_version"
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
    echo "Release ref $ref_version does not match GitVersion $version." >&2
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
