module.exports = {
  ci: {
    collect: {
      startServerCommand: "pnpm --filter web start",
      startServerReadyPattern: "Ready in",
      startServerReadyTimeout: 60_000,
      url: [
        "http://127.0.0.1:3000/",
        "http://127.0.0.1:3000/account/login",
        "http://127.0.0.1:3000/terms"
      ],
      numberOfRuns: 3,
      settings: {
        formFactor: "mobile",
        screenEmulation: {
          mobile: true,
          width: 412,
          height: 823,
          deviceScaleFactor: 1.75,
          disabled: false
        },
        throttlingMethod: "simulate",
        chromeFlags: "--headless=new --no-sandbox --disable-dev-shm-usage"
      }
    },
    assert: {
      assertions: {
        "categories:performance": ["error", { minScore: 0.8, aggregationMethod: "median" }],
        "largest-contentful-paint": [
          "error",
          // Simulated mobile LCP is intentionally paired with the stricter 2.5 s field-data alert.
          // This lab ceiling still rejects the pre-optimization 4.8 s result without making CI flaky.
          { maxNumericValue: 4000, aggregationMethod: "median" }
        ],
        "cumulative-layout-shift": [
          "error",
          { maxNumericValue: 0.1, aggregationMethod: "median" }
        ],
        "total-blocking-time": [
          "error",
          { maxNumericValue: 350, aggregationMethod: "median" }
        ],
        interactive: ["error", { maxNumericValue: 5000, aggregationMethod: "median" }],
        "total-byte-weight": [
          "error",
          { maxNumericValue: 1_600_000, aggregationMethod: "median" }
        ],
        "bootup-time": ["warn", { maxNumericValue: 1500, aggregationMethod: "median" }],
        "unused-javascript": ["warn", { maxLength: 0, aggregationMethod: "median" }]
      }
    },
    upload: {
      target: "filesystem",
      outputDir: ".lighthouseci/reports",
      reportFilenamePattern: "%%PATHNAME%%-%%DATETIME%%-report.%%EXTENSION%%"
    }
  }
};
