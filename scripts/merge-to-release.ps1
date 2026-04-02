<#
.SYNOPSIS
    Creates a PR to merge a development branch into its corresponding release branch.
.DESCRIPTION
    Used after a version bump PR has been merged. Creates a PR from main (or preview)
    into the appropriate release branch via the GitHub CLI (gh).
.PARAMETER Version
    The version that was just bumped (used to determine major version and PR title)
.PARAMETER Preview
    Merge preview -> release/preview/v{major} instead of main -> release/stable/v{major}
.EXAMPLE
    .\scripts\merge-to-release.ps1 8.6.0
    # main -> release/stable/v8
.EXAMPLE
    .\scripts\merge-to-release.ps1 8.6.0-preview.1 -Preview
    # preview -> release/preview/v8
.NOTES
    Prerequisites: git and gh (GitHub CLI) must be installed and authenticated
#>

param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [switch]$Preview
)

$ErrorActionPreference = "Stop"

# ── Validate version format ──────────────────────────────────────────────────

if ($Version -notmatch '^\d+\.\d+\.\d+(-preview(\.\d+)?)?$') {
    Write-Error "Invalid version format '$Version'. Expected: X.Y.Z or X.Y.Z-preview[.N]"
    exit 1
}

# ── Determine branches ───────────────────────────────────────────────────────

# Extract major version (e.g. "8" from "8.6.0" or "8.6.0-preview.1")
$MajorVersion = ($Version -split '\.')[0]

if ($Preview) {
    $SourceBranch = "preview"
    $TargetBranch = "release/preview/v$MajorVersion"
    $PrTitle = "Merge preview to release/preview/v$MajorVersion"
}
else {
    $SourceBranch = "main"
    $TargetBranch = "release/stable/v$MajorVersion"
    $PrTitle = "Merge main to release/stable/v$MajorVersion"
}

Write-Host "-- Source branch : $SourceBranch"
Write-Host "-- Target branch : $TargetBranch"
Write-Host "-- PR title      : $PrTitle"
Write-Host ""

# ── Confirm with user ────────────────────────────────────────────────────────

$confirm = Read-Host "Proceed? [y/N]"
if ($confirm -notmatch '^[Yy]$') {
    Write-Host "Aborted."
    exit 0
}
Write-Host ""

# ── Resolve project directory ────────────────────────────────────────────────

$ProjectDir = Split-Path $PSScriptRoot -Parent
Push-Location $ProjectDir

try {
    # ── Fetch latest branches ────────────────────────────────────────────────

    Write-Host "-- Fetching latest branches..."
    git fetch origin $SourceBranch
    git fetch origin $TargetBranch

    # ── Create PR ────────────────────────────────────────────────────────────

    Write-Host "-- Creating pull request..."
    $Body = @"
Merge ``$SourceBranch`` into ``$TargetBranch`` after version bump ``$Version``.

> **Important**: Use **Create a merge commit** (not "Squash and merge") when merging this PR to preserve commit history.

---
*This PR was created automatically by ``scripts/merge-to-release.ps1``.*
"@

    $PrUrl = gh pr create --base $TargetBranch --head $SourceBranch --title $PrTitle --body $Body

    Write-Host ""
    Write-Host "-- Done! PR created: $PrUrl"
    Write-Host ""
    Write-Host "WARNING: Use 'Create a merge commit' (not 'Squash and merge') when merging this PR."
}
finally {
    Pop-Location
}
