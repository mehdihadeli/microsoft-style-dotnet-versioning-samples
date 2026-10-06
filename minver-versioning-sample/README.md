# MinVer sample

MinVer CLI calculates versions from Git tags. This sample uses tag prefix `v`, starts at minimum line `1.0`, uses `preview` for untagged development builds, and keeps Git height enabled so each merge gets a unique preview. [`scripts/calculate-version.sh`](scripts/calculate-version.sh) holds those options and appends build metadata (`{yy}{julian}.{run_number}`) to prerelease builds. CI passes the calculated version to the .NET SDK with `-p:Version`; this sample does not require MinVer MSBuild integration.

## Configuration

MinVer deliberately has no configuration file. Its documentation contrasts it with Nerdbank.GitVersioning, which encapsulates versioning in a config file, precisely because MinVer has no config-file bootstrapper. MinVer reads Git tags plus one of three configuration surfaces: MSBuild properties for the `MinVer` package, environment variables for either package, or command-line options for `minver-cli`.

This sample uses `minver-cli`, so the CLI options are the strategy and [`scripts/calculate-version.sh`](scripts/calculate-version.sh) holds them:

| CLI option                    | Meaning                                                          |
| ----------------------------- | ---------------------------------------------------------------- |
| `-t v`                        | Reads release tags such as `v1.0.0-rc.1`.                        |
| `-m 1.0`                      | Keeps pre-release builds on the `1.0` line before the first tag. |
| `-p preview`                  | Uses `preview` as the default prerelease identifier.             |
| Omit `-i` (`--ignore-height`) | Keeps Git height enabled so untagged builds are unique.          |

The script resolves the CLI from `MINVER_CLI`, then from `.tools/minver/minver` in the sample, then from the `PATH`. It accepts an optional `github-output` argument that switches it from printing a version to emitting the CI outputs. Every caller uses the script, so the options are declared once instead of being repeated in each workflow.

`minver-cli` takes the working directory as an optional argument and otherwise uses the current directory. Git discovery walks upwards, so running it from this sample directory still finds the repository tags.

See the official [MinVer options guide](https://github.com/adamralph/minver#options), [how MinVer works](https://github.com/adamralph/minver#how-it-works), and [CLI reference](https://github.com/adamralph/minver/blob/main/_autodocs/cli-reference.md).

## Version sources

MinVer reads tags and Git height; a version tag at `HEAD` is returned exactly as tagged:

| Commit state                | Version           | Source                                    |
| --------------------------- | ----------------- | ----------------------------------------- |
| Untagged commit on `main`   | `1.0.0-preview.N` | `-m 1.0` and `-p preview` plus Git height |
| Commit tagged `vx.y.z-rc.N` | `x.y.z-rc.N`      | the tag at `HEAD`                         |
| Commit tagged `vx.y.z`      | `x.y.z`           | the tag at `HEAD`                         |

MinVer does not read commit messages, so nothing derives the next train from `feat:`, `fix:`, or `!`. The minimum major and minor option declares the development line, and starting a new line such as `1.1` is a reviewed change to the script. A stable tag outranks a prerelease tag on the same commit, which is how a validated release candidate is promoted without being rebuilt.

CI additionally appends `.{yy}{julian}.{run_number}` to prerelease versions (see [CI scenario](#ci-scenario)) so every build is unique; the local and tagged versions above stay unchanged.

## Project structure

- [`src/`](src/) contains the Web API, which has no versioning package reference.
- [`tests/minver-versioning-sample.Tests/`](tests/minver-versioning-sample.Tests/) contains real MinVer Git-history tests that run the script and the CLI against temporary repositories.
- [`scripts/calculate-version.sh`](scripts/calculate-version.sh) declares the CLI options and appends the prerelease build suffix.
- [`.github/workflows/build-and-publish.yml`](.github/workflows/build-and-publish.yml) builds, calculates the version, and publishes.

## Preview numbering and merge strategy

MinVer appends Git height to the pre-release identifiers. Height walks the **first parent** of every merge, so it counts one step per accepted pull request rather than one per commit in the branch:

| Merge style                        | Five-commit PR                 | Change per PR |
| ---------------------------------- | ------------------------------ | ------------- |
| Merge commit (`--no-ff`)           | `1.0.0-preview` → `.1` → `.2`  | +1            |
| Squash and merge                   | `1.0.0-preview` → `.1` → `.2`  | +1            |
| Rebase and merge (or fast-forward) | `1.0.0-preview` → `.5` → `.10` | +5            |

Height is the number of first-parent commits after the version-source commit, equivalent to `git rev-list --first-parent --count` relative to that commit. The MinVer documentation states that when history diverges it follows the paths in the order the commit's parents are stored in Git, and the first parent is the branch that was checked out when the merge was performed.

This is the opposite of a plain commit-count calculator such as GitVersion, whose preview number counts every commit reachable from `HEAD`. MinVer therefore tolerates merge commits: a merge commit contributes exactly one to height, however many commits the pull request contained. What breaks the one-pull-request-one-step contract is rebase merging, which replays every branch commit onto `main` as a first-parent commit.

Enable squash merging and merge commits, and disable rebase merging: **Settings → General → Pull Requests** → uncheck *Allow rebase merging*. Either squash merging or merge commits is then safe.

Rebase merging cannot be detected after the fact, because a rebased pull request leaves the same linear history as a squash merge. The repository setting is the only reliable control, which is why this sample documents it instead of shipping a history check. The integration tests squash-merge by default and also cover the merge-commit path, because the two must agree.

The GitHub pull-request settings are repository-wide. In this comparison repository the shared setting is squash-only, because the GitVersion, Arcade, and semantic-release samples count every commit rather than first-parent commits.

## CI scenario

The `calculate-version` job installs MinVer CLI `8.0.0` and runs [`scripts/calculate-version.sh`](scripts/calculate-version.sh) in its `github-output` mode. The script passes the CLI options and reports the calculated version, the assembly version, the informational version, the deployment environment, and the commit. It then appends `.{yy}{julian}.{run_number}` (for example `.26123.42`) to prerelease versions so every CI run is unique. Stable versions pass through unchanged.

| Ref                          | Version                    | Environment  |
| ---------------------------- | -------------------------- | ------------ |
| `refs/heads/main` (after PR) | `1.0.0-preview.1.26123.42` | `dev`        |
| `refs/tags/v1.0.0-rc.1`      | `1.0.0-rc.1.26123.43`      | `staging`    |
| `refs/tags/v1.0.0`           | `1.0.0`                    | `production` |

The suffix uses `date -u +%y%j` (two-digit year and Julian day) plus `GITHUB_RUN_NUMBER`, matching the GitVersion and nerdbank-versioning samples. At initial Git height zero MinVer returns `1.0.0-preview` without a numeric suffix, so that baseline never publishes and keeps environment `none`. Only `rc` and stable tags are release identities; any other tag ref fails the job instead of silently producing a preview artifact for a tagged commit.

The publish job consumes the one value the script reports:

```text
dotnet publish ... -p:Version=<calculated-version>
```

MinVer appends Git height to the prerelease identifiers. At initial Git height zero the native version is `1.0.0-preview`; successive untagged commits produce `1.0.0-preview.1` and `1.0.0-preview.2`. After a prerelease tag, MinVer preserves that tag's identifiers and appends Git height.

Example release scenario:

1. Create **PR1** with a feature and merge it into `main`. CI calculates the first `1.0.0-preview` build, adds the build suffix, creates no tag, and keeps the Release Drafter release as a draft.
2. Create **PR2** with another feature and merge it into `main`. CI calculates the second `1.0.0-preview` build with the same treatment.
3. Validate preview 2 and tag that exact commit `v1.0.0-rc.1`. MinVer uses the tag exactly and CI publishes prerelease `1.0.0-rc.1`.
4. Find a release-candidate problem, create a fix PR, and merge it into `main`. MinVer calculates an untagged build such as `1.0.0-rc.1.1`; the script treats an untagged build as `dev` rather than as a staged release. After validation, tag that commit `v1.0.0-rc.2`.
5. Run the RC2 checks. When they pass, tag the approved RC2 commit `v1.0.0`. MinVer returns stable version `1.0.0` with no suffix.

Only RC and stable commits receive tags. Keep `fetch-depth: 0`, because a shallow clone can hide the tag MinVer needs.

There is no release intent to compare a tag against. A well-formed tag with the `v` prefix is the release identity and is used exactly as written, so a mistyped `v9.9.9` publishes `9.9.9` instead of being rejected. A tag without the prefix, such as `1.0.0`, is not a version tag at all and is ignored. Reviewing the tag belongs to release review or a workflow step rather than to the version script.

Each step above is one pull request, so squash merging and merge commits both produce one height increment per step (see [Preview numbering and merge strategy](#preview-numbering-and-merge-strategy)). Rebase merging would multiply the numbers shown here.

The shared command scenario for this sample is:

```bash
# 1.0.0-preview.1
# Squash-merge or merge-commit the pull request: one pull request, one height step.
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
# Change the minver_options line to -m 1.1 in scripts/calculate-version.sh, then commit the change.
git add -A
git commit -m "chore: start 1.1.0 preview train"
git push -u origin chore/prepare-1.1.0-preview
```

## Local validation

```bash
dotnet restore tests/minver-versioning-sample.Tests/minver-versioning-sample.Tests.csproj
dotnet tool install --tool-path .tools/minver minver-cli --version 8.0.0
bash scripts/calculate-version.sh
bash scripts/calculate-version.sh github-output          # reads GITHUB_RUN_NUMBER
dotnet build src/minver-versioning-sample.csproj -p:Version=$(bash scripts/calculate-version.sh)
dotnet test tests/minver-versioning-sample.Tests/minver-versioning-sample.Tests.csproj
```

On Windows the installed executable is `minver.exe`, and the script finds it without extra configuration. The tests install the same CLI into a temporary repository and run the real script, so no local installation is needed to run them.

The version calculator is [`minver-cli`](https://github.com/adamralph/minver#can-i-use-minver-to-version-software-which-is-not-built-using-a-net-sdk-style-project), configured by the options in [`scripts/calculate-version.sh`](scripts/calculate-version.sh). Because the script is the only place those options appear, the test suite checks that both workflows run the script instead of calling the CLI directly, so the strategy cannot drift between workflows. Preview and tagged scenarios assert the script's version and the CLI's version agree, so the script cannot invent a version the tool did not calculate.

## Tradeoffs

MinVer is the simplest choice when tags are the release authority. It does not infer versions from branch names or commit messages, so a `feat:` after a stable tag still produces a patch preview and a breaking-change marker is ignored. Its Git height can be affected by unrelated commits in a monorepo.

Its first-parent height is also more forgiving than a plain commit count: merge commits and squash merges both advance the preview by exactly one pull request, so only rebase merging needs to be disabled. That forgiving behaviour cannot be verified from Git history, so it depends on a repository setting rather than a CI check.

The same minimalism removes the release-intent check. MinVer cannot compare a tag with an expected version, so nothing rejects a mistyped or unintended release tag, and moving to the next `MAJOR.MINOR` line is a reviewed change to the option in [`scripts/calculate-version.sh`](scripts/calculate-version.sh) rather than a derived calculation. Because `minver-cli` has no configuration file, that one option is not self-documenting configuration; local builds also receive the version only when the script's output is passed to the SDK explicitly.
