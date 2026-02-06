using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.WebAdminPortal.Security;

var builder = WebApplication.CreateBuilder(args);

// Force-load job assembly so Hangfire dashboard can deserialize recurring jobs.
_ = typeof(RefreshChampionAnalyticsJob).Assembly;

builder.Services.AddHangfire(config =>
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(options =>
            options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("MainDatabase"))));

var app = builder.Build();

var dashboardOptions = new DashboardOptions();
if (app.Environment.IsDevelopment())
{
    // Avoid the default "local requests only" filter so the dashboard is reachable from other machines.
    dashboardOptions.Authorization = Array.Empty<IDashboardAuthorizationFilter>();
}
else
{
    var username = builder.Configuration["Hangfire:Dashboard:Username"];
    var password = builder.Configuration["Hangfire:Dashboard:Password"];
    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException(
            "Missing Hangfire dashboard credentials. Set Hangfire:Dashboard:Username and Hangfire:Dashboard:Password.");
    }

    dashboardOptions.Authorization = [new HangfireDashboardBasicAuthFilter(username, password)];
}

app.UseHangfireDashboard("/hangfire", dashboardOptions);

// app.MapGet("/", () => "Hello World!");

app.Run();
