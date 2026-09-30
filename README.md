# .NET versioning strategy lab

This repository compares five ways to version the same .NET 10 Web API:

- [GitVersion](samples/gitversion-versioning-sample/README.md): branch-aware Git history calculation.
- [MinVer](samples/minver-versioning-sample/README.md): small tag-first MSBuild package.
- [Nerdbank.GitVersioning](samples/nerdbank-versioning-sample/README.md): reproducible `version.json` and Git height.
- [Arcade](samples/arcade-versioning-sample/README.md): Microsoft engineering build infrastructure.
- [semantic-release](samples/semantic-release-versioning-sample/README.md): Node release orchestration with an explicit .NET publish command.

## Release contract

Use GitHub Flow with `main` and short-lived feature branches. Each merge to `main` creates an untagged preview whose height increases automatically. RC and stable tags are explicit and immutable:

```text
1.0.0-preview.1
1.0.0-preview.2
v1.0.0-rc.1
v1.0.0-rc.2
v1.0.0
```

Every push to `main` updates an unpublished preview Release Drafter draft without creating a tag. RC tags and the stable tag publish the matching Release Drafter note. The workflow uses `fetch-depth: 0` because Git-derived tools need complete history.

## Concrete release scenario

Use this command sequence as the shared example for every tool sample. The preview labels are the policy names; individual tools may calculate a different numeric suffix for untagged commits.

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
