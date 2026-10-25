# Telegram gateway for the server OUTSIDE Iran. Build from the repository root:
#   docker build -f src/Karnama.TelegramGateway/Dockerfile -t karnama-telegram-gateway .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Karnama.TelegramGateway/ src/Karnama.TelegramGateway/
RUN dotnet publish src/Karnama.TelegramGateway/Karnama.TelegramGateway.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0 Gateway__QueuePath=/data/queue
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Karnama.TelegramGateway.dll"]
