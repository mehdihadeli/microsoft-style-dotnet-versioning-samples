# Nerdbank.GitVersioning sample

Nerdbank.GitVersioning CLI reads [version.json](version.json) and adds Git height and commit information to non-release builds. CI passes the calculated version to the .NET SDK with `-p:Version`; this sample does not require NBGV MSBuild integration.

## Configuration

The committed [`version.json`](version.json) is the source of the development line:

| Setting                           | Meaning                                                            |
| --------------------------------- | ------------------------------------------------------------------ |
| `version: 1.0.0-preview.{height}` | Sets the initial three-part version and exposes NBGV's Git height. |
| `versionHeightOffset: -1`         | Excludes the initialization commit from the public preview number. |
| `publicReleaseRefSpec`            | Identifies matching tag refs as public release refs.               |
| `cloudBuild.setVersionVariables`  | Enables CI version variables when NBGV native integration is used. |

The sample deliberately uses the CLI instead of the MSBuild package. See the official [`version.json` reference](https://dotnet.github.io/Nerdbank.GitVersioning/docs/versionJson.html), [recommended workflow](https://dotnet.github.io/Nerdbank.GitVersioning/docs/versioning-workflow.html), and [cloud-build guide](https://dotnet.github.io/Nerdbank.GitVersioning/docs/cloudbuild.html).

## Project structure

- [`src/`](src/) contains the Web API.
- [`tests/nerdbank-versioning-sample.Tests/`](tests/nerdbank-versioning-sample.Tests/) contains the real NBGV Git-history test.
- [`version.json`](version.json) stays at the sample root because it controls the complete release line.

Require squash merges so one accepted pull request contributes one first-parent commit. NBGV uses version height and commit identity; retaining branch commits can increase height more than once for one merged PR. The test verifies unique previews, one first-parent increment per squash merge, and exact RC/stable tag passthrough.

## CI scenario

The `calculate-version` job installs NBGV `3.10.94` and runs [`scripts/calculate-version.sh`](scripts/calculate-version.sh). The script verifies that NBGV can calculate the commit identity, then applies the shared release policy: explicit tags pass through unchanged, initial previews use first-parent height, and commits after a stable tag advance the patch version. The publish job passes the selected value with `dotnet publish -p:Version=...`.

Example release scenario:

1. Create **PR1** with a feature and merge it into `main`. CI calculates `1.0.0-preview.1`, creates no tag, and keeps the Release Drafter release as a draft.
2. Create **PR2** with another feature and merge it into `main`. CI calculates `1.0.0-preview.2`, again without creating a tag.
3. Validate preview 2 and tag that exact commit `v1.0.0-rc.1`. The workflow takes the tag value directly and publishes prerelease `1.0.0-rc.1`.
4. Find a release-candidate problem, create a fix PR, and merge it into `main`. CI calculates another untagged preview. After validation, tag that commit `v1.0.0-rc.2`.
5. Run the RC2 checks. When they pass, tag the approved RC2 commit `v1.0.0`. CI publishes stable version `1.0.0`.

Only RC and stable commits receive tags. After `v1.0.0`, the policy calculator advances the next untagged commit to `1.0.1-preview.1`; no manual `version.json` update is required for this sample. Keep `fetch-depth: 0` because both NBGV and the policy calculator require complete history.

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
# 1.0.1-preview.1
git switch -c feature/add-authurization
git add -A
git commit -m "feat: add authurization"
git push -u origin feature/add-authurization
```

## Local validation

```powershell
dotnet restore tests/nerdbank-versioning-sample.Tests/nerdbank-versioning-sample.Tests.csproj
dotnet tool install --tool-path .tools/nbgv nbgv --version 3.10.94
$version = bash scripts/calculate-version.sh . .tools/nbgv/nbgv
dotnet build src/nerdbank-versioning-sample.csproj -p:Version=$version
dotnet test tests/nerdbank-versioning-sample.Tests/nerdbank-versioning-sample.Tests.csproj
```

The complete workflow is in [`.github/workflows/build-and-publish.yml`](.github/workflows/build-and-publish.yml).

## Tradeoffs

NBGV is a good fit when every commit needs a reproducible identity and the development base belongs in source control. It requires an explicit base-version update after a stable release, unlike tag-first MinVer.
