# GitVersion sample

GitVersion CLI reads Git history and applies the `GitHubFlow/v1` model from [GitVersion.yml](GitVersion.yml). Preview identities come from GitVersion, and release-candidate and stable identities come from a release tag on `main`; Conventional Commit messages start the next train. A small CI formatter, [`scripts/calculate-version.sh`](scripts/calculate-version.sh), reads that release tag and appends build metadata (`{yy}{julian}.{run_number}`) to prerelease builds. CI passes the calculated version to the .NET SDK with `-p:Version`; this sample does not require GitVersion MSBuild integration.

## Configuration

The configuration is intentionally small:

| Setting                          | Meaning                                                                 |
| -------------------------------- | ----------------------------------------------------------------------- |
| `workflow: GitHubFlow/v1`        | Uses `main` plus short-lived feature branches.                          |
| `mode: ContinuousDelivery`       | Calculates a version for every accepted commit.                         |
| `next-version: 1.0.0`            | Sets the initial development line before the first release tag.         |
| `main.label: preview`            | Gives untagged `main` builds a `-preview.N` suffix.                     |
| `main.increment: Patch`          | Starts the next patch line after a stable tag.                          |
| `major/minor/patch-bump-message` | Maps Conventional Commits (`feat:`, `fix:`, `!`) to version increments. |
| `tag-prefix: "[vV]?"`            | Accepts both `v1.0.0` and `1.0.0` tags.                                 |

Keep `fetch-depth: 0`; GitVersion needs the complete graph and tags. The official references are the [configuration guide](https://gitversion.net/docs/reference/configuration), [GitHub Flow guide](https://gitversion.net/docs/learn/branching-strategies/githubflow/examples), and [version increments guide](https://gitversion.net/docs/reference/version-increments).

## Version sources

GitVersion derives preview versions from the Git graph, and a release tag on `main` supplies the release identity:

| Commit state                | Version           | Source                                            |
| --------------------------- | ----------------- | ------------------------------------------------- |
| Untagged commit on `main`   | `1.0.0-preview.N` | `main.label` plus Git height                      |
| Commit tagged `vx.y.z-rc.N` | `x.y.z-rc.N`      | the tag at `HEAD`, read by `calculate-version.sh` |
| Commit tagged `vx.y.z`      | `x.y.z`           | the tag at `HEAD`, read by `calculate-version.sh` |

GitVersion always labels untagged `main` commits as previews and never lets a prerelease tag's label win on a main branch; only stable tags pass through natively. The CI formatter therefore reads the release tag at `HEAD` directly. A tag on `main` is the approval, so RC and stable identities remain tag-gated with no release branch. Conventional Commit messages drive the increment that starts the next train: a `feat:` after `v1.0.0` opens `1.1.0-preview.1`, and a breaking-change marker such as `feat!:` opens the next major line.

CI additionally appends `.{yy}{julian}.{run_number}` to prerelease versions (see [CI scenario](#ci-scenario)) so every build is unique; the local and tagged versions above stay unchanged.

## Project structure

- [`src/`](src/) contains the Web API.
- [`tests/gitversion-versioning-sample.Tests/`](tests/gitversion-versioning-sample.Tests/) contains isolated Git-history integration tests that run the real GitVersion CLI.
- [`scripts/calculate-version.sh`](scripts/calculate-version.sh) is the CI formatter that appends the prerelease build suffix.
- [`GitVersion.yml`](GitVersion.yml) stays at the sample root so the CLI discovers it.
- [`.github/scripts/require-squash-merge.sh`](.github/scripts/require-squash-merge.sh) fails the version job when a merge commit lands on `main`.
- [`.github/workflows/build-and-publish.yml`](.github/workflows/build-and-publish.yml) builds, calculates the version, and publishes.

## Preview numbering and merge strategy

`preview.N` is GitVersion's `PreReleaseNumber`, which equals `CommitsSinceVersionSource`: the number of commits reachable from `HEAD` since the last version-source tag. It counts commits, not pull requests, so a merge commit advances it by every commit in the branch plus the merge commit itself:

| Merge style                         | Two-commit PR      | Change per PR |
| ----------------------------------- | ------------------ | ------------- |
| Merge commit (`--no-ff`)            | `.0` → `.3` → `.6` | +3            |
| Squash and merge                    | `.0` → `.1` → `.2` | +1            |
| Rebase and merge (two commits land) | `.0` → `.2` → `.4` | +2            |

In CI the jump is easy to miss as a bug: one two-commit pull request merged with a merge commit takes `1.0.0-preview.12` to `1.0.0-preview.15` instead of `1.0.0-preview.13`. No GitVersion mode, strategy, or workflow changes this; `mode: Mainline` is not a valid mode in GitVersion 6, and the `Mainline` version strategy still increments by more than one per merge commit. The release model expects one accepted pull request to equal one preview step, so use squash merges.

Configure the repository to allow squash merges only: **Settings → General → Pull Requests** → uncheck *Allow merge commits* and *Allow rebase merging*, check *Allow squash merging*, and set the default to *Squash*.

The `calculate-version` job runs [`.github/scripts/require-squash-merge.sh`](.github/scripts/require-squash-merge.sh) and fails the build when `HEAD` is a merge commit, so silent drift is caught before a release is published. The guard allows a root commit and skips release-tag builds, because a tagged build takes its identity from the tag. The tests perform `git merge --squash` to verify one new commit per accepted pull request.

Squash merges matter for the same reason in every Git-height tool in this repository, not only GitVersion.

## CI scenario

The `calculate-version` job installs GitVersion `6.8.2` and runs [`scripts/calculate-version.sh`](scripts/calculate-version.sh) in its `github-output` mode. The script reads a release tag at `HEAD` when one exists, otherwise it asks GitVersion for `SemVer`. It then appends `.{yy}{julian}.{run_number}` (for example `.26123.42`) to prerelease versions so every CI run is unique, and reports the deployment environment. Stable versions pass through unchanged.

| Ref                          | Version                    | Environment  |
| ---------------------------- | -------------------------- | ------------ |
| `refs/heads/main` (after PR) | `1.0.0-preview.1.26123.42` | `dev`        |
| `refs/tags/v1.0.0-rc.1`      | `1.0.0-rc.1.26123.43`      | `staging`    |
| `refs/tags/v1.0.0`           | `1.0.0`                    | `production` |

The suffix uses `date -u +%y%j` (two-digit year and Julian day) plus `GITHUB_RUN_NUMBER`, matching the nerdbank-versioning sample. The `-preview.0` baseline never publishes, so it keeps environment `none`.

The publish job consumes that one value:

```text
dotnet publish ... -p:Version=<calculated-version>
```

GitVersion numbers pre-releases from Git height. Without a version baseline tag, the initialization commit counts as well, so the first merged pull request can produce `1.0.0-preview.2`. This sample creates `v1.0.0-preview.0` once on the initialization commit so the first merged pull request produces `preview.1`.

The baseline fixes the starting point but not the step size. Each subsequent preview advances by the number of commits that landed on `main`, so squash merging is what keeps the step at one (see [Preview numbering and merge strategy](#preview-numbering-and-merge-strategy)).

Example release scenario:

1. Tag the initialization commit as `v1.0.0-preview.0`. This baseline is not published as a preview release.
2. Create **PR1** with a feature and squash-merge it into `main`. CI calculates `1.0.0-preview.1`, creates no new tag, and keeps the Release Drafter release as a draft.
3. Create **PR2** with another feature and squash-merge it into `main`. CI calculates `1.0.0-preview.2`, again without creating a tag.
4. Validate preview 2 and tag that exact `main` commit `v1.0.0-rc.1`. CI reads the tag as `1.0.0-rc.1` and publishes a prerelease.
5. Land a `fix:` on `main` through a squash-merged pull request. GitVersion calculates the next preview, and you tag that commit `v1.0.0-rc.2` after final RC validation.
6. Tag the approved `main` commit `v1.0.0`. CI publishes stable version `1.0.0`.
7. Squash-merge a `feat:` into `main`. GitVersion calculates `1.1.0-preview.1` and the next train begins.

Only release candidates and the stable release receive Git tags. Preview identity comes from GitVersion, but preview publication remains untagged.

The shared command scenario:

```bash
# One-time GitVersion baseline; do not publish this tag as a preview release
git switch main
git tag -a v1.0.0-preview.0 -m "1.0.0 preview baseline"
git push origin v1.0.0-preview.0

----------
# 1.0.0-preview.1
# Squash-merge the pull request: one pull request, one commit on main.
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
git add -A
git commit -m "fix: correct release candidate behavior"
git tag -a v1.0.0-rc.2 -m "Release candidate 1.0.0-rc.2"
git push origin v1.0.0-rc.2
--------
# 1.0.0
git tag -a v1.0.0 -m "Release 1.0.0"
git push origin main v1.0.0
------------
# 1.1.0-preview.1
git switch -c feature/add-authorization
git add -A
git commit -m "feat: add authorization"
git push -u origin feature/add-authorization
```

## Local validation

```powershell
dotnet restore tests/gitversion-versioning-sample.Tests/gitversion-versioning-sample.Tests.csproj
dotnet tool install --tool-path .tools/gitversion GitVersion.Tool --version 6.8.2
$version = .tools/gitversion/dotnet-gitversion /showvariable SemVer
dotnet build src/gitversion-versioning-sample.csproj -p:Version=$version
dotnet test tests/gitversion-versioning-sample.Tests/gitversion-versioning-sample.Tests.csproj
```

The complete workflow is in [`.github/workflows/build-and-publish.yml`](.github/workflows/build-and-publish.yml), and the version rules are in [`GitVersion.yml`](GitVersion.yml).

## Tradeoffs

GitVersion is the best fit when branch topology, merge history, and commit-message increments are part of the policy. It has more configuration and depends more heavily on complete Git history than MinVer.

Preview numbering counts commits rather than pull requests, so the sample requires squash merges and enforces them with [`.github/scripts/require-squash-merge.sh`](.github/scripts/require-squash-merge.sh). Keep that requirement if you adopt the sample; with merge commits enabled, `preview.N` advances by more than one per pull request and no GitVersion setting prevents it.
