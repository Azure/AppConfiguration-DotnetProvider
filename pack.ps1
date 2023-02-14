<#
.Synopsis
This script creates NuGet packages from all of the projects in this repository.
Note: build.cmd should be run before running this script.

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
$dotnet = & "$PSScriptRoot/build/resolve-dotnet.ps1"

$targetProjects = @(
    "Microsoft.Extensions.Configuration.AzureAppConfiguration",
    "Microsoft.Azure.AppConfiguration.AspNetCore",
    "Microsoft.Azure.AppConfiguration.Functions.Worker"
)

# Create the log directory.
if ((Test-Path -Path $LogDirectory) -ne $true) {
    New-Item -ItemType Directory -Path $LogDirectory | Write-Verbose
}

foreach ($project in $targetProjects)
{
    $projectPath = "$PSScriptRoot\src\$project\$project.csproj"
    $outputPath = "$PSScriptRoot\src\$project\$PublishRelativePath"

    & $dotnet pack -c $BuildConfig -o "$outputPath" "$projectPath" --no-build | Tee-Object -FilePath "$LogDirectory\build.log"
}

exit $LASTEXITCODE
