# Installs .NET 6 for CI/CD environment
# see: https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script#examples

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12;

&([scriptblock]::Create((Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1'))) -Version 6.0.301