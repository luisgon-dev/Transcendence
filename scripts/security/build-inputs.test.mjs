import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const inputs = JSON.parse(readFileSync(new URL("../../.github/component-inputs.json", import.meta.url)));
const affected = path => Object.keys(inputs).filter(component => inputs[component].some(pattern =>
  pattern.endsWith("/**") ? path.startsWith(pattern.slice(0, -2)) : path === pattern));

test("shared build inputs trigger their consuming images", () => {
  assert.deepEqual(affected("packages/web-routes/src/index.js"), ["web", "perf"]);
  assert.deepEqual(affected("packages/api-client/src/index.ts"), ["web"]);
  assert.deepEqual(affected("Directory.Build.props"), ["webapi", "service"]);
  assert.deepEqual(affected("scripts/ci/backend-runtime-smoke.sh"), ["webapi", "service"]);
  assert.deepEqual(affected(".npmrc"), ["web"]);
  assert.deepEqual(affected(".dockerignore"), ["webapi", "service", "web", "perf"]);
  assert.deepEqual(affected("docs/API.md"), []);
});

test("Docker contexts exclude environment secrets and preserve safe examples", () => {
  const rules = readFileSync(new URL("../../.dockerignore", import.meta.url), "utf8").split(/\r?\n/);
  assert.ok(rules.includes("**/.env.*"), "production/local environment files must stay out of image layers");
  assert.ok(rules.indexOf("!**/.env.example") > rules.indexOf("**/.env.*"));
});
