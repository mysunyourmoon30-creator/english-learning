FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY global.json EnglishMasterAI.sln ./
COPY src/EnglishMasterAI.Data/EnglishMasterAI.Data.csproj src/EnglishMasterAI.Data/
COPY src/EnglishMasterAI.Migrations.PostgreSql/EnglishMasterAI.Migrations.PostgreSql.csproj src/EnglishMasterAI.Migrations.PostgreSql/
COPY src/EnglishMasterAI.Web/EnglishMasterAI.Web.csproj src/EnglishMasterAI.Web/
RUN dotnet restore src/EnglishMasterAI.Web/EnglishMasterAI.Web.csproj

COPY src/ src/
RUN dotnet publish src/EnglishMasterAI.Web/EnglishMasterAI.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080

RUN mkdir -p /var/lib/englishmaster/keys /var/lib/englishmaster/audio-cache \
    && chown -R "$APP_UID:$APP_UID" /var/lib/englishmaster
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl --fail --silent http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "EnglishMasterAI.Web.dll"]
