---
phase: 1
slug: worker-startup-integrity
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-04
---

# Phase 1 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit + Moq + FluentAssertions |
| **Config file** | `tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj` |
| **Quick run command** | `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj -c Release --filter "FullyQualifiedName~SummonerRefreshJobTests|FullyQualifiedName~ChampionAnalyticsIngestionJobRampTests"` |
| **Full suite command** | `corepack pnpm backend:test` |
| **Estimated runtime** | ~180 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj -c Release --filter "FullyQualifiedName~SummonerRefreshJobTests|FullyQualifiedName~ChampionAnalyticsIngestionJobRampTests"`
- **After every plan wave:** Run `corepack pnpm backend:test`
- **Before `$gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** 240 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 01-01-01 | 01 | 1 | WORK-04 | unit | `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj -c Release --filter "FullyQualifiedName~WorkerPolicy"` | ❌ W0 | ⬜ pending |
| 01-01-02 | 01 | 1 | WORK-01 | unit/component | `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj -c Release --filter "FullyQualifiedName~StartupIntegrity"` | ❌ W0 | ⬜ pending |
| 01-02-01 | 02 | 2 | WORK-02 | unit/component | `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj -c Release --filter "FullyQualifiedName~MandatoryJobVerification"` | ❌ W0 | ⬜ pending |
| 01-02-02 | 02 | 2 | WORK-03 | unit | `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj -c Release --filter "FullyQualifiedName~CancellationPropagation"` | ❌ W0 | ⬜ pending |
| 01-03-01 | 03 | 3 | WORK-01, WORK-02, WORK-03, WORK-04 | integration | `corepack pnpm backend:test` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/Transcendence.Service.Core.Tests/WorkerStartupIntegrityTests.cs` — startup verification + fail-fast scenarios (WORK-01, WORK-02)
- [ ] `tests/Transcendence.Service.Core.Tests/WorkerSchedulingPolicyTests.cs` — dev/prod parity and profile behavior (WORK-04)
- [ ] `tests/Transcendence.Service.Core.Tests/CancellationPropagationTests.cs` — cancellation boundaries and lock-release behavior (WORK-03)

*If none: "Existing infrastructure covers all phase requirements."*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Hosted-service startup state transitions (healthy/degraded/fail-fast) under real Hangfire storage startup races | WORK-01, WORK-02 | Timing/race behavior can differ from mocked unit tests | Run service with temporary DB/Hangfire dependency interruption; verify health endpoint/log state and final startup outcome. |
| Cancellation behavior during deployment shutdown window with in-flight jobs | WORK-03 | Requires real host shutdown signal and active job workload | Start long refresh/ingestion job, trigger controlled shutdown, verify graceful window then cancellation and lock release semantics. |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 240s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
