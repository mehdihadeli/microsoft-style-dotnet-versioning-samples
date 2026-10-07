# Arcade sample

Arcade is the shared build infrastructure used by many .NET engineering repositories. This sample uses Arcade the way the toolset is designed to be used: the version inputs are committed in [`eng/Versions.props`](eng/Versions.props), the SDK is imported by name from [`Directory.Build.props`](Directory.Build.props) and [`Directory.Build.targets`](Directory.Build.targets), and Arcade's own version targets assemble `Version`.

## Use case

Use Arcade when your project needs .NET engineering conventions, shared build targets, official-build metadata, and integration with the `dotnet-eng` feed. Arcade is broader than a tag calculator, so it has more setup cost than MinVer.

The important difference from the other samples is that **Arcade has no Git-aware version calculator**. It does not read tags, commit messages, or commit counts. Its model is:

1. The repository commits the version inputs.
2. The build system supplies `OfficialBuildId` (the CI build number, `<yyyyMMdd>.<revision>`).
3. Arcade's targets turn those inputs into `Version`.

Something still has to map Git history onto those inputs, so this sample keeps [`scripts/calculate-version.sh`](scripts/calculate-version.sh) as the adapter. The adapter derives the inputs and reads the version back; Arcade owns the format..

## Configuration

Version policy lives in [`eng/Versions.props`](eng/Versions.props), which the Arcade SDK locates through `$(VersionsPropsPath)` and imports automatically:

| Property                     | Meaning                                                   |
| ---------------------------- | --------------------------------------------------------- |
| `VersionPrefix`              | The train being developed; validated against release tags. |
| `PreReleaseVersionLabel`     | `preview` while feature work lands, `rc` while hardening.  |
| `PreReleaseVersionIteration` | Fallback iteration for builds that do not run the adapter. |

Everything else is produced by Arcade:

| Arcade input                                | Effect                                                      |
| ------------------------------------------- | ----------------------------------------------------------- |
| `OfficialBuildId` (`<yyyyMMdd>.<revision>`) | Turns `OfficialBuild` on and produces `SHORT_DATE.revision`. |
| `ContinuousIntegrationBuild`                | Stops Arcade relabelling the version as `dev` outside CI.    |
| `DotNetFinalVersionKind=release`            | Drops the suffix, which is how a stable version is produced. |

## Project structure

- [`src/`](src/) contains the Web API.
- [`tests/arcade-versioning-sample.Tests/`](tests/arcade-versioning-sample.Tests/) tests the adapter and Arcade against each other over a real Git history.
- [`eng/Versions.props`](eng/Versions.props) holds the committed version inputs.
- [`Directory.Build.props`](Directory.Build.props) and [`Directory.Build.targets`](Directory.Build.targets) import `Sdk.props` and `Sdk.targets` from the Arcade SDK.
- [`version.proj`](version.proj) is an evaluation-only project the adapter queries for `Version`.
- [`global.json`](global.json) pins the SDK and maps `Microsoft.DotNet.Arcade.Sdk` to a version; Arcade treats this directory as the repository root, so all build output goes to `artifacts/`.
- [`NuGet.config`](NuGet.config) adds the `dotnet-eng` feed the SDK is resolved from.

## Version shapes

Arcade derives an official build's stamp from `OfficialBuildId`, and there is no unstamped development label: a non-official build is labelled `ci` or `dev`. A preview is not identified by a tag, so it always carries the stamp. A release tag already is a complete version, so [`Directory.Build.targets`](Directory.Build.targets) pins `Version` to it and it is used exactly as written.

| Build                      | Version                   |
| -------------------------- | ------------------------- |
| Untagged `main`, initial   | `1.0.0-preview.0.26051.7` |
| Untagged `main`, one merge | `1.0.0-preview.1.26051.7` |
| Release candidate tag      | `1.0.0-rc.1`              |
| Stable tag                 | `1.0.0`                   |
| Any plain `dotnet build`   | `1.0.0-dev`               |

The `26051` is Arcade's `SHORT_DATE` (`yy * 1000 + mm * 50 + dd`) and the `7` is the revision from `OfficialBuildId`. The adapter never formats those itself, and tagged releases skip them entirely.

## Restore requirement

The Arcade SDK is distributed through the Microsoft engineering feed, not nuget.org. [`NuGet.config`](NuGet.config) already adds it:

```powershell
dotnet restore tests/arcade-versioning-sample.Tests/arcade-versioning-sample.Tests.csproj
dotnet test tests/arcade-versioning-sample.Tests/arcade-versioning-sample.Tests.csproj
```

## Release steps

Previews need no action: each merge to `main` advances the preview number because the adapter derives `PreReleaseVersionIteration` from the commit height.

A release is a reviewed change plus a tag:

1. **Harden a candidate.** Set `PreReleaseVersionLabel` to `rc` in `eng/Versions.props` on a release-preparation branch and merge it. The adapter now takes the next RC ordinal from the tags that already exist.
2. **Tag the validated commit.** `git tag -a v1.0.0-rc.1 -m "Release candidate 1.0.0-rc.1"`. The workflow publishes an official prerelease whose version is exactly `1.0.0-rc.1`, for the `staging` audience.
3. **Promote.** Tag the same approved commit `v1.0.0`. The adapter passes `DotNetFinalVersionKind=release`, so Arcade drops the suffix and the build is promoted without being rebuilt.
4. **Start the next train.** Set `VersionPrefix` to `1.1.0` and `PreReleaseVersionLabel` back to `preview` on a release-preparation branch and merge it. Editing `eng/Versions.props` restarts the preview count, so the next merge is `1.1.0-preview.1`.

There is no `release-version.sh`. Earlier revisions of this sample used one to rewrite the MSBuild properties and create tags, but Arcade expresses release intent as committed properties, so the helper only duplicated what a reviewed edit and `git tag` already do.

Because Arcade counts commits rather than following first parents, the workflow requires squash merges. A merge commit that brought five commits onto `main` would advance the preview number by six instead of one.

## CI scenario

[`scripts/calculate-version.sh`](scripts/calculate-version.sh) is the only place the strategy lives. It reads the release tag at `HEAD`, falls back to the committed `rc` label or the commit height, composes `OfficialBuildId` from the UTC date and `GITHUB_RUN_NUMBER`, asks Arcade for `Version`, and emits `version`, `assembly_version`, `informational_version`, `environment`, and `commit`. `OFFICIAL_BUILD_ID` overrides the composed value, which is how a CI system that already has a build number would supply it.

Both workflows call the script, so the strategy cannot drift between them.

Example release scenario:

1. Merge **PR1** with a feature. CI calculates `1.0.0-preview.1.<stamp>.<run>`, creates no tag, and keeps the Release Drafter release as a draft.
2. Merge **PR2** with another feature. CI calculates the next preview, again without a tag.
3. Validate preview 2 and tag that exact commit `v1.0.0-rc.1`. CI publishes an official prerelease for the `staging` audience.
4. Find a release-candidate problem, merge a fix PR, and tag that commit `v1.0.0-rc.2`.
5. When the checks pass, tag the approved RC2 commit `v1.0.0`. CI publishes the official stable release for the `production` audience.

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
git switch -c chore/prepare-1.0.0-rc
# edit eng/Versions.props: set PreReleaseVersionLabel to rc
git add -A
git commit -m "chore: prepare 1.0.0 release candidate"
git push -u origin chore/prepare-1.0.0-rc
# after the merge, tag the validated commit
git tag -a v1.0.0-rc.1 -m "Release candidate 1.0.0-rc.1"
git push origin v1.0.0-rc.1

-------------
# 1.0.0-rc.2
git tag -a v1.0.0-rc.2 -m "Release candidate 1.0.0-rc.2"
git push origin v1.0.0-rc.2
--------
# 1.0.0
git tag -a v1.0.0 -m "Release 1.0.0"
git push origin v1.0.0
------------
# 1.1.0-preview.1
git switch -c chore/prepare-1.1.0-preview
# edit eng/Versions.props: set VersionPrefix to 1.1.0 and PreReleaseVersionLabel to preview
git add -A
git commit -m "chore: start 1.1.0 preview train"
git push -u origin chore/prepare-1.1.0-preview
```
