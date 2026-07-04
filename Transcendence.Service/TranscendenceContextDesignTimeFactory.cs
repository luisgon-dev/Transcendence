using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Transcendence.Data;

namespace Transcendence.Service;

public sealed class TranscendenceContextDesignTimeFactory : IDesignTimeDbContextFactory<TranscendenceContext>
{
    public TranscendenceContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                          ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                          ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(Path.Combine("config", "backend.shared.json"), optional: true)
            .AddJsonFile(Path.Combine("Transcendence.Service", "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine("Transcendence.Service", $"appsettings.{environment}.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("MainDatabase")
                               ?? "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=changme";

        var optionsBuilder = new DbContextOptionsBuilder<TranscendenceContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly("Transcendence.Service"));

        return new TranscendenceContext(optionsBuilder.Options);
    }
}
