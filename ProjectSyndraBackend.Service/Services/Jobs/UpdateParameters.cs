using System.Text.Json;
using Camille.RiotGames;
using Microsoft.EntityFrameworkCore;
using ProjectSyndraBackend.Data;
using ProjectSyndraBackend.Data.Models.Service;

namespace ProjectSyndraBackend.Service.Services.Jobs;

public class UpdateParameters(
    RiotGamesApi riotGamesApi,
    ProjectSyndraContext projectSyndraContext,
    ILogger<UpdateParameters> logger) : IJobTask
{
    private static readonly HttpClient HttpClient = new();

    public async Task Execute(CancellationToken stoppingToken)
    {
        string latestPatch = await GetLatestPatchAsync();
        logger.LogInformation($"Latest Patch: {latestPatch}");

        await UpdatePatchInDatabase(latestPatch);
    }

    private async Task<string> GetLatestPatchAsync()
    {
        const string versionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json";

        HttpResponseMessage response = await HttpClient.GetAsync(versionsUrl);
        response.EnsureSuccessStatusCode();

        string jsonResponse = await response.Content.ReadAsStringAsync();
        var versions = JsonSerializer.Deserialize<string[]>(jsonResponse);

        return versions?.Length > 0 ? versions[0] : "Unknown";
    }

    private async Task UpdatePatchInDatabase(string latestPatch)
    {
        var currentPatchEntry = await projectSyndraContext.CurrentDataParameters
            .OrderByDescending(p => p.StartDate) // Get the most recent patch entry
            .FirstOrDefaultAsync();

        if (currentPatchEntry == null || currentPatchEntry.Patch != latestPatch)
        {
            using var transaction = await projectSyndraContext.Database.BeginTransactionAsync();

            try
            {
                // If there's an existing patch, update its EndDate
                if (currentPatchEntry != null)
                {
                    currentPatchEntry.EndDate = DateTime.UtcNow;
                    projectSyndraContext.CurrentDataParameters.Update(currentPatchEntry);
                }

                // Insert a new entry for the latest patch
                var newPatchEntry = new CurrentDataParameters
                {
                    Patch = latestPatch,
                    StartDate = DateTime.UtcNow,
                    EndDate = null
                };

                await projectSyndraContext.CurrentDataParameters.AddAsync(newPatchEntry);
                await projectSyndraContext.SaveChangesAsync();

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