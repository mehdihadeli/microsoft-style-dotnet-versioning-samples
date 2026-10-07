#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

# Arcade ships no Git-aware version calculator. Its model is that the repository commits the
# version inputs in eng/Versions.props and the build system supplies OfficialBuildId, after
# which Arcade's version targets assemble Version. This adapter is the glue that derives those
# inputs from Git and reads the result back, so Arcade still owns the version format.
versions_props="eng/Versions.props"

output_mode="${1:-version}"
if [[ "$output_mode" == "github-output" ]]; then
  shift
fi

property() {
  sed -n "s:.*<$1[^>]*>\([^<]*\)</$1>.*:\1:p" "$versions_props" | head -n 1
}

version_prefix="$(property VersionPrefix)"
release_label="$(property PreReleaseVersionLabel)"
if [[ -z "$version_prefix" || -z "$release_label" ]]; then
  echo "Could not read VersionPrefix and PreReleaseVersionLabel from $versions_props." >&2
  exit 1
fi

# A release tag at HEAD is the release identity. A stable tag outranks an RC tag on the same
# commit, which is how a validated candidate is promoted without being rebuilt.
release_tags="$(git tag --points-at HEAD | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$' || true)"
release_tag="$(printf '%s\n' "$release_tags" | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | sort -V | tail -n 1 || true)"
if [[ -z "$release_tag" ]]; then
  release_tag="$(printf '%s\n' "$release_tags" | sort -V | tail -n 1 || true)"
fi

label="$release_label"
iteration=""
final_version_kind=""
# A tagged release is used exactly as written, so it bypasses Arcade's build stamp and travels
# through ReleaseVersion instead. Everything else is derived and stamped.
release_version=""
if [[ -n "$release_tag" ]]; then
  tagged_version="${release_tag#v}"
  release_version="$tagged_version"
  if [[ "${tagged_version%%-rc.*}" != "$version_prefix" ]]; then
    echo "Release tag $tagged_version does not match Arcade VersionPrefix $version_prefix." >&2
    exit 1
  fi
  if [[ "$tagged_version" == *-rc.* ]]; then
    label="rc"
    iteration="${tagged_version##*-rc.}"
  else
    # Arcade collapses a final release by dropping the suffix through DotNetFinalVersionKind.
    final_version_kind="release"
  fi
elif [[ "$release_label" == "rc" ]]; then
  # The committed rc label is the release intent, so the RC ordinal comes from the tags that
  # already exist rather than from the commit height.
  highest=0
  while IFS= read -r tag; do
    [[ "$tag" =~ -rc\.([0-9]+)$ ]] || continue
    if (( BASH_REMATCH[1] > highest )); then highest="${BASH_REMATCH[1]}"; fi
  done < <(git tag --merged HEAD --list "v$version_prefix-rc.*")
  label="rc"
  iteration="$((highest + 1))"
else
  # A preview advances per commit. The count restarts when eng/Versions.props, which holds the
  # train, is edited on a release-preparation branch.
  train_commit="$(git log -1 --format=%H -- "$versions_props")"
  initial_commit="$(git rev-list --max-parents=0 HEAD | head -n 1)"
  iteration="$(git rev-list --count "$train_commit..HEAD")"
  if [[ "$train_commit" != "$initial_commit" ]]; then iteration="$((iteration + 1))"; fi
fi

# The CI system owns Arcade's OfficialBuildId (<yyyyMMdd>.<revision>). GitHub has no build
# number of that shape, so the adapter composes one from the UTC date and the workflow run
# number; OFFICIAL_BUILD_ID lets a CI system or a test supply the value directly instead.
if [[ -n "${OFFICIAL_BUILD_ID:-}" ]]; then
  official_build_id="$OFFICIAL_BUILD_ID"
elif [[ "$output_mode" == "github-output" ]]; then
  official_build_id="$(date -u +%Y%m%d).${GITHUB_RUN_NUMBER:?GITHUB_RUN_NUMBER is required}"
else
  official_build_id="$(date -u +%Y%m%d).${iteration:-0}"
fi

# Supplying OfficialBuildId turns OfficialBuild on inside the SDK (DefaultVersions.props).
# ContinuousIntegrationBuild stops Arcade from relabelling the version as 'dev' outside CI.
msbuild_properties=(
  -p:ContinuousIntegrationBuild=true
  -p:OfficialBuildId="$official_build_id"
  -p:PreReleaseVersionLabel="$label"
  -p:PreReleaseVersionIteration="$iteration"
)
if [[ -n "$final_version_kind" ]]; then
  msbuild_properties+=(-p:DotNetFinalVersionKind="$final_version_kind")
fi
if [[ -n "$release_version" ]]; then
  # The official build still happens; only the version string comes straight from the tag.
  msbuild_properties+=(-p:ReleaseVersion="$release_version")
fi

# Arcade computes the version; this adapter only reads it back.
version="$(dotnet msbuild version.proj -getProperty:Version "${msbuild_properties[@]}" | tr -d '\r' | grep -E '^[0-9]+\.[0-9]+\.[0-9]+' | tail -n 1)"

if [[ "$output_mode" != "github-output" ]]; then
  printf '%s\n' "$version"
  exit 0
fi

commit="$(git rev-parse --short HEAD)"
environment="none"
if [[ -n "$release_tag" ]]; then
  if [[ "${release_tag#v}" == *-rc.* ]]; then
    environment="staging"
  else
    environment="production"
  fi
elif [[ "${GITHUB_REF:-}" == "refs/heads/main" ]]; then
  if [[ "$version" =~ -preview\.0(\.|$) ]]; then
    # Height zero is the initialization commit; it is not a published preview.
    environment="none"
  else
    environment="dev"
  fi
elif [[ "${GITHUB_REF:-}" == refs/tags/* ]]; then
  echo "Unsupported release tag: ${GITHUB_REF_NAME:-${GITHUB_REF#refs/tags/}}" >&2
  exit 1
fi

printf 'version=%s\n' "$version"
printf 'assembly_version=%s\n' "${version%%-*}"
printf 'informational_version=%s+%s\n' "$version" "$commit"
printf 'environment=%s\n' "$environment"
printf 'commit=%s\n' "$commit"
