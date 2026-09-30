# GitVersion sample

GitVersion CLI reads Git history and applies the `GitHubFlow/v1` model from [GitVersion.yml](GitVersion.yml). CI passes its calculated version to the .NET SDK with `-p:Version`; this sample does not require GitVersion MSBuild integration.

## Configuration

The configuration is intentionally small:

| Setting                    | Meaning                                                         |
| -------------------------- | --------------------------------------------------------------- |
| `workflow: GitHubFlow/v1`  | Uses `main` and short-lived feature branches.                   |
| `mode: ContinuousDelivery` | Calculates a version for every accepted commit.                 |
| `next-version: 1.0.0`      | Sets the initial development line before the first release tag. |
| `main.label: preview`      | Gives untagged `main` builds a `-preview.N` suffix.             |
| `main.increment: Patch`    | Starts the next patch line after a stable tag.                  |
| `tag-prefix: "[vV]?"`      | Accepts both `v1.0.0` and `1.0.0` tags.                         |

Keep `fetch-depth: 0`; GitVersion needs the complete graph and tags. The official references are the [configuration guide](https://gitversion.net/docs/reference/configuration), [GitHub Flow guide](https://gitversion.net/docs/learn/branching-strategies/githubflow/examples), and [version increments guide](https://gitversion.net/docs/reference/version-increments).

## Project structure

- [`src/`](src/) contains the Web API.
- [`tests/gitversion-versioning-sample.Tests/`](tests/gitversion-versioning-sample.Tests/) contains an isolated Git-history integration test.
- [`GitVersion.yml`](GitVersion.yml) stays at the sample root so the CLI discovers it.

Configure GitHub to allow only squash merges for this strategy. A normal merge preserves the feature commits and adds a merge commit, so Git-height tools may advance more than once for one pull request. The test creates a feature branch and performs `git merge --squash` to verify one new first-parent commit per accepted PR.

## CI scenario

The `calculate-version` job installs GitVersion `6.8.2`. For an untagged push to `main`, it runs `dotnet-gitversion /showvariable SemVer`. For an RC or stable tag, the workflow uses `GITHUB_REF_NAME` directly so GitVersion cannot reinterpret the explicit release identity.

The publish job consumes that one value:

```text
dotnet publish ... -p:Version=<calculated-version>
```

GitVersion numbers previews from Git height. Without a version baseline tag, the initialization commit counts as well, so the first merged pull request can produce `1.0.0-preview.2` and the next one `1.0.0-preview.3`. This sample creates `v1.0.0-preview.0` once on the initialization commit so the first merged pull request produces `preview.1`. The scenario fixture models the same setup with `repository.Tag("v1.0.0-preview.0")` before merging either feature branch.

Example release scenario:

1. Tag the initialization commit as `v1.0.0-preview.0`. This baseline is not published as a preview release.
2. Create **PR1** with a feature and merge it into `main`. CI calculates `1.0.0-preview.1`, creates no new tag, and keeps the Release Drafter release as a draft.
3. Create **PR2** with another feature and merge it into `main`. CI calculates `1.0.0-preview.2`, again without creating a tag.
4. Validate preview 2 and tag that exact commit `v1.0.0-rc.1`. CI reads the tag as `1.0.0-rc.1` and publishes a prerelease.
5. Tag the approved commit `v1.0.0-rc.2` after final RC validation.
6. Tag the approved commit `v1.0.0`. CI publishes stable version `1.0.0`.

Only RC and stable commits receive Git tags. Preview identity comes from GitVersion, but preview publication remains untagged.

The shared command scenario starts by establishing the GitVersion baseline on the initialization commit:

```bash
# One-time GitVersion baseline; do not publish this tag as a preview release
git switch main
git tag -a v1.0.0-preview.0 -m "1.0.0 preview baseline"
git push origin v1.0.0-preview.0

----------
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
dotnet restore tests/gitversion-versioning-sample.Tests/gitversion-versioning-sample.Tests.csproj
dotnet tool install --tool-path .tools/gitversion GitVersion.Tool --version 6.8.2
$version = .tools/gitversion/dotnet-gitversion /showvariable SemVer
dotnet build src/gitversion-versioning-sample.csproj -p:Version=$version
dotnet test tests/gitversion-versioning-sample.Tests/gitversion-versioning-sample.Tests.csproj
```

The local and CI calculator is [`scripts/calculate-version.sh`](scripts/calculate-version.sh). The complete workflow is in [`.github/workflows/gitversion.yml`](../.github/workflows/gitversion.yml), and the version rules are in [`GitVersion.yml`](GitVersion.yml).

## Tradeoffs

GitVersion is the best fit when branch topology, merge history, and commit-message increments are part of the policy. It has more configuration and depends more heavily on complete Git history than MinVer.
