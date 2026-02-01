FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
ENTRYPOINT ["dotnet", "Transcendence.Service.dll"]
