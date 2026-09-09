FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY SBLSS.System.sln ./
COPY TestingSystem.Web/TestingSystem.Web.csproj TestingSystem.Web/
RUN dotnet restore SBLSS.System.sln

COPY TestingSystem.Web/ TestingSystem.Web/
RUN dotnet publish TestingSystem.Web/TestingSystem.Web.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TestingSystem.Web.dll"]
