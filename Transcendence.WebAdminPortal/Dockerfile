FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Transcendence.WebAdminPortal/Transcendence.WebAdminPortal.csproj", "Transcendence.WebAdminPortal/"]
COPY ["Transcendence.Service.Core/Transcendence.Service.Core.csproj", "Transcendence.Service.Core/"]
COPY ["Transcendence.Data/Transcendence.Data.csproj", "Transcendence.Data/"]
RUN dotnet restore "Transcendence.WebAdminPortal/Transcendence.WebAdminPortal.csproj"
COPY . .
WORKDIR "/src/Transcendence.WebAdminPortal"
RUN dotnet build "Transcendence.WebAdminPortal.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Transcendence.WebAdminPortal.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Transcendence.WebAdminPortal.dll"]
