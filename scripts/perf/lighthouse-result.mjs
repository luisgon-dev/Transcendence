/** Lighthouse can return an LHR with runtimeError instead of throwing. */
export function validateLighthouseResult(lhr, url) {
  if (!lhr) throw new Error(`Lighthouse returned no result for ${url}`);
  if (lhr.runtimeError) {
    throw new Error(`Lighthouse failed for ${url}: ${lhr.runtimeError.code}: ${lhr.runtimeError.message}`);
  }
  const score = lhr.categories?.performance?.score;
  if (typeof score !== "number" || !Number.isFinite(score)) {
    throw new Error(`Lighthouse returned no performance score for ${url}`);
  }
  for (const audit of ["first-contentful-paint", "largest-contentful-paint"]) {
    const value = lhr.audits?.[audit]?.numericValue;
    if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) {
      throw new Error(`Lighthouse returned no usable ${audit} for ${url}`);
    }
  }
}
