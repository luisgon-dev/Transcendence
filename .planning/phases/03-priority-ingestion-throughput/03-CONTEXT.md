# Phase 3: Priority Ingestion Throughput - Context

**Gathered:** 2026-03-05
**Status:** Ready for planning

<domain>
## Phase Boundary

Increase patch-window ingestion throughput so user-visible/high-value data is prioritized first, while long-tail ingestion still makes forward progress over time.

This phase clarifies prioritization behavior and fairness policy for existing ingestion/refresh workflows. It does not add new product capabilities.

</domain>

<decisions>
## Implementation Decisions

### Priority scoring behavior
- Candidate ranking is patch-first freshness: prioritize current-patch stale candidates before lower-value freshness work.
- Candidate pool should keep the existing "favorites first + tracked fallback" behavior.
- Manual/user-triggered profile refresh remains all-match-types; ranked-only focus applies to automatic ingestion policy, not user refresh.
- Automatic ingestion should use ranked-only focus during ramp and switch to mixed/all-mode behavior when patch coverage target is reached.

### Throughput budget policy
- Use adaptive auto-budgeting (not static knobs) to shift throughput based on conditions.
- During active high-priority demand, high-priority work may consume all capacity.
- After high-priority pressure drops, run aggressive low-priority catch-up bursts to reduce backlog age.
- Adaptive decisions should use both live and historical signals (queue/backlog age + patch coverage progress + historical ingestion velocity).

### Starvation guardrails
- Primary fairness guardrail is max defer age for eligible low-priority candidates.
- Breaching defer-age threshold triggers forced catch-up windows.
- Initial guardrail scope is champion analytics ingestion + summoner maintenance.
- Guardrail behavior must emit metrics plus structured logs for operational visibility.

### Claude's Discretion
- Exact scoring formula, weighting method, and tie-break order within the chosen policy.
- Numeric thresholds for defer age, catch-up duration/intensity, and adaptive shift boundaries.
- Exact metric names/dimensions for budget-shift and guardrail activation telemetry.
- Concrete implementation shape for historical signal storage/windowing used by auto-budget decisions.

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `Transcendence.Service.Core/Services/Jobs/ChampionAnalyticsIngestionJob.cs`: existing candidate selection, ramp handling, and queue target scaling baseline.
- `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs`: separate high-priority (`RefreshByRiotId`) and low-priority (`RefreshForAnalytics`) execution paths.
- `Transcendence.Service.Core/Services/Jobs/SummonerMaintenanceJob.cs`: existing stale-candidate selection and low-priority queueing flow.
- `Transcendence.Service.Core/Services/Jobs/Configuration/ChampionAnalyticsIngestionJobOptions.cs`: current prioritization and ramp controls.
- `Transcendence.Service.Core/Services/Jobs/Configuration/WorkerJobScheduleOptions.cs` + scheduling profiles: existing config surface for behavior tuning by environment.

### Established Patterns
- High-priority API demand is already represented via active `refresh-priority:api` lock-prefix checks.
- Low-priority producers currently pause when API-priority demand is active.
- Queue order already enforces `refresh-high`, `default`, `refresh-low` precedence.
- New-patch ramp behavior already exists as time-window + separate ramp knobs.

### Integration Points
- Policy logic: `ChampionAnalyticsIngestionJob`, `SummonerMaintenanceJob`, and related low-priority producers.
- Priority/coverage behavior: `SummonerRefreshJob.RefreshForAnalytics(...)` and ingestion options.
- Scheduling/tuning surfaces: `Transcendence.Service/appsettings.json`, `Transcendence.Service/appsettings.Development.json`, and `Jobs:SchedulingProfiles` overrides.
- Regression coverage extensions: `tests/Transcendence.Service.Core.Tests/ChampionAnalyticsIngestionJobRampTests.cs` and neighboring job-policy tests.

</code_context>

<specifics>
## Specific Ideas

- Keep operator control minimal: prefer adaptive policy over many manual tuning knobs.
- Adaptive decisions should infer "enough data" from both historical and current state rather than fixed static-only thresholds.
- Preserve user expectation that manual profile refresh fetches all match types even when auto-ingestion is ranked-focused in ramp windows.

</specifics>

<deferred>
## Deferred Ideas

None - discussion stayed within phase scope.

</deferred>

---

*Phase: 03-priority-ingestion-throughput*
*Context gathered: 2026-03-05*
