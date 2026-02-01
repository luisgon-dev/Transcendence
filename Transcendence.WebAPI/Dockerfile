FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Transcendence.WebAPI/Transcendence.WebAPI.csproj", "Transcendence.WebAPI/"]
COPY ["Transcendence.Data/Transcendence.Data.csproj", "Transcendence.Data/"]
COPY ["Transcendence.Service.Core/Transcendence.Service.Core.csproj", "Transcendence.Service.Core/"]
RUN dotnet restore "Transcendence.WebAPI/Transcendence.WebAPI.csproj"
COPY . .
WORKDIR "/src/Transcendence.WebAPI"
RUN dotnet build "Transcendence.WebAPI.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Transcendence.WebAPI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Transcendence.WebAPI.dll"]
