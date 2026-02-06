# Technology Stack

**Analysis Date:** 2026-01-31

## Languages

**Primary:**
- C# 12 - Used across all projects (WebAPI, Worker Service, Data layer, Service Core)

## Runtime

**Environment:**
- .NET 10.0 - Latest LTS version with nullable reference types enabled
- Docker with multi-stage builds (aspnet:10.0 base, sdk:10.0 build image)

**Package Manager:**
- NuGet - .NET package manager
- Lockfile: Implicitly managed via csproj, no explicit lockfile

## Frameworks

**Core Web:**
- ASP.NET Core - Web API framework used in `Transcendence.WebAPI`
- ASP.NET Core Worker Service - Background job host in `Transcendence.Service`

**API Documentation:**
- Swashbuckle.AspNetCore 10.1.0 - Swagger/OpenAPI generation and UI

**Data Access:**
- Entity Framework Core 10.0.2 - ORM for database abstraction
  - Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0 - PostgreSQL provider
  - Microsoft.EntityFrameworkCore.SqlServer 10.0.2 - SQL Server provider (development)

**Job Queue:**
- Hangfire 1.8.22 - Background job processing
  - Hangfire.Core 1.8.22
  - Hangfire.NetCore 1.8.22
  - Hangfire.AspNetCore 1.8.22
  - Hangfire.PostgreSql 1.20.13 - PostgreSQL job storage

**External APIs:**
- Camille.RiotGames 3.0.0-nightly-2025-06-24-d2b94715b7 - Riot Games API SDK
  - Provides access to Summoner V4, Account V1, Match V5, Ranked endpoints

**HTTP & DI:**
- Microsoft.Extensions.Http 10.0.2 - HttpClient factory pattern
- Microsoft.Extensions.DependencyInjection 10.0.2 - Service container
- Microsoft.Extensions.Configuration.Abstractions 10.0.2 - Configuration abstraction
- Microsoft.Extensions.Logging.Abstractions 10.0.2 - Logging abstraction

## Key Dependencies

**Critical:**
- Camille.RiotGames - Enables communication with Riot Games API for summoner, match, and rank data
- EntityFrameworkCore - Handles all data persistence and migrations
- Hangfire - Enables asynchronous background job processing for data refresh and analysis

**Infrastructure:**
- Microsoft.Extensions.* - Provides DI, configuration, logging, and HTTP client management
- Npgsql.EntityFrameworkCore.PostgreSQL - PostgreSQL database connectivity

## Configuration

**Environment:**
- User Secrets (development only) - Managed via `dotnet user-secrets` per project
  - `ConnectionStrings:MainDatabase` - PostgreSQL connection string
  - `RiotApi:ApiKey` - Riot Games API key (only in Service project, not WebAPI)
- Environment-specific appsettings:
  - `appsettings.json` - Base configuration for logging
  - `appsettings.Development.json` - Development logging overrides

**Build:**
- `global.json` - Specifies .NET 10.0 SDK with allowPrerelease: true
- Project files use `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`
- Docker targets Linux with `<DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>`

## Projects & Assemblies

**Transcendence.WebAPI** (`Transcendence.WebAPI.csproj`)
- Target: net10.0 (ASP.NET Core Web SDK)
- Role: REST API endpoints with Swagger documentation
- Dependencies: Transcendence.Data, Transcendence.Service.Core
- Key packages: Swashbuckle.AspNetCore, Hangfire client libraries

**Transcendence.Service** (`Transcendence.Service.csproj`)
- Target: net10.0 (Worker Service SDK)
- Role: Background job server with Hangfire job processing
- Dependencies: Transcendence.Service.Core
- Key packages: Hangfire (with server), EF Core, Riot Games API integration

**Transcendence.Service.Core** (`Transcendence.Service.Core.csproj`)
- Target: net10.0 (Class Library)
- Role: Shared business logic (analysis, Riot API services, job definitions, static data)
- Dependencies: Transcendence.Data
- Key packages: Camille.RiotGames, Hangfire.Core

**Transcendence.Data** (`Transcendence.Data.csproj`)
- Target: net10.0 (Class Library)
- Role: Data models, DbContext, and repositories
- Key packages: EntityFrameworkCore, EF Design tools (build-time only)

**Transcendence.WebAdminPortal** (`Transcendence.WebAdminPortal.csproj`)
- Target: net10.0 (ASP.NET Core Web SDK)
- Role: Admin interface for Hangfire dashboard
- Key packages: Hangfire dashboard integration

## Platform Requirements

**Development:**
- .NET 10.0 SDK
- PostgreSQL 12+ or SQL Server 2019+ (configurable via connection string)
- Riot Games Developer API key from https://developer.riotgames.com

**Production:**
- Docker runtime (Linux x64 base image: mcr.microsoft.com/dotnet/aspnet:10.0)
- PostgreSQL 12+ for Hangfire job storage and application data
- Riot Games API key injected via environment or secrets

## Database Providers

**Production:**
- PostgreSQL via Npgsql provider
- Hangfire job storage: PostgreSQL via Hangfire.PostgreSql 1.20.13

**Development:**
- SQL Server via Microsoft.EntityFrameworkCore.SqlServer 10.0.2

## Deployment & Containerization

**Docker Build Strategy:**
- Multi-stage build (see `Transcendence.WebAPI/Dockerfile`, `Transcendence.Service/Dockerfile`)
- Build stage: sdk:10.0 with dotnet restore and build
- Publish stage: Creates release artifacts with UseAppHost=false
- Runtime stage: aspnet:10.0 base image, app runs as non-root user
- Entrypoint: Executes dll directly (e.g., `dotnet Transcendence.WebAPI.dll`)

**Compose Orchestration:**
- `docker-compose.yml` defines three services:
  - transcendence.webapi (port 8080/8081)
  - transcendence.service (Hangfire server)
  - transcendence.webadminportal (admin dashboard)

---

*Stack analysis: 2026-01-31*
