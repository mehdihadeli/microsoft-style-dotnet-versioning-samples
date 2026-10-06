#!/usr/bin/env bash
# GitVersion numbers previews by commit count, so a merge commit would advance
# preview.N by every commit in the branch plus the merge commit itself. Squash
# merging keeps one accepted pull request equal to one commit on main.
#
# Release tags are exempt: a tagged build takes its identity from the tag.
set -euo pipefail

if [[ "${GITHUB_REF:-}" == refs/tags/* ]]; then
  echo "Tag build; the release identity comes from the tag."
  exit 0
fi

parents="$(git log -1 --pretty=%P)"

if [[ "$parents" == *" "* ]]; then
  echo "::error::HEAD is a merge commit ($(git rev-parse --short HEAD)); preview numbering requires squash merges."
  echo "::error::GitVersion counts commits, so a merge commit advances preview.N by every branch commit plus itself."
  echo "::error::Settings > General > Pull Requests: uncheck 'Allow merge commits' and 'Allow rebase merging', check 'Allow squash merging', set the default to Squash."
  exit 1
fi

echo "HEAD is not a merge commit ($(git rev-parse --short HEAD)); one accepted pull request advances preview.N by one."
