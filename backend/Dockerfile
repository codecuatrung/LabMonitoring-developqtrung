# Production image for the Lab Monitoring API (multi-stage: SDK build -> slim ASP.NET runtime).
# Build context is ./backend (see deploy/docker-compose.prod.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 NUGET_XMLDOC_MODE=skip

# Restore first (cached layer) — only the files that define dependencies.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/LabMonitoring.Domain/*.csproj src/LabMonitoring.Domain/
COPY src/LabMonitoring.Contracts/*.csproj src/LabMonitoring.Contracts/
COPY src/LabMonitoring.Application/*.csproj src/LabMonitoring.Application/
COPY src/LabMonitoring.Infrastructure/*.csproj src/LabMonitoring.Infrastructure/
COPY src/LabMonitoring.Api/*.csproj src/LabMonitoring.Api/
RUN dotnet restore src/LabMonitoring.Api/LabMonitoring.Api.csproj

COPY src/ src/
RUN dotnet publish src/LabMonitoring.Api/LabMonitoring.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV TZ=Asia/Ho_Chi_Minh \
    ASPNETCORE_URLS=http://+:5200

# curl for the compose healthcheck; tzdata so TZ is honoured.
RUN apt-get update && apt-get install -y --no-install-recommends curl tzdata \
    && rm -rf /var/lib/apt/lists/*

# Data Protection key ring (AgentSecret encryption) + uploaded Agent packages live on volumes
# mounted here; create them owned by the non-root runtime user so a fresh named volume is writable.
RUN mkdir -p /var/lib/labmonitoring-api/dp-keys /var/lib/labmonitoring-api/agent-packages /var/lib/labmonitoring-api/file-transfers \
    && chown -R app:app /var/lib/labmonitoring-api

COPY --from=build --chown=app:app /app .
USER app
EXPOSE 5200
ENTRYPOINT ["dotnet", "LabMonitoring.Api.dll"]
