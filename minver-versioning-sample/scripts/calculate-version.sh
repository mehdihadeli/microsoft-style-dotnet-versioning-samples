#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=../version.env
source "$repository_root/version.env"
cd "$repository_root"

minver_cli="${MINVER_CLI:-minver}"
if [[ "$minver_cli" == "minver" && ! -e "$minver_cli" && -f ".tools/minver/minver" ]]; then
	minver_cli=".tools/minver/minver"
elif [[ ! -e "$minver_cli" && -f "${minver_cli}.exe" ]]; then
	minver_cli="${minver_cli}.exe"
fi
if [[ "$minver_cli" == ".tools/minver/minver" && ! -e "$minver_cli" && -f "${minver_cli}.exe" ]]; then
	minver_cli="${minver_cli}.exe"
fi
output_mode="${1:-version}"
if [[ "$output_mode" == "github-output" ]]; then
	shift
fi

release_tag="$(git tag --points-at HEAD | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$' | sort -V | tail -n 1 || true)"
version="$($minver_cli -t "$MINVER_TAG_PREFIX" -m "$MINVER_MINIMUM_MAJOR_MINOR" -p "$MINVER_DEFAULT_PRE_RELEASE_PHASE" "$@")"
version="${version%%+*}"
if [[ -n "$release_tag" ]]; then
	tag_version="${release_tag#v}"
	tag_core="${tag_version%%-rc.*}"
	if [[ "$tag_core" != "$MINVER_RELEASE_VERSION" ]]; then
		echo "Release tag $tag_version does not match release intent $MINVER_RELEASE_VERSION." >&2
		exit 1
	fi
	if [[ "${release_tag#v}" != "$version" ]]; then
		echo "Release tag ${release_tag#v} does not match MinVer $version." >&2
		exit 1
	fi
	version="${release_tag#v}"
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
		echo "Release ref $ref_version does not match MinVer $version." >&2
		exit 1
	fi
	if [[ "$ref_version" == *-rc.* ]]; then
		environment="staging"
	else
		environment="production"
	fi
elif [[ "${GITHUB_REF:-}" == "refs/heads/main" && "$version" == *-* ]]; then
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
