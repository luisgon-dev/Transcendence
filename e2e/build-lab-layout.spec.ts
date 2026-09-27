import { expect, test, type Page } from "@playwright/test";

// Layout guard for Build Lab. The page once scrolled 467px sideways on a phone and wrapped item
// names under the win-rate bars on a tablet, and nothing failed. This renders the page at phone,
// tablet, laptop and desktop widths and fails on horizontal overflow or on any cell whose content
// spills into its neighbour.
//
// The page's own data request is answered with a dense fixture (long names, full rune pages, a
// recommended build, a stage long enough to collapse), so the check does not depend on what the
// backend under test has counted. Server-rendered static data (names, icons) is real.

type Option = Record<string, unknown>;

function option(actionIds: number[], games: number, overrides: Partial<Option> = {}): Option {
  const lowSample = games < 100;
  return {
    actionKey: actionIds.join("+"),
    actionIds,
    games,
    pickRate: Math.min(0.99, games / 20_000),
    winRate: 0.517,
    adjustedWinRate: 0.512,
    lift: lowSample ? 0.0004 : 0.012,
    confidenceLow: 0.49,
    confidenceHigh: 0.534,
    averageTimingMinutes: 13.4,
    isLowSample: lowSample,
    ...overrides
  };
}

function stage(family: string, stageNumber: number, label: string, options: Option[]) {
  return { family, stage: stageNumber, label, games: 14_400, winRate: 0.513, scope: "ALL", isFallback: false, options };
}

const LEGENDARIES = [3089, 4645, 3157, 6655, 3118, 2503, 3135, 3165, 4629, 3102, 6653];
const RUNE_PAGE = [8112, 8139, 8138, 8135, 8226, 8210, 5008, 5008, 5001];

const SECTIONS: Record<string, unknown[]> = {
  items: [
    stage("STARTER", 0, "Starting items", [option([1056, 2003, 2003], 14_000, { averageTimingMinutes: null }), option([1082, 2003], 40)]),
    stage("ITEM", 1, "First item", LEGENDARIES.map((id, index) => option([id], index < 4 ? 4_000 - index * 700 : 60 - index))),
    stage("BOOTS", 1, "Boots", [option([3020], 9_000), option([3158], 3_000), option([3111], 20)])
  ],
  runes: [
    stage("RUNE_PAGE", 0, "Complete rune page", [
      option(RUNE_PAGE, 6_000, { averageTimingMinutes: null }),
      option([8112, 8139, 8138, 8135, 8226, 8237, 5008, 5008, 5001], 400, { averageTimingMinutes: null }),
      option([8128, 8126, 8138, 8135, 8210, 8226, 5008, 5008, 5011], 30, { averageTimingMinutes: null })
    ]),
    stage("RUNE", 1, "Keystone", [option([8112], 12_000, { averageTimingMinutes: null }), option([9923], 1_500, { averageTimingMinutes: null })])
  ],
  skills: [
    stage("SKILLS", 0, "Skill priority", [option([1, 3, 2], 12_000, { averageTimingMinutes: null }), option([1, 2, 3], 900, { averageTimingMinutes: null })]),
    stage("SKILLS", 1, "First three levels", [option([1, 3, 2], 8_000, { averageTimingMinutes: null })])
  ]
};

function response(section: string) {
  return {
    available: true,
    context: { championId: 103, role: "MIDDLE", opponentChampionId: null, requestedPatch: null, requestedRegion: "ALL", section: section.toUpperCase(), mode: "SUPPORTED" },
    coverage: {
      includedPatches: ["16.19", "16.18", "16.17"],
      patchWeights: [1, 0.6, 0.35],
      countedMatches: 311_000,
      lastCountedAtUtc: "2026-09-26T20:00:00Z",
      includedRegions: ["EUW1", "KR", "NA1"],
      rankScope: "ALL_TRACKED"
    },
    selectedPath: [],
    stages: SECTIONS[section],
    unavailableReason: null,
    summary: {
      starter: option([1056, 2003, 2003], 14_000),
      items: [option([6655], 4_000), option([3089], 2_500), option([3157], 1_800)],
      boots: option([3020], 9_000),
      runePage: option(RUNE_PAGE, 6_000),
      spellPair: option([4, 14], 7_000),
      skillPriority: option([1, 3, 2], 12_000)
    }
  };
}

async function layoutProblems(page: Page) {
  return page.evaluate(() => {
    const problems: string[] = [];
    const overflow = document.documentElement.scrollWidth - window.innerWidth;
    if (overflow > 1) problems.push(`page scrolls ${overflow}px sideways`);
    for (const row of document.querySelectorAll('[role="rowgroup"] [role="row"]')) {
      const cells = [...row.querySelectorAll<HTMLElement>('[role="cell"]')].filter((cell) => cell.offsetParent !== null);
      const label = (cells[0]?.textContent ?? "").trim().slice(0, 40);
      // The results panel clips (overflow: hidden), so a row too wide for it is cut off rather than
      // scrolling the page -- the page-level check alone cannot see that.
      const bounds = (row.parentElement ?? row).getBoundingClientRect();
      for (const cell of cells) {
        const box = cell.getBoundingClientRect();
        if (box.right > bounds.right + 1 || box.left < bounds.left - 1) {
          problems.push(`"${label}": a cell sits outside its row's container by ${Math.round(Math.max(box.right - bounds.right, bounds.left - box.left))}px`);
        }
      }
      for (const cell of cells) {
        if (cell.scrollWidth > cell.clientWidth + 1) problems.push(`"${label}": a cell's content spills by ${cell.scrollWidth - cell.clientWidth}px`);
      }
      for (let index = 0; index + 1 < cells.length; index++) {
        const a = cells[index].getBoundingClientRect();
        const b = cells[index + 1].getBoundingClientRect();
        const sameLine = a.top < b.bottom && b.top < a.bottom;
        if (sameLine && a.right > b.left + 1) problems.push(`"${label}": cells ${index} and ${index + 1} overlap by ${Math.round(a.right - b.left)}px`);
      }
    }
    return problems;
  });
}

for (const [width, height] of [[390, 844], [768, 1024], [1024, 768], [1440, 900]] as const) {
  for (const section of Object.keys(SECTIONS)) {
    test(`Build Lab ${section} lays out without overflow at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height });
      await page.route("**/api/trn/public/lol/analytics/build-lab/**", (route) =>
        route.fulfill({ contentType: "application/json", body: JSON.stringify(response(section)) })
      );

      await page.goto(`/lol/builds/103?role=MIDDLE&section=${section}`);
      // next dev's floating indicator can sit over the expander; production builds have none.
      await page.addStyleTag({ content: "nextjs-portal { display: none !important; }" });
      // The server renders whatever the backend under test has counted before the page's own
      // request returns the fixture; wait for the fixture itself (its coverage line) so the checks
      // never run against the first render and race its replacement.
      await expect(page.getByText(/311K games/)).toBeVisible();
      await expect(page.getByRole("heading", { name: "Recommended build" })).toBeVisible();
      await expect(page.getByRole("row").nth(1)).toBeVisible();

      expect(await layoutProblems(page)).toEqual([]);

      // The collapsed rows are part of the layout too.
      const expander = page.getByRole("button", { name: /^Show \d+ more/ }).first();
      if (await expander.isVisible()) {
        await expander.click();
        expect(await layoutProblems(page)).toEqual([]);
      }
    });
  }
}
