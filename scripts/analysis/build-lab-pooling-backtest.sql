-- Build Lab pooling backtest: which way of borrowing older patches predicts the next patch best?
--
-- Usage (read-only):
--   psql ... -v target=16.19 -v prior1=16.18 -v prior2=16.17 -f scripts/analysis/build-lab-pooling-backtest.sql
--
-- Every scheme builds the served model -- gold-bucket baseline plus observed-minus-expected lift, as
-- in build-lab-backtest.sql -- from the two prior patches only, with per-row weights, and is scored
-- on the target patch's games. A row is "changed" when, between its patch and the target, anything
-- it depends on changed: one of its items or runes (content hash, the same test the retired modeler
-- used) or the champion's balance hash.
--
--   prior1_only        prior1 at 1, prior2 unused
--   decay              prior1 0.6, prior2 0.35 (the recency weights Build Lab shipped with)
--   flat               both at 1
--   aware_drop         unchanged rows at 1, changed rows excluded
--   aware_damp         unchanged rows at 1, changed rows at 0.25

\set ON_ERROR_STOP on

CREATE TEMP TABLE changed_item AS
SELECT p.patch, iv."ItemId" AS id
FROM (VALUES (:'prior1'), (:'prior2')) p(patch)
JOIN "ItemVersions" iv ON iv."PatchVersion" IN (p.patch, :'target')
GROUP BY 1, 2
HAVING count(DISTINCT md5(coalesce(iv."Name", '') || '|' || coalesce(iv."Description", '') || '|' ||
       iv."PriceTotal"::text || '|' || coalesce(iv."BuildsFrom"::text, '') || '|' ||
       coalesce(iv."BuildsInto"::text, '') || '|' || iv."InStore"::text)) > 1;

CREATE TEMP TABLE changed_rune AS
SELECT p.patch, rv."RuneId" AS id
FROM (VALUES (:'prior1'), (:'prior2')) p(patch)
JOIN "RuneVersions" rv ON rv."PatchVersion" IN (p.patch, :'target')
GROUP BY 1, 2
HAVING count(DISTINCT md5(coalesce(rv."Name", '') || '|' || coalesce(rv."Description", ''))) > 1;

CREATE TEMP TABLE changed_champion AS
SELECT p.patch, cv."ChampionId" AS id
FROM (VALUES (:'prior1'), (:'prior2')) p(patch)
JOIN "ChampionVersions" cv ON cv."PatchVersion" IN (p.patch, :'target')
GROUP BY 1, 2
HAVING count(DISTINCT cv."BalanceHash") > 1;

CREATE TEMP TABLE src AS
SELECT s."Patch" AS patch, s."ChampionId" c, s."Role" r, s."PrefixHash" h, s."Family" f, s."Stage" s,
       s."ActionKey" a, s."GoldBucket" b, s."Games"::float8 g, s."Wins"::float8 w,
       (EXISTS (SELECT 1 FROM changed_champion x WHERE x.patch = s."Patch" AND x.id = s."ChampionId")
        OR (s."Family" IN (0, 1, 2) AND EXISTS (
            SELECT 1 FROM changed_item x
            WHERE x.patch = s."Patch" AND x.id = ANY (string_to_array(s."ActionKey", '+')::int[])))
        OR (s."Family" IN (3, 4) AND EXISTS (
            SELECT 1 FROM changed_rune x
            WHERE x.patch = s."Patch" AND x.id = ANY (string_to_array(s."ActionKey", '+')::int[])))
       ) AS changed
FROM "BuildLabOptionStats" s
WHERE s."Patch" IN (:'prior1', :'prior2') AND s."OpponentChampionId" = 0 AND s."Region" = 'ALL';

CREATE TEMP TABLE tgt AS
SELECT "ChampionId" c, "Role" r, "PrefixHash" h, "Family" f, "Stage" s,
       "ActionKey" a, "GoldBucket" b, "Games"::float8 g, "Wins"::float8 w
FROM "BuildLabOptionStats"
WHERE "Patch" = :'target' AND "OpponentChampionId" = 0 AND "Region" = 'ALL';

CREATE TEMP TABLE scheme (name text, w1_same float8, w1_changed float8, w2_same float8, w2_changed float8);
INSERT INTO scheme VALUES
    ('prior1_only', 1, 1, 0, 0),
    ('decay', 0.6, 0.6, 0.35, 0.35),
    ('flat', 1, 1, 1, 1),
    ('aware_drop', 1, 0, 1, 0),
    ('aware_damp', 1, 0.25, 1, 0.25);

CREATE TEMP TABLE weighted AS
SELECT sc.name AS scheme, s.c, s.r, s.h, s.f, s.s, s.a, s.b,
       sum(s.g * wt) AS g, sum(s.w * wt) AS w
FROM src s
CROSS JOIN scheme sc
CROSS JOIN LATERAL (SELECT CASE
        WHEN s.patch = :'prior1' AND NOT s.changed THEN sc.w1_same
        WHEN s.patch = :'prior1' THEN sc.w1_changed
        WHEN NOT s.changed THEN sc.w2_same
        ELSE sc.w2_changed END AS wt) weight
WHERE weight.wt > 0
GROUP BY 1, 2, 3, 4, 5, 6, 7, 8;
ANALYZE weighted;
ANALYZE tgt;

WITH cell AS (
    SELECT scheme, c, r, h, f, s, sum(w) / sum(g) AS rate FROM weighted GROUP BY 1, 2, 3, 4, 5, 6
),
bucket AS (
    SELECT scheme, c, r, h, f, s, b, sum(w) / sum(g) AS rate FROM weighted GROUP BY 1, 2, 3, 4, 5, 6, 7
),
opt AS (
    SELECT o.scheme, o.c, o.r, o.h, o.f, o.s, o.a,
           (sum(o.w) - sum(o.g * bk.rate)) / sum(o.g) AS lift
    FROM weighted o
    JOIN bucket bk USING (scheme, c, r, h, f, s, b)
    GROUP BY 1, 2, 3, 4, 5, 6, 7
),
-- Scored only on decisions every scheme can answer, so no scheme is graded on a different set.
common AS (
    SELECT c, r, h, f, s FROM cell GROUP BY 1, 2, 3, 4, 5
    HAVING count(DISTINCT scheme) = (SELECT count(*) FROM scheme)
),
scored AS (
    SELECT cl.scheme, t.f, t.g, t.w,
           least(0.99, greatest(0.01, coalesce(bk.rate, cl.rate) + coalesce(o.lift, 0))) AS p,
           least(0.99, greatest(0.01, coalesce(bk.rate, cl.rate))) AS p0
    FROM tgt t
    JOIN common USING (c, r, h, f, s)
    JOIN cell cl USING (c, r, h, f, s)
    LEFT JOIN bucket bk ON bk.scheme = cl.scheme AND (bk.c, bk.r, bk.h, bk.f, bk.s, bk.b) = (t.c, t.r, t.h, t.f, t.s, t.b)
    LEFT JOIN opt o ON o.scheme = cl.scheme AND (o.c, o.r, o.h, o.f, o.s, o.a) = (t.c, t.r, t.h, t.f, t.s, t.a)
)
SELECT scheme,
       coalesce(CASE f WHEN 0 THEN 'STARTER' WHEN 1 THEN 'ITEM' WHEN 2 THEN 'BOOTS'
                WHEN 3 THEN 'RUNE_PAGE' WHEN 4 THEN 'RUNE' WHEN 5 THEN 'SPELLS' END, 'ALL') AS family,
       round(sum(g)) AS games,
       round((sum(w * (1 - p) ^ 2 + (g - w) * p ^ 2) / sum(g))::numeric, 6) AS brier,
       round((-sum(w * ln(p) + (g - w) * ln(1 - p)) / sum(g))::numeric, 6) AS log_loss,
       round(((1 - sum(w * (1 - p) ^ 2 + (g - w) * p ^ 2) / sum(w * (1 - p0) ^ 2 + (g - w) * p0 ^ 2)) * 1e4)::numeric, 2)
           AS brier_skill_bp
FROM scored
GROUP BY GROUPING SETS ((scheme), (scheme, f))
ORDER BY family NULLS FIRST, brier;
