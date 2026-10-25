# Background jobs (Hangfire) and the notification outbox. Build from the repository root:
#   docker build -f src/Karnama.Worker/Dockerfile -t karnama-worker .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Karnama.sln ./
COPY src/ src/
RUN dotnet publish src/Karnama.Worker/Karnama.Worker.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .
ENV DOTNET_EnableDiagnostics=0
USER app
ENTRYPOINT ["dotnet", "Karnama.Worker.dll"]
