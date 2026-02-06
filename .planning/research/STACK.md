# Technology Stack

**Project:** Transcendence Backend - League of Legends Analytics
**Researched:** 2026-02-01
**Scope:** Additional libraries for analytics, live game, auth, and management features

## Context

This research focuses on **additions** to an existing .NET 10 backend that already has:
- .NET 10 with ASP.NET Core WebAPI and Worker Service
- Entity Framework Core 10.0.2 with PostgreSQL (via Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0)
- Hangfire 1.8.22 for background job processing
- Camille SDK 3.0.0-nightly for Riot API integration
- Swashbuckle.AspNetCore 10.1.0 for API documentation

The additions below support four new capability areas:
1. Champion performance analytics (aggregations, caching)
2. Live game analysis (Spectator API integration)
3. Authentication system (API keys + user accounts)
4. Management dashboard (health checks, metrics, monitoring)

## Recommended Additions

### Caching Layer
| Library | Version | Purpose | Why | Confidence |
|---------|---------|---------|-----|-----------|
| Microsoft.Extensions.Caching.Hybrid | 10.2.0 | Two-tier caching (memory + distributed) | NEW in .NET 9/10. Replaces manual IMemoryCache + IDistributedCache coordination. Built-in stampede protection, tag-based invalidation. Perfect for analytics aggregations that are expensive to compute. | HIGH |
| Microsoft.Extensions.Caching.StackExchangeRedis | 10.0.2 | Distributed cache backend (Redis) | Official Microsoft package for Redis integration. Required for HybridCache distributed tier. Version 10.0.2 released 1/13/2026, explicitly supports .NET 10. | HIGH |
| StackExchange.Redis | >= 2.7.27 | Redis client | Dependency of Microsoft.Extensions.Caching.StackExchangeRedis. Industry standard Redis client for .NET. | HIGH |

**Rationale:** HybridCache is the modern .NET approach (GA in .NET 9, refined in 10). It automatically handles L1 (memory) + L2 (Redis) caching with stampede protection, which is critical when multiple API requests simultaneously ask for expensive champion analytics. Tag-based invalidation lets you invalidate all "champion-123" analytics when new match data arrives.

**Alternatives Considered:**
- Manual IMemoryCache + IDistributedCache: More boilerplate, no stampede protection, requires custom invalidation logic
- CachingFramework.Redis: Third-party with tagging support, but HybridCache now provides this natively

### Authentication & Authorization
| Library | Version | Purpose | Why | Confidence |
|---------|---------|---------|-----|-----------|
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.2 | JWT token validation | Official package for JWT bearer authentication. Version 10.0.2 released 1/13/2026 for .NET 10. Supports user account auth (desktop/web clients). | HIGH |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.2 | User account management | ASP.NET Core Identity with EF Core storage. Handles users, passwords, roles, claims, tokens, email confirmation. NEW in .NET 10: built-in metrics for monitoring sign-ins, user creation, password changes. | HIGH |
| System.IdentityModel.Tokens.Jwt | >= 7.0.0 | JWT token creation/validation | Low-level JWT library, dependency of JwtBearer. Use for custom token generation in user registration/login flows. | MEDIUM |

**Rationale:** Dual authentication scheme needed:
1. **API Keys** for machine-to-machine (desktop app, web frontend) - implement custom authentication handler (no package needed, ASP.NET Core built-in)
2. **JWT Bearer** for user accounts (personalization, saved searches, favorites) - official Microsoft packages

ASP.NET Core Identity provides full user management with EF Core integration (already using PostgreSQL). .NET 10 adds observability metrics for auth operations. JWT tokens are stateless, short-lived (15-30 min), with refresh tokens for longer sessions.

**Best Practices from Research:**
- Store access tokens in HttpOnly cookies for web clients (XSS protection)
- Keep access tokens short-lived (15-30 minutes)
- Use refresh tokens for longer sessions (days/weeks)
- Consider Redis cache of revoked JTI claims for high-security token invalidation
- Validate issuer, audience, lifetime, signing key strictly
- Use OIDC/OAuth 2.0 patterns with PKCE for web flows

**Alternatives Considered:**
- Duende IdentityServer: Overkill for this use case, adds OAuth server complexity
- Auth0/Okta: External dependencies, not needed for API-first backend
- AspNetCore.Authentication.ApiKey (third-party): Custom handler is simpler for basic API key validation

### Health Checks & Monitoring
| Library | Version | Purpose | Why | Confidence |
|---------|---------|---------|-----|-----------|
| AspNetCore.HealthChecks.NpgSql | 9.0.0 | PostgreSQL health check | Community package from Xabaril/AspNetCore.Diagnostics.HealthChecks. Verifies database connectivity. Version 9.0.0 updated January 2026, targets .NET 8 (compatible with .NET 10 via .NET Standard 2.0). | HIGH |
| AspNetCore.HealthChecks.Redis | 9.0.0 | Redis health check | Verifies Redis cache availability. Same ecosystem as NpgSql check. | MEDIUM |
| AspNetCore.HealthChecks.Hangfire | 9.0.0 | Hangfire health check | Verifies background job processing health. Targets .NET 8, computed compatible with .NET 10. | MEDIUM |

**Rationale:** ASP.NET Core's built-in health check framework (Microsoft.AspNetCore.Diagnostics.HealthChecks, included in framework) exposes `/health` endpoints for monitoring. Add specific checks for each infrastructure component. Monitoring systems (k8s, Docker, external monitoring) query these endpoints to detect failures.

**Note on Versions:** Xabaril packages version 9.0.0 target .NET 8 but are compatible with .NET 10 through .NET Standard 2.0 support. No .NET 10-specific versions exist yet (as of Feb 2026), but these work without issues.

**What to Monitor:**
- PostgreSQL: Database connectivity and query execution
- Redis: Cache availability
- Hangfire: Background job server status
- Memory/CPU: Built-in ASP.NET Core metrics

**Alternatives Considered:**
- Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore: For basic EF Core checks, but NpgSql-specific check is more thorough
- Custom health checks: More control but reinventing the wheel, community packages are well-tested

### Observability & Metrics
| Library | Version | Purpose | Why | Confidence |
|---------|---------|---------|-----|-----------|
| OpenTelemetry.Instrumentation.AspNetCore | 1.15.0 | ASP.NET Core telemetry | Collects metrics and traces for incoming HTTP requests. Official OpenTelemetry package, version 1.15.0 released 1/21/2026 with .NET 10 support. 213.8M total downloads. | HIGH |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | 1.15.0-beta.1 | Prometheus metrics export | Exposes /metrics endpoint for Prometheus scraping. Beta but stable, supports .NET 10 (net8.0, net9.0, net10.0). Released 1/21/2026. | HIGH |
| OpenTelemetry.Instrumentation.EntityFrameworkCore | (check version) | EF Core telemetry | Traces database queries, detects N+1 problems, slow queries. Part of OpenTelemetry ecosystem. | MEDIUM |

**Rationale:** OpenTelemetry is the CNCF standard for observability (logs, metrics, traces). ASP.NET Core 10 has native integration. Prometheus exporter allows scraping metrics into Grafana dashboards. This provides:
- Request/response metrics (latency, throughput, status codes)
- Database query performance
- Cache hit/miss rates
- Background job metrics (via Hangfire)
- ASP.NET Core Identity metrics (NEW in .NET 10: sign-ins, user creation, password changes)

**Two Implementation Approaches:**
1. **OTLP Exporter:** Push metrics to Prometheus via OpenTelemetry Protocol (requires Prometheus --web.enable-otlp-receiver)
2. **Direct Prometheus Exporter:** Expose /metrics endpoint for Prometheus scraping (simpler, recommended)

**Alternatives Considered:**
- prometheus-net: Older .NET-specific approach, OpenTelemetry is now the standard
- Application Insights: Azure-specific, vendor lock-in
- Serilog metrics: Good for logging, but OpenTelemetry handles metrics/traces better

### Structured Logging
| Library | Version | Purpose | Why | Confidence |
|---------|---------|---------|-----|-----------|
| Serilog.AspNetCore | 10.0.0 | Structured logging | Routes ASP.NET Core logs through Serilog. Version 10.0.0 released for .NET 10 SDK. Provides structured event logging with rich sinks (Console, File, Seq, Elasticsearch). | HIGH |
| Serilog.Sinks.Console | Latest | Console output | Development/Docker logging. | HIGH |
| Serilog.Sinks.File | Latest | File output | Production logging to disk. | HIGH |
| Serilog.Sinks.Seq | Latest | Centralized log server | Optional: Seq for log aggregation (alternative to ELK stack). | MEDIUM |

**Rationale:** Serilog is the de facto standard for .NET structured logging. Version 10.0.0 tracks .NET 10 dependencies. Structured logging is critical for analytics backend to trace request flows, debug Riot API integration issues, monitor background job execution.

**Best Practices:**
- Always use structured properties (not string interpolation): `logger.Information("User {UserId} refreshed summoner {SummonerId}", userId, summonerId)`
- Never log raw JWT tokens, use hashes or claims only
- Log expensive operations (Riot API calls, database aggregations) with timings
- Use correlation IDs to trace requests through async flows

**Alternatives Considered:**
- Microsoft.Extensions.Logging alone: Works but less powerful than Serilog's sinks ecosystem
- NLog: Good alternative, but Serilog has better DI integration and sink ecosystem

### API Documentation
| Library | Version | Purpose | Why | Confidence |
|---------|---------|---------|-----|-----------|
| Swashbuckle.AspNetCore | 10.1.0 | OpenAPI/Swagger UI | ALREADY INSTALLED. Version 10.1.0 supports OpenAPI 3.1 and .NET 10. Breaking change: now requires Microsoft.OpenApi v2, drops .NET Framework support. | HIGH |

**Rationale:** Already in use. Ensure upgrade to v10 for .NET 10 compatibility. Document authentication schemes (JWT bearer, API key) in Swagger for client developers.

**Important:** Swashbuckle v10 has breaking changes from v9 (Microsoft.OpenApi v2 dependency). Strongly recommend upgrading to v9.0.6 first if migrating from older versions.

### Performance & Resilience
| Library | Version | Purpose | Why | Confidence |
|---------|---------|---------|-----|-----------|
| Microsoft.AspNetCore.ResponseCompression | Built-in | Gzip/Brotli compression | INCLUDED in ASP.NET Core framework. Enable Brotli for best compression (HTML/CSS/JS), fallback to Gzip. Defaults to CompressionLevel.Fastest. | HIGH |
| Microsoft.AspNetCore.RateLimiting | Built-in | Rate limiting middleware | INCLUDED in ASP.NET Core framework. Protect API from abuse. Built-in algorithms: Fixed Window, Sliding Window, Token Bucket, Concurrency. Configure per-endpoint policies. | HIGH |

**Rationale:** Both are built into ASP.NET Core 10, no packages needed. Response compression reduces bandwidth for JSON responses (champion stats, match history). Rate limiting protects against API abuse, especially important for public endpoints.

**Rate Limiting Strategy:**
- Desktop app endpoints: Token bucket (burst allowance)
- Web API endpoints: Fixed window per user/IP
- Riot API proxy endpoints: Match Riot's rate limits
- Background jobs: No rate limiting (internal)

**Alternatives Considered:**
- AspNetCoreRateLimit (third-party): Was popular before built-in middleware existed
- Manual compression: Built-in middleware is optimized

### Data Operations
| Library | Version | Purpose | Why | Confidence |
|---------|---------|---------|-----|-----------|
| EFCore.BulkExtensions | 10.0.0 | Bulk database operations | High-performance bulk insert/update/delete for match data ingestion. Version 10.0.0 released 1/9/2026 for EF Core 10/.NET 10. Supports PostgreSQL. Open-source, free. ~9-15x faster than SaveChanges for large batches. | HIGH |
| AutoMapper | 16.0.0 | Object mapping | Maps Riot API DTOs to domain models, domain to API responses. Version 13+ includes DI support in core package (AutoMapper.Extensions.Microsoft.DependencyInjection is deprecated). | MEDIUM |

**Rationale:**
- **Bulk operations:** When ingesting match history (100+ matches), bulk insert is 9-15x faster than EF Core's SaveChanges. Critical for background jobs that refresh summoner data.
- **AutoMapper:** Reduces boilerplate for DTO mapping. League data models are complex (match participants, items, runes, champion stats).

**Bulk Operations Best Practice:** Use bulk ops for sets > 1000 rows. Small batches have overhead from temp table creation. For analytics aggregations with millions of match rows, bulk operations are essential.

**Alternatives Considered:**
- Entity Framework Extensions (ZZZ Projects): Commercial, 95% performance gains but costs money. EFCore.BulkExtensions is free and good enough.
- Manual SQL: More control but loses EF Core type safety
- Dapper: Good for reads, but EF Core + bulk extensions handles writes well

### Optional: CQRS Pattern
| Library | Version | Purpose | Why | Confidence |
|---------|---------|---------|-----|-----------|
| MediatR | Latest | CQRS/Mediator pattern | OPTIONAL. Separates read (queries) from write (commands) operations. Helps organize complex analytics queries vs data ingestion commands. No dependencies, supports DI, request/response pipeline. | LOW |

**Rationale:** NOT REQUIRED initially, but consider for analytics complexity. CQRS helps when:
- Read models differ significantly from write models (analytics aggregations vs raw match data)
- Query optimization needs (read replicas, caching strategies per query type)
- Background job commands vs API query handlers

**When to Add:** If analytics queries become complex enough to justify separation. Monitor first, add if needed.

**Alternatives Considered:**
- Manual repository pattern: Works but MediatR provides better pipeline (validation, logging, caching)
- No pattern: Fine for simple CRUD, but analytics backends benefit from query/command separation

### Validation
| Library | Version | Purpose | Why | Confidence |
|---------|---------|---------|-----|-----------|
| FluentValidation | 11.3.1+ | Request validation | Cleaner validation syntax than data annotations. FluentValidation.AspNetCore 11.3.1 provides MVC auto-validation (NOT for Minimal APIs/Blazor). FluentValidation 12 requires .NET 6+, v11 supports .NET Standard 2.0. | MEDIUM |

**Rationale:** Validate API request models (summoner lookup, match history filters, auth requests). Fluent syntax more readable than [Required], [Range] attributes for complex validation.

**Important Limitation:** FluentValidation.AspNetCore auto-validation is MVC-only. If using Minimal APIs, manual validation is required.

**When to Skip:** If using data annotations is sufficient, skip FluentValidation. Only add if complex validation rules emerge.

**Alternatives Considered:**
- Data Annotations: Built-in, simpler for basic validation
- Manual validation: More control but verbose

## Alternatives Considered

### Caching
| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| Caching strategy | HybridCache | Manual IMemoryCache + IDistributedCache | No stampede protection, manual coordination, more boilerplate |
| Distributed cache | StackExchange.Redis (Microsoft package) | CachingFramework.Redis | Third-party, HybridCache provides tagging now |
| Cache invalidation | HybridCache tags | Manual key tracking | Error-prone, doesn't scale |

### Authentication
| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| User management | ASP.NET Core Identity | Custom user tables | Reinventing the wheel, Identity has password hashing, claims, roles built-in |
| JWT library | Microsoft.AspNetCore.Authentication.JwtBearer | Manual JWT validation | Less secure, framework handles edge cases |
| API keys | Custom authentication handler | Third-party ApiKey package | Simple enough to implement, no dependency needed |

### Observability
| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| Metrics | OpenTelemetry + Prometheus | prometheus-net | OpenTelemetry is the CNCF standard, better ecosystem |
| Logging | Serilog | NLog | Serilog has better DI integration, richer sink ecosystem |
| APM | OpenTelemetry traces | Application Insights | Vendor lock-in (Azure), OpenTelemetry is standard |

### Data Operations
| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| Bulk operations | EFCore.BulkExtensions | Entity Framework Extensions (ZZZ) | Commercial license, EFCore.BulkExtensions is free and sufficient |
| Bulk operations | EFCore.BulkExtensions | Manual SQL | Loses EF Core type safety and migrations |
| Mapping | AutoMapper | Manual mapping | Too much boilerplate for complex League data models |

## Installation

### Core Analytics & Caching
```bash
dotnet add package Microsoft.Extensions.Caching.Hybrid --version 10.2.0
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis --version 10.0.2
```

### Authentication
```bash
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 10.0.2
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 10.0.2
```

### Health Checks
```bash
dotnet add package AspNetCore.HealthChecks.NpgSql --version 9.0.0
dotnet add package AspNetCore.HealthChecks.Redis --version 9.0.0
dotnet add package AspNetCore.HealthChecks.Hangfire --version 9.0.0
```

### Observability
```bash
dotnet add package OpenTelemetry.Instrumentation.AspNetCore --version 1.15.0
dotnet add package OpenTelemetry.Exporter.Prometheus.AspNetCore --version 1.15.0-beta.1
dotnet add package Serilog.AspNetCore --version 10.0.0
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Sinks.File
```

### Data Operations
```bash
dotnet add package EFCore.BulkExtensions --version 10.0.0
dotnet add package AutoMapper --version 16.0.0
```

### Optional
```bash
# Only if complex analytics queries justify CQRS
dotnet add package MediatR

# Only if complex validation rules emerge
dotnet add package FluentValidation.AspNetCore --version 11.3.1
```

### Built-in (No Installation)
These are included in ASP.NET Core 10 framework:
- Microsoft.AspNetCore.ResponseCompression (Gzip/Brotli)
- Microsoft.AspNetCore.RateLimiting (rate limiting middleware)
- Microsoft.AspNetCore.Diagnostics.HealthChecks (health check framework)

## Configuration Notes

### HybridCache Setup
```csharp
services.AddHybridCache(options =>
{
    options.MaximumKeySize = 512;
    options.MaximumEntrySize = 1024 * 1024; // 1MB
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };
});
```

### Authentication Setup
```csharp
// Custom API key handler for app-to-app auth
services.AddAuthentication()
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>("ApiKey", null)
    .AddJwtBearer("Bearer", options => { /* JWT config */ });

// ASP.NET Core Identity for user accounts
services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();
```

### Health Checks Setup
```csharp
services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgresql")
    .AddRedis(redisConnectionString, name: "redis")
    .AddHangfire(options => { /* hangfire config */ }, name: "hangfire");

// Expose endpoint
app.MapHealthChecks("/health");
```

### OpenTelemetry Setup
```csharp
services.AddOpenTelemetry()
    .WithMetrics(builder => builder
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter())
    .WithTracing(builder => builder
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation());

// Expose Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint();
```

### Rate Limiting Setup
```csharp
services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", limiterOptions =>
    {
        limiterOptions.PermitLimit = 100;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
    });
});

app.UseRateLimiter();
```

### Response Compression Setup
```csharp
services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest; // or Optimal for better compression
});

app.UseResponseCompression();
```

## Sources

### Official Microsoft Documentation
- [Microsoft.Extensions.Caching.StackExchangeRedis NuGet](https://www.nuget.org/packages/microsoft.extensions.caching.stackexchangeredis)
- [Distributed caching in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-9.0)
- [HybridCache library in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0)
- [Microsoft.AspNetCore.Authentication.JwtBearer NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer)
- [Health checks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0)
- [ASP.NET Core metrics](https://learn.microsoft.com/en-us/aspnet/core/log-mon/metrics/metrics?view=aspnetcore-10.0)
- [Introduction to Identity on ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0)
- [Response compression in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/response-compression?view=aspnetcore-10.0)
- [Rate limiting middleware in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [Cache in-memory in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/memory?view=aspnetcore-10.0)

### Community Packages & Documentation
- [AspNetCore.Diagnostics.HealthChecks GitHub](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks)
- [AspNetCore.HealthChecks.NpgSql NuGet](https://www.nuget.org/packages/AspNetCore.HealthChecks.NpgSql/)
- [OpenTelemetry.Instrumentation.AspNetCore NuGet](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.AspNetCore)
- [OpenTelemetry .NET Getting Started](https://opentelemetry.io/docs/languages/dotnet/getting-started/)
- [Serilog.AspNetCore NuGet](https://www.nuget.org/packages/serilog.aspnetcore)
- [Serilog GitHub](https://github.com/serilog/serilog-aspnetcore)
- [Swashbuckle.AspNetCore NuGet](https://www.nuget.org/packages/swashbuckle.aspnetcore)
- [EFCore.BulkExtensions GitHub](https://github.com/borisdj/EFCore.BulkExtensions)
- [AutoMapper.Extensions.Microsoft.DependencyInjection NuGet](https://www.nuget.org/packages/automapper.extensions.microsoft.dependencyinjection/)
- [FluentValidation ASP.NET Core](https://docs.fluentvalidation.net/en/latest/aspnet.html)

### Best Practices & Guides
- [Hello HybridCache! - .NET Blog](https://devblogs.microsoft.com/dotnet/hybrid-cache-is-now-ga/)
- [Using JWT as API Keys: Security Best Practices](https://securityboulevard.com/2026/01/using-jwt-as-api-keys-security-best-practices-implementation-guide/)
- [Authentication and authorization best practices in .Net](https://empty-chair.medium.com/authentication-and-authorization-best-practices-in-net-442b986bbfe1)
- [Complete Observability with OpenTelemetry in .NET 10](https://vitorafgomes.medium.com/complete-observability-with-opentelemetry-in-net-10-a-practical-and-universal-guide-c9dda9edaace)
- [ASP.NET Core Identity in .NET 10](https://dev.to/cristiansifuentes/aspnet-core-identity-in-net-10-from-login-page-to-production-grade-security-4n6o)
- [EF Core 10 Bulk Operations](https://dotnetstories.wordpress.com/2025/12/08/ef-core-10-on-the-net-10-runtime-high-performance-bulk-updates-with-executeupdate-executedelete/)
- [CQRS Pattern With MediatR](https://www.milanjovanovic.tech/blog/cqrs-pattern-with-mediatr)

## Confidence Assessment

| Category | Confidence | Reason |
|----------|-----------|--------|
| Caching (HybridCache, Redis) | HIGH | Official Microsoft packages with .NET 10 support verified via NuGet (10.0.2, 10.2.0). Documentation current as of January 2026. |
| Authentication (JWT, Identity) | HIGH | Official Microsoft packages verified for .NET 10 (10.0.2). Best practices cross-referenced from multiple 2026 sources. |
| Health Checks | MEDIUM | Community packages (Xabaril) version 9.0.0 target .NET 8 but compatible via .NET Standard 2.0. No .NET 10-native versions yet, but verified working. |
| Observability (OpenTelemetry) | HIGH | Official OpenTelemetry packages 1.15.0/1.15.0-beta.1 released 1/21/2026 with explicit .NET 10 support. |
| Logging (Serilog) | HIGH | Serilog.AspNetCore 10.0.0 released for .NET 10. Well-established library with strong ecosystem. |
| Performance (compression, rate limiting) | HIGH | Built into ASP.NET Core 10 framework, official Microsoft documentation verified. |
| Data Operations (bulk, mapping) | HIGH | EFCore.BulkExtensions 10.0.0 verified for .NET 10/EF Core 10 (released 1/9/2026). AutoMapper 16.0.0 current. |
| CQRS (MediatR) | LOW | Marked optional. Pattern well-documented but not essential initially. |
| Validation (FluentValidation) | MEDIUM | Marked optional. Version 11.3.1 compatible, but MVC-only limitation noted. |

## Recommendation Summary

**High Priority (Install Now):**
1. HybridCache + Redis for analytics caching
2. JWT Bearer + Identity for authentication
3. Health checks for PostgreSQL, Redis, Hangfire
4. OpenTelemetry + Prometheus for observability
5. Serilog for structured logging
6. EFCore.BulkExtensions for match data ingestion
7. Enable response compression and rate limiting (built-in)

**Medium Priority (Install Soon):**
1. AutoMapper for DTO mapping complexity
2. Additional OpenTelemetry instrumentation (EF Core)

**Low Priority (Evaluate Later):**
1. MediatR for CQRS (only if analytics queries become complex)
2. FluentValidation (only if data annotations insufficient)

**Don't Install:**
- Third-party caching libraries (HybridCache is now standard)
- Commercial bulk operation libraries (EFCore.BulkExtensions is free and sufficient)
- Third-party rate limiting packages (built into ASP.NET Core 10)
- Application Insights (vendor lock-in, use OpenTelemetry)

## Next Steps for Implementation

1. **Phase 1 - Foundation:** Add HybridCache, Redis, authentication, health checks
2. **Phase 2 - Observability:** Configure OpenTelemetry, Serilog, Prometheus metrics
3. **Phase 3 - Performance:** Enable compression, rate limiting, bulk operations
4. **Phase 4 - Polish:** Add AutoMapper, consider CQRS if needed

This stack positions the backend for production-grade analytics with caching, authentication, monitoring, and operational tooling while leveraging .NET 10's modern features (HybridCache, built-in metrics, rate limiting).
