using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations
{
    public class RuneRepository (TranscendenceContext context) : IRuneRepository
    {

        public async Task<Runes?> GetExistingRunesAsync(
        int primaryStyle,
        int subStyle,
        int[] primaryRunes,
        int[] subRunes,
        int statDefense,
        int statFlex,
        int statOffense,
        CancellationToken cancellationToken = default)
        {
            return await context.Runes.FirstOrDefaultAsync(r =>
                r.PrimaryStyle == primaryStyle &&
                r.SubStyle == subStyle &&
                r.Perk0 == primaryRunes[0] &&
                r.Perk1 == primaryRunes[1] &&
                r.Perk2 == primaryRunes[2] &&
                r.Perk3 == primaryRunes[3] &&
                r.Perk4 == subRunes[0] &&
                r.Perk5 == subRunes[1] &&
                r.StatDefense == statDefense &&
                r.StatFlex == statFlex &&
                r.StatOffense == statOffense,
                cancellationToken);
        }

        public async Task<Runes> AddRunesAsync(Runes runes, CancellationToken cancellationToken = default)
        {
            var entry = await context.Runes.AddAsync(runes, cancellationToken);
            return entry.Entity;
        }
        
    }
}
