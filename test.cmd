cd /D "%~dp0"

dotnet test tests\Tests.AzureAppConfiguration\Tests.AzureAppConfiguration.csproj --logger trx ||  exit /b 1
dotnet test tests\Tests.AzureAppConfiguration.AspNetCore\Tests.AzureAppConfiguration.AspNetCore.csproj --logger trx ||  exit /b 1
dotnet test tests\Tests.AzureAppConfiguration.Functions.Worker\Tests.AzureAppConfiguration.Functions.Worker.csproj --logger trx ||  exit /b 1
