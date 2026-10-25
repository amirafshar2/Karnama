# Multi-stage build; the runtime image has no SDK and runs as the non-root "app" user (uid 1654).
# Build from the repository root:  docker build -f src/Karnama.Api/Dockerfile -t karnama-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Karnama.sln ./
COPY src/ src/
RUN dotnet publish src/Karnama.Api/Karnama.Api.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Karnama.Api.dll"]
