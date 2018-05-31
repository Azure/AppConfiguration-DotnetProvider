<#
.Synopsis
This script creates NuGet packages from all of the projects in this repo.

.Parameter BuildConfig
Indicates whether the build config should be set to Debug or Release. The default is Release.
#>

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug','Release')]
    [string]$BuildConfig = "Release"
)

$ErrorActionPreference = "Stop"

$PublishRelativePath = "bin\PackageOutput"

$LogDirectory = "$PSScriptRoot\buildlogs"
$Solution     = "$PSScriptRoot\Microsoft.Extensions.Configuration.Azconfig.sln"

# Create the log directory.
if ((Test-Path -Path $LogDirectory) -ne $true) {
    New-Item -ItemType Directory -Path $LogDirectory | Write-Verbose
}

# Pack
dotnet pack -o "$PublishRelativePath" -c $BuildConfig "$Solution" | Tee-Object -FilePath "$LogDirectory\build.log"

exit $LASTEXITCODE
