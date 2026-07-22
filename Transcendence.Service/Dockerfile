FROM mcr.microsoft.com/dotnet/runtime:10.0@sha256:ed5d539b27842d656a06a5984dbcb5114d3e885fbada612a49a5a7c3c3a44e1c AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664 AS build
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
WORKDIR /app
COPY --from=publish /app/publish .
# Liveness: the worker writes /tmp/worker-heartbeat on every producer tick. Mark the container
# unhealthy when that file is missing or older than 10 min (the in-process watchdog exits on the
# same condition to trigger a restart; this gives docker ps / monitoring visibility). coreutils only.
HEALTHCHECK --interval=60s --timeout=10s --start-period=240s --retries=3 \
    CMD test -f /tmp/worker-heartbeat && test $(( $(date +%s) - $(stat -c %Y /tmp/worker-heartbeat) )) -lt 600
ENTRYPOINT ["dotnet", "Transcendence.Service.dll"]
