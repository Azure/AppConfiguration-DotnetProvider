$ErrorActionPreference = "Stop"

$dotnet = & "$PSScriptRoot/build/resolve-dotnet.ps1"

& $dotnet test "$PSScriptRoot\tests\Tests.AzureAppConfiguration\Tests.AzureAppConfiguration.csproj" --logger trx

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $dotnet test "$PSScriptRoot\tests\Tests.AzureAppConfiguration.AspNetCore\Tests.AzureAppConfiguration.AspNetCore.csproj" --logger trx

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $dotnet test "$PSScriptRoot\tests\Tests.AzureAppConfiguration.Functions.Worker\Tests.AzureAppConfiguration.Functions.Worker.csproj" --logger trx

exit $LASTEXITCODE