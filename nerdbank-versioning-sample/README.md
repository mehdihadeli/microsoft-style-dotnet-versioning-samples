# Nerdbank.GitVersioning sample

Nerdbank.GitVersioning CLI reads [version.json](version.json) and adds Git height and commit information to non-release builds. CI passes the calculated version to the .NET SDK with `-p:Version`; this sample does not require NBGV MSBuild integration.

## Configuration

The committed [`version.json`](version.json) is the source of the development line:

| Setting                           | Meaning                                                            |
| --------------------------------- | ------------------------------------------------------------------ |
| `version: 1.0.0-preview.{height}` | Sets the initial three-part version and exposes NBGV's Git height. |
| `versionHeightOffset: -1`         | Excludes the initialization commit from the public preview number. |
| `versionHeightOffsetAppliesTo`    | Limits the bootstrap offset to the initial preview train.          |
| `publicReleaseRefSpec`            | Identifies matching tag refs as public release refs.               |

The sample deliberately uses the CLI instead of the MSBuild package. See the official [`version.json` reference](https://dotnet.github.io/Nerdbank.GitVersioning/docs/versionJson.html), [recommended workflow](https://dotnet.github.io/Nerdbank.GitVersioning/docs/versioning-workflow.html), and [cloud-build guide](https://dotnet.github.io/Nerdbank.GitVersioning/docs/cloudbuild.html).

## Project structure

- [`src/`](src/) contains the Web API.
- [`tests/nerdbank-versioning-sample.Tests/`](tests/nerdbank-versioning-sample.Tests/) contains the real NBGV Git-history test.
- [`version.json`](version.json) stays at the sample root because it controls the complete release line.

Require squash merges so one accepted pull request contributes one first-parent commit. NBGV uses version height and commit identity; retaining branch commits can increase height more than once for one merged PR. The test verifies unique previews, release-intent changes, matching RC/stable tags, and rejection of a mismatched tag.

## CI scenario

The `calculate-version` job installs NBGV `3.10.94` and calls [`scripts/calculate-version.sh`](scripts/calculate-version.sh) in `github-output` mode. The script calculates the version from `version.json` and complete Git history, validates release tags, adds preview/RC `YYDDD.RUN_NUMBER` suffixes, and emits `version`, `assembly_version`, `informational_version`, `environment`, and `commit` outputs. The publish job passes the selected value with `dotnet publish -p:Version=...`.

Example release scenario:

1. Start from the initialized repository at `1.0.0-preview.0`.
2. Create **PR1** with a feature and merge it into `main`. CI calculates `1.0.0-preview.1`, creates no tag, and keeps the Release Drafter release as a draft.
3. Create **PR2** with another feature and merge it into `main`. CI calculates `1.0.0-preview.2`, again without creating a tag.
4. Change version intent to `1.0.0-rc.{height}` with `release-version.sh prepare-rc`, merge that change, and tag the approved commit as `1.0.0-rc.1`. CI publishes `1.0.0-rc.1.YYDDD.RUN_NUMBER` to staging.
5. Merge an RC fix for `rc.2`, then tag the approved commit as `1.0.0-rc.2`. CI publishes the second RC build to staging.
6. Change version intent to `1.0.0` with `release-version.sh prepare-stable`, merge that change, and tag the approved stable commit. CI publishes clean `1.0.0` to production.
7. Start the next preview train with `release-version.sh prepare-train 1.1.0`, merge it, then merge feature PRs for `1.1.0-preview.1`, `1.1.0-preview.2`, and `1.1.0-preview.3`.

Only approved RC and stable commits receive tags. Starting the next release train is an explicit reviewed `version.json` change, such as `release-version.sh prepare-train 1.1.0`. Keep `fetch-depth: 0` because NBGV requires complete history.

The shared command scenario for this sample is:

```bash
# Repository starts at 1.0.0-preview.0

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
# Prepare 1.0.0-rc.{height}, then merge this release-intent PR.
git switch main
git pull --ff-only
git switch -c chore/prepare-1.0.0-rc
./release-version.sh prepare-rc 1.0.0
git add version.json
git commit -m "chore: prepare 1.0.0 RC train"
git push -u origin chore/prepare-1.0.0-rc
# Squash-merge the PR, then run ./release-version.sh tag on main.
# The tag is v1.0.0-rc.1.

-------------
# 1.0.0-rc.2: merge an RC fix; no version.json edit is needed.
git switch main
git pull --ff-only
git switch -c fix/release-candidate
git add -A
git commit -m "fix: correct release candidate behavior"
git push -u origin fix/release-candidate
# Squash-merge the PR, then run ./release-version.sh tag on main.
# The tag is v1.0.0-rc.2.
--------
# Prepare stable intent, then merge this release-intent PR.
git switch -c chore/prepare-1.0.0
./release-version.sh prepare-stable 1.0.0
git add version.json
git commit -m "chore: prepare 1.0.0"
git push -u origin chore/prepare-1.0.0
# Squash-merge the PR, then run ./release-version.sh tag on main.
# The tag is v1.0.0.
------------
# Start next preview train explicitly.
git switch -c chore/prepare-1.1.0-preview
./release-version.sh prepare-train 1.1.0
git add version.json
git commit -m "chore: start 1.1.0 preview train"
git push -u origin chore/prepare-1.1.0-preview
# Squash-merge the PR; this produces 1.1.0-preview.1.
git switch -c feature/add-authorization
git add -A
git commit -m "feat: add authorization"
git push -u origin feature/add-authorization
# Squash-merge the PR; this produces 1.1.0-preview.2.
git switch -c feature/add-reporting
git add -A
git commit -m "feat: add reporting"
git push -u origin feature/add-reporting
# Squash-merge the PR; this produces 1.1.0-preview.3.
```

## Local validation

```powershell
dotnet restore tests/nerdbank-versioning-sample.Tests/nerdbank-versioning-sample.Tests.csproj
dotnet tool install --tool-path .tools/nbgv nbgv --version 3.10.94
$version = bash scripts/calculate-version.sh . .tools/nbgv/nbgv
dotnet build src/nerdbank-versioning-sample.csproj -p:Version=$version
dotnet test tests/nerdbank-versioning-sample.Tests/nerdbank-versioning-sample.Tests.csproj
```

For GitHub Actions-style outputs, pass `github-output` as the third argument:

```bash
bash scripts/calculate-version.sh . .tools/nbgv/nbgv github-output
```

The complete workflow is in [`.github/workflows/nerdbank.yml`](.github/workflows/nerdbank.yml).

## Tradeoffs

NBGV is a good fit when every commit needs a reproducible identity and release intent belongs in source control. It requires explicit version-intent changes when entering RC, stable, or a new preview train; tags approve those already-calculated versions rather than replacing them.
