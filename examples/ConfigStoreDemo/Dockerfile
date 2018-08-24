FROM microsoft/aspnetcore:2.0
WORKDIR /app
EXPOSE 80
ENV ASPNETCORE_URLS=http://*:80
COPY ["bin/PublishOut", "."]
ENTRYPOINT ["dotnet", "ConfigStoreDemo.dll"]