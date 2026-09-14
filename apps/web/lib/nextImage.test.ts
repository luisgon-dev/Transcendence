import { describe, expect, it } from "vitest";

import { fixedImageProps, nextImageUrl, nextImageWidth } from "./nextImage";

// These URLs are hand-built rather than produced by next/image, so the format
// and the allowed widths/qualities are a contract with next.config.mjs. A
// width outside `images.imageSizes`/`deviceSizes` or a quality outside
// `images.qualities` is a 400 from the optimizer, which would show up as a
// page full of broken champion squares.
describe("nextImage", () => {
  it("snaps to the smallest configured width that covers the target", () => {
    expect(nextImageWidth(30)).toBe(32);
    expect(nextImageWidth(32)).toBe(32);
    expect(nextImageWidth(60)).toBe(64);
    expect(nextImageWidth(64)).toBe(64);
  });

  it("clamps to the largest configured width rather than inventing one", () => {
    expect(nextImageWidth(100_000)).toBe(384);
  });

  it("builds an optimizer URL with an encoded source and the default quality", () => {
    expect(nextImageUrl("https://ddragon.leagueoflegends.com/cdn/16.18.1/img/champion/Ahri.png", 32)).toBe(
      "/_next/image?url=https%3A%2F%2Fddragon.leagueoflegends.com%2Fcdn%2F16.18.1%2Fimg%2Fchampion%2FAhri.png&w=32&q=75"
    );
  });

  it("emits the 1x/2x pair next/image would for a fixed size, with 2x as the fallback src", () => {
    const { src, srcSet } = fixedImageProps("https://example.test/a.png", 30);

    expect(srcSet).toBe(
      "/_next/image?url=https%3A%2F%2Fexample.test%2Fa.png&w=32&q=75 1x, " +
        "/_next/image?url=https%3A%2F%2Fexample.test%2Fa.png&w=64&q=75 2x"
    );
    expect(src).toBe("/_next/image?url=https%3A%2F%2Fexample.test%2Fa.png&w=64&q=75");
  });

  it("only ever asks for a quality next.config.mjs allows", () => {
    // next.config.mjs: images.qualities = [55, 75]
    expect(nextImageUrl("https://example.test/a.png", 32)).toContain("&q=75");
    expect(nextImageUrl("https://example.test/a.png", 32, 55)).toContain("&q=55");
  });
});
