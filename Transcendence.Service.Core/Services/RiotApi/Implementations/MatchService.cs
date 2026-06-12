using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.MatchV5;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;
using Transcendence.Service.Core.Services.StaticData.Interfaces;
using Match = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Services.RiotApi.Implementations;

using DataMatch = Match;

public class MatchService(
    LeagueRiotApiContext riotApiContext,
    TranscendenceContext context,
    IMatchRepository matchRepository,
    ISummonerService summonerService,
    ISummonerRepository summonerRepository,
    IStaticDataService staticDataService,
    IRiotRateGate rateGate,
    ILogger<MatchService> logger) : IMatchService
{
    public async Task<DataMatch?> GetMatchDetailsAsync(
        string matchId,
        RegionalRoute regionalRoute,
        PlatformRoute platformRoute,
        CancellationToken cancellationToken = default)
    {
        // Pace under the per-region Riot budget so Camille never saturates; skip (retry later) rather than
        // park this worker if the region's budget stays exhausted past the gate's max wait.
        if (!await rateGate.AcquireAsync(regionalRoute.ToString(), cancellationToken))
        {
            logger.LogDebug("Riot rate gate skipped match {MatchId} ({Region}); will retry later.", matchId, regionalRoute);
            return null;
        }

        // Fetch match from Riot
        var matchDto = await riotApiContext.Api.MatchV5()
            .GetMatchAsync(regionalRoute, matchId, cancellationToken);
        if (matchDto == null)
        {
            logger.LogWarning("Riot API returned null for match {MatchId}", matchId);
            return null;
        }

        var info = matchDto.Info;
        var metadata = matchDto.Metadata;

        // Build match entity (do not persist here; caller handles persistence)
        var match = new DataMatch
        {
            MatchId = metadata.MatchId,
            MatchDate = info.GameCreation, // epoch ms
            Duration = (int)info.GameDuration,
            Patch = NormalizePatch(info.GameVersion),
            EndOfGameResult = info.EndOfGameResult,
            PlatformRegion = platformRoute.ToString(),
            Status = FetchStatus.Success,
            FetchedAt = DateTime.UtcNow
        };
        ApplyQueueMetadata(match, (int)info.QueueId);
        PopulateMatchBans(match, info);
        PopulateTeamObjectives(match, info);

        // Ensure static data for this match patch exists
        await staticDataService.EnsureStaticDataForPatchAsync(match.Patch, cancellationToken);

        var summonersByPuuid = await ResolveSummonersByPuuidAsync(
            info.Participants.Select(p => p.Puuid),
            platformRoute,
            cancellationToken);

        var missingPuuidParticipants = info.Participants.Count(p => string.IsNullOrWhiteSpace(p.Puuid));
        var unresolvedPuuids = info.Participants
            .Select(p => p.Puuid)
            .Where(puuid => !string.IsNullOrWhiteSpace(puuid))
            .Select(puuid => puuid!)
            .Distinct(StringComparer.Ordinal)
            .Where(puuid => !summonersByPuuid.ContainsKey(puuid))
            .ToList();

        if (missingPuuidParticipants > 0 || unresolvedPuuids.Count > 0)
        {
            logger.LogWarning(
                "Aborting match {MatchId} preparation due to unresolved participants. MissingPuuidParticipants={MissingCount}, UnresolvedPuuids={UnresolvedCount}, Sample={UnresolvedSample}",
                matchId,
                missingPuuidParticipants,
                unresolvedPuuids.Count,
                unresolvedPuuids.Take(5).ToArray());

            throw new InvalidOperationException(
                $"Unable to resolve all participants for match {matchId}. MissingPuuidParticipants={missingPuuidParticipants}, UnresolvedPuuids={unresolvedPuuids.Count}.");
        }

        var skippedDuplicateParticipants = 0;
        var seenSummonerIds = new HashSet<Guid>();
        var seenParticipantIds = new HashSet<int>();

        // Ensure Summoners exist, build participants and relationships
        foreach (var p in info.Participants)
        {
            var summoner = summonersByPuuid[p.Puuid!];
            TouchLastActive(summoner, match.MatchDate);

            // Link summoner to this match (many-to-many)
            if (match.Summoners.All(s => s.Id != summoner.Id)) match.Summoners.Add(summoner);

            if (!seenSummonerIds.Add(summoner.Id) || !seenParticipantIds.Add(p.ParticipantId))
            {
                skippedDuplicateParticipants++;
                logger.LogWarning(
                    "Skipping duplicate participant while preparing match {MatchId}. ParticipantId={ParticipantId}, Puuid={Puuid}, SummonerId={SummonerId}",
                    matchId,
                    p.ParticipantId,
                    p.Puuid,
                    summoner.Id);
                continue;
            }

            // Create participant
            var participant = new MatchParticipant
            {
                Match = match,
                Summoner = summoner,
                Puuid = p.Puuid,
                ParticipantId = p.ParticipantId,
                TeamId = (int)p.TeamId,
                ChampionId = (int)p.ChampionId,
                TeamPosition = !string.IsNullOrWhiteSpace(p.TeamPosition) ? p.TeamPosition : p.IndividualPosition,
                Win = p.Win,
                Kills = p.Kills,
                Deaths = p.Deaths,
                Assists = p.Assists,
                ChampLevel = p.ChampLevel,
                GoldEarned = p.GoldEarned,
                TotalDamageDealtToChampions = p.TotalDamageDealtToChampions,
                // keep the damage split in sync across all 3 participant-mapping blocks
                PhysicalDamageDealtToChampions = p.PhysicalDamageDealtToChampions,
                MagicDamageDealtToChampions = p.MagicDamageDealtToChampions,
                TrueDamageDealtToChampions = p.TrueDamageDealtToChampions,
                VisionScore = p.VisionScore,
                TotalMinionsKilled = p.TotalMinionsKilled,
                NeutralMinionsKilled = p.NeutralMinionsKilled,
                SummonerSpell1Id = p.Summoner1Id,
                SummonerSpell2Id = p.Summoner2Id
            };

            participant.Runes = CreateMatchParticipantRunes(p.Perks, match.Patch);
            participant.Items = CreateMatchParticipantItems(p, match.Patch);

            match.Participants.Add(participant);
        }

        var expectedParticipants = seenSummonerIds.Count;
        if (match.Participants.Count != expectedParticipants)
        {
            throw new InvalidOperationException(
                $"Participant integrity check failed for match {matchId}. Expected {expectedParticipants} participants, resolved {match.Participants.Count}.");
        }

        if (skippedDuplicateParticipants > 0)
        {
            logger.LogWarning(
                "Prepared match {MatchId} after skipping {SkippedCount} duplicate participant rows.",
                matchId,
                skippedDuplicateParticipants);
        }

        logger.LogDebug(
            "Prepared match {MatchId} with {Count} participants for persistence.",
            matchId,
            match.Participants.Count);
        return match;
    }

    public async Task<DataMatch?> GetMatchDetailsLightweightAsync(
        string matchId,
        RegionalRoute regionalRoute,
        PlatformRoute platformRoute,
        CancellationToken cancellationToken = default)
    {
        if (!await rateGate.AcquireAsync(regionalRoute.ToString(), cancellationToken))
        {
            logger.LogDebug("[Lightweight] Riot rate gate skipped match {MatchId} ({Region}); will retry later.", matchId, regionalRoute);
            return null;
        }

        var matchDto = await riotApiContext.Api.MatchV5()
            .GetMatchAsync(regionalRoute, matchId, cancellationToken);
        if (matchDto == null)
        {
            logger.LogWarning("[Lightweight] Riot API returned null for match {MatchId}", matchId);
            return null;
        }

        var info = matchDto.Info;
        var metadata = matchDto.Metadata;

        var match = new DataMatch
        {
            MatchId = metadata.MatchId,
            MatchDate = info.GameCreation,
            Duration = (int)info.GameDuration,
            Patch = NormalizePatch(info.GameVersion),
            EndOfGameResult = info.EndOfGameResult,
            PlatformRegion = platformRoute.ToString(),
            Status = FetchStatus.Success,
            FetchedAt = DateTime.UtcNow
        };
        ApplyQueueMetadata(match, (int)info.QueueId);
        PopulateMatchBans(match, info);
        PopulateTeamObjectives(match, info);

        await staticDataService.EnsureStaticDataForPatchAsync(match.Patch, cancellationToken);

        // Batch lookup all participant PUUIDs in a single query instead of N+1
        var participantPuuids = info.Participants
            .Select(p => p.Puuid)
            .Where(puuid => !string.IsNullOrWhiteSpace(puuid))
            .Distinct()
            .ToList();

        var existingSummonersRaw = await context.Summoners
            .Where(s => s.Puuid != null && participantPuuids.Contains(s.Puuid))
            .ToListAsync(cancellationToken);

        var duplicatePuuids = existingSummonersRaw
            .GroupBy(s => s.Puuid!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicatePuuids.Count > 0)
        {
            logger.LogWarning(
                "[Lightweight] Found {DuplicateCount} duplicate summoner records by PUUID while processing {MatchId}. Using the most recently updated record per PUUID.",
                duplicatePuuids.Count,
                matchId);
        }

        var existingSummoners = existingSummonersRaw
            .GroupBy(s => s.Puuid!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.Id)
                    .First(),
                StringComparer.Ordinal);

        var skippedLightweightDuplicates = 0;
        var seenLightweightSummonerIds = new HashSet<Guid>();
        var seenLightweightParticipantIds = new HashSet<int>();

        foreach (var p in info.Participants)
        {
            if (string.IsNullOrWhiteSpace(p.Puuid))
            {
                logger.LogWarning(
                    "[Lightweight] Skipping participant with missing PUUID in match {MatchId}.",
                    matchId);
                continue;
            }

            if (!existingSummoners.TryGetValue(p.Puuid, out var summoner))
            {
                // Create a minimal stub from match participant data instead of calling Riot API
                var stubSummoner = new Data.Models.LoL.Account.Summoner
                {
                    Id = Guid.NewGuid(),
                    Puuid = p.Puuid,
                    GameName = p.RiotIdGameName,
                    TagLine = p.RiotIdTagline,
                    GameNameNormalized = NormalizeForLookup(p.RiotIdGameName),
                    TagLineNormalized = NormalizeForLookup(p.RiotIdTagline),
                    SummonerName = !string.IsNullOrWhiteSpace(p.RiotIdGameName)
                        ? $"{p.RiotIdGameName}#{p.RiotIdTagline}"
                        : null,
                    PlatformRegion = platformRoute.ToString(),
                    Region = platformRoute.ToRegional().ToString(),
                    UpdatedAt = DateTime.MinValue,
                    Ranks = []
                };

                summoner = await summonerRepository.AddOrUpdateSummonerAsync(stubSummoner, cancellationToken);
                existingSummoners[p.Puuid] = summoner;
            }

            TouchLastActive(summoner, match.MatchDate);

            if (match.Summoners.All(s => s.Id != summoner.Id)) match.Summoners.Add(summoner);

            if (!seenLightweightSummonerIds.Add(summoner.Id) || !seenLightweightParticipantIds.Add(p.ParticipantId))
            {
                skippedLightweightDuplicates++;
                logger.LogWarning(
                    "[Lightweight] Skipping duplicate participant while preparing match {MatchId}. ParticipantId={ParticipantId}, Puuid={Puuid}, SummonerId={SummonerId}",
                    matchId,
                    p.ParticipantId,
                    p.Puuid,
                    summoner.Id);
                continue;
            }

            var participant = new MatchParticipant
            {
                Match = match,
                Summoner = summoner,
                Puuid = p.Puuid,
                ParticipantId = p.ParticipantId,
                TeamId = (int)p.TeamId,
                ChampionId = (int)p.ChampionId,
                TeamPosition = !string.IsNullOrWhiteSpace(p.TeamPosition) ? p.TeamPosition : p.IndividualPosition,
                Win = p.Win,
                Kills = p.Kills,
                Deaths = p.Deaths,
                Assists = p.Assists,
                ChampLevel = p.ChampLevel,
                GoldEarned = p.GoldEarned,
                TotalDamageDealtToChampions = p.TotalDamageDealtToChampions,
                // keep the damage split in sync across all 3 participant-mapping blocks
                PhysicalDamageDealtToChampions = p.PhysicalDamageDealtToChampions,
                MagicDamageDealtToChampions = p.MagicDamageDealtToChampions,
                TrueDamageDealtToChampions = p.TrueDamageDealtToChampions,
                VisionScore = p.VisionScore,
                TotalMinionsKilled = p.TotalMinionsKilled,
                NeutralMinionsKilled = p.NeutralMinionsKilled,
                SummonerSpell1Id = p.Summoner1Id,
                SummonerSpell2Id = p.Summoner2Id
            };

            participant.Runes = CreateMatchParticipantRunes(p.Perks, match.Patch);
            participant.Items = CreateMatchParticipantItems(p, match.Patch);

            match.Participants.Add(participant);
        }

        if (skippedLightweightDuplicates > 0)
        {
            logger.LogWarning(
                "[Lightweight] Prepared match {MatchId} after skipping {SkippedCount} duplicate participant rows.",
                matchId,
                skippedLightweightDuplicates);
        }

        logger.LogDebug(
            "[Lightweight] Prepared match {MatchId} with {Count} participants for persistence.",
            matchId,
            match.Participants.Count);
        return match;
    }

    public async Task<bool> FetchMatchWithRetryAsync(string matchId, string region, CancellationToken cancellationToken = default)
    {
        var match = await matchRepository.GetMatchByIdAsync(matchId, cancellationToken)
                    ?? new DataMatch { MatchId = matchId, Status = FetchStatus.Unfetched };

        // Check retention window BEFORE attempting fetch
        if (match.MatchDate > 0)
        {
            var matchAge = DateTime.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(match.MatchDate).UtcDateTime;
            if (matchAge.TotalDays > 730) // 2 years
            {
                match.Status = FetchStatus.OutsideRetentionWindow;
                match.LastAttemptAt = DateTime.UtcNow;
                match.LastErrorMessage = "Match data outside Riot API 2-year retention window";

                if (match.Id == Guid.Empty)
                {
                    context.Matches.Add(match);
                }

                await context.SaveChangesAsync(cancellationToken);
                return false;
            }
        }

        try
        {
            match.LastAttemptAt = DateTime.UtcNow;

            // Camille handles rate limiting automatically
            if (!TryParseRegionalRoute(region, out var regionalRoute))
                throw new ArgumentException($"Unsupported region '{region}' for match retry.", nameof(region));

            var platformRoute = ResolvePlatformRoute(matchId, regionalRoute);
            if (!await rateGate.AcquireAsync(regionalRoute.ToString(), cancellationToken))
                throw new InvalidOperationException($"Riot rate gate exhausted for match {matchId} ({regionalRoute}); retry later.");
            var matchDto = await riotApiContext.Api.MatchV5().GetMatchAsync(regionalRoute, matchId, cancellationToken);

            if (matchDto == null)
            {
                throw new InvalidOperationException($"Riot API returned null for match {matchId}");
            }

            // Parse and store match data
            var info = matchDto.Info;
            var metadata = matchDto.Metadata;

            match.MatchId = metadata.MatchId;
            match.MatchDate = info.GameCreation;
            match.Duration = (int)info.GameDuration;
            match.Patch = NormalizePatch(info.GameVersion);
            match.EndOfGameResult = info.EndOfGameResult;
            match.PlatformRegion = platformRoute.ToString();
            ApplyQueueMetadata(match, (int)info.QueueId);
            PopulateMatchBans(match, info);
            PopulateTeamObjectives(match, info);

            // Ensure static data for this match patch exists
            await staticDataService.EnsureStaticDataForPatchAsync(match.Patch, cancellationToken);

            var summonersByPuuid = await ResolveSummonersByPuuidAsync(
                info.Participants.Select(p => p.Puuid),
                platformRoute,
                cancellationToken);

            var missingPuuidParticipants = info.Participants.Count(p => string.IsNullOrWhiteSpace(p.Puuid));
            var unresolvedPuuids = info.Participants
                .Select(p => p.Puuid)
                .Where(puuid => !string.IsNullOrWhiteSpace(puuid))
                .Select(puuid => puuid!)
                .Distinct(StringComparer.Ordinal)
                .Where(puuid => !summonersByPuuid.ContainsKey(puuid))
                .ToList();

            if (missingPuuidParticipants > 0 || unresolvedPuuids.Count > 0)
            {
                logger.LogWarning(
                    "Aborting retry match fetch {MatchId} due to unresolved participants. MissingPuuidParticipants={MissingCount}, UnresolvedPuuids={UnresolvedCount}, Sample={UnresolvedSample}",
                    matchId,
                    missingPuuidParticipants,
                    unresolvedPuuids.Count,
                    unresolvedPuuids.Take(5).ToArray());

                throw new InvalidOperationException(
                    $"Unable to resolve all participants for retry match fetch {matchId}. MissingPuuidParticipants={missingPuuidParticipants}, UnresolvedPuuids={unresolvedPuuids.Count}.");
            }

            var skippedRetryDuplicates = 0;
            var seenRetrySummonerIds = new HashSet<Guid>();
            var seenRetryParticipantIds = new HashSet<int>();

            // Ensure Summoners exist, build participants and relationships
            foreach (var p in info.Participants)
            {
                var summoner = summonersByPuuid[p.Puuid!];
                TouchLastActive(summoner, match.MatchDate);

                if (match.Summoners.All(s => s.Id != summoner.Id)) match.Summoners.Add(summoner);

                if (!seenRetrySummonerIds.Add(summoner.Id) || !seenRetryParticipantIds.Add(p.ParticipantId))
                {
                    skippedRetryDuplicates++;
                    logger.LogWarning(
                        "Skipping duplicate participant while retry-fetching match {MatchId}. ParticipantId={ParticipantId}, Puuid={Puuid}, SummonerId={SummonerId}",
                        matchId,
                        p.ParticipantId,
                        p.Puuid,
                        summoner.Id);
                    continue;
                }

                var participant = new MatchParticipant
                {
                    Match = match,
                    Summoner = summoner,
                    Puuid = p.Puuid,
                    ParticipantId = p.ParticipantId,
                    TeamId = (int)p.TeamId,
                    ChampionId = (int)p.ChampionId,
                    TeamPosition = !string.IsNullOrWhiteSpace(p.TeamPosition) ? p.TeamPosition : p.IndividualPosition,
                    Win = p.Win,
                    Kills = p.Kills,
                    Deaths = p.Deaths,
                    Assists = p.Assists,
                    ChampLevel = p.ChampLevel,
                    GoldEarned = p.GoldEarned,
                    TotalDamageDealtToChampions = p.TotalDamageDealtToChampions,
                    // keep the damage split in sync across all 3 participant-mapping blocks
                    PhysicalDamageDealtToChampions = p.PhysicalDamageDealtToChampions,
                    MagicDamageDealtToChampions = p.MagicDamageDealtToChampions,
                    TrueDamageDealtToChampions = p.TrueDamageDealtToChampions,
                    VisionScore = p.VisionScore,
                    TotalMinionsKilled = p.TotalMinionsKilled,
                    NeutralMinionsKilled = p.NeutralMinionsKilled,
                    SummonerSpell1Id = p.Summoner1Id,
                    SummonerSpell2Id = p.Summoner2Id
                };

                participant.Runes = CreateMatchParticipantRunes(p.Perks, match.Patch);
                participant.Items = CreateMatchParticipantItems(p, match.Patch);

                match.Participants.Add(participant);
            }

            var expectedParticipants = seenRetrySummonerIds.Count;
            if (match.Participants.Count != expectedParticipants)
            {
                throw new InvalidOperationException(
                    $"Participant integrity check failed for retry match fetch {matchId}. Expected {expectedParticipants} participants, resolved {match.Participants.Count}.");
            }

            if (skippedRetryDuplicates > 0)
            {
                logger.LogWarning(
                    "Retry fetch prepared match {MatchId} after skipping {SkippedCount} duplicate participant rows.",
                    matchId,
                    skippedRetryDuplicates);
            }

            match.Status = FetchStatus.Success;
            match.FetchedAt = DateTime.UtcNow;
            match.LastErrorMessage = null;

            if (match.Id == Guid.Empty)
            {
                context.Matches.Add(match);
            }

            await context.SaveChangesAsync(cancellationToken);

            logger.LogDebug("Successfully fetched match {MatchId}", matchId);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            match.RetryCount++;
            match.LastErrorMessage = ex.Message;

            if (match.RetryCount >= 5)
            {
                match.Status = FetchStatus.PermanentlyUnfetchable;
                logger.LogWarning("Match {MatchId} marked unfetchable after {RetryCount} attempts: {Error}",
                    matchId, match.RetryCount, ex.Message);
            }
            else
            {
                match.Status = FetchStatus.TemporaryFailure;

                // Schedule retry with exponential backoff: 30s, 60s, 120s, 300s
                var delays = new[] { 30, 60, 120, 300 };
                var delay = TimeSpan.FromSeconds(delays[Math.Min(match.RetryCount - 1, delays.Length - 1)]);

                BackgroundJob.Schedule<IMatchService>(
                    service => service.FetchMatchWithRetryAsync(matchId, region, CancellationToken.None),
                    delay);

                logger.LogDebug("Match {MatchId} retry scheduled in {Delay}s (attempt {RetryCount})",
                    matchId, delay.TotalSeconds, match.RetryCount);
            }

            if (match.Id == Guid.Empty)
            {
                context.Matches.Add(match);
            }

            await context.SaveChangesAsync(cancellationToken);
            return false;
        }
    }

    private async Task<Dictionary<string, Data.Models.LoL.Account.Summoner>> ResolveSummonersByPuuidAsync(
        IEnumerable<string?> puuids,
        PlatformRoute platformRoute,
        CancellationToken cancellationToken)
    {
        var participantPuuids = puuids
            .Where(puuid => !string.IsNullOrWhiteSpace(puuid))
            .Select(puuid => puuid!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (participantPuuids.Count == 0)
            return new Dictionary<string, Data.Models.LoL.Account.Summoner>(StringComparer.Ordinal);

        var existingSummonersRaw = await context.Summoners
            .Where(s => s.Puuid != null && participantPuuids.Contains(s.Puuid))
            .ToListAsync(cancellationToken);

        var duplicatePuuids = existingSummonersRaw
            .GroupBy(s => s.Puuid!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicatePuuids.Count > 0)
        {
            logger.LogWarning(
                "Found {DuplicateCount} duplicate summoner records by PUUID during match processing. Using the most recently updated record per PUUID.",
                duplicatePuuids.Count);
        }

        var existingSummoners = existingSummonersRaw
            .GroupBy(s => s.Puuid!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.Id)
                    .First(),
                StringComparer.Ordinal);

        foreach (var puuid in participantPuuids)
        {
            if (existingSummoners.ContainsKey(puuid))
                continue;

            try
            {
                var summoner = await summonerService.GetSummonerByPuuidAsync(puuid, platformRoute, cancellationToken);
                var persistedSummoner = await summonerRepository.AddOrUpdateSummonerAsync(summoner, cancellationToken);
                existingSummoners[puuid] = persistedSummoner;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve summoner for participant PUUID {Puuid}.", puuid);
            }
        }

        return existingSummoners;
    }

    private static string NormalizePatch(string? gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion)) return string.Empty;
        var parts = gameVersion.Split('.');
        if (parts.Length >= 2) return $"{parts[0]}.{parts[1]}";
        return gameVersion;
    }

    private static bool TryParseRegionalRoute(string input, out RegionalRoute regionalRoute)
    {
        var normalized = input.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty)
            .ToUpperInvariant();

        if (Enum.TryParse(normalized, true, out regionalRoute))
            return true;

        if (PlatformRouteParser.TryParse(normalized, out var platformRoute))
        {
            regionalRoute = platformRoute.ToRegional();
            return true;
        }

        regionalRoute = default;
        return false;
    }

    private static PlatformRoute ResolvePlatformRoute(string matchId, RegionalRoute regionalRoute)
    {
        var matchPrefix = matchId.Split('_')[0].ToUpperInvariant();
        if (Enum.TryParse<PlatformRoute>(matchPrefix, true, out var platformFromMatchId))
            return platformFromMatchId;

        // Fallback only when the match ID is malformed.
        return regionalRoute switch
        {
            RegionalRoute.AMERICAS => PlatformRoute.NA1,
            RegionalRoute.EUROPE => PlatformRoute.EUW1,
            RegionalRoute.ASIA => PlatformRoute.KR,
            RegionalRoute.SEA => PlatformRoute.OC1,
            _ => PlatformRoute.NA1
        };
    }

    private ICollection<MatchParticipantRune> CreateMatchParticipantRunes(Perks perks, string patch)
    {
        var participantRunes = new List<MatchParticipantRune>();
        var styles = perks.Styles?.ToList() ?? [];

        var primaryStyle = styles.FirstOrDefault(s =>
            string.Equals(s.Description?.ToString(), "primaryStyle", StringComparison.OrdinalIgnoreCase));
        var secondaryStyle = styles.FirstOrDefault(s =>
            string.Equals(s.Description?.ToString(), "subStyle", StringComparison.OrdinalIgnoreCase));

        if (primaryStyle == null && styles.Count > 0)
            primaryStyle = styles[0];

        if (secondaryStyle == null)
            secondaryStyle = styles.FirstOrDefault(s => !ReferenceEquals(s, primaryStyle));

        if (primaryStyle != null)
        {
            var primaryIndex = 0;
            foreach (var selection in primaryStyle.Selections)
            {
                var runeId = selection.Perk;
                if (runeId == 0) continue;

                participantRunes.Add(new MatchParticipantRune
                {
                    RuneId = runeId,
                    PatchVersion = patch,
                    SelectionTree = RuneSelectionTree.Primary,
                    SelectionIndex = primaryIndex++,
                    StyleId = primaryStyle.Style
                });
            }
        }

        if (secondaryStyle != null)
        {
            var secondaryIndex = 0;
            foreach (var selection in secondaryStyle.Selections)
            {
                var runeId = selection.Perk;
                if (runeId == 0) continue;

                participantRunes.Add(new MatchParticipantRune
                {
                    RuneId = runeId,
                    PatchVersion = patch,
                    SelectionTree = RuneSelectionTree.Secondary,
                    SelectionIndex = secondaryIndex++,
                    StyleId = secondaryStyle.Style
                });
            }
        }

        var statRuneIds = new[] { perks.StatPerks.Offense, perks.StatPerks.Flex, perks.StatPerks.Defense };
        for (var i = 0; i < statRuneIds.Length; i++)
        {
            var runeId = statRuneIds[i];
            if (runeId == 0) continue;

            participantRunes.Add(new MatchParticipantRune
            {
                RuneId = runeId,
                PatchVersion = patch,
                SelectionTree = RuneSelectionTree.StatShards,
                SelectionIndex = i,
                StyleId = 0
            });
        }

        return participantRunes;
    }

    private ICollection<MatchParticipantItem> CreateMatchParticipantItems(Participant participant, string patch)
    {
        var slotItems = new[]
        {
            participant.Item0,
            participant.Item1,
            participant.Item2,
            participant.Item3,
            participant.Item4,
            participant.Item5,
            participant.Item6
        };

        var participantItems = slotItems
            .Select((itemId, slotIndex) => new { itemId, slotIndex })
            .Where(x => x.itemId != 0)
            .Select(x => new MatchParticipantItem
            {
                SlotIndex = x.slotIndex,
                ItemId = x.itemId,
                PatchVersion = patch
            })
            .ToList();

        return participantItems;
    }

    private static void ApplyQueueMetadata(DataMatch match, int queueId)
    {
        match.QueueId = queueId;
        match.QueueFamily = QueueCatalog.ResolveQueueFamily(queueId);
        match.QueueType = QueueCatalog.ResolveQueueLabel(queueId);
    }

    private static void PopulateMatchBans(DataMatch match, Info info)
    {
        match.Bans.Clear();
        var teams = info.Teams?.ToList() ?? [];
        foreach (var team in teams)
        {
            var bans = team.Bans?.ToList() ?? [];
            foreach (var ban in bans)
            {
                if (ban.ChampionId <= 0)
                    continue;

                match.Bans.Add(new MatchBan
                {
                    Match = match,
                    TeamId = (int)team.TeamId,
                    PickTurn = ban.PickTurn,
                    ChampionId = (int)ban.ChampionId
                });
            }
        }
    }

    private static void PopulateTeamObjectives(DataMatch match, Info info)
    {
        match.TeamObjectives.Clear();
        var teams = info.Teams?.ToList() ?? [];
        foreach (var team in teams)
        {
            var objectives = team.Objectives;
            if (objectives == null)
                continue;

            match.TeamObjectives.Add(new MatchTeamObjective
            {
                Match = match,
                TeamId = (int)team.TeamId,
                FirstBlood = objectives.Champion?.First ?? false,
                BaronKills = objectives.Baron?.Kills ?? 0,
                BaronFirst = objectives.Baron?.First ?? false,
                DragonKills = objectives.Dragon?.Kills ?? 0,
                DragonFirst = objectives.Dragon?.First ?? false,
                RiftHeraldKills = objectives.RiftHerald?.Kills ?? 0,
                RiftHeraldFirst = objectives.RiftHerald?.First ?? false,
                HordeKills = objectives.Horde?.Kills ?? 0,
                HordeFirst = objectives.Horde?.First ?? false,
                TowerKills = objectives.Tower?.Kills ?? 0,
                TowerFirst = objectives.Tower?.First ?? false,
                InhibitorKills = objectives.Inhibitor?.Kills ?? 0,
                InhibitorFirst = objectives.Inhibitor?.First ?? false
            });
        }
    }

    private static string? NormalizeForLookup(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    // Records the summoner's most recent game-creation time (a true activity signal used by ingestion
    // candidate selection). Monotonic max — EF only writes when it actually advances, so re-processing
    // older matches costs nothing. The summoner is already tracked by this scope's context; the value
    // flushes with the caller's match persist.
    private static void TouchLastActive(Data.Models.LoL.Account.Summoner summoner, long matchDateEpochMs)
    {
        if (matchDateEpochMs <= 0)
            return;

        var matchCreatedUtc = DateTimeOffset.FromUnixTimeMilliseconds(matchDateEpochMs).UtcDateTime;
        if (!summoner.LastActiveAtUtc.HasValue || matchCreatedUtc > summoner.LastActiveAtUtc.Value)
            summoner.LastActiveAtUtc = matchCreatedUtc;
    }
}
