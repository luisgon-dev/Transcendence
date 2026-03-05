---
phase: 2
slug: refresh-lock-lifecycle-control
status: draft
nyquist_compliant: true
wave_0_complete: true
created: 2026-03-04
---

# Phase 2 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit + FluentAssertions + Moq |
| **Config file** | none — SDK-style test projects |
| **Smoke command (target <30s)** | `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj -c Release --filter "FullyQualifiedName~SummonerRefreshJobTests" -m:1` |
| **Quick run command (targeted, ~180-240s)** | `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj -c Release --filter "FullyQualifiedName~SummonerRefreshJobTests|FullyQualifiedName~ChampionAnalyticsIngestionJobRampTests|FullyQualifiedName~RefreshLockLifecycle|FullyQualifiedName~WorkerSchedulingPolicy|FullyQualifiedName~RefreshLockLifecycleTelemetry" -m:1 && dotnet test tests/Transcendence.WebAPI.Tests/Transcendence.WebAPI.Tests.csproj -c Release --filter "FullyQualifiedName~SummonersControllerTests|FullyQualifiedName~ProSummonersControllerTests" -m:1` |
| **Full suite command** | `dotnet test Transcendence.sln -c Release -m:1` |
| **Estimated runtime** | Smoke: <30s target; Quick: ~240s; Full: repo-scale |

---

## Sampling Rate

- **After every task commit:** Run smoke command first (sub-30s target)
- **After every 2 task commits (or before switching plans):** Run quick targeted suite command
- **After every plan wave:** Run `dotnet test Transcendence.sln -c Release -m:1`
- **Before `$gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** 30s for smoke loop, 300s for targeted loop

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 02-01-01 | 01 | 1 | LOCK-01 | unit/api | `dotnet test tests/Transcendence.WebAPI.Tests/Transcendence.WebAPI.Tests.csproj -c Release --filter "FullyQualifiedName~SummonersControllerTests|FullyQualifiedName~ProSummonersControllerTests" -m:1` | ✅ | ⬜ pending |
| 02-02-01 | 02 | 1 | LOCK-02 | unit/integration | `dotnet build Transcendence.Data/Transcendence.Data.csproj -c Release` | ✅ | ⬜ pending |
| 02-04-01 | 04 | 2 | LOCK-01 | contract | `dotnet test tests/Transcendence.WebAPI.Tests/Transcendence.WebAPI.Tests.csproj -c Release --filter "FullyQualifiedName~SummonersControllerTests|FullyQualifiedName~ProSummonersControllerTests" -m:1` | ✅ | ⬜ pending |
| 02-05-01 | 05 | 2 | LOCK-02 | unit/scheduling | `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj -c Release --filter "FullyQualifiedName~RefreshLockLifecycle|FullyQualifiedName~WorkerSchedulingPolicy" -m:1` | ✅ | ⬜ pending |
| 02-03-01 | 03 | 3 | LOCK-03 | unit | `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj -c Release --filter "FullyQualifiedName~RefreshLockLifecycleTelemetry|FullyQualifiedName~SummonerRefreshJobTests" -m:1` | ✅ | ⬜ pending |
| 02-06-01 | 06 | 4 | LOCK-03 | api/docs regression | `dotnet test tests/Transcendence.WebAPI.Tests/Transcendence.WebAPI.Tests.csproj -c Release --filter "FullyQualifiedName~SummonersControllerTests|FullyQualifiedName~ProSummonersControllerTests" -m:1` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Lock lifecycle telemetry dimensions are visible in ops log stream | LOCK-03 | Environment-specific sink formatting can differ from local test execution | Run worker locally, trigger refresh contention and cleanup cycle, verify emitted fields include lock class/prefix, platform/region, and lifecycle outcome. |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 300s (targeted loop) with sub-30s smoke loop for per-task checks
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
