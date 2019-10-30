<#
.Synopsis
This script creates NuGet packages from all of the projects in this repo. 

Note: build.cmd should be run before running this script.

#>

[CmdletBinding()]
param(
)

$ErrorActionPreference = "Stop"

$PrebuiltBinariesDir = "bin\BuildOutput"
$PublishRelativePath = "bin\PackageOutput"
$LogDirectory = "$PSScriptRoot\buildlogs"

$AzureAppConfigurationProjectName = "Microsoft.Extensions.Configuration.AzureAppConfiguration"
$AzureAppConfigurationProjectPath = "$PSScriptRoot\src\$AzureAppConfigurationProjectName\$AzureAppConfigurationProjectName.csproj"
$AzureAppConfigurationOutputPath = "$PSScriptRoot\src\$AzureAppConfigurationProjectName\$PublishRelativePath"

$AzureAppConfigurationAspNetCoreProjectName = "Microsoft.Azure.AppConfiguration.AspNetCore"
$AzureAppConfigurationAspNetCoreProjectPath = "$PSScriptRoot\src\$AzureAppConfigurationAspNetCoreProjectName\$AzureAppConfigurationAspNetCoreProjectName.csproj"
$AzureAppConfigurationAspNetCoreOutputPath = "$PSScriptRoot\src\$AzureAppConfigurationAspNetCoreProjectName\$PublishRelativePath"

# Create the log directory.
if ((Test-Path -Path $LogDirectory) -ne $true) {
    New-Item -ItemType Directory -Path $LogDirectory | Write-Verbose
}

# The build system expects pre-built binaries to be in the folder pointed to by 'OutDir'.
dotnet pack -o "$AzureAppConfigurationOutputPath" /p:OutDir="$PrebuiltBinariesDir" /p:NuspecFile="$($PrebuiltBinariesDir)\$AzureAppConfigurationProjectName.nuspec" "$AzureAppConfigurationProjectPath" --no-build | Tee-Object -FilePath "$LogDirectory\build.log"
dotnet pack -o "$AzureAppConfigurationAspNetCoreOutputPath" /p:OutDir="$PrebuiltBinariesDir" "$AzureAppConfigurationAspNetCoreProjectPath" --no-build | Tee-Object -FilePath "$LogDirectory\build.log"

exit $LASTEXITCODE
