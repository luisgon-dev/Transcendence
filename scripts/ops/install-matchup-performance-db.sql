-- One-time online database preparation for the resumable matchup pipeline.
--
-- Run with psql (not through an EF transaction): CREATE INDEX CONCURRENTLY cannot execute inside
-- a transaction block. Every statement is idempotent and keeps the large source tables writable.

\set ON_ERROR_STOP on

CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Matches_AnalyticsEligible"
    ON "Matches" ("Patch", "Id")
    WHERE "Status" = 1
      AND ("QueueId" = 420 OR ("QueueId" = 0 AND "QueueType" = '420'));

CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_MatchParticipantTimelineSnapshots_Minute15"
    ON "MatchParticipantTimelineSnapshots" ("MatchId", "ParticipantId")
    INCLUDE ("Gold", "Xp", "DerivedAtUtc")
    WHERE "MinuteMark" = 15;

-- The defaults wait for roughly 20% churn before analyzing. These append-heavy tables are large
-- enough that this leaves the planner with stale cardinality for too long. Keep thresholds bounded
-- without globally increasing autovacuum pressure.
ALTER TABLE "MatchParticipants" SET (
    autovacuum_vacuum_scale_factor = 0.02,
    autovacuum_vacuum_threshold = 50000,
    autovacuum_analyze_scale_factor = 0.01,
    autovacuum_analyze_threshold = 25000
);

ALTER TABLE "MatchParticipantTimelineSnapshots" SET (
    autovacuum_vacuum_scale_factor = 0.01,
    autovacuum_vacuum_threshold = 100000,
    autovacuum_analyze_scale_factor = 0.005,
    autovacuum_analyze_threshold = 50000
);

ALTER TABLE "Matches" SET (
    autovacuum_vacuum_scale_factor = 0.05,
    autovacuum_analyze_scale_factor = 0.02
);

ANALYZE "Matches";
ANALYZE "MatchParticipants";
ANALYZE "MatchParticipantTimelineSnapshots";
