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
    [string]$BuildConfig = "Release"
)

$ErrorActionPreference = "Stop"

$BuildRelativePath = "bin\BuildOutput"
$LogDirectory = "$PSScriptRoot\buildlogs"
$Solution     = "$PSScriptRoot\Microsoft.Extensions.Configuration.Azconfig.sln"

# Create the log directory.
if ((Test-Path -Path $LogDirectory) -ne $true) {
    New-Item -ItemType Directory -Path $LogDirectory | Write-Verbose
}

# Build (We use 'publish' to pull the Microsoft.Azconfig.Client.dll to be able to include it in the Microsoft.Extensions.Configuration.Azconfig NuGet package)
dotnet publish -o "$BuildRelativePath" -c $BuildConfig "$Solution" /p:OutDir=$BuildRelativePath | Tee-Object -FilePath "$LogDirectory\build.log"

exit $LASTEXITCODE
