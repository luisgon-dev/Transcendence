using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Transcendence.WebAPI.Controllers;
using Transcendence.WebAPI.Telemetry;
namespace Transcendence.WebAPI.Tests;
public sealed class DesktopTelemetryTests
{
    private static DesktopTelemetryBatch Valid() => new() { Release = "0.1.6", Os = "windows", Arch = "x86_64", Events = [new() { Name = "app_launch", Outcome = "success", DurationMs = 125 }] };
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DesktopTelemetry:Releases:0"] = "0.1.6" }).Build();
    [Fact] public void ValidBatchIsAccepted() => Assert.True(DesktopTelemetry.IsValid(Valid()));
    [Fact] public void RejectsUnboundedMetadata()
    {
        Assert.False(DesktopTelemetry.IsValid(Valid() with { Release = "user@example.com" }));
        Assert.False(DesktopTelemetry.IsValid(Valid() with { Os = "secret" }));
        Assert.False(DesktopTelemetry.IsValid(Valid() with { Arch = "user-device" }));
        Assert.False(DesktopTelemetry.IsValid(Valid() with { Events = [] }));
        Assert.False(DesktopTelemetry.IsValid(Valid() with { Events = Enumerable.Repeat(Valid().Events[0], 33).ToArray() }));
        Assert.False(DesktopTelemetry.IsValid(Valid() with { Events = [null!] }));
    }
    [Theory]
    [InlineData("https://secret", "success", 1, 1)]
    [InlineData("app_launch", "token", 1, 1)]
    [InlineData("app_launch", "success", 101, 1)]
    [InlineData("app_launch", "success", 0, 1)]
    [InlineData("app_launch", "success", 1, -1)]
    [InlineData("app_launch", "success", 1, 600001)]
    [InlineData("app_launch", "success", 2, 1)]
    public void RejectsInvalidEvent(string name, string outcome, int count, double duration) => Assert.False(DesktopTelemetry.IsValid(Valid() with { Events = [new() { Name = name, Outcome = outcome, Count = count, DurationMs = duration }] }));
    [Fact] public void RejectsUnknownJsonFields()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DesktopTelemetryBatch>("{\"email\":\"secret\"}", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DesktopTelemetryEvent>("{\"url\":\"secret\"}", options));
    }
    [Fact] public void BudgetIsBoundedAndRenews()
    {
        var clock = new Clock(); using var telemetry = new DesktopTelemetry(Config(), clock);
        for (var i = 0; i < 300; i++) Assert.True(telemetry.TryConsumeBudget());
        Assert.False(telemetry.TryConsumeBudget()); clock.Now += TimeSpan.FromMinutes(1); Assert.True(telemetry.TryConsumeBudget());
    }
    [Fact] public void InvalidBatchRecordsNothingAndUnknownReleaseHasFixedLabel()
    {
        using var telemetry = new DesktopTelemetry(Config()); var observed = new List<(long Count, string? Release)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) => { if (instrument.Meter.Name == DesktopTelemetry.MeterName) current.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, count, tags, _) => observed.Add((count, tags.ToArray().First(t => t.Key == "release").Value?.ToString())));
        listener.Start(); telemetry.Record(Valid()); telemetry.Record(Valid() with { Release = "9.9.9" });
        Assert.Throws<ArgumentException>(() => telemetry.Record(Valid() with { Events = [Valid().Events[0], new() { Name = "unknown" }] }));
        Assert.Equal(new (long, string?)[] { (1L, "0.1.6"), (1L, "other") }, observed);
    }
    [Fact] public void ControllerReturnsExpectedStatuses()
    {
        using var telemetry = new DesktopTelemetry(Config()); var controller = new DesktopTelemetryController(telemetry);
        Assert.IsType<BadRequestObjectResult>(controller.Post(Valid() with { Os = "bad" })); Assert.IsType<NoContentResult>(controller.Post(Valid()));
        for (var i = 1; i < 300; i++) telemetry.TryConsumeBudget();
        Assert.Equal(429, Assert.IsType<StatusCodeResult>(controller.Post(Valid())).StatusCode);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch; public override DateTimeOffset GetUtcNow() => Now; }
}
