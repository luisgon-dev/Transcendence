using Transcendence.Service.Services.Analysis.Interfaces;
namespace Transcendence.Service.Services.Jobs;

// ReSharper disable once UnusedType.Global
public class AnalyzeData(IChampionLoadoutAnalysisService championLoadoutAnalysis)
{
    public async Task Execute(CancellationToken stoppingToken)
    {
        var loadouts = await championLoadoutAnalysis.GetChampionLoadoutsAsync(stoppingToken);
        // do something with the loadouts
        // for now print all the loadouts
        foreach (var loadout in loadouts) Console.WriteLine(loadout);
    }
}
