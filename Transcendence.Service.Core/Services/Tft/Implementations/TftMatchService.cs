using Camille.Enums;
using Camille.RiotGames;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.Tft.Account;
using Transcendence.Data.Models.Tft.Match;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.Tft.Interfaces;

namespace Transcendence.Service.Core.Services.Tft.Implementations;

public class TftMatchService(
    TftRiotApiContext riotApiContext,
    ITftSummonerRepository summonerRepository) : ITftMatchService
{
    public async Task<TftMatch?> GetMatchDetailsAsync(string matchId, RegionalRoute regionalRoute, PlatformRoute platformRoute,
        CancellationToken ct = default)
    {
        var matchDto = await riotApiContext.Api.TftMatchV1().GetMatchAsync(regionalRoute, matchId, ct);
        if (matchDto == null)
            return null;

        var info = matchDto.Info;
        var metadata = matchDto.Metadata;
        var match = new TftMatch
        {
            Id = Guid.NewGuid(),
            MatchId = metadata.MatchId,
            MatchDate = info.GameCreation ?? info.GameDatetime,
            DurationSeconds = (int)Math.Round(info.GameLength),
            Patch = NormalizePatch(info.GameVersion),
            SetNumber = info.TftSetNumber,
            SetCoreName = info.TftSetCoreName,
            TftGameType = info.TftGameType,
            GameVariation = info.GameVariation,
            QueueId = info.QueueId.ToString(),
            PlatformRegion = platformRoute.ToString(),
            Status = TftFetchStatus.Success,
            FetchedAtUtc = DateTime.UtcNow
        };

        foreach (var participantDto in info.Participants ?? [])
        {
            if (participantDto == null || string.IsNullOrWhiteSpace(participantDto.Puuid))
                continue;

            var summoner = await ResolveSummonerAsync(participantDto, platformRoute, ct);
            var participant = new TftMatchParticipant
            {
                Id = Guid.NewGuid(),
                Match = match,
                MatchId = match.Id,
                Summoner = summoner,
                SummonerId = summoner.Id,
                Puuid = participantDto.Puuid,
                RiotIdGameName = participantDto.RiotIdGameName,
                RiotIdTagLine = participantDto.RiotIdTagline,
                Placement = participantDto.Placement,
                Level = participantDto.Level,
                LastRound = participantDto.LastRound,
                PlayersEliminated = participantDto.PlayersEliminated,
                GoldLeft = participantDto.GoldLeft,
                TotalDamageToPlayers = participantDto.TotalDamageToPlayers,
                TimeEliminatedSeconds = participantDto.TimeEliminated,
                Win = participantDto.Win,
                Augments = participantDto.Augments ?? [],
                CompanionItemId = participantDto.Companion?.ItemID,
                CompanionSkinId = participantDto.Companion?.SkinID,
                CompanionSpecies = participantDto.Companion?.Species
            };

            foreach (var trait in participantDto.Traits ?? [])
            {
                participant.Traits.Add(new TftMatchParticipantTrait
                {
                    Id = Guid.NewGuid(),
                    Participant = participant,
                    Name = trait.Name ?? string.Empty,
                    NumUnits = trait.NumUnits,
                    Style = trait.Style,
                    TierCurrent = trait.TierCurrent,
                    TierTotal = trait.TierTotal
                });
            }

            for (var augmentIndex = 0; augmentIndex < participant.Augments.Length; augmentIndex++)
            {
                participant.AugmentRows.Add(new TftMatchParticipantAugment
                {
                    Id = Guid.NewGuid(),
                    Participant = participant,
                    SlotIndex = augmentIndex,
                    ApiName = participant.Augments[augmentIndex]
                });
            }

            foreach (var unit in participantDto.Units ?? [])
            {
                participant.Units.Add(new TftMatchParticipantUnit
                {
                    Id = Guid.NewGuid(),
                    Participant = participant,
                    CharacterId = unit.CharacterId ?? string.Empty,
                    Name = unit.Name,
                    Rarity = unit.Rarity,
                    Tier = unit.Tier,
                    Chosen = unit.Chosen,
                    Items = unit.Items ?? [],
                    ItemNames = unit.ItemNames ?? []
                });
            }

            match.Participants.Add(participant);
        }

        return match;
    }

    private async Task<TftSummoner> ResolveSummonerAsync(Camille.RiotGames.TftMatchV1.Participant participantDto, PlatformRoute platformRoute,
        CancellationToken ct)
    {
        var existing = await summonerRepository.GetByPuuidAsync(participantDto.Puuid!, q => q.Include(x => x.Ranks), ct);
        if (existing != null)
            return existing;

        var stub = new TftSummoner
        {
            Id = Guid.NewGuid(),
            Puuid = participantDto.Puuid,
            GameName = participantDto.RiotIdGameName,
            TagLine = participantDto.RiotIdTagline,
            PlatformRegion = platformRoute.ToString(),
            Region = platformRoute.ToRegional().ToString()
        };

        return await summonerRepository.AddOrUpdateAsync(stub, ct);
    }

    private static string NormalizePatch(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "Unknown";

        var parts = version.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
    }
}
