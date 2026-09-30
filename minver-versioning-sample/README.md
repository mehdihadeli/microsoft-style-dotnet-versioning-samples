# MinVer sample

MinVer CLI calculates versions from Git tags. This sample uses tag prefix `v`, starts at minimum line `1.0`, uses `preview` for untagged development builds, and keeps Git height enabled so each merge gets a unique preview. The settings live in [`scripts/calculate-version.sh`](scripts/calculate-version.sh).

## Configuration

MinVer CLI does not support a native JSON or YAML configuration file. The project-owned shell wrapper centralizes the CLI options instead:

| CLI option   | Meaning                                                          |
| ------------ | ---------------------------------------------------------------- |
| `-t v`       | Reads release tags such as `v1.0.0-rc.1`.                        |
| `-m 1.0`     | Keeps pre-release builds on the `1.0` line before the first tag. |
| `-p preview` | Uses `preview` as the default prerelease identifier.             |
| Omit `-i`    | Keeps Git height enabled so untagged builds are unique.          |

See the official [MinVer options guide](https://github.com/adamralph/minver#options), [how MinVer works](https://github.com/adamralph/minver#how-it-works), and [CLI reference](https://github.com/adamralph/minver/blob/main/_autodocs/cli-reference.md).

## Project structure

- [`src/`](src/) contains the Web API.
- [`tests/minver-versioning-sample.Tests/`](tests/minver-versioning-sample.Tests/) contains the real MinVer Git-history test.

Require squash merges on GitHub. MinVer height counts commits, not pull requests; preserving branch commits and adding a merge commit can look like a doubled version increment. The integration test uses squash merges and asserts that two accepted PRs advance the preview suffix once each.

## CI scenario

The `calculate-version` job installs MinVer CLI `8.0.0` and runs the project wrapper:

```text
MINVER_CLI="$RUNNER_TEMP/minver/minver" ./scripts/calculate-version.sh
```

The result is passed to `dotnet publish -p:Version=...`. On a tagged commit MinVer returns the tag version. At initial Git height zero, native MinVer returns `1.0.0-preview` without a numeric suffix. Successive untagged commits append Git height, producing versions such as `1.0.0-preview.1` and `1.0.0-preview.2`. After a prerelease tag, MinVer preserves that tag's identifiers and appends Git height.

Example release scenario:

1. Create **PR1** with a feature and merge it into `main`. CI calculates the first `1.0.0-preview` build, creates no tag, and keeps the Release Drafter release as a draft.
2. Create **PR2** with another feature and merge it into `main`. CI calculates the second `1.0.0-preview` build. Git height supplies the unique numeric suffix; the exact height can differ between repositories.
3. Validate preview 2 and tag that exact commit `v1.0.0-rc.1`. MinVer uses the tag exactly and CI publishes prerelease `1.0.0-rc.1`.
4. Find a release-candidate problem, create a fix PR, and merge it into `main`. MinVer calculates an untagged build such as `1.0.0-rc.1.1`; the workflow accepts it as an internal prerelease. After validation, tag that commit `v1.0.0-rc.2`.
5. Run the RC2 checks. When they pass, tag the approved RC2 commit `v1.0.0`. MinVer returns stable version `1.0.0`.

Only RC and stable commits receive tags. Keep `fetch-depth: 0`, because a shallow clone can hide the tag MinVer needs.

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

```bash
dotnet restore tests/minver-versioning-sample.Tests/minver-versioning-sample.Tests.csproj
dotnet tool install --tool-path .tools/minver minver-cli --version 8.0.0
MINVER_CLI=.tools/minver/minver bash scripts/calculate-version.sh
dotnet build src/minver-versioning-sample.csproj -p:Version=$(MINVER_CLI=.tools/minver/minver bash scripts/calculate-version.sh)
dotnet test tests/minver-versioning-sample.Tests/minver-versioning-sample.Tests.csproj
```

On Windows, run these commands from Git Bash. The wrapper automatically uses `minver.exe` when the local tool installation has the Windows executable name.

The local and CI calculator is [`scripts/calculate-version.sh`](scripts/calculate-version.sh). The complete workflow is in [`.github/workflows/minver.yml`](../.github/workflows/minver.yml).

## Tradeoffs

MinVer is the simplest choice when tags are the release authority. It does not infer versions from branch names or commit messages, and its Git height can be affected by unrelated commits in a monorepo.
