# Follow-up: increase ingestion throughput via candidate-selection breadth

> **✅ Implemented 2026-06-11.** Snowball-frontier prioritization, a per-summoner coverage cooldown,
> activity-aware scoring (new `Summoner.LastActiveAtUtc`, maintained in `MatchService`), long-tail
> rotation, and a discovery `WorkerCount` bump (8 → 12) shipped. Confirmed: the snowball candidates
> already exist (the lightweight match path mints participant stubs with `UpdatedAt = MinValue`); the
> bug was that `RankCandidates` buried them below the unfiltered high-elo bucket. See
> `docs/ARCHITECTURE.md` → "Candidate selection strategy (yield-first breadth)". Two migrations
> (`AddSummonerLastActiveAtUtc`, `AddSummonerRegionUpdatedAtIndex`) added; the index is built
> `CONCURRENTLY` in prod. Still open (measured prod step): raising the gate `TokensPerPeriod` once the
> yield lift is observed. Analysis below retained for history.

> Scoped task for a fresh session. The ingestion pipeline is now **stable** (no hangs) and
> **paced** to the Riot key budget, but throughput is capped by *yield*, not rate. This task is
> about making the discovery producers select **productive** candidates so the (barely-used)
> per-region budget gets filled with fetches that actually return new matches.

## TL;DR

- **Symptom:** ~300 matches/hr ingested for the active patch, even though the per-region Riot
  rate budget is only ~2-5% used. Most analytics-refresh consumers complete with **0 new
  matches persisted** (`rankedHead=0`) — they keep re-refreshing already-covered or inactive
  summoners.
- **Goal:** raise productive yield per refresh so ingestion uses much more of the available
  per-region budget (target: from ~300/hr toward several-thousand/hr on the current personal key;
  it lifts straight to the production budget once that key lands).
- **NOT in scope:** rate limiting, the hang, patch detection — all solved (see "Already done").
  A production key does **not** fix this (we're nowhere near the budget); breadth does.

## Background — what's already been done (don't redo)

Recent commits on `main` (this is the state you're starting from):

- **Data-driven patch promotion** (`StaticDataService.DetectAndRefreshAsync` + `PatchPromotionOptions`):
  a new patch is promoted to active only once its `gameVersion` has rolled out across regions
  (Riot's match `gameVersion` lags the Data Dragon label). Validated in prod. See
  `docs/ARCHITECTURE.md` → "Patch detection: data-driven promotion".
- **{active ∪ pending} ingestion filter + recent-window clamp** (`SummonerRefreshJob.RefreshForAnalytics`
  / `GetIngestiblePatchesAsync`): keeps ingestion productive across a patch rollover.
- **Early-stop on not-yet-rolled-out regions** (`SyncMatchWindowAsync`): if the newest ranked match
  isn't on an acceptable patch, the region hasn't rolled out — skip the summoner after 1 fetch.
- **Per-region Riot rate gate** (`IRiotRateGate` / `RiotRateGate`, `Jobs:RiotRateGate`): a per-routing-region
  token bucket that paces outbound Riot calls UNDER the key's per-region budget so Camille's limiter
  never saturates. Gates `MatchService.GetMatch*`, `RiotMatchIdsClient`, `MatchTimelineIngestionJob`.
- **Thread-pool pre-warm** (`Transcendence.Service/Program.cs`: `ThreadPool.SetMinThreads(200,200)`):
  the actual root cause of the prior "stalled consumer" outage was thread-pool starvation — the
  rate-limiter refill timers got starved and the buckets never replenished. Fixed.
- **Self-pacing producers + discovery lane + queue backpressure** (earlier "Phase 2"): producers run
  one self-paced job each on a dedicated `discovery` Hangfire lane; `IQueueDepthProbe` backpressure.

**The Riot key is a personal/dev-tier key** (~20 req/s + ~100 req/2min PER routing region; does not
expire). A production key has been applied for (months ago, pending). The per-region budget is the
ultimate ceiling, but we are currently using only ~2-5% of it — so breadth, not the key, is the lever.

## The actual problem (root-caused)

Discovery producers (`ChampionAnalyticsIngestionJob`, `SummonerMaintenanceJob`) pick candidate
summoners and enqueue `RefreshForAnalytics` consumers. The candidate selection
(`GetCandidatesAsync` in each producer + `IngestionPriorityScoringPolicy`) prioritizes a **narrow,
mostly-static set**: tracked pros, favorites, and high-elo ranked summoners, with a fallback that
orders the broad pool by `UpdatedAt` (staleness). Consequences:

1. The high-priority set gets re-refreshed repeatedly → their matches are already in the DB →
   `GetExistingMatchIdsAsync` dedups → `pendingIds` empty → **0 new matches** (and the early-stop
   never even runs, because there's nothing to fetch).
2. `UpdatedAt`-staleness ≠ activity. A summoner not refreshed recently may simply be **inactive**
   (not playing) → refreshing them yields 0.
3. So thousands of consumers churn for ~0 yield, the discovery queue stays deep (~13k), and 16.12
   grows at ~300/hr while the Riot budget sits ~95% idle.

The fix is to bias candidate selection toward summoners who **recently played** (have new games) and
who are **not already covered** for the current patch — i.e., maximize *new matches per refresh*.

## Approaches to evaluate (pick/combine; verify with data)

1. **Snowball from freshly-ingested match participants (highest expected value).** Every ingested
   match names 10 players who *just played* — they're guaranteed-active and likely uncovered.
   - Check whether `MatchService` (the build path: `ResolveSummonersByPuuidAsync` / participant
     construction) already **inserts** unseen participant PUUIDs as `Summoner` rows, or only resolves
     existing ones. If it doesn't insert them, adding them (with region) makes them future candidates.
   - Then bias the producer to prefer **recently-discovered, never-refreshed** summoners (the snowball
     frontier) over re-refreshing the covered core. This is how op.gg/u.gg-style crawlers stay
     productive: ingest a match → enqueue its participants → ingest their matches → repeat.
2. **Activity-aware candidate scoring.** Derive a "last active" signal (e.g., max `MatchDate` per
   summoner from `MatchParticipants`, or a `LastActiveAtUtc` column maintained on persist) and
   prioritize recently-active summoners. Fold it into `IngestionPriorityScoringPolicy`.
3. **Coverage-aware dedup / cooldown.** Don't enqueue a summoner who was refreshed in the last N
   minutes for the active patch (already covered) — they can't have meaningfully new games yet. A
   per-summoner refresh cooldown keyed on the active patch would stop the wasteful re-churn. (Note a
   refresh lock already exists per summoner — see `RefreshLockKeys`; consider a longer analytics
   cooldown distinct from the short execution lock.)
4. **Rotate the long tail.** The fallback should walk the broad pool rather than re-selecting the same
   stale head every run (it currently `OrderBy(UpdatedAt).Take(...)`, so it can re-pick the same set).
   Consider a rotating cursor / random sampling so coverage spreads.
5. **Raise concurrency once productive.** With the gate pacing and the pool pre-warmed, discovery
   `WorkerCount` is currently a conservative **8** (`Transcendence.Service/Program.cs`). Once each
   refresh is productive, raise it (and/or the gate's `TokensPerPeriod`) toward the per-region budget
   so the budget is actually filled. Tune `Jobs:RiotRateGate` upward only as far as the key allows.

## Key code locations

- Producers / candidate selection:
  - `Transcendence.Service.Core/Services/Jobs/ChampionAnalyticsIngestionJob.cs` → `GetCandidatesAsync`,
    `RankCandidates`, the per-region `ExecuteForRegionInternalAsync` (adaptive budget + starvation guardrail).
  - `Transcendence.Service.Core/Services/Jobs/SummonerMaintenanceJob.cs` → `GetCandidatesAsync` (similar).
  - `Transcendence.Service.Core/Services/Jobs/Priority/IngestionPriorityScoringPolicy.cs` + `IngestionPriorityPolicyOptions`.
- Consumer / fetch + persist:
  - `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs` → `RefreshForAnalytics`, `SyncMatchWindowAsync`
    (the early-stop + `acceptablePatches` filter live here), `GetIngestiblePatchesAsync`.
  - `Transcendence.Service.Core/Services/RiotApi/Implementations/MatchService.cs` → match fetch + entity build
    (check participant insertion here for the snowball approach), gated by `IRiotRateGate`.
- Rate gate: `Transcendence.Service.Core/Services/RiotApi/RiotRateGate.cs` + `RiotRateGateOptions` (`Jobs:RiotRateGate`).
- Worker config / pool / worker counts: `Transcendence.Service/Program.cs` (Hangfire servers, `SetMinThreads`).

## Constraints / invariants

- **Personal Riot key** — stay under ~100 req/2min PER routing region; the gate enforces this. Don't
  raise `Jobs:RiotRateGate` past the key's budget (causes the Camille saturation we just fixed).
- **EF `DbContext` is not thread-safe** — keep entity build/persist sequential per job scope (the worker
  uses `AddDbContextPool`). The Riot fetch is the only thing safe to parallelize, and it's gated.
- **LoL/TFT surface isolation** — don't mix surfaces in one job/transaction/cache.
- **Fail-closed analytics** — never fabricate/estimate stats on missing data.
- **EF migrations via CLI only** if you add a column (e.g. `LastActiveAtUtc`):
  `dotnet ef migrations add <Name> --project Transcendence.Service --startup-project Transcendence.Service`.
- **Docs hygiene** — update `docs/ARCHITECTURE.md` (ingestion section) if you change the discovery model.

## How to verify (prod is `root@192.168.0.221`, container `transcendence-postgres`, db `transcendence`)

- **Yield per refresh** (the headline metric): fraction of `[AnalyticsRefresh] Completed` log lines
  with `rankedHead=[1-9]` vs `rankedHead=0`. Today almost all are 0.
  `docker logs transcendence-service --since 10m | grep "AnalyticsRefresh] Completed" | grep -oE "rankedHead=[0-9]+" | sort | uniq -c`
- **Active-patch growth rate** (matches/hr): sample
  `select count(*) from "Matches" where "Status"=1 and "Patch"=(select "Version" from "Patches" where "IsActive")`
  over a window. Baseline ~300/hr; target much higher.
- **Budget utilization:** the gate should show **gate-skip** logs (`rate gate skipped`) once you're
  actually pushing the budget — today there are ~none (budget idle). Watch `Jobs:RiotRateGate` headroom.
- **Distinct productive summoners/hr** vs total refreshed (measures breadth).
- **Stability guardrails** (must not regress): consumers keep completing (no 0-completion stall),
  discovery queue drains, no Camille saturation, CPU not pinned, no thread-pool re-starvation.

## Suggested order of work

1. Confirm whether match-participant PUUIDs are already inserted as `Summoner` rows (read `MatchService`).
   If yes, the snowball candidates already exist — just need prioritizing. If no, insert them.
2. Add a coverage cooldown so covered summoners aren't re-enqueued every cycle (quick, high-impact).
3. Add activity-awareness (prefer recently-active / snowball-frontier summoners) to the candidate ranking.
4. Re-measure yield. Once each refresh is productive, raise discovery `WorkerCount` + gate `TokensPerPeriod`
   toward the per-region budget and re-measure until the budget is the binding constraint again.
5. Update `docs/ARCHITECTURE.md`.

When you start the new session, point it at this file and at `docs/ARCHITECTURE.md` (ingestion +
patch-detection sections) for full context.
