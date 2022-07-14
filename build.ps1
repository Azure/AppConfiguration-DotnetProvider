<#
.Synopsis
This script builds all of the projects in this repo.

.Parameter BuildConfig
Indicates whether the build config should be set to Debug or Release. The default is Release.
#>

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug','Release')]
    [string]$BuildConfig = "Release",
    
    [Parameter()]
    [switch]$RestoreOnly = $false
)

$ErrorActionPreference = "Stop"

$LogDirectory = "$PSScriptRoot\buildlogs"
$Solution     = "$PSScriptRoot\Microsoft.Extensions.Configuration.AzureAppConfiguration.sln"
$dotnet       = & "$PSScriptRoot/build/resolve-dotnet.ps1"

# Create the log directory.
if ((Test-Path -Path $LogDirectory) -ne $true) {
    New-Item -ItemType Directory -Path $LogDirectory | Write-Verbose
}

if ($RestoreOnly)
{
    & $dotnet restore "$Solution"
}
else
{
    & $dotnet build -c $BuildConfig "$Solution" | Tee-Object -FilePath "$LogDirectory\build.log"
}

exit $LASTEXITCODE
