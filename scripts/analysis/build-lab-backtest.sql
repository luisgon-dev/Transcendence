-- Build Lab backtest: do the published numbers predict the NEXT patch's games?
--
-- Usage (read-only; temp tables only):
--   psql ... -v source=16.17 -v target=16.18 -f scripts/analysis/build-lab-backtest.sql
--
-- Every model predicts a game's win probability from the SOURCE patch's counts and is scored on the
-- TARGET patch's games. They all share one baseline -- the decision's win rate in that game's gold
-- bucket -- and differ only in what they add for the choice made. Comparing "adjusted" with "raw"
-- therefore asks exactly one question: given the gold state, does the gold-adjusted lift say more
-- about the outcome than the raw lift, which partly re-counts the lead the bucket already holds?
--
--   baseline         bucket win rate, no choice information
--   raw              baseline + (option raw win rate - decision win rate)
--   adjusted         baseline + observed-minus-expected lift          <- what Build Lab serves
--   *_k<N>           the same lift shrunk toward 0 by N pseudo-games: lift * g / (g + N)
--
-- Scope: the all-games scope (no matchup/region), decisions present in both patches. An option the
-- source patch never saw gets no lift. Predictions are clipped to [0.01, 0.99].
-- Metrics are per game: Brier (lower is better) and log loss (lower is better), plus Brier skill
-- against the baseline (higher is better; 0 = no better than knowing the gold state alone).

\set ON_ERROR_STOP on

-- Materialized and analyzed before the joins. As plain CTEs the planner had no row estimates for
-- ~2.6M rows a patch and chose nested loops; it ran for 20 minutes before being cancelled.
CREATE TEMP TABLE src AS
SELECT "ChampionId" c, "Role" r, "PrefixHash" h, "Family" f, "Stage" s,
       "ActionKey" a, "GoldBucket" b, "Games"::float8 g, "Wins"::float8 w
FROM "BuildLabOptionStats"
WHERE "Patch" = :'source' AND "OpponentChampionId" = 0 AND "Region" = 'ALL';

CREATE TEMP TABLE tgt AS
SELECT "ChampionId" c, "Role" r, "PrefixHash" h, "Family" f, "Stage" s,
       "ActionKey" a, "GoldBucket" b, "Games"::float8 g, "Wins"::float8 w
FROM "BuildLabOptionStats"
WHERE "Patch" = :'target' AND "OpponentChampionId" = 0 AND "Region" = 'ALL';

CREATE TEMP TABLE cell AS SELECT c, r, h, f, s, sum(w) / sum(g) AS rate FROM src GROUP BY 1, 2, 3, 4, 5;
CREATE TEMP TABLE bucket AS SELECT c, r, h, f, s, b, sum(w) / sum(g) AS rate FROM src GROUP BY 1, 2, 3, 4, 5, 6;
ANALYZE src; ANALYZE tgt; ANALYZE cell; ANALYZE bucket;

CREATE TEMP TABLE opt AS
SELECT o.c, o.r, o.h, o.f, o.s, o.a,
       sum(o.g) AS g,
       sum(o.w) / sum(o.g) - min(cl.rate) AS raw_lift,
       (sum(o.w) - sum(o.g * bk.rate)) / sum(o.g) AS adj_lift
FROM src o
JOIN cell cl USING (c, r, h, f, s)
JOIN bucket bk USING (c, r, h, f, s, b)
GROUP BY 1, 2, 3, 4, 5, 6;
ANALYZE opt;

CREATE TEMP TABLE scored AS
SELECT t.f, t.g, t.w,
       coalesce(bk.rate, cl.rate) AS base,
       coalesce(o.raw_lift, 0) AS raw_lift,
       coalesce(o.adj_lift, 0) AS adj_lift,
       coalesce(o.g, 0) AS og
FROM tgt t
JOIN cell cl USING (c, r, h, f, s)
LEFT JOIN bucket bk USING (c, r, h, f, s, b)
LEFT JOIN opt o USING (c, r, h, f, s, a);

WITH models AS (
    SELECT f, g, w, 'baseline' AS model, base AS p FROM scored
    UNION ALL SELECT f, g, w, 'raw', base + raw_lift FROM scored
    UNION ALL SELECT f, g, w, 'raw_k100', base + raw_lift * og / (og + 100) FROM scored
    UNION ALL SELECT f, g, w, 'adjusted', base + adj_lift FROM scored
    UNION ALL SELECT f, g, w, 'adjusted_k30', base + adj_lift * og / (og + 30) FROM scored
    UNION ALL SELECT f, g, w, 'adjusted_k100', base + adj_lift * og / (og + 100) FROM scored
    UNION ALL SELECT f, g, w, 'adjusted_k300', base + adj_lift * og / (og + 300) FROM scored
    UNION ALL SELECT f, g, w, 'adjusted_k1000', base + adj_lift * og / (og + 1000) FROM scored
    UNION ALL SELECT f, g, w, 'adjusted_k3000', base + adj_lift * og / (og + 3000) FROM scored
    UNION ALL SELECT f, g, w, 'raw_k1000', base + raw_lift * og / (og + 1000) FROM scored
),
clipped AS (
    SELECT f, g, w, model, least(0.99, greatest(0.01, p)) AS p FROM models
),
metrics AS (
    SELECT CASE f WHEN 0 THEN 'STARTER' WHEN 1 THEN 'ITEM' WHEN 2 THEN 'BOOTS'
                  WHEN 3 THEN 'RUNE_PAGE' WHEN 4 THEN 'RUNE' WHEN 5 THEN 'SPELLS' ELSE f::text END AS family,
           model,
           sum(g) AS games,
           sum(w * (1 - p) ^ 2 + (g - w) * p ^ 2) / sum(g) AS brier,
           -sum(w * ln(p) + (g - w) * ln(1 - p)) / sum(g) AS log_loss
    FROM clipped
    GROUP BY GROUPING SETS ((f, model), (model))
)
SELECT coalesce(family, 'ALL') AS family, model, round(games) AS games,
       round(brier::numeric, 6) AS brier,
       round(log_loss::numeric, 6) AS log_loss,
       round((1 - brier / first_value(brier) OVER (PARTITION BY family ORDER BY model = 'baseline' DESC))::numeric * 1e4, 2)
           AS brier_skill_bp
FROM metrics
ORDER BY family NULLS FIRST, brier;
