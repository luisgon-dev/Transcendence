#!/usr/bin/env node
/**
 * Frontend lab performance runner.
 *
 * Replaces `@lhci/cli`, which pinned an abandoned Lighthouse and produced results nothing
 * retained. This drives Lighthouse's programmatic API directly so the metrics can be emitted
 * in *our* shape: `transcendence_web_lab_*`, labelled with the exact same bounded `route`
 * vocabulary the browser Web Vitals pipeline uses (`@transcendence/web-routes`). That shared
 * label is the whole point — it is what lets a lab series and a field series for the same
 * route sit on one Grafana panel.
 *
 * Two modes, both from one run:
 *   --budgets <file>    assert per-route thresholds, exit non-zero on breach  (CI gate)
 *   --prom-out <file>   write a Prometheus textfile exposition                (prod trend)
 *   --report-dir <dir>  keep the full Lighthouse report per route             (diagnosis)
 *
 * The aggregate numbers say *which* route regressed; only the full report says why. Reach for
 * --report-dir when a route is slow and you need the main-thread breakdown, long tasks or
 * bootup cost by script. Reports are one file per route and are not written by default.
 *
 * Usage:
 *   node scripts/perf/web-lab.mjs --base-url http://127.0.0.1:3000 \
 *     --routes scripts/perf/routes.ci.json \
 *     --samples 3 \
 *     --budgets scripts/perf/web-budgets.json \
 *     --json-out .performance-artifacts/web-lab.json
 */

import { writeFileSync, renameSync, mkdirSync, readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { pathToFileURL } from "node:url";

import * as chromeLauncher from "chrome-launcher";
import lighthouse from "lighthouse";

import { webVitalsRouteTemplate } from "@transcendence/web-routes";

// Chrome flags mirror the retired lighthouserc.cjs so numbers stay comparable across the swap.
const CHROME_FLAGS = ["--headless=new", "--no-sandbox", "--disable-dev-shm-usage"];

const LIGHTHOUSE_SETTINGS = {
  formFactor: "mobile",
  screenEmulation: {
    mobile: true,
    width: 412,
    height: 823,
    deviceScaleFactor: 1.75,
    disabled: false
  },
  throttlingMethod: "simulate",
  onlyCategories: ["performance", "accessibility", "best-practices", "seo"]
};

/** Audit id -> the metric key we report it as. Numeric audits only. */
const METRIC_AUDITS = {
  "largest-contentful-paint": "lcp",
  "cumulative-layout-shift": "cls",
  "total-blocking-time": "tbt",
  "first-contentful-paint": "fcp",
  "server-response-time": "ttfb",
  "speed-index": "speed_index",
  interactive: "interactive",
  "total-byte-weight": "total_bytes"
};

/** Prometheus metric name + unit suffix per metric key. */
const METRIC_EXPOSITION = {
  lcp: ["transcendence_web_lab_lcp_milliseconds", "Lab largest contentful paint."],
  cls: ["transcendence_web_lab_cls", "Lab cumulative layout shift."],
  tbt: ["transcendence_web_lab_tbt_milliseconds", "Lab total blocking time."],
  fcp: ["transcendence_web_lab_fcp_milliseconds", "Lab first contentful paint."],
  ttfb: ["transcendence_web_lab_ttfb_milliseconds", "Lab server response time."],
  speed_index: ["transcendence_web_lab_speed_index_milliseconds", "Lab speed index."],
  interactive: ["transcendence_web_lab_interactive_milliseconds", "Lab time to interactive."],
  total_bytes: ["transcendence_web_lab_total_bytes", "Lab total transferred bytes."]
};

function parseArgs(argv) {
  const args = {
    baseUrl: "http://127.0.0.1:3000",
    routes: null,
    samples: 3,
    budgets: null,
    jsonOut: null,
    promOut: null,
    reportDir: null
  };
  for (let i = 0; i < argv.length; i += 1) {
    const flag = argv[i];
    const next = () => {
      const value = argv[i + 1];
      if (value === undefined) throw new Error(`${flag} requires a value`);
      i += 1;
      return value;
    };
    switch (flag) {
      case "--base-url": args.baseUrl = next().replace(/\/$/, ""); break;
      case "--routes": args.routes = next(); break;
      case "--samples": args.samples = Number.parseInt(next(), 10); break;
      case "--budgets": args.budgets = next(); break;
      case "--json-out": args.jsonOut = next(); break;
      case "--prom-out": args.promOut = next(); break;
      case "--report-dir": args.reportDir = next(); break;
      default: throw new Error(`Unknown flag: ${flag}`);
    }
  }
  if (!args.routes) throw new Error("--routes is required");
  if (!Number.isInteger(args.samples) || args.samples < 1) {
    throw new Error("--samples must be a positive integer");
  }
  return args;
}

function readJson(file) {
  return JSON.parse(readFileSync(resolve(file), "utf8"));
}

/**
 * Median, not mean. `@lhci/cli` aggregated with a median and a mean is far more exposed to a
 * single slow sample, which on a shared CI runner is a realistic and frequent event.
 */
function median(values) {
  const sorted = [...values].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
}

async function runOnce(url, port) {
  const result = await lighthouse(
    url,
    { port, output: "json", logLevel: "error" },
    { extends: "lighthouse:default", settings: LIGHTHOUSE_SETTINGS }
  );
  if (!result?.lhr) throw new Error(`Lighthouse returned no result for ${url}`);
  return result.lhr;
}

function extractSample(lhr) {
  const metrics = {};
  for (const [auditId, key] of Object.entries(METRIC_AUDITS)) {
    const value = lhr.audits?.[auditId]?.numericValue;
    if (typeof value === "number" && Number.isFinite(value)) metrics[key] = value;
  }
  const categories = {};
  for (const [id, category] of Object.entries(lhr.categories ?? {})) {
    if (typeof category.score === "number") categories[id] = category.score;
  }
  return { metrics, categories };
}

function aggregate(samples) {
  const metrics = {};
  const categories = {};
  for (const key of Object.keys(METRIC_EXPOSITION)) {
    const values = samples.map((s) => s.metrics[key]).filter((v) => typeof v === "number");
    if (values.length) metrics[key] = median(values);
  }
  const categoryIds = new Set(samples.flatMap((s) => Object.keys(s.categories)));
  for (const id of categoryIds) {
    const values = samples.map((s) => s.categories[id]).filter((v) => typeof v === "number");
    if (values.length) categories[id] = median(values);
  }
  return { metrics, categories };
}

/**
 * Budgets are resolved per route with a `default` fallback, because holding a tier list to the
 * homepage's numbers would either make the gate meaningless or make it permanently red.
 */
function assertBudgets(results, budgets) {
  const failures = [];
  for (const result of results) {
    const limits = { ...(budgets.default ?? {}), ...(budgets.routes?.[result.route] ?? {}) };
    for (const [key, limit] of Object.entries(limits)) {
      const actual = key.startsWith("category:")
        ? result.categories[key.slice("category:".length)]
        : result.metrics[key];
      if (typeof actual !== "number") continue;
      // Category scores are floors (higher is better); every other metric is a ceiling.
      const breached = key.startsWith("category:") ? actual < limit : actual > limit;
      if (breached) {
        failures.push({ route: result.route, url: result.url, key, limit, actual });
      }
    }
  }
  return failures;
}

function escapeLabel(value) {
  return String(value).replaceAll("\\", "\\\\").replaceAll("\n", "\\n").replaceAll('"', '\\"');
}

function renderPrometheus(results, formFactor) {
  const lines = [];
  for (const [key, [name, help]] of Object.entries(METRIC_EXPOSITION)) {
    const withMetric = results.filter((r) => typeof r.metrics[key] === "number");
    if (!withMetric.length) continue;
    lines.push(`# HELP ${name} ${help}`, `# TYPE ${name} gauge`);
    for (const result of withMetric) {
      const labels = `{route="${escapeLabel(result.route)}",form_factor="${escapeLabel(formFactor)}"}`;
      lines.push(`${name}${labels} ${result.metrics[key]}`);
    }
  }

  const scored = results.filter((r) => Object.keys(r.categories).length);
  if (scored.length) {
    lines.push(
      "# HELP transcendence_web_lab_category_score Lab Lighthouse category score, 0-1.",
      "# TYPE transcendence_web_lab_category_score gauge"
    );
    for (const result of scored) {
      for (const [category, score] of Object.entries(result.categories)) {
        const labels =
          `{route="${escapeLabel(result.route)}",form_factor="${escapeLabel(formFactor)}"` +
          `,category="${escapeLabel(category)}"}`;
        lines.push(`transcendence_web_lab_category_score${labels} ${score}`);
      }
    }
  }

  // Mirrors transcendence_analytics_matchup_last_success_unixtime_seconds so the staleness
  // alert can reuse the existing rule shape. Without this a failed sweep leaves node-exporter
  // serving yesterday's file forever and every graph looks healthy.
  lines.push(
    "# HELP transcendence_web_lab_last_success_unixtime_seconds Unix time of the last successful lab sweep.",
    "# TYPE transcendence_web_lab_last_success_unixtime_seconds gauge",
    `transcendence_web_lab_last_success_unixtime_seconds ${Math.floor(Date.now() / 1000)}`,
    "# HELP transcendence_web_lab_routes_measured Routes with at least one usable sample in the last sweep.",
    "# TYPE transcendence_web_lab_routes_measured gauge",
    `transcendence_web_lab_routes_measured ${results.length}`
  );

  return `${lines.join("\n")}\n`;
}

/**
 * Atomic write. node-exporter's textfile collector polls the directory and will happily parse a
 * half-written file, so the rename is what makes this safe rather than a nicety.
 */
function writeAtomic(file, contents) {
  const target = resolve(file);
  mkdirSync(dirname(target), { recursive: true });
  const tmp = `${target}.tmp`;
  writeFileSync(tmp, contents);
  renameSync(tmp, target);
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const routes = readJson(args.routes);
  if (!Array.isArray(routes) || !routes.length) {
    throw new Error(`${args.routes} must be a non-empty array of paths`);
  }

  const chrome = await chromeLauncher.launch({ chromeFlags: CHROME_FLAGS });
  const results = [];
  let failedRoutes = 0;

  try {
    for (const path of routes) {
      const url = `${args.baseUrl}${path}`;
      const route = webVitalsRouteTemplate(path);
      const samples = [];
      for (let i = 0; i < args.samples; i += 1) {
        try {
          const lhr = await runOnce(url, chrome.port);
          samples.push(extractSample(lhr));
          // One report per route is enough to diagnose with; the rest only differ by noise.
          if (args.reportDir && i === 0) {
            const slug = route.replace(/[^a-z0-9]+/gi, "-").replace(/^-|-$/g, "") || "root";
            writeAtomic(`${args.reportDir}/${slug}.json`, JSON.stringify(lhr));
          }
        } catch (error) {
          console.error(`  ! ${url} sample ${i + 1}/${args.samples}: ${error.message}`);
        }
      }
      if (!samples.length) {
        failedRoutes += 1;
        console.error(`  ! ${url}: every sample failed, route omitted`);
        continue;
      }
      const { metrics, categories } = aggregate(samples);
      results.push({ path, url, route, samples: samples.length, metrics, categories });
      const lcp = metrics.lcp === undefined ? "n/a" : `${Math.round(metrics.lcp)}ms`;
      const perf = categories.performance === undefined ? "n/a" : categories.performance.toFixed(2);
      console.log(`  ${route.padEnd(46)} lcp=${lcp.padEnd(9)} perf=${perf}`);
    }
  } finally {
    await chrome.kill();
  }

  if (args.jsonOut) {
    writeAtomic(
      args.jsonOut,
      `${JSON.stringify({ baseUrl: args.baseUrl, samples: args.samples, results }, null, 2)}\n`
    );
  }

  // Only written when every route reported. A partial file would silently redefine the
  // baseline for the routes that did survive.
  if (args.promOut) {
    if (failedRoutes) {
      console.error(`Refusing to write ${args.promOut}: ${failedRoutes} route(s) produced no samples.`);
      process.exitCode = 1;
      return;
    }
    writeAtomic(args.promOut, renderPrometheus(results, LIGHTHOUSE_SETTINGS.formFactor));
    console.log(`Wrote ${results.length} routes to ${args.promOut}`);
  }

  if (failedRoutes) {
    console.error(`\n${failedRoutes} route(s) produced no samples.`);
    process.exitCode = 1;
    return;
  }

  if (args.budgets) {
    const failures = assertBudgets(results, readJson(args.budgets));
    if (failures.length) {
      console.error(`\nBudget failures (${failures.length}):`);
      for (const f of failures) {
        const comparator = f.key.startsWith("category:") ? ">=" : "<=";
        console.error(
          `  ${f.route}  ${f.key}  expected ${comparator} ${f.limit}, got ${Number(f.actual.toFixed(3))}`
        );
      }
      process.exitCode = 1;
      return;
    }
    console.log(`\nAll budgets met across ${results.length} route(s).`);
  }
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch((error) => {
    console.error(error.stack ?? String(error));
    process.exitCode = 1;
  });
}
