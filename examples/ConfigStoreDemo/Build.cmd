set ROOT=%~dp0

dotnet publish -c Release %ROOT%ConfigStoreDemo.csproj -o bin\PublishOut

docker build -t configstoredemo -f %ROOT%Dockerfile %ROOT%
