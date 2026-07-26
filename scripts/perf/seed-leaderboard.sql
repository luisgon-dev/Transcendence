\set ON_ERROR_STOP on

CREATE TEMP TABLE perf_summoners AS
SELECT gen_random_uuid() AS id, n
FROM generate_series(1, 200) AS series(n);

INSERT INTO "Summoners" (
  "Id", "RiotSummonerId", "ProfileIconId", "SummonerLevel", "RevisionDate",
  "Puuid", "GameName", "TagLine", "GameNameNormalized", "TagLineNormalized",
  "PlatformRegion", "Region", "UpdatedAt", "LastActiveAtUtc"
)
SELECT
  id,
  'perf-summoner-' || n,
  29,
  100 + n,
  0,
  'perf-puuid-' || n,
  'Performance' || lpad(n::text, 3, '0'),
  'CI',
  upper('Performance' || lpad(n::text, 3, '0')),
  'CI',
  'NA1',
  'americas',
  now(),
  now()
FROM perf_summoners;

INSERT INTO "Ranks" (
  "Id", "SummonerId", "QueueType", "Tier", "RankNumber",
  "LeaguePoints", "Wins", "Losses", "UpdatedAt"
)
SELECT
  gen_random_uuid(),
  id,
  'RANKED_SOLO_5x5',
  CASE WHEN n <= 25 THEN 'CHALLENGER' WHEN n <= 75 THEN 'MASTER' ELSE 'DIAMOND' END,
  'I',
  1000 - n,
  60 + (n % 30),
  30 + (n % 20),
  now()
FROM perf_summoners;

CREATE TEMP TABLE perf_matches AS
SELECT
  gen_random_uuid() AS id,
  s.id AS summoner_id,
  s.n AS summoner_number,
  game_number
FROM perf_summoners AS s
CROSS JOIN generate_series(1, 20) AS games(game_number);

INSERT INTO "Matches" (
  "Id", "MatchId", "MatchDate", "Duration", "Patch", "QueueId",
  "QueueFamily", "QueueType", "PlatformRegion", "Status", "RetryCount", "FetchedAt"
)
SELECT
  id,
  'NA1_PERF_' || replace(id::text, '-', ''),
  (extract(epoch FROM (now() - make_interval(hours => game_number))) * 1000)::bigint,
  1800,
  '99.1',
  420,
  'RANKED_SOLO',
  'RANKED_SOLO_5x5',
  'NA1',
  1,
  0,
  now()
FROM perf_matches;

INSERT INTO "MatchParticipants" (
  "Id", "MatchId", "SummonerId", "Puuid", "ParticipantId", "TeamId",
  "ChampionId", "TeamPosition", "Win", "Kills", "Deaths", "Assists",
  "ChampLevel", "GoldEarned", "TotalDamageDealtToChampions",
  "PhysicalDamageDealtToChampions", "MagicDamageDealtToChampions",
  "TrueDamageDealtToChampions", "VisionScore", "TotalMinionsKilled",
  "NeutralMinionsKilled", "SummonerSpell1Id", "SummonerSpell2Id"
)
SELECT
  gen_random_uuid(),
  m.id,
  m.summoner_id,
  'perf-puuid-' || m.summoner_number,
  1,
  CASE WHEN m.game_number % 2 = 0 THEN 100 ELSE 200 END,
  157,
  (ARRAY['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'])
    [1 + ((m.summoner_number + m.game_number) % 5)],
  (m.summoner_number + m.game_number) % 2 = 0,
  2 + (m.game_number % 9),
  1 + (m.game_number % 6),
  3 + (m.summoner_number % 12),
  18,
  11000 + (m.game_number * 100),
  15000 + (m.game_number * 250),
  9000,
  5500,
  500,
  10 + (m.game_number % 30),
  150 + m.game_number,
  10,
  4,
  12
FROM perf_matches AS m;

ANALYZE "Summoners";
ANALYZE "Ranks";
ANALYZE "Matches";
ANALYZE "MatchParticipants";
