using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.Service;
using Transcendence.Service.Core.Services.Analysis.Interfaces;

namespace Transcendence.Service.Core.Services.Analysis.Implementations;

public class ChampionLoadoutAnalysisService(TranscendenceContext context) : IChampionLoadoutAnalysisService
{
    public async Task<List<CurrentChampionLoadout>> GetChampionLoadoutsAsync(CancellationToken stoppingToken)
    {
        return await context.CurrentChampionLoadouts
            .AsNoTracking()
            .Include(loadout => loadout.UnitWinPercents)
            .ToListAsync(stoppingToken);
    }
}
