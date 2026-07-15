using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Transcendence.Data;

namespace Transcendence.IntegrationTests;

/// <summary>
/// Boots the real <c>Transcendence.WebAPI</c> host against a Testcontainers Postgres instance, so the
/// integration tests exercise the actual middleware pipeline, authentication/authorization, EF Core
/// model and Npgsql translation — not the InMemory/SQLite substitutes the unit tests use.
///
/// Two overrides are required to boot cleanly with no Redis:
///  - The DbContext is re-pointed at the container connection string (keeping the
///    <c>Transcendence.Service</c> migrations assembly) via ConfigureTestServices, which runs after
///    the app's own registrations and therefore wins deterministically (no reliance on config timing).
///  - <c>AddStackExchangeRedisCache</c> is registered UNCONDITIONALLY by the app and HybridCache uses it
///    as its L2, so the first cache write would attempt a Redis connection that isn't there. We swap the
///    <see cref="IDistributedCache"/> for an in-memory implementation.
///
/// The host runs in the Development environment so the JWT dev-fallback signing key resolves
/// consistently on both the minting side (<c>IJwtService</c>) and the validating side (the JwtBearer
/// options), and so the bootstrap admin step early-returns without a DB hit (no configured emails).
/// </summary>
public sealed class TestWebAppFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Point the app's MainDatabase connection string at the container. This is what Hangfire's
        // PostgreSqlStorage reads (Program.cs registers it against GetConnectionString("MainDatabase")),
        // so without it the JobStorage singleton — built when a controller that injects
        // IBackgroundJobClient is activated — would target the appsettings default (localhost:5432),
        // which is wrong in a test host and would break if any tested endpoint ever touched Hangfire.
        // The retained-source ordering in ConfigureSharedBackendConfiguration re-adds host settings after
        // the JSON files, so this override wins.
        builder.UseSetting("ConnectionStrings:MainDatabase", connectionString);

        builder.ConfigureTestServices(services =>
        {
            // Belt-and-suspenders: also re-point the DbContext directly (retain the migrations assembly),
            // so it uses the container regardless of config-source ordering.
            services.RemoveAll<DbContextOptions<TranscendenceContext>>();
            services.AddDbContext<TranscendenceContext>(options =>
                options.UseNpgsql(connectionString, npg => npg.MigrationsAssembly("Transcendence.Service")));

            // No Redis in tests → give HybridCache an in-memory L2 so cache-backed reads don't
            // try to open a Redis connection that doesn't exist.
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
        });
    }
}
