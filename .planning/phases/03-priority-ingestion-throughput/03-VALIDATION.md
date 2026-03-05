---
phase: 3
slug: priority-ingestion-throughput
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-05
---

# Phase 3 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit (`dotnet test`) |
| **Config file** | none — default .NET test runner configuration |
| **Quick run command** | `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj --filter "FullyQualifiedName~ChampionAnalyticsIngestion|FullyQualifiedName~SummonerMaintenance|FullyQualifiedName~SummonerRefresh"` |
| **Full suite command** | `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj` |
| **Estimated runtime** | ~120 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj --filter "FullyQualifiedName~ChampionAnalyticsIngestion|FullyQualifiedName~SummonerMaintenance|FullyQualifiedName~SummonerRefresh"`
- **After every plan wave:** Run `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj`
- **Before `$gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** 180 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 03-01-01 | 01 | 1 | INGT-02 | unit | `dotnet test ... --filter "FullyQualifiedName~PriorityScoring"` | ❌ W0 | ⬜ pending |
| 03-02-01 | 02 | 1 | INGT-01, INGT-03 | integration | `dotnet test ... --filter "FullyQualifiedName~ThroughputBudget"` | ❌ W0 | ⬜ pending |
| 03-03-01 | 03 | 2 | INGT-04 | integration | `dotnet test ... --filter "FullyQualifiedName~StarvationGuardrail"` | ❌ W0 | ⬜ pending |
| 03-04-01 | 04 | 2 | INGT-01, INGT-03, INGT-04 | integration | `dotnet test ... --filter "FullyQualifiedName~ChampionAnalyticsIngestion|FullyQualifiedName~SummonerMaintenance"` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠ flaky*

---

## Wave 0 Requirements

- [ ] `tests/Transcendence.Service.Core.Tests/Jobs/PriorityScoringPolicyTests.cs` — scoring and tie-breaker tests for INGT-02
- [ ] `tests/Transcendence.Service.Core.Tests/Jobs/AdaptiveThroughputBudgetPolicyTests.cs` — budget mode transition tests for INGT-01/03
- [ ] `tests/Transcendence.Service.Core.Tests/Jobs/StarvationGuardrailPolicyTests.cs` — defer-age and catch-up window tests for INGT-04

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| High-priority perceived freshness during load spikes | INGT-01, INGT-03 | Requires realistic queue pressure and operational timing | Trigger refresh while background ingestion runs, verify high-priority completion latency remains within operational SLO bounds |
| Catch-up behavior after pressure drop | INGT-04 | End-to-end cadence hard to simulate fully in unit tests | Sustain high-priority pressure, release pressure, confirm low-priority backlog ages decline during catch-up window |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 180s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
