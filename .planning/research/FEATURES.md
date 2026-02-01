# Feature Landscape: League of Legends Analytics Backend

**Domain:** League of Legends Analytics Platform (op.gg/u.gg-like service)
**Researched:** 2026-02-01
**Confidence:** MEDIUM (based on WebSearch findings cross-referenced across multiple major platforms)

## Table Stakes

Features users expect from every League analytics platform. Missing these = product feels incomplete.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| **Summoner Profile Lookup** | Core entry point - users search by Riot ID | Low | Already implemented. Must support all 16 regions. |
| **Match History Display** | Every platform shows recent games with basic stats | Low | Already implemented. Need pagination and filtering. |
| **KDA & Performance Stats** | Users expect kills/deaths/assists, CS, damage metrics | Low | Already implemented via SummonerStatsController. |
| **Current Rank Display** | Shows current LP, tier, and division for ranked queues | Low | Already implemented. Need historical rank snapshots visible. |
| **Champion Statistics** | Per-champion win rate, games played, average KDA for a summoner | Medium | Basic version exists. Need matchup-specific stats. |
| **Build Recommendations** | Runes, items, skill order based on high win-rate data | Medium | Missing. Requires aggregating builds from high-performing matches. |
| **Tier Lists** | Champions ranked by win rate, pick rate, ban rate per role | Medium | Missing. Requires patch-specific champion meta analysis. |
| **Live Game Lookup** | See who's in a game, their ranks, champion stats, recent performance | Medium | Missing. Critical for desktop app champ select feature. Uses Spectator API. |
| **Champion Counters** | Which champions perform well against specific matchups | Medium | Missing. Requires matchup analysis across large match dataset. |
| **Role-Specific Stats** | Performance broken down by lane/role (Top, Jungle, Mid, ADC, Support) | Low | Basic version exists. Need deeper breakdown. |
| **Multi-Region Support** | Search summoners across all regions (NA, EUW, KR, etc.) | Low | Already implemented with platform routing. |
| **Recent Performance Trends** | Win streak, loss streak, recent form indicator | Low | Missing. Simple calculation from recent match results. |

## Differentiators

Features that set platforms apart. Not expected by all users, but create competitive advantage.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| **Champion Mastery Visualization** | Show mastery points, levels, progression charts | Low | Uses Riot's Champion Mastery API. Players love seeing their progression. |
| **Pro Build Tracking** | Show what pros/high-elo streamers build on champions | Medium | Requires identifying pro player accounts and tracking their builds. Highly valued feature. |
| **AI-Powered Insights** | Personalized tips, champion recommendations based on playstyle | High | U.GG launched this in 2026 with Theta. Emerging differentiator. |
| **Performance Score/GPI** | Single metric summarizing player skill (like Mobalytics GPI) | High | Requires sophisticated algorithm. Strong engagement hook. |
| **Matchup Analysis Videos** | Video guides for struggling matchups | High | DPM.LOL offers this. High effort, high value for learning. |
| **Head-to-Head Comparison** | Compare two summoners side-by-side | Low | Simple but engaging. Good for competitive/social use cases. |
| **Champion Pool Analysis** | Visualize champion diversity, suggest pool expansion | Medium | MasteryChart.com specializes in this. Good for improvement-focused users. |
| **Jungle Timer Tracking** | Objective respawn timers, team gold difference in real-time | Medium | Desktop app feature via Live Client API. Porofessor/Blitz offer this. |
| **Auto-Import Builds** | One-click import runes/items into League client | Low | Desktop app feature. Blitz does this automatically. Huge UX win. |
| **Duo Performance Tracking** | Stats when playing with specific duo partners | Medium | Shows synergy. Good for premade groups. |
| **Champion Milestone Tracking** | Track progress toward mastery milestones | Low | Uses official mastery system. Simple engagement feature. |
| **Regional Meta Differences** | Compare champion performance across regions (KR vs NA meta) | Medium | Interesting for competitive players. Requires regional data aggregation. |
| **Win Probability Prediction** | Predict match outcome based on team comps, recent form | High | Engaging but complex. Requires ML model. |
| **Performance Spike Detection** | Alert when enemy hits power spike (level/item) | Medium | Desktop overlay feature. Mobalytics does this well. |

## Anti-Features

Features to explicitly NOT build. Common mistakes in this domain or things that hurt UX.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| **Real-Time Push Notifications** | Adds massive complexity for minimal value. League updates are poll-friendly. | Use polling with reasonable intervals. Desktop app can poll locally faster. |
| **Social Features (Friends, Sharing)** | Scope creep. Not core to analytics value. Riot handles social layer. | Focus on analytics excellence. Let users share via external means. |
| **In-App Chat/Messaging** | Completely out of scope. Riot provides comms. | N/A |
| **Account Boosting Services** | Violates Riot ToS and damages brand integrity. | Never integrate MMR boosting, account selling, etc. |
| **Excessive Data Retention** | Storing every match forever bloats DB and increases costs. Old data has diminishing value. | Retain detailed match data for recent matches (30-90 days), aggregate older data into summaries. |
| **Overwolf Dependency** | Users increasingly reject Overwolf due to performance issues and bloat. | Build standalone desktop app. DPM.LOL is moving away from Overwolf. |
| **Paywalling Core Analytics** | Users expect free stats. Paywalling basics drives users to competitors. | Free tier for core analytics. Premium for advanced features (AI insights, ad-free, priority refresh). |
| **Intrusive Ads in Desktop App** | Kills UX. Users will uninstall. | Minimal/no ads in desktop app. Web can have non-intrusive ads. |
| **Revealing Strategic Patterns Too Deeply** | Showing enemy jungler's exact gank patterns crosses line from "helpful" to "unfair advantage." Creates privacy concerns. | Show general tendencies, not predictive opponent tracking. Balance insight vs. fairness. |
| **Auto-Play on All Platforms** | Automatically importing builds without user confirmation can backfire if data is wrong. | Require user confirmation for imports. Make it one-click, not zero-click. |
| **Unlimited Match Refresh** | Allows API abuse, hits rate limits, wastes resources. | Throttle refresh requests (e.g., once per 5 minutes per summoner). Already implemented with refresh locks. |

## Feature Dependencies

```
Core Foundation (Already Implemented):
├─ Summoner Lookup (Riot ID)
├─ Match History Fetching
├─ Rank Tracking
└─ Background Jobs (Hangfire)

Tier 1 (Build on Foundation):
├─ Live Game Lookup
│   └─ Requires: Spectator API integration
│   └─ Enables: Champ select features (desktop app)
│
├─ Build Recommendations
│   └─ Requires: Match data aggregation by champion/patch
│   └─ Enables: Rune/item suggestions, skill orders
│
└─ Tier Lists
    └─ Requires: Champion performance aggregation across matches
    └─ Enables: Meta insights, champion viability

Tier 2 (Enhance Tier 1):
├─ Champion Counters
│   └─ Requires: Tier Lists, matchup performance analysis
│   └─ Enables: Draft recommendations
│
├─ Pro Build Tracking
│   └─ Requires: Pro player account database
│   └─ Enables: High-elo build alternatives
│
└─ Performance Trends
    └─ Requires: Match history
    └─ Enables: Win streaks, recent form

Tier 3 (Advanced):
├─ AI Insights
│   └─ Requires: All Tier 1/2 features for training data
│
├─ Performance Score (GPI)
│   └─ Requires: Comprehensive stats across all features
│
└─ Win Probability Prediction
    └─ Requires: Team comp data, player performance data, ML model
```

## MVP Recommendation (Next Milestone)

For the next milestone, prioritize features that enable the desktop app use case (live game during champ select):

### Must-Have (Table Stakes Gaps):
1. **Live Game Lookup** - Critical for desktop app champ select feature
2. **Build Recommendations** - Expected baseline feature, missing entirely
3. **Champion Counters** - Expected baseline feature, helps with draft
4. **Tier Lists** - Expected baseline feature, shows meta
5. **Recent Performance Trends** - Simple but high-value addition to profiles

### Nice-to-Have (Quick Wins):
1. **Champion Mastery Display** - Low effort, uses existing Riot API
2. **Head-to-Head Comparison** - Low effort, engaging feature
3. **Role-Specific Deep Dive** - Enhance existing role stats

### Defer to Post-MVP:
- **AI Insights**: High complexity, requires mature data pipeline first. Emerging differentiator but not table stakes.
- **Performance Score/GPI**: High complexity, need established user base to validate algorithm.
- **Pro Build Tracking**: Medium effort, valuable but not critical for MVP functionality.
- **Auto-Import Builds**: Desktop-only feature, can layer in after core analytics proven.
- **Win Probability**: High complexity ML project, nice-to-have not must-have.
- **Regional Meta Comparison**: Interesting but niche use case.

## Implementation Priority Notes

**Critical Path for Desktop App:**
1. Live Game Lookup (Spectator API) - Blocks champ select features
2. Build Recommendations - Needed for in-game overlay
3. Champion Counters - Needed for draft assistance

**Critical Path for Web Interface:**
1. Enhanced summoner profiles (fill stats gaps)
2. Tier Lists - Most visited page on competitor sites
3. Champion-specific pages (builds, counters, matchups)

**Quick Wins for User Engagement:**
1. Champion Mastery - Simple API call, players love progression
2. Recent Form Indicators - Simple calculation, high visibility
3. Role Breakdown Enhancement - Extend existing feature

## Data Requirements by Feature

| Feature | Data Source | Update Frequency | Storage Impact |
|---------|-------------|------------------|----------------|
| Live Game | Spectator API | Real-time (poll every 30s) | Minimal (transient) |
| Build Recommendations | Match V5 aggregated | Per-patch | Medium (aggregates only) |
| Tier Lists | Match V5 aggregated | Daily | Low (summary tables) |
| Champion Counters | Match V5 matchup analysis | Per-patch | Medium (matchup matrix) |
| Pro Builds | Match V5 + Pro account list | Hourly | Medium (recent pro games) |
| Champion Mastery | Champion Mastery API | On-demand | Low (per-summoner) |
| Performance Trends | Existing match data | Real-time from DB | None (calculated) |
| AI Insights | All sources + ML model | On-demand | High (model + features) |

## API Design Implications

Based on this feature research, the API should support:

**Core Endpoints:**
- `GET /api/summoners/{region}/{name}/{tag}` - Already exists
- `GET /api/summoners/{id}/stats/*` - Already exists (overview, champions, roles, matches)

**Missing Endpoints:**
- `GET /api/live-game/{region}/{name}/{tag}` - Live game lookup
- `GET /api/champions/{id}/builds` - Build recommendations (runes, items, skills)
- `GET /api/champions/{id}/counters` - Counter matchups
- `GET /api/champions/tier-list` - Tier list by role/patch
- `GET /api/champions/{id}/mastery/{summonerId}` - Mastery data
- `GET /api/pro-builds/{championId}` - Pro player builds
- `GET /api/summoners/{id}/compare/{otherId}` - Head-to-head comparison

**Desktop App Specific:**
- Live game polling optimized for low latency
- Build import format compatible with League client
- Lightweight response payloads for overlay performance

**Web Interface Specific:**
- Paginated match history (already exists)
- Rich metadata for SEO (summoner profiles)
- Historical data visualization endpoints

## Sources

### Platform Feature Analysis
- [OP.GG MCP Server](https://github.com/opgginc/opgg-mcp)
- [U.GG FAQ](https://u.gg/faq)
- [U.GG Features Overview](https://u.gg/)
- [Mobalytics vs OP.GG Comparison](https://mobalytics.gg/opgg-vs-mobalytics/)
- [Mobalytics Features Guide](https://mobalytics.gg/blog/lol-how-to-use-mobalytics-overlay-live-companion/)
- [Best League Companion Apps 2025](https://www.itero.gg/articles/what-is-the-best-league-of-legends-companion-app-in-2025)

### Live Game & Champ Select
- [League Client API Documentation](https://downthecrop.xyz/blog/reading-writing-data-from-the-league-of-legends-client/)
- [Riot Developer Portal](https://developer.riotgames.com/docs/lol)
- [5 Best LoL Overlay Apps](https://1v9.gg/blog/league-of-legends-lol-best-overlay-apps)

### Desktop App Ecosystem
- [Porofessor on Overwolf](https://www.overwolf.com/app/trebonius-porofessor.gg)
- [Best LoL Programs 2025](https://eloboostleague.com/blog/best-programs-for-league-of-legends-2024/)
- [DPM.LOL Analytics Innovation](https://esports.gg/news/league-of-legends/how-dpm-lol-is-transforming-lol-analytics/)

### Analytics & Statistics
- [LoLalytics](https://lolalytics.com/)
- [METAsrc Stats](https://www.metasrc.com/lol/stats)
- [LeagueOfGraphs](https://www.leagueofgraphs.com/)
- [Champion Mastery Guide](https://support-leagueoflegends.riotgames.com/hc/en-us/articles/204211284-Champion-Mastery-Guide)

### Community Feedback & Concerns
- [U.GG AI Integration Announcement](https://u.gg/lol/articles/theta-ugg-brings-ai-powered-league-of-legends-stats)
- [League Data Privacy Discussion](https://www.zleague.gg/theportal/unraveling-the-debate-league-of-legends-data-privacy-discussion/)
- [Companion App Performance Issues](https://www.itero.gg/articles/what-is-the-best-league-of-legends-companion-app-in-2025)
