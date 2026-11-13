\set ON_ERROR_STOP on
-- Disposable local/CI database only. No Riot calls or production credentials required.
INSERT INTO "Summoners" (
  "Id", "RiotSummonerId", "ProfileIconId", "SummonerLevel", "RevisionDate",
  "Puuid", "GameName", "TagLine", "GameNameNormalized", "TagLineNormalized",
  "PlatformRegion", "Region", "UpdatedAt", "LastActiveAtUtc"
) VALUES (
  'a1111111-1111-4111-8111-111111111111', 'ci-faker', 29, 100, 0,
  'ci-profile-puuid', 'faker', 'NA1', 'FAKER', 'NA1', 'NA1', 'americas', now(), now()
) ON CONFLICT ("Id") DO UPDATE SET "UpdatedAt" = now();

-- The performance seed owns representative match facts. Give its first player
-- the profile used by the keyboard/match-expansion browser regressions.
UPDATE "Summoners" SET "GameName" = 'Kronic', "TagLine" = 'NA1',
  "GameNameNormalized" = 'KRONIC', "TagLineNormalized" = 'NA1'
WHERE "RiotSummonerId" = 'perf-summoner-1';
INSERT INTO "Patches" ("Version", "ReleaseDate", "DetectedAt", "IsActive")
VALUES ('99.1', now() - interval '7 days', now(), true)
ON CONFLICT ("Version") DO UPDATE SET "IsActive" = true;
