# semantic-release sample

`semantic-release` is a Node release orchestrator, not an MSBuild versioning package. This sample uses `@semantic-release/commit-analyzer` and `@semantic-release/release-notes-generator` to calculate a release from Conventional Commits. `@semantic-release/exec` then invokes `dotnet publish` with `${nextRelease.version}`.

## Use case

Use this option when one release process must coordinate .NET output with other ecosystems. Use GitVersion, MinVer, NBGV, or Arcade instead when the main requirement is native MSBuild version calculation.

## Configuration

[`release.config.cjs`](release.config.cjs) defines the release policy:

| Setting/plugin                              | Meaning                                                                     |
| ------------------------------------------- | --------------------------------------------------------------------------- |
| `branches: ["main"]`                        | Restricts release analysis to the protected release branch.                 |
| `tagFormat: "v${version}"`                  | Matches the repository's RC and stable tag format.                          |
| `@semantic-release/commit-analyzer`         | Maps Conventional Commits to patch, minor, or major.                        |
| `@semantic-release/release-notes-generator` | Produces notes during semantic-release dry runs.                            |
| `@semantic-release/github`                  | Provides GitHub release integration when semantic-release owns publication. |

See the official [configuration guide](https://semantic-release.org/usage/configuration), [workflow configuration](https://semantic-release.org/usage/workflow-configuration/), and [GitHub Actions guide](https://semantic-release.org/recipes/ci-configurations/github-actions/).

## Project structure

- [`src/`](src/) contains the Web API.
- [`tests/semantic-release-versioning-sample.Tests/`](tests/semantic-release-versioning-sample.Tests/) tests Conventional Commit base selection and the workflow adapter.
- [`release.config.cjs`](release.config.cjs), [`package.json`](package.json), and [`scripts/`](scripts/) stay at the sample root for Node tool discovery.

semantic-release analyzes commits, while this sample uses `GITHUB_RUN_NUMBER` for preview uniqueness. Squash merging is still required so one PR contributes one Conventional Commit on `main`; configure the squash commit title from the PR title (`feat:`, `fix:`, or a breaking-change form).

Without a CI run number, the shell adapter derives local preview ordinals from Git history since the release-intent change. Scenario tests call parameterless fixture methods. During RC intent, the adapter calculates the next RC ordinal from reachable RC tags; it does not return to preview numbering after RC1.

The initialization commit carries a `v0.0.0` bootstrap tag so semantic-release has a history anchor. The calculator treats that tag as setup metadata, not a publishable release: while `release.env` declares the `1.0.0` preview train, the bootstrap commit calculates `1.0.0-preview.0`.

## Local steps

```powershell
npm install
npx semantic-release --dry-run --no-ci
```

The default commit rules are:

```text
fix: patch release
feat: minor release
BREAKING CHANGE: major release
```

The CI workflow runs a dry run on `main`. A production semantic-release setup needs a protected `main`, `GITHUB_TOKEN` permissions, and a deliberate publication policy. This repository keeps NuGet and application publishing in the .NET workflow rather than adding an unrelated npm package publication.

## Release phases

Merges to `main` create untagged previews using the calculated stable base and GitHub run number. Use explicit tags only for RC and stable releases:

```text
v1.0.0-rc.1
v1.0.0-rc.2
v1.0.0
```

Release Drafter handles the human-reviewed draft and publication state. semantic-release remains the automation example for deriving a next version from commits.

## CI scenario

The local and CI calculator in [`scripts/calculate-version.sh`](scripts/calculate-version.sh) runs the JavaScript API in dry-run mode. It reads Conventional Commits and returns the next stable base, then appends `-preview.${GITHUB_RUN_NUMBER}` for untagged `main` builds. For RC and stable tags, it validates the tag at `HEAD` and uses its exact version. The .NET publish job receives the final value with `-p:Version`.

Example release scenario:

1. Create **PR1** with a `feat:` title and merge it into `main`. semantic-release calculates the next stable base, and CI publishes an untagged `1.0.0-preview.<run-number>` draft build.
2. Create **PR2** with another `feat:` title and merge it into `main`. CI calculates the next run-number preview, still without creating a tag.
3. Validate preview 2 and tag that exact commit `v1.0.0-rc.1`. The workflow reads the tag directly and publishes prerelease `1.0.0-rc.1`.
4. Find a release-candidate problem, create a `fix:` PR, and merge it into `main`. The committed RC intent calculates `1.0.0-rc.2` for validation. Tag that commit `v1.0.0-rc.2` to publish it.
5. Run the RC2 checks. When they pass, tag the approved RC2 commit `v1.0.0`. CI publishes stable version `1.0.0`.

Only RC and stable commits receive Git tags. Preview tags are intentionally not created. semantic-release calculates the next version here; Release Drafter remains responsible for the reviewed GitHub release draft and publication state.

The shared command scenario for this sample is:

```bash
# 1.0.0-preview.1
git switch -c feature/customer-export
git add -A
git commit -m "feat: add customer export"
git push -u origin feature/customer-export

----------
# 1.0.0-preview.2
git switch -c feature/add-auth
git add -A
git commit -m "feat: add authentication"
git push -u origin feature/add-auth

----------
# 1.0.0-rc.1
git switch main
git pull --ff-only
git tag -a v1.0.0-rc.1 -m "Release candidate 1.0.0-rc.1"
git push origin v1.0.0-rc.1

-------------
# 1.0.0-rc.2
git switch main
git pull --ff-only
git tag -a v1.0.0-rc.2 -m "Release candidate 1.0.0-rc.2"
git push origin v1.0.0-rc.2
--------
# 1.0.0
git tag -a v1.0.0 -m "Release 1.0.0"
git push origin v1.0.0
------------
# 1.1.0-preview.1
git switch -c chore/prepare-1.1.0-preview
./release-version.sh prepare-train 1.1.0
git add -A
git commit -m "chore: start 1.1.0 preview train"
git push -u origin chore/prepare-1.1.0-preview
```

## Local validation

```powershell
npm ci
npx semantic-release --dry-run --no-ci
dotnet test tests/semantic-release-versioning-sample.Tests/semantic-release-versioning-sample.Tests.csproj
```

The dry run requires a repository with a `main` branch, reachable release tags, and valid Git metadata. The complete CI implementation is in [`.github/workflows/semantic-release.yml`](../.github/workflows/semantic-release.yml).
