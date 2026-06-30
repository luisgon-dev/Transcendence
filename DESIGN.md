---
name: Transcendence
description: A flat command-deck for League of Legends analytics. Data is the hero; red lights the controls.
colors:
  bg: "oklch(0.985 0.003 255)"
  surface: "oklch(0.997 0.002 255)"
  surface-2: "oklch(0.965 0.004 255)"
  border: "oklch(0.905 0.006 255)"
  border-strong: "oklch(0.82 0.009 255)"
  fg: "oklch(0.24 0.022 264)"
  muted: "oklch(0.45 0.02 264)"
  primary: "oklch(0.555 0.215 25)"
  primary-fg: "oklch(0.985 0.012 25)"
  primary-2: "oklch(0.62 0.2 36)"
  success: "oklch(0.50 0.13 158)"
  danger: "oklch(0.50 0.20 27)"
  warning: "oklch(0.55 0.13 70)"
  info: "oklch(0.50 0.13 245)"
  win: "oklch(0.50 0.13 158)"
  loss: "oklch(0.50 0.20 27)"
  wr-high: "oklch(0.50 0.13 158)"
  wr-low: "oklch(0.50 0.20 27)"
  tier-s: "oklch(0.66 0.14 76)"
  tier-a: "oklch(0.55 0.13 158)"
  tier-b: "oklch(0.55 0.12 235)"
  tier-c: "oklch(0.60 0.14 55)"
  tier-d: "oklch(0.55 0.015 264)"
typography:
  display:
    fontFamily: "Bricolage Grotesque, ui-sans-serif, sans-serif"
    fontSize: "clamp(2.1rem, 6vw, 4.25rem)"
    fontWeight: 700
    lineHeight: 0.96
    letterSpacing: "-0.04em"
  headline:
    fontFamily: "Bricolage Grotesque, ui-sans-serif, sans-serif"
    fontSize: "clamp(1.5rem, 3vw, 2.1rem)"
    fontWeight: 700
    lineHeight: 1.04
    letterSpacing: "-0.03em"
  title:
    fontFamily: "Bricolage Grotesque, ui-sans-serif, sans-serif"
    fontSize: "clamp(1.2rem, 1.8vw, 1.6rem)"
    fontWeight: 700
    lineHeight: 1.12
    letterSpacing: "-0.025em"
  body:
    fontFamily: "Hanken Grotesk, ui-sans-serif, system-ui, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.6
    letterSpacing: "-0.006em"
  label:
    fontFamily: "Hanken Grotesk, ui-sans-serif, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 600
    lineHeight: 1.3
    letterSpacing: "0.06em"
rounded:
  control: "0.625rem"
  card: "0.875rem"
  panel: "1.125rem"
  hero: "1.375rem"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.primary-fg}"
    rounded: "{rounded.control}"
    padding: "0 1rem"
    height: "2.75rem"
  button-outline:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.fg}"
    rounded: "{rounded.control}"
    padding: "0 1rem"
    height: "2.75rem"
  button-ghost:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.fg}"
    rounded: "{rounded.control}"
    padding: "0 1rem"
    height: "2.75rem"
  card:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.fg}"
    rounded: "{rounded.card}"
    padding: "1rem"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.fg}"
    rounded: "{rounded.control}"
    padding: "0 1rem"
    height: "3rem"
---

# Design System: Transcendence

## 1. Overview

**Creative North Star: "The Command Deck"**

Transcendence is a flat, lit instrument panel for League of Legends data. Every surface is a readout. The interface reads like a high-tech command deck: layered solid surfaces, hairline borders, and a barely-perceptible atmospheric wash, with a single red accent that lights only the controls a player can act on. It earns trust through craft and information density, not flash. Immersive enough to feel premium, restrained enough to stay out of the data's way.

The system is built for competitive climbers first. They make in-game decisions on these numbers, so the design's job is to make data fast to read and honest about its own certainty. Color is not decoration here: it is a data channel. Win rates render as diverging bars centered on 50%, tiers carry a categorical S→D scale, ranks carry an iron→challenger scale, and a confidence meter signals sample strength. When something wears the vivid action red, it is a control you can act on; when something is green or a muted red, it is a measured outcome (a win, a loss, a low win rate). The two reds are kept deliberately apart (see the Named Rules). Nothing is colored to look nice.

The aesthetic is deliberately the opposite of the "gamer dashboard" reflex. It rejects cyan-and-purple neon, gradient text, glow as a substitute for hierarchy, noise textures, hero-metric layouts, and glassmorphism-as-decoration. Depth comes from tonal layering and 1px borders, not from blur and shadow theatrics. Both light and dark themes are first-class (system-aware with a header toggle and a FOUC-safe head script); neither is the "default."

**Key Characteristics:**
- Flat, solid surfaces with 1px borders and restrained shadow; never glass-by-default.
- Action red (`oklch(0.555 0.215 25)` light / `oklch(0.63 0.21 25)` dark) reserved exclusively for actions, active state, and focus, with near-white text on its solid fills, kept visibly distinct from the muted data red.
- Color encodes data: diverging win-rate, tier categorical, rank categorical, win/loss semantic, confidence.
- Bricolage Grotesque display + Hanken Grotesk body; JetBrains Mono only for real codes/IDs.
- oklch throughout, theme-flipping via `--t-*` variables mapped through Tailwind v4 `@theme inline`.
- Tables lead. Compact sticky toolbars replace tall heroes.

## 2. Colors

A cool-slate neutral spine, one vivid red action accent, and a set of purpose-built data scales. Light and dark are defined as parallel token sets that flip via a `.dark` class.

### Primary
- **Signal Red** (`oklch(0.555 0.215 25)` light / `oklch(0.63 0.21 25)` dark): The single action accent. Primary buttons, active tab/filter state, focus rings. It is a vivid scarlet-crimson, intentionally more saturated and a touch lighter than the muted data red (`danger`/`loss`/`wr-low`, `oklch(0.50 0.20 27)` light), and it appears only as interactive chrome (solid fills with near-white text, active underlines, focus rings), never as a measured value. `primary-fg` is near-white (`oklch(0.985 0.012 25)` light / `oklch(0.99 0.008 25)` dark) because the red is dark enough to need light text, where the old gold needed dark text. A warmer companion, **Ember** (`primary-2`, `oklch(0.62 0.2 36)`), appears only in tiny accent washes (auth/hero radial tints). The brand marks (favicon/icon SVGs and regenerated raster icons) carry the same accent as a `#f24a3d → #c11436` gradient. The action red is never decorative.

### Secondary
- **Win Green** (`oklch(0.50 0.13 158)` light / `oklch(0.72 0.15 158)` dark): The positive pole of every win-rate bar, the "win" match outcome, the `success` semantic, and tier-A.
- **Loss Red** (`oklch(0.50 0.20 27)` light / `oklch(0.66 0.18 22)` dark): The negative pole of every win-rate bar, the "loss" match outcome, and the `danger` semantic. Tuned to pass AA as text on light surfaces.

### Tertiary
- **Info Blue** (`oklch(0.50 0.13 245)`): Ops/admin notes, the second atmospheric wash, tier-B. Utilitarian, never a brand color.
- **Tier & Rank scales**: Tier categorical (S gold, A green, B blue, C amber, D slate) and rank categorical (iron→challenger, 10 steps) are full named scales used as small badges, dots, and the Tier Spine. They are data legends, not palette expansion. Note gold now lives here only: it encodes tier-S (`oklch(0.66 0.14 76)`), gold rank, and challenger, and is no longer the action accent.

### Neutral
- **Deck White / Ink** (`bg` `oklch(0.985 0.003 255)`, `surface` `oklch(0.997 0.002 255)`, `surface-2` `oklch(0.965 0.004 255)`; `fg` `oklch(0.24 0.022 264)`): Off-white surfaces, slate-ink text. Every neutral is tinted toward 255–264 hue; never pure `#fff`/`#000`.
- **Slate Dark** (dark theme `bg` `oklch(0.17 0.015 264)` → `surface-2` `oklch(0.255 0.018 264)`): Cool slate, layered light-to-dark for depth.
- **Borders** (`border` `oklch(0.905 0.006 255)`, `border-strong` `oklch(0.82 0.009 255)`): Hairlines do the structural work that shadow doesn't.

### Named Rules
**The Red-Is-A-Verb Rule.** The action red means "you can act here": primary action, active state, focus. It is the vivid, saturated scarlet, and it is deliberately separated from the muted data red used for loss, danger, and the low pole of win-rate bars (more chroma, slightly lighter, only ever on interactive chrome with near-white text). If an element wears the action red but does nothing, it is wrong; repaint it neutral. If it encodes a measured-bad value, use the muted data red, not the action red.

**The Color-Is-Data Rule.** Outside of the action red, every saturated color must encode a value: win/loss, win-rate magnitude, tier, rank, or confidence. Gold is now one such data color (tier-S, gold rank, challenger), not a brand accent. There is no "brand teal," no decorative accent. If you cannot say what a color measures, it does not belong.

## 3. Typography

**Display Font:** Bricolage Grotesque (with `ui-sans-serif, sans-serif`)
**Body Font:** Hanken Grotesk (with `ui-sans-serif, system-ui, sans-serif`)
**Label/Mono Font:** JetBrains Mono (with `ui-monospace, "SF Mono", monospace`)

**Character:** Bricolage is characterful and editorial-technical, carrying display and headings with tight negative tracking. Hanken is clean at small sizes with true tabular figures, ideal for dense tables. The pairing reads precise and authored, not templated. Hierarchy comes from size and weight, never from color.

### Hierarchy
- **Display** (700, `clamp(2.1rem, 6vw, 4.25rem)`, line-height 0.96, `-0.04em`): `.type-display`. Largest marketing/landing statements only.
- **Headline** (700, `clamp(1.5rem, 3vw, 2.1rem)`, 1.04, `-0.03em`): `.type-page-title` / `.type-panel-title`. Page and panel titles.
- **Title** (700, `clamp(1.2rem, 1.8vw, 1.6rem)`, 1.12, `-0.025em`): `.type-title`. Card and section headers.
- **Body** (400, `1rem`, 1.6, `-0.006em`): Default UI/prose. Paragraphs capped at 68ch (`max-width: 68ch` on `p, li`).
- **Label** (600, `0.75rem`, `0.06em`, UPPERCASE): `.type-meta` / `.field-label`. Overlines (`.type-kicker`) push to `0.18em`. The system's "technical" voice.

### Named Rules
**The Mono-Is-For-Machines Rule.** JetBrains Mono is forbidden as decorative "technical flavor." It appears only for real machine codes and IDs (admin job IDs, key prefixes). Tabular numerics in tables use `.type-tabular` (Hanken `tnum`), not mono.

**The Tabular-Figures Rule.** Any column of numbers that a user scans vertically (win rates, KDA, game counts) uses tabular figures so digits align. Never render scannable stats in proportional figures.

## 4. Elevation

Flat by default, state-driven. Surfaces sit flush at rest, separated by `surface`/`surface-2` tonal steps and 1px borders. Shadow and backdrop-blur are responses to state, not resting decoration: a card lifts 1px and gains `shadow-card` on hover, the tier-list head blurs only because it is sticky over scrolling content, the command palette and overlays float because they genuinely float. There is no ambient glow.

### Shadow Vocabulary
- **Soft** (`--shadow-soft`, `0 1px 2px / 0 2px 8px` slate-tinted): Resting lift for toolbars and panels. Barely there.
- **Card** (`--shadow-card`, `0 1px 2px / 0 6px 18px`): Hover/active state for cards and match rows; default for true cards.
- **Overlay** (`--shadow-overlay`, `0 12px 48px`): Command palette, dialogs, anything genuinely floating.
- **Inset hairline** (`--shadow-inset`): A 1px top highlight that gives flat surfaces a crisp edge.

### Named Rules
**The Flat-At-Rest Rule.** Surfaces are flat at rest. If you reach for a shadow or `backdrop-filter` on a static element, ask whether it actually floats or changes state. If not, delete it and let the border and tonal step carry the layer.

**The Blur-Is-Earned Rule.** `backdrop-filter: blur()` is reserved for layers that float over moving content: sticky table heads, the command palette, overlays. Decorative frosted glass over images or cards is prohibited.

## 5. Components

For each component, lead with character, then shape, color, and states.

### Buttons
Confident and quiet. Solid red primary with near-white text, restrained neutral secondaries.
- **Shape:** `rounded-control` (`0.625rem`); min-height `2.75rem` (`md`) / `2.25rem` (`sm`).
- **Primary:** `bg-primary text-primary-fg` + `shadow-soft`; hover `bg-primary/92`, active `bg-primary/85` plus a 1px downward nudge.
- **Outline:** `border-border bg-surface text-fg/92`; hover lifts to `border-border-strong` + `surface-2/60`.
- **Ghost:** transparent; hover `surface-2/60`. For tertiary, in-table actions.
- **Focus:** `ring-2 ring-primary/40 ring-offset-2 ring-offset-bg` on every variant. Motion-reduce disables transitions.

### Chips / Filters
Filters are not pill clusters. The primary affordances are the `SegmentedControl` and `Select` primitives (Radix-backed, keyboard-accessible). Where filter pills exist (`.tierlist-filter-pill`), active state is a faint red wash + red-tinted border (`primary/88` bg, `primary/55` border), never a solid red fill.

### Cards / Containers
- **Corner Style:** `rounded-card` (`0.875rem`); panels `rounded-panel` (`1.125rem`), heroes `rounded-hero` (`1.375rem`).
- **Background:** Solid `surface`; nested/secondary content steps to `surface-2`.
- **Shadow Strategy:** `shadow-card` (see Elevation); flat at rest, lifts on hover.
- **Border:** Always a 1px `border`; hover deepens to `border-strong`.
- **Internal Padding:** `1rem` base; vary for rhythm. Never nest a card inside a card.

### Inputs / Fields
- **Style:** `h-12`, `rounded-control`, 1px `border`, solid `surface` background, `muted` placeholder.
- **Focus:** Border shifts to `primary/60` plus `ring-2 ring-primary/30 ring-offset-2`. No glow.
- **Error:** `.field-error`: danger-tinted border + `danger/88` background wash, danger text. Disabled drops to 60% opacity.

### Navigation
Tabs (`.control-tab`) are underline-driven: a 1px bottom hairline at rest, deepening on hover, becoming a 2px red underline + red text when active. No filled tab backgrounds. The header carries the theme toggle and command-palette trigger.

### Signature Components
- **Tier Spine** (`TierSpine.tsx`): A continuous S→D tier-colored vertical rail the data hangs off. Each segment is a jump target into its tier section. The defining structural element of the tier list.
- **DataBar** (`DataBar.tsx`): The core data language. Every win rate is a diverging bar centered on 50%, filling right in win-green above 50 and left in loss-red below, magnitude scaled over a ±8pp window. Optionally draws a faint 95% CI whisker when sample size is passed, so thin samples read as less certain.
- **Confidence** (`Confidence.tsx`): A 3-pip signal-strength meter (staggered heights) answering "how much should I trust this?" Neutral `fg/70` pips for high/moderate samples; a single `warning` pip for genuinely thin samples. Confidence is data, never decoration.

## 6. Do's and Don'ts

### Do:
- **Do** reserve the action red (`oklch(0.555 0.215 25)`) for actions, active state, and focus rings, and nothing else; keep it more saturated than the muted data red so the two never blur. Top-tier (S) is gold, a data color, not this accent.
- **Do** render every win rate as a `DataBar` diverging from 50%, and pass `games` so thin samples show a CI whisker.
- **Do** keep surfaces flat at rest; introduce shadow or `backdrop-filter` only as a response to state (hover, sticky, overlay, focus).
- **Do** lead pages with a compact sticky `Toolbar`, answer-first, op.gg/u.gg-style. Tables are the product.
- **Do** surface sample size and confidence next to any stat that could be small-n noise.
- **Do** use Bricolage for display/headings and Hanken for body/UI; reserve JetBrains Mono for real codes and IDs only.
- **Do** pair every data color with a non-hue cue (position, value, label, shape) so encodings survive color blindness, and honor `prefers-reduced-motion` on the one staggered reveal.

### Don't:
- **Don't** use cyan-and-purple "gamer" schemes or neon-on-black. This is the first AI reflex; reject it.
- **Don't** use gradient text or `background-clip: text`. Emphasis comes from weight and size.
- **Don't** use glassmorphism, frosted glass over images, or outer glow as decoration. Blur is earned by genuinely floating layers only.
- **Don't** build hero-metric layouts (big number, small label, supporting stats, gradient accent). Tables lead.
- **Don't** cluster rounded-full pills as a primary control pattern; use `SegmentedControl` / `Select`.
- **Don't** add noise textures, decorative shimmer, or skeleton shimmer; skeletons are flat.
- **Don't** use a `border-left`/`border-right` greater than 1px as a colored accent stripe on cards, rows, or callouts.
- **Don't** color anything that isn't an action or a measured value. If you can't say what a color measures, remove it.
- **Don't** animate layout properties or use bounce/elastic easing; transform/opacity only, ease-out-quart/expo, 140–160ms.
