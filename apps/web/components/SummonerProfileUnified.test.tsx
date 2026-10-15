import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";

import { SummonerProfileClient } from "./SummonerProfileUnified";

vi.mock("next/navigation", () => ({ usePathname: () => "/lol/summoners/na/Kronic-NA1" }));

describe("summoner response health markers", () => {
  it("exposes an initial backend error in the streamed HTML for the production probe", () => {
    const html = renderToStaticMarkup(<SummonerProfileClient region="na" gameName="Kronic" tagLine="NA1"
      initialLookup={null} initialError={{ message: "The backend timed out." }} />);
    expect(html).toContain('data-trn-backend-error="summoner-profile"');
  });

  it("does not label a legitimate missing lookup as a backend error", () => {
    const html = renderToStaticMarkup(<SummonerProfileClient region="na" gameName="Nobody" tagLine="NA1"
      initialLookup={{ status: "missing" }} initialError={null} />);
    expect(html).not.toContain("data-trn-backend-error");
  });
});
