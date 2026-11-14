import { describe, expect, it } from "vitest";
import { validateProductionConfig } from "@/lib/productionConfig";

const valid = { NODE_ENV: "production", TRN_TRUSTED_PROXY_HEADERS: "true", TRN_PUBLIC_ORIGIN: "https://transcend.kronic.one",
  TRN_BACKEND_BASE_URL: "http://webapi:8080", TRN_BACKEND_API_KEY: "configured-runtime-key" };

describe("production configuration", () => {
  it("requires an explicit canonical origin and runtime backend settings", () => {
    expect(() => validateProductionConfig(valid)).not.toThrow();
    for (const key of ["TRN_PUBLIC_ORIGIN", "TRN_BACKEND_BASE_URL", "TRN_BACKEND_API_KEY", "TRN_TRUSTED_PROXY_HEADERS"]) {
      expect(() => validateProductionConfig({ ...valid, [key]: "" })).toThrow(key);
    }
    for (const origin of ["http://public.example", "https://example.com/path", "https://user:secret@example.com"]) {
      expect(() => validateProductionConfig({ ...valid, TRN_PUBLIC_ORIGIN: origin })).toThrow("canonical");
    }
  });
  it("allows local production smoke tests and defers runtime validation during the build", () => {
    expect(() => validateProductionConfig({ ...valid, TRN_PUBLIC_ORIGIN: "http://localhost:3000" })).not.toThrow();
    expect(() => validateProductionConfig({ NODE_ENV: "production", TRN_TRUSTED_PROXY_HEADERS: "true", NEXT_PHASE: "phase-production-build" })).not.toThrow();
    expect(() => validateProductionConfig({ NODE_ENV: "development" })).not.toThrow();
  });
});
