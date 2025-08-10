using System.Text.Json;
using Camille.RiotGames;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.Service;

namespace Transcendence.Service.Services.Jobs;

// ReSharper disable once ClassNeverInstantiated.Global
public class UpdateParameters(
    RiotGamesApi riotGamesApi,
    TranscendenceContext transcendenceContext,
    ILogger<UpdateParameters> logger) : IJobTask
{
    private static readonly HttpClient HttpClient = new();

    public async Task Execute(CancellationToken stoppingToken)
    {
        var latestPatch = await GetLatestPatchAsync();
        logger.LogInformation($"Latest Patch: {latestPatch}");

        await UpdatePatchInDatabase(latestPatch);
    }

    private async Task<string> GetLatestPatchAsync()
    {
        const string versionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json";

        var response = await HttpClient.GetAsync(versionsUrl);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadAsStringAsync();
        var versions = JsonSerializer.Deserialize<string[]>(jsonResponse);

        return versions?.Length > 0 ? versions[0] : "Unknown";
    }

    private async Task UpdatePatchInDatabase(string latestPatch)
    {
        var currentPatchEntry = await transcendenceContext.CurrentDataParameters
            .OrderByDescending(p => p.StartDate) // Get the most recent patch entry
            .FirstOrDefaultAsync();

        if (currentPatchEntry == null || currentPatchEntry.Patch != latestPatch)
        {
            using var transaction = await transcendenceContext.Database.BeginTransactionAsync();

            try
            {
                // If there's an existing patch, update its EndDate
                if (currentPatchEntry != null)
                {
                    currentPatchEntry.EndDate = DateTime.UtcNow;
                    transcendenceContext.CurrentDataParameters.Update(currentPatchEntry);
                }

                // Insert a new entry for the latest patch
                var newPatchEntry = new CurrentDataParameters
                {
                    Patch = latestPatch,
                    StartDate = DateTime.UtcNow,
                    EndDate = null
                };

                await transcendenceContext.CurrentDataParameters.AddAsync(newPatchEntry);
                await transcendenceContext.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating patch data");
                await transaction.RollbackAsync();
            }
        }
    }
}