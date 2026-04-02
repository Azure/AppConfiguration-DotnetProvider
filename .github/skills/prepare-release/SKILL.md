---
name: prepare-release
description: "Prepare a release for the Azure App Configuration .NET Provider. Use when user mentions release preparation, version bump, creating merge PRs, preview release, or stable release for this project."
---

# Prepare Release

This skill automates the release preparation workflow for the [Azure App Configuration .NET Provider](https://github.com/Azure/AppConfiguration-DotnetProvider) project.

## When to Use This Skill

Use this skill when you need to:
- Bump the package version for a new stable or preview release
- Create merge PRs to sync branches (main → release/stable, preview → release/preview)
- Prepare all the PRs needed before publishing a new release

## Background

### Repository Information
- **GitHub Repo**: https://github.com/Azure/AppConfiguration-DotnetProvider
- **Packages** (all 3 are released together with the same version):
  1. `Microsoft.Extensions.Configuration.AzureAppConfiguration` — Base package
  2. `Microsoft.Azure.AppConfiguration.AspNetCore` — ASP.NET Core package
  3. `Microsoft.Azure.AppConfiguration.Functions.Worker` — Azure Functions (isolated worker) package

### Branch Structure
- `main` – primary development branch for stable releases
- `preview` – development branch for preview releases
- `release/stable/v{major}` – release branch for stable versions (e.g., `release/stable/v8`)
- `release/preview/v{major}` – release branch for preview versions (e.g., `release/preview/v8`)

### Version Files
The version must be updated in the `<OfficialVersion>` property in **all three** `.csproj` files simultaneously:
1. `src/Microsoft.Extensions.Configuration.AzureAppConfiguration/Microsoft.Extensions.Configuration.AzureAppConfiguration.csproj`
2. `src/Microsoft.Azure.AppConfiguration.AspNetCore/Microsoft.Azure.AppConfiguration.AspNetCore.csproj`
3. `src/Microsoft.Azure.AppConfiguration.Functions.Worker/Microsoft.Azure.AppConfiguration.Functions.Worker.csproj`

Each file contains a line like:
```xml
<OfficialVersion>8.5.0</OfficialVersion>
```

### Version Format
- **Stable**: `{major}.{minor}.{patch}` (e.g., `8.5.0`)
- **Preview**: `{major}.{minor}.{patch}-preview.{N}` (e.g., `8.6.0-preview.1`)

## Quick Start

Ask the user whether this is a **stable** or **preview** release, and what the **new version number** should be. Then follow the appropriate workflow below.

---

### Workflow A: Stable Release

#### Step 1: Version Bump PR

Create a version bump PR targeting `main` by running the version bump script:

```powershell
.\scripts\version-bump.ps1 <new_version>
```

For example: `.\scripts\version-bump.ps1 8.6.0`

The script will automatically:
1. Read the current version from the first `.csproj` file.
2. Create a new branch from `main` named `<username>/version-<new_version>` (e.g., `linglingye/version-8.6.0`).
3. Update the `<OfficialVersion>` in all three `.csproj` files.
4. Commit, push, and create a PR to `main` with title: `Version bump <new_version>`.

When the script prompts `Proceed? [y/N]`, confirm by entering `y`.

**Sample PR**: https://github.com/Azure/AppConfiguration-DotnetProvider/pull/723

#### Step 2: Merge Main to Release Branch

After the version bump PR is merged, create a PR to merge `main` into the stable release branch by running:

```powershell
.\scripts\merge-to-release.ps1 <new_version>
```

For example: `.\scripts\merge-to-release.ps1 8.6.0`

When the script prompts `Proceed? [y/N]`, confirm by entering `y`.

> **Important**: Use "Create a merge commit" (not "Squash and merge") when merging this PR to preserve commit history.

**Sample PR**: https://github.com/Azure/AppConfiguration-DotnetProvider/pull/724

---

### Workflow B: Preview Release

#### Step 1: Version Bump PR

Create a version bump PR targeting `preview` by running the version bump script with the `-Preview` flag:

```powershell
.\scripts\version-bump.ps1 <new_version> -Preview
```

For example: `.\scripts\version-bump.ps1 8.6.0-preview.1 -Preview`

When the script prompts `Proceed? [y/N]`, confirm by entering `y`.

**Sample PR**: https://github.com/Azure/AppConfiguration-DotnetProvider/pull/708

#### Step 2: Merge Preview to Release Branch

After the version bump PR is merged, create a PR to merge `preview` into the preview release branch by running:

```powershell
.\scripts\merge-to-release.ps1 <new_version> -Preview
```

For example: `.\scripts\merge-to-release.ps1 8.6.0-preview.1 -Preview`

When the script prompts `Proceed? [y/N]`, confirm by entering `y`.

> **Important**: Use "Create a merge commit" (not "Squash and merge") when merging this PR to preserve commit history.

**Sample PR**: https://github.com/Azure/AppConfiguration-DotnetProvider/pull/727

---

## Review Checklist

Each PR should be reviewed with the following checks:
- [ ] Version is updated consistently across all 3 `.csproj` files
- [ ] No unintended file changes are included
- [ ] Merge PRs use **merge commit** strategy (not squash)
- [ ] Branch names follow the naming conventions
- [ ] All CI checks pass
