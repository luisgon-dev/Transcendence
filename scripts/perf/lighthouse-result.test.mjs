import { test } from "node:test";
import assert from "node:assert/strict";
import { validateLighthouseResult } from "./lighthouse-result.mjs";

const valid = () => ({
  categories: { performance: { score: 0 } },
  audits: {
    "first-contentful-paint": { numericValue: 1200 },
    "largest-contentful-paint": { numericValue: 2300 }
  }
});

test("NO_FCP is a failed sample even when Lighthouse returns an LHR", () => {
  assert.throws(() => validateLighthouseResult({
    finalDisplayedUrl: "about:blank",
    runtimeError: { code: "NO_FCP", message: "The page did not paint any content." }
  }, "https://example.test"), /NO_FCP/);
});

test("missing and nonfinite paint metrics cannot publish a fresh baseline", () => {
  for (const value of [undefined, null, NaN, Infinity, 0, -1]) {
    const lhr = valid();
    lhr.audits["largest-contentful-paint"].numericValue = value;
    assert.throws(() => validateLighthouseResult(lhr, "https://example.test"), /usable/);
  }
  assert.throws(() => validateLighthouseResult(undefined, "https://example.test"), /no result/);
  const lhr = valid();
  lhr.categories.performance.score = null;
  assert.throws(() => validateLighthouseResult(lhr, "https://example.test"), /score/);
});

test("a real paint with a zero performance score remains a valid bad measurement", () => {
  assert.doesNotThrow(() => validateLighthouseResult(valid(), "https://example.test"));
});
