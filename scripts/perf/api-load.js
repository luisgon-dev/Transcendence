import http from "k6/http";
import { check, sleep } from "k6";
import { Counter, Trend } from "k6/metrics";

const baseUrl = __ENV.BASE_URL || "http://127.0.0.1:8080";
const roles = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

const failedChecks = new Counter("transcendence_failed_checks");
const readyDuration = new Trend("transcendence_ready_duration", true);
const regionalDuration = new Trend("transcendence_regional_leaderboard_duration", true);
const championDuration = new Trend("transcendence_champion_leaderboard_duration", true);
const queryMatrixDuration = new Trend("transcendence_query_matrix_duration", true);

export const options = {
  scenarios: {
    cache_hot_reads: {
      executor: "constant-vus",
      exec: "cacheHotReads",
      vus: 5,
      duration: "15s"
    },
    varied_queries: {
      executor: "constant-vus",
      exec: "variedQueries",
      vus: 2,
      duration: "15s"
    }
  },
  thresholds: {
    checks: ["rate>0.99"],
    http_req_failed: ["rate<0.01"],
    transcendence_failed_checks: ["count<1"],
    transcendence_ready_duration: ["p(95)<200"],
    transcendence_regional_leaderboard_duration: ["p(95)<500"],
    transcendence_champion_leaderboard_duration: ["p(95)<750"],
    transcendence_query_matrix_duration: ["p(95)<1500"]
  }
};

function verify(response, name, expectedContentType = "application/json") {
  const passed = check(response, {
    [`${name}: status 200`]: (res) => res.status === 200,
    [`${name}: expected content type`]: (res) =>
      String(res.headers["Content-Type"] || "").includes(expectedContentType)
  });
  if (!passed) failedChecks.add(1, { endpoint: name });
}

export function setup() {
  const warmUrls = [
    { url: `${baseUrl}/health/ready`, contentType: "text/plain" },
    {
      url: `${baseUrl}/api/lol/leaderboards?region=na&queue=solo&limit=50`,
      contentType: "application/json"
    },
    {
      url: `${baseUrl}/api/lol/leaderboards?region=na&queue=solo&championId=157&role=MIDDLE&limit=50&minimumChampionGames=5`,
      contentType: "application/json"
    }
  ];

  for (const target of warmUrls) {
    const response = http.get(target.url, { tags: { phase: "warmup" } });
    verify(response, "warmup", target.contentType);
  }
}

export function cacheHotReads() {
  const ready = http.get(`${baseUrl}/health/ready`, {
    tags: { endpoint: "ready", cache: "n/a" }
  });
  verify(ready, "ready", "text/plain");
  readyDuration.add(ready.timings.duration);

  const regional = http.get(
    `${baseUrl}/api/lol/leaderboards?region=na&queue=solo&limit=50`,
    { tags: { endpoint: "regional-leaderboard", cache: "hot" } }
  );
  verify(regional, "regional leaderboard");
  regionalDuration.add(regional.timings.duration);

  const champion = http.get(
    `${baseUrl}/api/lol/leaderboards?region=na&queue=solo&championId=157&role=MIDDLE&limit=50&minimumChampionGames=5`,
    { tags: { endpoint: "champion-leaderboard", cache: "hot" } }
  );
  verify(champion, "champion leaderboard");
  championDuration.add(champion.timings.duration);

  sleep(0.25);
}

export function variedQueries() {
  const role = roles[(__VU + __ITER) % roles.length];
  const minimumGames = 1 + ((__VU * 7 + __ITER) % 20);
  const limit = 20 + ((__VU * 11 + __ITER) % 31);
  const response = http.get(
    `${baseUrl}/api/lol/leaderboards?region=na&queue=solo&championId=157&role=${role}&limit=${limit}&minimumChampionGames=${minimumGames}`,
    { tags: { endpoint: "champion-leaderboard", cache: "varied" } }
  );

  verify(response, "varied champion leaderboard");
  queryMatrixDuration.add(response.timings.duration, { role });
  sleep(0.25);
}
