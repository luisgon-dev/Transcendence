# Frontend Revamp: League of Legends Stats Presentation

## Context

The Transcendence frontend currently presents champion/summoner data in a functional but generic way that doesn't feel like a League of Legends stats site. Sites like u.gg and op.gg set the standard with tier badges, color-coded win rates, sample sizes shown everywhere, instant role/rank filters, numbered tier lists, and structured item builds. This plan brings Transcendence's presentation in line with those standards using the **existing backend API data**, while keeping Transcendence's unique purple/blue aurora visual identity.

**Win/Loss color scheme**: Uses Transcendence's purple (primary) tint for wins and red for losses -- a distinctive identity choice rather than copying u.gg's green/red or op.gg's blue/red.

**Workflow**: Create a new branch, write this plan as a .md file in the repo, then work through phases sequentially with atomic commits per phase for continuity.

---

## Phase 0: Foundation -- Design Tokens, Utilities, Shared Components

Everything built here is reused across all subsequent phases.

### 0A. New CSS Variables + Tailwind Tokens

**`apps/web/app/globals.css`** -- add new variables:
```
--tier-s: 25 95% 53%       (orange/gold)
--tier-a: 142 71% 45%      (green)
--tier-b: 217 91% 60%      (blue)
--tier-c: 270 70% 60%      (purple)
--tier-d: 220 10% 50%      (muted gray)
--wr-high: 142 71% 45%     (green, >52%)
--wr-low: 0 84% 60%        (red, <48%)
--win: 268 92% 68%          (purple/primary tint for wins -- unique to Transcendence)
--loss: 0 84% 60%           (red for losses)
```

**`apps/web/tailwind.config.ts`** -- extend `colors`:
```
tier-s, tier-a, tier-b, tier-c, tier-d, wr-high, wr-low, win, loss
```

### 0B. Format Utilities

**`apps/web/lib/format.ts`** -- add alongside existing `formatPercent`:
- `winRateColorClass(winRate)` -- returns `"text-wr-high"` (>52%), `"text-wr-low"` (<48%), or `""` (neutral)
- `formatGames(count)` -- returns `"17,183"` (comma-separated, no "Matches" suffix -- callers add context)

### 0C. Tier Display Utilities

**`apps/web/lib/tierlist.ts`** -- add:
- `tierColorClass(tier)` -- `"text-tier-s"`, `"text-tier-a"`, etc.
- `tierBgClass(tier)` -- `"bg-tier-s/15"`, etc.
- `movementIcon(movement)` -- returns arrow characters: `"▲"` / `"▼"` / `"★"` / `"–"`
- `deriveTier(winRate)` -- frontend heuristic: S >=53%, A >=51%, B >=49%, C >=47%, D <47%

### 0D. New Shared Components

| Component | File | Purpose |
|-----------|------|---------|
| `TierBadge` | `components/TierBadge.tsx` | Colored pill showing tier letter (S/A/B/C/D) |
| `WinRateText` | `components/WinRateText.tsx` | Win rate with conditional green/red coloring |
| `StatsBar` | `components/StatsBar.tsx` | Horizontal stat cells row (Tier, WR, Pick%, Games) for champion pages |
| `RoleFilterTabs` | `components/RoleFilterTabs.tsx` | Horizontal role pills using Links (instant nav, no form submit) |
| `RankFilterDropdown` | `components/RankFilterDropdown.tsx` | Dropdown that navigates on change via `router.push` |
| `FilterBar` | `components/FilterBar.tsx` | Combines RoleFilterTabs + RankFilterDropdown + patch badge |
| `ChampionPortrait` | `components/ChampionPortrait.tsx` | Reusable champion icon + optional name |
| `ItemBuildDisplay` | `components/ItemBuildDisplay.tsx` | Structured item display: core items / situational items |

---

## Phase 1: Homepage Rebrand

**File: `apps/web/app/page.tsx`**

**Current**: Generic "Find What You Need" centered card with search launcher.

**Changes**:
- Heading: "Transcendence" with subtitle "League of Legends Win Rates, Builds & Tier Lists"
- Keep search card and `GlobalSearchLauncher`
- Replace the 3 quick-link pills (Tier List, Champions, Favorites) with role-specific tier list shortcuts:
  - "S Tier" -> `/tierlist`
  - "Best Top" -> `/tierlist?role=TOP`
  - "Best Jungle" -> `/tierlist?role=JUNGLE`
  - "Best Mid" -> `/tierlist?role=MIDDLE`
  - "Best Bot" -> `/tierlist?role=BOTTOM`
  - "Best Support" -> `/tierlist?role=UTILITY`
- Add "Patch X.Y" badge fetched from DDragon version (already available via `fetchChampionMap()`)

Small scope. No new components needed beyond what Phase 0 provides.

---

## Phase 2: Tier List Overhaul

**File: `apps/web/app/tierlist/page.tsx`**

**Current**: Form-submit filters, cards-per-tier with flat rows. No numbered ranking, no tier badges, no win rate coloring.

**Changes**:

1. **Replace `<form>` with `<FilterBar>`** -- instant navigation via role tabs + rank dropdown. Remove the "Apply" button.

2. **Page header metadata**: Show `"Patch {patch} · {role} · {rank} · {entries.length} champions analyzed"` using Badges.

3. **Replace card-per-tier layout with a single numbered table**:
   - Tier separator rows: full-width colored bar with tier letter + champion count
   - Champion rows with columns:

   ```
   #  | Tier | Champion (icon+name) | Role | Win Rate | Pick Rate | Games | Movement
   ```

   - `#` = continuous ranking across all tiers (1, 2, 3...)
   - Tier column uses `<TierBadge>`
   - Win Rate uses `<WinRateText>` with conditional coloring
   - Games use `formatGames()` with comma formatting
   - Movement uses `movementIcon()` with colored arrows (green up, red down)
   - Champion name links to `/champions/{id}?role={role}`

4. **Row styling**: `hover:bg-white/5` on rows, subtle `border-b border-border/40` between rows.

**Note**: Ban rate column **requires backend work** (`TierListEntry` schema has no `banRate` field). Omit for now.

---

## Phase 3: Champion Detail Page Overhaul

**File: `apps/web/app/champions/[championId]/page.tsx`**

This is the largest change. Currently ~520 lines of flat data display.

### 3A. Champion Header Redesign

- Larger champion icon (64px, up from 52px)
- `<TierBadge>` next to champion name, derived via `deriveTier(winRate)` from the most-played role entry
- Role subtitle below name

### 3B. Stats Bar (new section below header)

`<StatsBar>` showing:
- **Tier**: Derived `<TierBadge>`
- **Win Rate**: From selected role/tier, with `<WinRateText>` coloring
- **Pick Rate**: Formatted to 1 decimal
- **Matches**: Formatted with commas
- **Ban Rate**: Dash (not available in API). **Requires backend work** to add.

### 3C. Replace Filters with `<FilterBar>`

Remove both the `<form>` block (lines 258-295) and the `<nav>` role links (lines 297-311). Replace with a single `<FilterBar>` component.

### 3D. Win Rates Table Enhancement

- Win rates to 2 decimal places via `formatPercent(value, { decimals: 2 })`
- Conditional coloring via `<WinRateText>`
- Games formatted with commas
- Consider adding pick rate coloring too

### 3E. Builds Section Restructure

Replace flat "Build 1/2/3" cards with structured display:

- **"Recommended Build"** (highest-games build):
  - Use `<ItemBuildDisplay>` showing `coreItems` labeled "Core Build" and `situationalItems` labeled "Situational"
  - Show `globalCoreItems` from the API response as "Core Items (all builds)"
  - Win rate + game count inline per build
  - `<RuneSetupDisplay>` with "Runes" label (reuse existing component)

- **"Alternative Builds"** (builds 2-3): Condensed cards with items + stats

### 3F. Matchups Section Relabel + Enhancement

- Rename "Counters" to **"Toughest Matchups"** with subtitle "These champions counter {name}"
- Rename "Favorable" to **"Best Matchups"**
- Win rates with conditional coloring (red for counters, green for favorable)
- Game count always inline: `"41.2% (1,234 games)"`
- Use `<ChampionPortrait>` for opponent icons

### 3G. Import types from schema package

Replace the local type definitions (lines 21-75) with imports from `@transcendence/api-client/schema`, matching the pattern already used in `tierlist/page.tsx`.

---

## Phase 4: Champions Grid Enhancement

**File: `apps/web/app/champions/page.tsx`**

**Current**: Simple grid of icon + name. No stats, no filtering.

**Changes**:
1. Add a client-side **search input** at top to filter champions by name (no API call, filter the already-loaded DDragon list)
2. Optionally fetch tier list data alongside champion map to show **tier badge and primary role** on each card
3. Keep the existing responsive grid layout (`grid-cols-2 sm:3 md:4 lg:6`)

Fetch tier list data alongside champion map to show tier badges, win rates, and primary roles. This adds a backend call but makes the page significantly richer.

---

## Phase 5: Summoner Profile Polish

**File: `apps/web/components/SummonerProfileClient.tsx`**

### 5A. Rank Display Enhancement
- Format rank as: `GOLD II` in tier-colored text, `45 LP` below, `123W / 98L` with W colored green, L colored red
- Add a thin win rate bar underneath the W/L record
- Use rank-appropriate text colors (Iron=gray, Bronze=#CD7F32, Silver=#C0C0C0, Gold=#FFD700, Plat=#4CA3DD, Emerald=#50C878, Diamond=#B9F2FF, Master+=#9B59B6)

### 5B. Overview Stats Enhancement
- Win rate with conditional coloring via `<WinRateText>`
- Show W/L record: `"123W 98L"` with win/loss colors, instead of just total matches count
- KDA with color coding: green if >3.0, neutral 2.0-3.0, red <2.0

### 5C. Match Cards Color Scheme
- Change from `emerald-400/red-400` to `win/loss` color tokens (purple tint for wins / red for losses)
- Add relative time display ("2 hours ago")

### 5D. Role Breakdown Polish
- Win rate text with conditional coloring on each role row

---

## Phase 6: Match History + Detail Polish

**Files**:
- `apps/web/app/summoners/[region]/[riotId]/matches/page.tsx`
- `apps/web/app/summoners/[region]/[riotId]/matches/[matchId]/page.tsx`

### Match History:
- Switch to `win/loss` color tokens
- Add relative time display on each match card
- Add role position label alongside champion name

### Match Detail:
- Switch to `win/loss` color tokens
- Highlight the searched player's row: `bg-primary/10 border-l-2 border-primary`

---

## Phase 7: Navigation Refinements

**File: `apps/web/components/SiteHeaderClient.tsx`**

1. **Active nav link highlighting**: Use `usePathname()` (already imported) to highlight current page:
   ```
   const isActive = pathname?.startsWith("/tierlist");
   // "text-fg font-medium" when active vs "text-fg/70" when inactive
   ```

2. **Patch badge in header**: Small `<Badge>` showing current patch version, fetched from DDragon

---

## Backend Work Flagged (Not Blocking)

These features are common on u.gg/op.gg but require new API data:

| Feature | Missing Data | Priority |
|---------|-------------|----------|
| Ban rate on tier list + champion pages | `banRate` field on TierListEntry and ChampionWinRateDto | Medium |
| Skill order / leveling grid | Skill max order data not in schema | High (major gap vs u.gg) |
| Champion rank (#X of Y in role) | Requires tier list cross-reference or new field | Low |
| Rank emblem images | Hosted rank icon assets from Riot | Low |

---

## Implementation Order

**Step 0**: Create branch `frontend-revamp`, commit this plan as `apps/web/REVAMP_PLAN.md`

Then work sequentially, one commit per phase:

1. **Phase 0** (Foundation) -- all other phases depend on this
2. **Phase 1** (Homepage) -- quick win, small scope
3. **Phase 2** (Tier List) -- highest visual impact, establishes patterns
4. **Phase 3** (Champion Detail) -- most complex, biggest improvement
5. **Phase 4** (Champions Grid) -- enhancement with search + tier data
6. **Phase 5** (Summoner Profile) -- polish
7. **Phase 6** (Match History/Detail) -- polish
8. **Phase 7** (Navigation) -- small refinements

Each phase gets its own commit for continuity and easy rollback.

---

## Verification

After each phase:
1. `corepack pnpm web:lint` -- no lint errors
2. `corepack pnpm web:build` -- production build succeeds
3. `corepack pnpm web:dev` -- visual verification in browser
4. `corepack pnpm web:test` -- existing tests pass
