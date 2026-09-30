# Arcade sample

Arcade is the shared build infrastructure used by many .NET engineering repositories. This sample references `Microsoft.DotNet.Arcade.Sdk` and keeps a small `Directory.Build.props` so the version is visible in a standalone Web API.

## Use case

Use Arcade when your project needs .NET engineering conventions, shared build targets, official-build metadata, and integration with the `dotnet-eng` feed. Arcade is broader than a tag calculator, so it has more setup cost than MinVer.

## Configuration

This compact sample keeps version policy in [`Directory.Build.props`](Directory.Build.props):

| Property         | Meaning                                                     |
| ---------------- | ----------------------------------------------------------- |
| `VersionPrefix`  | Current development base, initially `1.0.0`.                |
| `Version`        | Receives the CI-calculated preview or explicit tag version. |
| `PackageVersion` | Keeps package metadata aligned with `Version`.              |
| `OfficialBuild`  | Is enabled only for tagged RC/stable builds.                |

Full Arcade repositories normally use `eng/Versions.props`, `eng/common/build.ps1`, and `eng/common/CIBuild.cmd`. See the official [Arcade SDK guide](https://github.com/dotnet/arcade/blob/main/Documentation/ArcadeSdk.md), especially `eng/Versions.props`, `Directory.Build.props`, and official-build sections.

## Project structure

- [`src/`](src/) contains the Web API.
- [`tests/arcade-versioning-sample.Tests/`](tests/arcade-versioning-sample.Tests/) tests the workflow-owned version adapter and squash-merge history.
- [`Directory.Build.props`](Directory.Build.props) stays at the sample root so it applies to source and test projects.

CI previews use `GITHUB_RUN_NUMBER`. Without that input, the shell adapter derives the preview ordinal from Git history since the train's configuration change. Scenario tests take no run-number arguments and compare this history-derived version with MSBuild stamping. RC intent produces the next RC ordinal, not another preview.

## Restore requirement

The Arcade package is distributed through the Microsoft engineering feed, not nuget.org:

```powershell
dotnet nuget add source https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-eng/nuget/v3/index.json --name dotnet-eng
dotnet restore tests/arcade-versioning-sample.Tests/arcade-versioning-sample.Tests.csproj
dotnet test tests/arcade-versioning-sample.Tests/arcade-versioning-sample.Tests.csproj
```

## Release steps

The workflow creates an untagged preview on each merge to `main`. Use explicit tags only for RC and stable releases:

```text
v1.0.0-rc.1
v1.0.0-rc.2
v1.0.0
```

The GitHub workflow adds the feed, restores, builds, tests, and publishes tagged output. It also updates Release Drafter, leaving previews as drafts and publishing RC/stable notes.

## CI scenario

The local and CI calculator in [`scripts/calculate-version.sh`](scripts/calculate-version.sh) uses `1.0.0-preview.${GITHUB_RUN_NUMBER}` for an untagged `main` build and validates the exact RC or stable tag at `HEAD`. The publish job passes that value to Arcade through `-p:Version` and sets `-p:OfficialBuild=true` only for tags.

Example release scenario:

1. Create **PR1** with a feature and merge it into `main`. CI calculates `1.0.0-preview.<run-number>`, creates no tag, and keeps the Release Drafter release as a draft.
2. Create **PR2** with another feature and merge it into `main`. CI calculates the next run-number preview, again without a tag.
3. Validate preview 2 and tag that exact commit `v1.0.0-rc.1`. CI passes `1.0.0-rc.1` to Arcade and publishes an official prerelease.
4. Find a release-candidate problem, create a fix PR, and merge it into `main`. The committed RC intent calculates `1.0.0-rc.2` for validation. Tag that commit `v1.0.0-rc.2` to publish it.
5. Run the RC2 checks. When they pass, tag the approved RC2 commit `v1.0.0`. CI passes `1.0.0` to Arcade and publishes the official stable release.

Only RC and stable commits receive tags. Arcade does not automatically advance `VersionPrefix` after stable publication. Run `./release-version.sh prepare-train 1.1.0` on a release-preparation branch before the next feature train. Use the shell calculator for local history-derived versions; a plain MSBuild preview without supplied inputs still defaults to `preview.0`.

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
