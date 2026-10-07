# semantic-release sample

`semantic-release` is a Node release orchestrator, not an MSBuild versioning package. This sample uses `@semantic-release/commit-analyzer` to decide the next version from Conventional Commits, and [`scripts/calculate-version.sh`](scripts/calculate-version.sh) is the adapter that reports that decision to CI. The commits are the release intent, so there is no committed release-intent file and no `release-version.sh` helper to write one.

## Use case

Use this option when the same release process must coordinate .NET output with other ecosystems, or when Conventional Commits are already the rule. Use GitVersion, MinVer, NBGV, or Arcade instead when the requirement is a native MSBuild version that the SDK calculates during the build.

## Configuration

[`release.config.cjs`](release.config.cjs) is the strategy:

| Setting/plugin                              | Meaning                                                                  |
| ------------------------------------------- | ------------------------------------------------------------------------ |
| `branches: ["main"]`                        | Makes `main` the release branch and restricts analysis to it.            |
| `tagFormat: "v${version}"`                  | Matches the repository's release-candidate and stable tag format.        |
| `@semantic-release/commit-analyzer`         | Maps Conventional Commits to patch, minor, or major.                     |
| `@semantic-release/release-notes-generator` | Produces notes during a semantic-release run.                            |
| `@semantic-release/exec`                    | Runs `dotnet publish` with `${nextRelease.version}` when semantic-release owns publication. |
| `@semantic-release/github`                  | Provides GitHub release integration when semantic-release owns publication. |

[`scripts/calculate-version.mjs`](scripts/calculate-version.mjs) calls the semantic-release API in dry-run mode and reads `branches`, `tagFormat`, and the configured options of `commit-analyzer` from that file, so editing the config changes what the adapter calculates. Only the analyzer is loaded, because it alone decides a version; the release-notes, exec, and github plugins act on a release instead of calculating one. That also keeps the calculation offline: the adapter needs no token.

See the official [configuration guide](https://semantic-release.org/usage/configuration), [workflow configuration](https://semantic-release.org/usage/workflow-configuration/), and [GitHub Actions guide](https://semantic-release.org/recipes/ci-configurations/github-actions/)..

## Version sources

`commit-analyzer` returns a **base** version. The release identity then comes from a tag, or from the preview train when nothing is tagged:

| Commit state                | Version           | Source                                                     |
| --------------------------- | ----------------- | ---------------------------------------------------------- |
| Untagged commit on `main`   | `1.0.0-preview.N` | the base the analyzer calculated, plus a Git-derived ordinal |
| Commit tagged `vx.y.z-rc.N` | `x.y.z-rc.N`      | the tag at `HEAD`, used exactly as written                  |
| Commit tagged `vx.y.z`      | `x.y.z`           | the tag at `HEAD`, used exactly as written                  |

A stable tag outranks a release-candidate tag on the same commit, so a validated candidate is promoted without being rebuilt or renumbered.

semantic-release does not produce a prerelease suffix on this branch model. Its `prerelease` option numbers `1.0.0-preview.1`, `1.0.0-preview.2`, and so on, but it requires a separate release branch beside the prerelease branch, and the sample's whole premise is that a tag on `main` promotes a candidate. The preview ordinal therefore comes from Git, exactly as it does for Arcade.

These behaviours are measured, not assumed. `fix:` releases a patch, `feat:` releases a minor, and the `BREAKING CHANGE:` footer releases a major:

| Commit                                | Next base | Note                                                              |
| ------------------------------------- | --------- | ----------------------------------------------------------------- |
| `fix: correct the export`             | patch     |                                                                   |
| `feat: add customer export`           | minor     |                                                                   |
| `feat: replace the API` + `BREAKING CHANGE:` footer | major | The footer is how the default preset reads a breaking change. |
| `feat!: replace the API`              | none      | The default `angular` preset does not treat a `!` header as breaking. |
| `chore:`, `docs:`, `ci:`              | none      | No release is declared, so the shipped line is kept.              |

When the analyzer declares no release, the adapter keeps the nearest reachable release line so the build is still versioned, and the run stays an internal build. Before anything has shipped it falls back to semantic-release's first release of `1.0.0`, which is why the initialization commit calculates `1.0.0-preview.0`.

## Preview numbering and merge strategy

The preview ordinal counts every commit since the train baseline, which is the nearest reachable release tag that is not a release candidate. A release candidate identifies a candidate rather than a train, so an `rc` tag does not reset the counter.

| Merge style                        | Five-commit PR                  | Change per PR |
| ---------------------------------- | ------------------------------- | ------------- |
| Squash and merge                   | `1.0.0-preview.0` → `.1` → `.2` | +1            |
| Merge commit (`--no-ff`)           | `1.0.0-preview.0` → `.6` → `.12`| +6            |
| Rebase and merge (or fast-forward) | `1.0.0-preview.0` → `.5` → `.10`| +5            |

This matches GitVersion, which also counts every commit, and is the opposite of MinVer, which follows the first parent and advances by one per pull request. Enable squash merging and disable rebase merging: **Settings → General → Pull Requests** → uncheck *Allow rebase merging*. This comparison repository uses that shared setting for all samples; the test suite covers the merge-commit path so the difference stays visible.

## Project structure

- [`src/`](src/) contains the Web API, which has no versioning package reference.
- [`tests/semantic-release-versioning-sample.Tests/`](tests/semantic-release-versioning-sample.Tests/) contains Git-history tests that run the real script and the real semantic-release API against temporary repositories.
- [`release.config.cjs`](release.config.cjs), [`package.json`](package.json), and [`scripts/`](scripts/) stay at the sample root for Node tool discovery.
- [`.github/workflows/build-and-publish.yml`](.github/workflows/build-and-publish.yml) builds, calculates the version, and publishes.

## Local steps

```powershell
npm install
npx semantic-release --dry-run --no-ci
```

The dry run needs a repository URL, which semantic-release reads from the git origin, plus a `main` branch and valid Git metadata.

## Release phases

Merges to `main` create untagged previews. Use explicit tags for release candidates and stable releases:

```text
v1.0.0-rc.1
v1.0.0-rc.2
v1.0.0
```

Release Drafter owns the human-reviewed draft and the publication state. semantic-release contributes the release decision; `@semantic-release/github` and `@semantic-release/exec` are configured for a setup where semantic-release owns publication, which this repository does not enable.

## CI scenario

The `calculate-version` job runs [`scripts/calculate-version.sh`](scripts/calculate-version.sh) in its `github-output` mode. The script reads the analyzer, resolves the release identity, and reports the version, the assembly version, the informational version, the deployment environment, and the commit. Previews then receive `.{yy}{julian}.{run_number}` (for example `.26123.42`) so every CI run is unique, while a tagged release is published exactly as tagged.

| Ref                          | Version                    | Environment  |
| ---------------------------- | -------------------------- | ------------ |
| `refs/heads/main` (initial)  | `1.0.0-preview.0`          | `none`       |
| `refs/heads/main` (after PR) | `1.0.0-preview.1.26123.42` | `dev`        |
| `refs/tags/v1.0.0-rc.1`      | `1.0.0-rc.1`               | `staging`    |
| `refs/tags/v1.0.0`           | `1.0.0`                    | `production` |

The suffix uses `date -u +%y%j` (two-digit year and Julian day) plus `GITHUB_RUN_NUMBER`, matching the GitVersion and MinVer samples. The initialization build at ordinal zero carries no suffix and no audience, so it never publishes. Only `rc` and stable tags are release identities; any other tag ref fails the job instead of silently producing a preview artifact for a tagged commit.

The publish job consumes the one value the script reports:

```text
dotnet publish ... -p:Version=<calculated-version>
```

Example release scenario:

1. Create **PR1** with a `feat:` title and merge it into `main`. The analyzer calculates base `1.0.0`, CI publishes an untagged `1.0.0-preview.1.<suffix>` draft build, and no tag is created.
2. Create **PR2** with another `feat:` title and merge it into `main`. CI publishes `1.0.0-preview.2.<suffix>`.
3. Validate preview 2 and tag that exact commit `v1.0.0-rc.1`. The workflow reads the tag directly and publishes prerelease `1.0.0-rc.1`.
4. Find a release-candidate problem, create a `fix:` PR, and merge it into `main`. semantic-release has no release-candidate channel on `main`, so the commit returns to the preview train, keeps counting commits, and is published as an internal `dev` build such as `1.0.0-preview.3.<suffix>`. After validation, tag that commit `v1.0.0-rc.2`.
5. Run the RC2 checks. When they pass, tag the approved RC2 commit `v1.0.0`. CI publishes stable version `1.0.0`.

Only RC and stable commits receive Git tags. Keep `fetch-depth: 0`, because a shallow clone can hide the tags the analyzer reads.

There is no release intent to compare a tag against. A well-formed tag with the `v` prefix is the release identity and is used exactly as written, so a mistyped `v9.9.9` publishes `9.9.9` instead of being rejected. A tag without the prefix, such as `1.0.0`, does not match `tagFormat` and is ignored. Reviewing the tag belongs to release review or a workflow step rather than to the version script.

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
# No intent file to edit: a `feat:` after the stable tag opens 1.1.0 on its own.
git switch -c feature/add-authorization
git add -A
git commit -m "feat: add authorization"
git push -u origin feature/add-authorization
```

## Local validation

```bash
npm ci
bash scripts/calculate-version.sh
bash scripts/calculate-version.sh github-output          # reads GITHUB_RUN_NUMBER
dotnet publish src/semantic-release-versioning-sample.csproj -p:Version=$(bash scripts/calculate-version.sh)
dotnet test tests/semantic-release-versioning-sample.Tests/semantic-release-versioning-sample.Tests.csproj
```

The tests install the sample's Node dependencies once, copy the real [`release.config.cjs`](release.config.cjs) and the real [`scripts/`](scripts/) into a temporary repository each, and compare the adapter's preview base against the version the analyzer calculated. That way the script cannot invent a version the tool did not calculate, and both workflows are checked for delegating to the script instead of calculating inline.

## Tradeoffs

semantic-release is the only option here whose release decision depends entirely on commit messages, which makes the history the source of truth but also makes an unreviewed squash-commit title a version change. It is also the only option that needs a Node runtime and network-installed packages in the version job.

This sample reads semantic-release's decision instead of letting semantic-release publish. The tool can tag, publish, and write notes on its own, and it does all of that in the configured setup, but then the release timing belongs to the push rather than to a reviewed tag, and the .NET publish would need the artifact path to line up with the Node release. Keeping the calculation in dry-run mode preserves the review step the other samples share, at the cost of supporting only part of what semantic-release can do.

Because the calculation is a dry run, the version job needs no GitHub token, but it does need `npm ci` and the full history. The default `angular` preset is also easy to over-trust: `feat!:` is not a breaking marker under it, so the `BREAKING CHANGE:` footer is the only reliable form.
