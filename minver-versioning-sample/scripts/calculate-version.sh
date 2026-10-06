#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

# minver-cli has no configuration file, so the strategy is declared here. Every caller
# runs this script, which keeps the options in one place instead of repeating them in
# each workflow.
minver_options=(-t v -m 1.0 -p preview)

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

# MinVer writes the calculated version to stdout and its log to stderr. A version tag
# at HEAD is returned exactly as tagged, so the tag is the release identity.
release_tags="$(git tag --points-at HEAD | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$' || true)"
# A stable tag outranks a prerelease tag when both point at the same commit, which is
# how a validated release candidate is promoted to stable. MinVer makes the same choice
# for the version, and this selection only decides the audience and the build suffix.
release_tag="$(printf '%s\n' "$release_tags" | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | sort -V | tail -n 1 || true)"
if [[ -z "$release_tag" ]]; then
	release_tag="$(printf '%s\n' "$release_tags" | sort -V | tail -n 1 || true)"
fi
version="$($minver_cli "${minver_options[@]}" "$@")"
version="${version%%+*}"

if [[ "$output_mode" != "github-output" ]]; then
	printf '%s\n' "$version"
	exit 0
fi

commit="$(git rev-parse --short HEAD)"
environment="none"
# Microsoft-style artifact identity: two-digit UTC year and Julian day, plus the
# workflow run number. Preview and RC artifacts carry it so every build is unique;
# stable versions stay clean.
build_suffix="$(date -u +%y%j).${GITHUB_RUN_NUMBER:?GITHUB_RUN_NUMBER is required}"

if [[ -n "$release_tag" ]]; then
	if [[ "${release_tag#v}" == *-rc.* ]]; then
		version="$version.$build_suffix"
		environment="staging"
	else
		environment="production"
	fi
elif [[ "${GITHUB_REF:-}" == "refs/heads/main" ]]; then
	if [[ "$version" == *-preview ]]; then
		# Height zero is the initialization commit; it is not a published preview.
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
printf 'version=%s\n' "$version"
printf 'assembly_version=%s\n' "$assembly_version"
printf 'informational_version=%s+%s\n' "$version" "$commit"
printf 'environment=%s\n' "$environment"
printf 'commit=%s\n' "$commit"
