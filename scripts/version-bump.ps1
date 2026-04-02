<#
.SYNOPSIS
    Automates the version bump workflow for Azure App Configuration .NET Provider.
.DESCRIPTION
    Updates OfficialVersion in all 3 package .csproj files, creates a branch,
    commits, pushes, and opens a PR via the GitHub CLI (gh).
.PARAMETER NewVersion
    The version to bump to (e.g. 8.6.0 or 8.6.0-preview.1)
.PARAMETER Preview
    Target the preview branch instead of main
.EXAMPLE
    .\scripts\version-bump.ps1 8.6.0
    # stable release -> PR to main
.EXAMPLE
    .\scripts\version-bump.ps1 8.6.0-preview.1 -Preview
    # preview release -> PR to preview
.NOTES
    Prerequisites: git and gh (GitHub CLI) must be installed and authenticated
#>

param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$NewVersion,

    [switch]$Preview
)

$ErrorActionPreference = "Stop"

# ── Validate version format ──────────────────────────────────────────────────

if ($NewVersion -notmatch '^\d+\.\d+\.\d+(-preview(\.\d+)?)?$') {
    Write-Error "Invalid version format '$NewVersion'. Expected: X.Y.Z or X.Y.Z-preview[.N]"
    exit 1
}

if ($NewVersion -match '-preview' -and -not $Preview) {
    Write-Error "Version '$NewVersion' looks like a preview version. Did you forget -Preview?"
    exit 1
}

# ── Resolve paths & context ──────────────────────────────────────────────────

$ProjectDir = Split-Path $PSScriptRoot -Parent

$CsprojRelPaths = @(
    "src/Microsoft.Extensions.Configuration.AzureAppConfiguration/Microsoft.Extensions.Configuration.AzureAppConfiguration.csproj",
    "src/Microsoft.Azure.AppConfiguration.AspNetCore/Microsoft.Azure.AppConfiguration.AspNetCore.csproj",
    "src/Microsoft.Azure.AppConfiguration.Functions.Worker/Microsoft.Azure.AppConfiguration.Functions.Worker.csproj"
)

$CsprojFiles = $CsprojRelPaths | ForEach-Object { Join-Path $ProjectDir $_ }

# Determine target branch
$TargetBranch = if ($Preview) { "preview" } else { "main" }

# Get git username for branch naming
$GitUsername = git config user.name 2>$null
if (-not $GitUsername) {
    Write-Error "Could not determine git user.name. Please set it with: git config user.name <name>"
    exit 1
}
$BranchPrefix = ($GitUsername -split '\s+')[0].ToLower()
$BranchName = "$BranchPrefix/version-$NewVersion"

# ── Show plan ────────────────────────────────────────────────────────────────

Write-Host "-- New version     : $NewVersion"
Write-Host "-- Target branch   : $TargetBranch"
Write-Host "-- New branch      : $BranchName"
Write-Host ""

# ── Confirm with user ────────────────────────────────────────────────────────

$confirm = Read-Host "Proceed? [y/N]"
if ($confirm -notmatch '^[Yy]$') {
    Write-Host "Aborted."
    exit 0
}
Write-Host ""

# ── Create branch from target ────────────────────────────────────────────────

Push-Location $ProjectDir
try {
    Write-Host "-- Fetching latest $TargetBranch..."
    git fetch origin $TargetBranch

    Write-Host "-- Creating branch '$BranchName' from origin/$TargetBranch..."
    git checkout -b $BranchName "origin/$TargetBranch"

    # ── Read current version ─────────────────────────────────────────────────

    $content = [System.IO.File]::ReadAllText($CsprojFiles[0])
    if ($content -match '<OfficialVersion>([^<]+)</OfficialVersion>') {
        $CurrentVersion = $Matches[1]
    }
    else {
        throw "Could not find OfficialVersion in $($CsprojFiles[0])"
    }

    Write-Host "-- Current version : $CurrentVersion"

    if ($CurrentVersion -eq $NewVersion) {
        throw "Current version is already $NewVersion. Nothing to do."
    }

    # ── Update version in all .csproj files ──────────────────────────────────

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)

    foreach ($csproj in $CsprojFiles) {
        $fileName = Split-Path $csproj -Leaf
        Write-Host "-- Updating $fileName..."
        $text = [System.IO.File]::ReadAllText($csproj)
        $updated = $text -replace "<OfficialVersion>$([regex]::Escape($CurrentVersion))</OfficialVersion>", "<OfficialVersion>$NewVersion</OfficialVersion>"
        [System.IO.File]::WriteAllText($csproj, $updated, $utf8NoBom)
    }

    # ── Verify changes ───────────────────────────────────────────────────────

    Write-Host "-- Verifying updates..."
    foreach ($csproj in $CsprojFiles) {
        $text = [System.IO.File]::ReadAllText($csproj)
        if ($text -notmatch "<OfficialVersion>$([regex]::Escape($NewVersion))</OfficialVersion>") {
            throw "Version not updated in $(Split-Path $csproj -Leaf)"
        }
    }
    Write-Host "-- All version files updated"
    Write-Host ""

    # ── Commit, push, and create PR ──────────────────────────────────────────

    Write-Host "-- Committing changes..."
    git add $CsprojRelPaths
    git commit -m "Version bump $NewVersion"

    Write-Host "-- Pushing branch '$BranchName'..."
    git push origin $BranchName

    Write-Host "-- Creating pull request..."
    $Body = @"
Bump version from ``$CurrentVersion`` to ``$NewVersion``.

### Changes
- Updated ``OfficialVersion`` in all 3 package .csproj files:
  - ``Microsoft.Extensions.Configuration.AzureAppConfiguration.csproj``
  - ``Microsoft.Azure.AppConfiguration.AspNetCore.csproj``
  - ``Microsoft.Azure.AppConfiguration.Functions.Worker.csproj``

---
*This PR was created automatically by ``scripts/version-bump.ps1``.*
"@

    $PrUrl = gh pr create --base $TargetBranch --head $BranchName --title "Version bump $NewVersion" --body $Body

    Write-Host ""
    Write-Host "-- Done! PR created: $PrUrl"
}
finally {
    Pop-Location
}
