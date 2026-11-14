FROM mcr.microsoft.com/dotnet/runtime:11.0@sha256:3fe5f5495141c97986a49d3a4e76061f6225c3f93f94e8a7e522781376193863 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:11.0@sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Transcendence.Service/Transcendence.Service.csproj", "Transcendence.Service/"]
COPY ["Transcendence.Service.Core/Transcendence.Service.Core.csproj", "Transcendence.Service.Core/"]
COPY ["Transcendence.Data/Transcendence.Data.csproj", "Transcendence.Data/"]
RUN dotnet restore "Transcendence.Service/Transcendence.Service.csproj"
COPY . .
WORKDIR "/src/Transcendence.Service"
RUN dotnet build "Transcendence.Service.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Transcendence.Service.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
ARG SOURCE_REVISION=unknown
ENV TRN_RELEASE_SHA=$SOURCE_REVISION
WORKDIR /app
COPY --from=publish /app/publish .
# Liveness: the worker publishes the last job-progress timestamp to /tmp/worker-heartbeat. Mark the container
# unhealthy when that file is missing or older than 10 min (the in-process watchdog exits on the
# same condition to trigger a restart; this gives docker ps / monitoring visibility). coreutils only.
HEALTHCHECK --interval=60s --timeout=10s --start-period=240s --retries=3 \
    CMD test -f /tmp/worker-heartbeat && test $(( $(date +%s) - $(stat -c %Y /tmp/worker-heartbeat) )) -lt 600
ENTRYPOINT ["dotnet", "Transcendence.Service.dll"]
