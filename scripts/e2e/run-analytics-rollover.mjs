import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { once } from "node:events";
import { createServer } from "node:http";
import { createRequire } from "node:module";
import { resolve } from "node:path";
import { setTimeout } from "node:timers/promises";
import { chromium, expect } from "@playwright/test";

const earlier = "16.22";
const active = "16.23";
const now = "2026-11-19T06:30:06Z";
const queueFamily = "RANKED_SOLO_DUO";
const role = "BOTTOM";
const championId = 51;
let published = false;
let buildsReady = false;
const profileRequests = [];

function selection(requestedPatch) {
  const preparing = requestedPatch === active && !published;
  return {
    state: preparing ? "Preparing" : "Ready",
    requestedPatch,
    activePatch: active,
    effectivePatch: preparing ? null : (requestedPatch ?? (published ? active : earlier))
  };
}

function grade(patch) {
  return {
    championId, role, tier: 1,
    games: patch === earlier ? 2400 : 1900,
    winRate: patch === earlier ? 0.523 : 0.634,
    pickRate: 0.122, banRate: 0.092, contestedScore: 0.12,
    strengthScore: 0.07, roleBaseline: 0.5, isLowSample: false
  };
}

function fixture(url) {
  const requested = url.searchParams.get("patch");
  const patchSelection = selection(requested);
  const patch = patchSelection.effectivePatch ?? requested;
  const rankTier = url.searchParams.get("rankTier") ?? "EMERALD_PLUS";
  const region = url.searchParams.get("region") ?? "ALL";
  const data = patchSelection.state === "Ready";

  if (url.pathname === "/api/lol/analytics/status") {
    return { patch: active, activePatchReleasedAtUtc: now, activePatchDetectedAtUtc: now };
  }
  if (url.pathname === "/api/lol/analytics/regions") {
    return [{ code: "ALL", label: "Global", isDefault: true }, { code: "NA1", label: "North America" }];
  }
  if (url.pathname === "/api/lol/analytics/published-patches") {
    return [active, earlier].map(version => ({
      patch: version, isActive: version === active, queueFamily,
      state: version === active && !published ? "Preparing" : "Ready",
      releasedAtUtc: now, detectedAtUtc: now, matchCount: 2400
    }));
  }
  if (url.pathname === "/api/lol/analytics/tierlist") {
    return { patch, patchSelection, rankTier, region, queueFamily, entries: data ? [grade(patch)] : [] };
  }
  if (url.pathname === "/api/lol/analytics/champions/51/profile") {
    profileRequests.push(new URLSearchParams(url.search));
    return {
      championId, effectiveRole: role, queueFamily, patchSelection,
      winRates: {
        championId, patch, queueFamily,
        byRoleTier: data ? [{ ...grade(patch), rankTier: "EMERALD", patch }] : []
      },
      grade: data ? { ...grade(patch), rankScope: rankTier } : null,
      buildsState: patch === active && !buildsReady ? "Preparing" : "Ready",
      matchupsState: "Ready",
      builds: { championId, role, rankTier, region, patch, queueFamily, builds: [], globalCoreItems: [] },
      matchups: { championId, role, rankTier, region, patch, queueFamily, counters: [], favorableMatchups: [], allMatchups: [] }
    };
  }
}

async function boundServer(handler) {
  const server = createServer(handler).listen(0, "127.0.0.1");
  await once(server, "listening");
  return server;
}

const backend = await boundServer((req, res) => {
  const response = fixture(new URL(req.url, "http://127.0.0.1"));
  res.writeHead(response === undefined ? 404 : 200, { "Content-Type": "application/json", "Cache-Control": "no-store" });
  res.end(JSON.stringify(response ?? {}));
});
const portReservation = await boundServer();
const port = portReservation.address().port;
await new Promise((done, reject) => portReservation.close(error => error ? reject(error) : done()));
const origin = `http://127.0.0.1:${port}`;
const require = createRequire(import.meta.url);
const next = spawn(process.execPath, [require.resolve("next/dist/bin/next"), "start", "-H", "127.0.0.1", "-p", String(port)], {
  cwd: resolve("apps/web"),
  env: {
    ...process.env,
    TRN_BACKEND_BASE_URL: `http://127.0.0.1:${backend.address().port}`,
    TRN_PUBLIC_ORIGIN: origin,
    TRN_BACKEND_API_KEY: "rollover-test-fixture",
    TRN_TRUSTED_PROXY_HEADERS: "true",
    NEXT_TELEMETRY_DISABLED: "1"
  },
  windowsHide: true,
  stdio: ["ignore", "pipe", "pipe"]
});
let logs = "";
next.stdout.on("data", chunk => { logs = (logs + chunk).slice(-8000); });
next.stderr.on("data", chunk => { logs = (logs + chunk).slice(-8000); });
let browser;
try {
  const readyBy = Date.now() + 12_000;
  while (true) {
    try {
      if ((await fetch(`${origin}/api/health`, { signal: AbortSignal.timeout(500) })).ok) break;
    } catch {}
    if (next.exitCode !== null || Date.now() >= readyBy) throw new Error("The production Next server did not start");
    await setTimeout(150);
  }
  browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1360, height: 1050 } });
  const oldBanner = page.getByText("Showing patch 16.22 while 16.23 stats are calculated.", { exact: true });
  await page.goto(`${origin}/lol`);
  await expect(oldBanner).toBeVisible();
  await page.goto(`${origin}/lol/champions`);
  await expect(oldBanner).toBeVisible();
  await expect(page.getByRole("link", { name: /Caitlyn/ })).toBeVisible();
  await page.goto(origin);
  await expect(oldBanner).toBeVisible();
  await page.getByRole("region", { name: "Strongest by lane" }).getByRole("link", { name: /Caitlyn/ }).click();
  await expect(page).toHaveURL(/\/lol\/champions\/51\?role=BOTTOM/);
  await expect(page.getByRole("heading", { name: "Caitlyn", exact: true })).toBeVisible();
  await expect(oldBanner).toBeVisible();
  await expect(page.getByText("52.3%", { exact: true }).first()).toBeVisible();
  if (process.env.FORGE_ARTIFACT) await page.screenshot({ path: process.env.FORGE_ARTIFACT, animations: "disabled" });

  const filter = page.getByLabel("Patch", { exact: true });
  await filter.selectOption(active);
  await expect(page.getByText("Stats for patch 16.23 are being calculated.", { exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Caitlyn", exact: true })).toBeVisible();
  await expect(filter).toHaveValue(active);
  await expect(page.getByText("52.3%", { exact: true })).toHaveCount(0);
  assert(profileRequests.some(params => params.get("patch") === active && params.get("role") === role && params.get("rankTier") === "EMERALD_PLUS"));

  published = true;
  await filter.selectOption("");
  await expect(page.getByText("63.4%", { exact: true }).first()).toBeVisible();
  await expect(oldBanner).toHaveCount(0);
  await expect(page.getByText("Builds are being calculated for these filters.")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Matchups", exact: true })).toBeVisible();
  buildsReady = true;
  await page.getByRole("button", { name: "Check again", exact: true }).click();
  await expect(page.getByText("There are not enough games for these filters yet.")).toBeVisible();
  await page.getByRole("navigation").getByRole("link", { name: "Tier List", exact: true }).first().click();
  await expect(page.locator("table").getByText("63.40%", { exact: true })).toBeVisible();
  await expect(page.getByText("This page is taking too long to load.", { exact: true })).toHaveCount(0);
  console.log("Verified last published homepage navigation, exact-patch preparing state, and subsequent publication in one browser tab.");
} catch (error) {
  console.error(logs);
  throw error;
} finally {
  await browser?.close();
  if (next.exitCode === null) next.kill();
  backend.closeAllConnections();
  backend.close();
}
