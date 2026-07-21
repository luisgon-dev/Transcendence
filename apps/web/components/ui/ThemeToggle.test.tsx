import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";

import { ThemeToggle } from "./ThemeToggle";

describe("ThemeToggle", () => {
  it("renders a truthful pre-hydration control with both theme icons", () => {
    const html = renderToStaticMarkup(<ThemeToggle />);

    expect(html).toContain('aria-label="Toggle color theme"');
    expect(html).toContain('title="Toggle color theme"');
    expect(html.match(/<svg/g)).toHaveLength(2);
    expect(html).toContain("dark:block");
    expect(html).toContain("dark:hidden");
    expect(html).not.toContain("opacity-0");
  });
});
