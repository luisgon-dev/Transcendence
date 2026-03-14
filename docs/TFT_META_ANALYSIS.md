# TFT Meta Analysis Research and Best Practices

This document defines a practical, production-friendly method for determining the Teamfight Tactics (TFT) meta from match data.

It is designed for the current Transcendence architecture where TFT analytics are read-only against persisted data and exposed via `/api/tft/analytics/*`.

## Goals

A good TFT meta model should answer:

1. **What is strong now?** (comps/lines, items, augments, openers)
2. **For whom is it strong?** (rank, region, patch, sample quality)
3. **How reliable is the claim?** (uncertainty, patch freshness, sample size)
4. **Why is it strong?** (tempo, cap, consistency, forcing cost, contestability)

## Data Model Requirements

Treat each game as one observation per player (`placement` 1–8), with denormalized links to:

- patch/version
- queue type
- region
- player rank tier/division/LP bucket
- final board units + star levels
- active traits + breakpoints
- augments by stage
- itemization by unit and item type (core/carry/tank/flex)
- economy/tempo markers (level timings, reroll profile, streak proxies if available)

### Canonical Entities for Analysis

- **Comp archetype**: Stable identifier for a board pattern (not exact unit set only).
- **Line**: Mid-game route into one or more endgame archetypes.
- **Variant**: Same archetype with meaningful differences (carry swap, emblem branch, hero augment branch).

Without this hierarchy, dashboards overfit to exact final boards and miss how TFT is actually played.

## Comp Identification (Critical)

### 1) Archetype fingerprints (primary)

Build a fingerprint from:

- trait breakpoints (weighted highest)
- carry identity (weighted high)
- frontline package
- emblem presence
- key support units

Use weighted similarity (e.g., Jaccard + trait-weight bonus) against a curated archetype library.

### 2) Clustering fallback (discovery)

For unknown boards, cluster on feature vectors (traits, carries, items, augments).
Promote stable clusters to archetypes only after they persist across enough games and ranks.

### 3) Variant tagging

After archetype assignment, tag branch variants:

- AD vs AP carry branch
- reroll vs fast-8 execution
- emblem branch

This prevents one archetype from hiding opposite performance patterns.

## Meta Strength Metrics

Do not rank meta by average placement alone.

### Core scorecard per archetype/variant

- **Top 4 rate**
- **Win rate (1st rate)**
- **Average placement**
- **Bottom 4 rate** (volatility/risk)
- **Pick rate** (share of eligible lobbies)
- **Contest rate** (how many players hold same core)
- **Net placement delta vs lobby baseline**
- **Expected LP delta proxy** (placement-to-LP mapping)

### Recommended composite score

A robust default:

`MetaScore = 0.40 * z(Top4) + 0.25 * z(Win) - 0.20 * z(Bot4) + 0.15 * z(PlacementDelta)`

Then multiply by:

- **ReliabilityWeight** (sample + confidence)
- **PatchRecencyWeight** (older patches decay)

This avoids “1st-or-8th bait comps” dominating just from ceiling.

## Statistical Reliability and Uncertainty

### Minimum sample gates

Use tiered gates (example defaults):

- show publicly at `n >= 200`
- “stable” badge at `n >= 800`
- “high confidence” at `n >= 2000`

Apply gates separately per rank tier and patch.

### Shrinkage for low samples

Apply empirical-Bayes shrinkage toward patch-level priors:

- Top4, Win, Bot4 as binomial rates with Beta prior
- Placement with normal prior and variance pooling

This prevents tiny-sample outliers from topping rankings.

### Confidence intervals

Display 95% intervals for rates and placement delta.
If intervals overlap strongly, avoid hard ordering (show same tier band).

## Patch-Aware Weighting

TFT changes quickly; old matches should decay.

- hard reset on new set
- patch-specific partitions on balance patch
- intra-patch time decay (e.g., exponential half-life 3–5 days)

Suggested weight for a match:

`w = exp(-lambda * days_since_match)` where `lambda = ln(2)/half_life_days`

Use weighted estimators and weighted sample size (effective N).

## Rank- and Region-Specific Views

Meta is not one global truth.

Produce slices at minimum:

- all ranks
- Emerald+
- Diamond+
- Master+

And by region when enough data exists.

If data is thin, back off to broader buckets using a clear fallback chain:

`Master+ region -> Diamond+ region -> Emerald+ region -> Emerald+ global -> all global`

Expose which fallback was used.

## Contestedness and Forceability

A comp is weaker when many players force it.

Track:

- average number of players per lobby on same archetype
- placement delta as contest count rises
- item overlap pressure (core item contention)
- unit pool pressure proxies (same-cost carry overlap)

Report **Forceability Index**:

`Forceability = performance_when_contested / performance_when_uncontested`

This is often the difference between ladder-viable and trap recommendations.

## Tempo and Execution Difficulty

Two comps with same average placement can differ in execution burden.

Add execution features:

- average level timing by stage
- reroll gold burden proxies
- pivot frequency from opener to cap board
- fail-state severity (8th percentile placement)

Create a **Consistency Score** for guides:

`Consistency = 1 - volatility` where volatility combines Bot4, placement variance, and contested drop-off.

## Item and Augment Interaction Analysis

Single-feature win rates are misleading; model interactions.

### Item interactions

- carry-item trios: measure marginal gain over baseline carry performance
- frontline package effects
- anti-synergy detection (popular but negative delta combinations)

### Augment interactions

- augment x archetype lift
- augment x stage timing (2-1 / 3-2 / 4-2)
- “bailout” vs “win-more” classification by when taken and player HP/econ state

Use conditional metrics, not global standalone rates.

## Early-Patch and Data-Scarce Behavior

For first 24–72 hours of a patch:

- widen uncertainty bands
- down-rank hard ordering
- label “emerging meta”
- prioritize robust signals (Top4 and Bot4 over Win)

If sample size is below threshold:

- do not output definitive S/A/B tiers
- show “insufficient data” + nearest fallback view

## Recommended Pipeline (Daily + Near-Real-Time)

1. Ingest matches continuously.
2. Normalize and feature-extract participants.
3. Assign archetype + variant.
4. Compute weighted metrics by patch/rank/region.
5. Apply shrinkage and confidence estimation.
6. Compute MetaScore + Consistency + Forceability.
7. Publish tier bands and explanation fields.
8. Recompute on cadence (e.g., hourly rolling + daily full recompute).

## Suggested API/Data Contract Additions

For comp endpoints, expose additional fields:

- `effectiveSampleSize`
- `confidence.top4Lower`, `confidence.top4Upper` (and similar)
- `patchRecencyHours` or weighted recency summary
- `contestRate`, `forceabilityIndex`
- `consistencyScore`
- `rankScopeUsed`, `regionScopeUsed` (explicit fallback metadata)
- `isEmerging`, `isDataScarce`

These make frontend messaging honest and reduce overclaiming.

## Validation Strategy

Before adopting a metric, validate offline:

1. **Backtest** on previous patches: does top-tier recommendation beat baseline?  
2. **Stability test**: does ranking jitter excessively day-to-day?  
3. **Calibration test**: predicted strong comps should realize expected Top4 rates.  
4. **Sensitivity analysis**: ensure one region/rank cannot dominate global output unexpectedly.

## Anti-Patterns to Avoid

- Ranking solely by average placement.
- Ignoring uncertainty and sample size.
- Combining patches without decay.
- Publishing one global tier list as universal truth.
- Treating exact final boards as comps (too brittle).
- Ignoring contestedness and execution burden.

## Practical “Best Practices” Checklist

- [ ] Patch-partitioned, rank-aware datasets
- [ ] Archetype + variant mapping (not exact-board only)
- [ ] Shrinkage + confidence intervals for all public rates
- [ ] Contest and consistency metrics alongside raw power
- [ ] Explicit fallback metadata for thin data
- [ ] Early-patch safeguards and messaging
- [ ] Backtesting and calibration checks in CI/ops cadence

## Notes on Research Constraints

This environment currently blocks direct access to many external websites. The methodology above is grounded in standard game analytics and statistical best practices and aligned to the existing Transcendence TFT architecture and API surfaces.
