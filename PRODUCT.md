# Product

## Register

product

## Users

**Primary: competitive climbers.** Players grinding ranked who come for tier lists, matchups, and optimal builds. They need data that is fast, fresh, well-sampled, and statistically honest, because they make in-game decisions on it. They arrive with intent and a specific question ("is this matchup winnable", "what's the optimal build into this matchup") and want the answer immediately, with drill-down available when they want to verify or go deeper.

**Secondary: casual players.** They browse profiles, check post-game stats, and explore champions and builds. The surface must stay approachable for them, never overwhelming, but their needs do not set the center of gravity. When a tradeoff pits casual approachability against competitive trust or density, competitive wins.

The interface uses progressive depth: a surface-level answer is immediate, with layered drill-down for those who want it.

## Product Purpose

Transcendence is a League of Legends analytics platform. It turns a large, continuously ingested match corpus into trustworthy, fast answers about champions, matchups, builds, and tier lists.

**Success = trust through data quality.** The product wins when competitive players believe the numbers: that samples are large enough to matter, that data reflects the current patch, that win rates and tiers are computed honestly (empirical-Bayes shrinkage, sample gating, an explicit confidence layer rather than raw small-n noise). Speed and breadth serve that trust; they do not substitute for it. A fast wrong answer is a failure.

## Brand Personality

**Immersive, precise, confident.** Transcendence reads like a high-tech command deck for gaming data: layered surfaces, subtle atmospheric depth, a restrained-but-present sci-fi sensibility. It earns trust through craft and information density, not flash. Immersive enough to feel premium, restrained enough to stay out of the data's way. The voice is expert and direct: it states what the data shows, flags when it is uncertain, and never pads.

## Anti-references

The interface must never read as generic AI output or a glowing gamer dashboard. Hard avoids:

- Cyan + purple "gamer" schemes; neon-on-black.
- Gradient text and `background-clip: text` decoration.
- Noise textures and decorative grain.
- Hero-metric layouts (big number, small label, supporting stats).
- Rounded-full pill clusters as a primary control pattern.
- Decorative shimmer and skeleton shimmer.
- Side-stripe accent borders on cards, rows, or callouts.
- Dark-mode-with-glow used as a substitute for actual design decisions.

**Loosened (revisit if the direction warrants):** glassmorphism and outer glow are not blanket-banned. They remain off by default and decorative use is still wrong, but a genuinely floating layer or a purposeful, restrained application can be considered rather than reflex-rejected.

## Design Principles

1. **Data is the hero.** Tables, stats, and rankings are the product. Atmosphere may enhance but never competes with readability. If a decorative element obscures or distracts from data, it goes.
2. **Trust is earned in the numbers.** Surface sample size, recency, and confidence. Never present small-n noise as fact. Honest uncertainty beats false precision; this is the competitive user's whole reason to be here.
3. **One accent, used with intent.** A vivid action red marks what you can act on: primary actions, active states, focus. It is deliberately more saturated than the muted data red (loss, danger, low win rate) and appears only as interactive chrome, never as a measured value, so the two reds never blur. Everything else is grayscale. Semantic color (win/loss, diverging win-rate scales, tier/rank categoricals, including gold for tier-S and gold rank) encodes data, and is the only other licensed color.
4. **Progressive disclosure.** Show the answer first, then offer depth. Grade at a glance, click for stats, click again for matchup detail. Each layer adds information without requiring it.
5. **Earn every element.** If a component, badge, or border does not help a user reach an answer faster, delete it. No placeholder features, no redundant labels.
6. **Inclusive by default.** Good contrast, keyboard navigability, readable type, respected reduced-motion. A baseline, not a feature.

## Accessibility & Inclusion

Target the documented baseline (WCAG AA-ish in practice):

- Sufficient contrast in both light and dark themes, including text over the diverging win-rate and tier/rank color scales.
- Full keyboard navigability; filters and controls built on accessible primitives (Radix-backed `SegmentedControl` / `Select`).
- `prefers-reduced-motion` honored everywhere; the one permitted staggered data-load reveal is fully gated.
- Color-blind-safe data encodings: never rely on hue alone to distinguish win/loss or tier; pair with position, value, label, or shape.
