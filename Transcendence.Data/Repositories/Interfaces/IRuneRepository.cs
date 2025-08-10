using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Transcendence.Data.Models.LoL.Match;

namespace Transcendence.Data.Repositories.Interfaces
{
    public interface IRuneRepository
    {
        Task<Runes?> GetExistingRunesAsync(
            int primaryStyle,
            int subStyle,
            int[] primaryRunes,
            int[] subRunes,
            int statDefense,
            int statFlex,
            int statOffense,
            CancellationToken cancellationToken = default);

        Task<Runes> AddRunesAsync(Runes runes, CancellationToken cancellationToken = default);
    }
}
