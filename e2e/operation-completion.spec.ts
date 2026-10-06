import { expect, test } from "@playwright/test";

import { accepted, childOperationId, observedAtUtc, operationStatus } from "../apps/web/test/operationFixtures";

const liveUrl = "/lol/live?region=na&riotId=Kronic%23NA1";
const localHost = new URL(process.env.BASE_URL ?? "https://kronic.one").hostname;
test.skip(!["localhost", "127.0.0.1", "[::1]"].includes(localHost), "Operation completion fixtures require the isolated local app/backend.");

test.describe("request-correlated live-game completion", () => {
  test.beforeEach(async ({ page }) => {
    await page.route("**/api/static/spells", (route) => route.fulfill({ json: { version: "test", spells: {} } }));
    await page.route("**/api/static/runes", (route) => route.fulfill({ json: { runeById: {} } }));
    await page.route("**/api/trn/app/lol/summoners/na/Kronic/NA1/live-game", (route) => route.fulfill({
      json: { state: "offline", participants: [], lastUpdatedUtc: "2026-09-01T00:00:00Z" }
    }));
    await page.route("**/api/trn/app/lol/summoners/na/Kronic/NA1/live-game/probe", (route) => route.fulfill({ status: 202, json: accepted }));
  });

  test("does not certify a stored snapshot until this probe finishes", async ({ page }, testInfo) => {
    let completed = false;
    let polls = 0;
    await page.route("**/api/trn/app/lol/operations/*", (route) => {
      polls += 1;
      return route.fulfill({ json: operationStatus(completed ? {} : { status: "running", retryAfterSeconds: 1 }) });
    });
    await page.goto(liveUrl);
    await expect.poll(() => polls).toBeGreaterThan(0);
    await expect(page.getByText("Stored snapshot: no active game. Current status has not been verified.")).toBeVisible();
    await expect(page.getByText(/^Checked /)).toHaveCount(0);
    completed = true;
    await expect(page.getByText("Not currently in a game.")).toBeVisible();
    await expect(page.getByText(/^Checked /)).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath("verified-observation.png"), fullPage: true });
  });

  test("failed upstream parsing retains stored data without a fresh check stamp", async ({ page }, testInfo) => {
    await page.route("**/api/trn/app/lol/operations/*", (route) => route.fulfill({ json: operationStatus({
      status: "failed", errorCode: "INVALID_UPSTREAM_RESPONSE"
    }) }));
    await page.goto(liveUrl);
    await expect(page.getByText(/INVALID_UPSTREAM_RESPONSE/)).toBeVisible();
    await expect(page.getByText("Stored snapshot: no active game. Current status has not been verified.")).toBeVisible();
    await expect(page.getByText(/^Checked /)).toHaveCount(0);
    await page.screenshot({ path: testInfo.outputPath("unverified-stored-observation.png"), fullPage: true });
  });

  test("a failed re-check preserves the prior verified observation time", async ({ page }) => {
    let polls = 0;
    await page.route("**/api/trn/app/lol/operations/*", (route) => route.fulfill({ json: operationStatus(
      ++polls === 1 ? {} : { status: "failed", errorCode: "RETRIES_EXHAUSTED" }
    ) }));
    await page.goto(liveUrl);
    const stamp = page.getByText(/^Checked /);
    await expect(stamp).toBeVisible();
    const before = await stamp.textContent();
    await page.getByRole("button", { name: "Re-check" }).click();
    await expect(page.getByText(/RETRIES_EXHAUSTED/)).toBeVisible();
    await expect(stamp).toHaveText(before ?? "");
    await expect(page.getByText("Stored snapshot: no active game. Current status has not been verified.")).toBeVisible();
  });

  test("rejects a completed operation for a different player", async ({ page }) => {
    await page.route("**/api/trn/app/lol/operations/*", (route) => route.fulfill({ json: operationStatus({
      gameName: "Another Player", result: {
        snapshotId: "33333333-3333-3333-3333-333333333333", observedAtUtc,
        liveGame: { state: "offline", participants: [], lastUpdatedUtc: observedAtUtc }
      }
    }) }));
    await page.goto(liveUrl);
    await expect(page.getByText("The operation status response is invalid.")).toBeVisible();
    await expect(page.getByText(/^Checked /)).toHaveCount(0);
  });
});

// SSR uses the separately seeded local API; only browser lifecycle responses are synthetic.
// Run with the local Next server configured to that API, never against production.
test.describe("request-correlated profile completion", () => {
  test.skip(process.env.LOCAL_OPERATION_BACKEND !== "seeded", "Requires the separately seeded local API for SSR profile reads.");
  test("waits for recent imports, tracks child history separately, and preserves active filters", async ({ page }, testInfo) => {
    const storedResponse = await page.request.get("/api/trn/public/lol/summoners/na/faker/NA1");
    expect(storedResponse.ok()).toBe(true);
    const stored = await storedResponse.json();
    expect(stored.status).toBe("ready");
    let recentComplete = false;
    let historyComplete = false;
    let profileReads = 0;
    let recentPolls = 0;
    const historyRequests: string[] = [];
    await page.route("**/api/trn/user/lol/summoners/na/faker/NA1/refresh", (route) => route.fulfill({ status: 202, json: accepted }));
    await page.route("**/api/trn/user/lol/operations/*", (route) => {
      const child = route.request().url().endsWith(childOperationId);
      if (!child) recentPolls += 1;
      return route.fulfill({ json: operationStatus({
        operationId: child ? childOperationId : accepted.operationId,
        kind: child ? "full_history" : "summoner_refresh",
        gameName: "faker",
        status: (child ? historyComplete : recentComplete) ? "succeeded" : "running",
        retryAfterSeconds: 1,
        phases: child ? [] : [{ name: "fullHistory", status: "queued", operationId: childOperationId }],
        result: { profileUpdatedAtUtc: observedAtUtc, recentImportCompletedAtUtc: observedAtUtc }
      }) });
    });
    await page.route("**/api/trn/public/lol/summoners/na/faker/NA1", (route) => route.fulfill({ json: {
      ...stored, profile: { ...stored.profile, summonerLevel: stored.profile.summonerLevel + ++profileReads }
    } }));
    await page.route("**/api/trn/public/lol/summoners/*/matches/recent?*", (route) => {
      historyRequests.push(route.request().url());
      return route.fulfill({ json: {
        items: [], page: 3, pageSize: 20, totalCount: 60, totalPages: 3,
        facets: { queues: [{ queueId: 420, queueType: "RANKED_SOLO_5x5", queueFamily: "RANKED_SOLO_DUO" }], championIds: [103] }
      } });
    });
    await page.goto("/lol/summoners/na/faker-NA1?page=3&queue=family:RANKED_SOLO_DUO&sort=kda_desc&champion=103");
    await expect(page.getByText(`Level ${stored.profile.summonerLevel}`, { exact: true })).toBeVisible();
    await expect.poll(() => historyRequests.length).toBe(1);
    await page.getByRole("button", { name: "Update Now" }).click();
    await expect.poll(() => recentPolls).toBeGreaterThan(0);
    await expect(page.getByText(`Level ${stored.profile.summonerLevel}`, { exact: true })).toBeVisible();
    expect(profileReads).toBe(0);
    expect(historyRequests).toHaveLength(1);
    recentComplete = true;
    await expect(page.getByText(/Full history is importing separately/)).toBeVisible();
    await expect(page.getByText(`Level ${stored.profile.summonerLevel + 1}`, { exact: true })).toBeVisible();
    await expect.poll(() => historyRequests.length).toBe(2);
    historyComplete = true;
    await expect(page.getByText(/Full-history import complete/)).toBeVisible();
    await expect(page.getByText(`Level ${stored.profile.summonerLevel + 2}`, { exact: true })).toBeVisible();
    await expect.poll(() => historyRequests.length).toBe(3);
    for (const request of historyRequests) {
      const query = new URL(request).searchParams;
      expect(query.get("page")).toBe("3");
      expect(query.get("championId")).toBe("103");
      expect(query.get("queueFamily")).toBe("RANKED_SOLO_DUO");
    }
    await expect(page).toHaveURL(/page=3/);
    await expect(page).toHaveURL(/sort=kda_desc/);
    await expect(page).toHaveURL(/champion=103/);
    await page.screenshot({ path: testInfo.outputPath("recent-and-full-history-complete.png"), fullPage: true });
  });

  test("a failed operation retains the initial stored profile without claiming completion", async ({ page }) => {
    const storedResponse = await page.request.get("/api/trn/public/lol/summoners/na/faker/NA1");
    expect(storedResponse.ok()).toBe(true);
    const stored = await storedResponse.json();
    expect(stored.status).toBe("ready");
    await page.route("**/api/trn/user/lol/summoners/na/faker/NA1/refresh", (route) => route.fulfill({ status: 202, json: accepted }));
    await page.route("**/api/trn/user/lol/operations/*", (route) => route.fulfill({ json: operationStatus({
      kind: "summoner_refresh", gameName: "faker", status: "failed", errorCode: "RETRIES_EXHAUSTED", result: {}
    }) }));
    await page.goto("/lol/summoners/na/faker-NA1");
    await expect(page.getByText(`Level ${stored.profile.summonerLevel}`, { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Update Now" }).click();
    await expect(page.getByText(/RETRIES_EXHAUSTED/)).toBeVisible();
    await expect(page.getByText(`Level ${stored.profile.summonerLevel}`, { exact: true })).toBeVisible();
    await expect(page.getByText(/recent-match refresh complete/)).toHaveCount(0);
  });

  test("a failed full-history status request does not erase recent-refresh success", async ({ page }) => {
    const storedResponse = await page.request.get("/api/trn/public/lol/summoners/na/faker/NA1");
    expect(storedResponse.ok()).toBe(true);
    const stored = await storedResponse.json();
    expect(stored.status).toBe("ready");
    await page.route("**/api/trn/user/lol/summoners/na/faker/NA1/refresh", (route) => route.fulfill({ status: 202, json: accepted }));
    await page.route("**/api/trn/user/lol/operations/*", (route) => route.request().url().endsWith(childOperationId)
      ? route.fulfill({ status: 503, json: { message: "History status unavailable" } })
      : route.fulfill({ json: operationStatus({
        kind: "summoner_refresh", gameName: "faker", phases: [{ name: "fullHistory", status: "queued", operationId: childOperationId }],
        result: { profileUpdatedAtUtc: observedAtUtc, recentImportCompletedAtUtc: observedAtUtc }
      }) }));
    await page.goto("/lol/summoners/na/faker-NA1");
    await expect(page.getByText(`Level ${stored.profile.summonerLevel}`, { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Update Now" }).click();
    await expect(page.getByText(/recent-match refresh complete.*Full-history completion has not been verified/)).toBeVisible();
    await expect(page.getByText(/History status unavailable/)).toBeVisible();
    await expect(page.getByText(`Level ${stored.profile.summonerLevel}`, { exact: true })).toBeVisible();
  });
});
