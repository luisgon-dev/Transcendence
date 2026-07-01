# Transcendence

League of Legends analytics platform. Monorepo with .NET 10 backend and Next.js 16 frontend.

## Design Context

### Users
Transcendence serves both competitive climbers and casual players. Competitive players come for tier lists, matchups, and optimal builds — they need fast, trustworthy data. Casual players browse profiles, check post-game stats, and explore champions — they want it to feel approachable, not overwhelming. The interface uses progressive depth: surface-level answers are immediate, with drill-down available for those who want it. Users arrive with intent and want answers fast.

### Brand Personality
**Immersive, precise, confident.** Transcendence feels like a high-tech command center for gaming data — layered surfaces, subtle atmospheric depth, and a restrained-but-present sci-fi sensibility. It earns trust through craft and information density, not flash. The aesthetic is deliberate: immersive enough to feel premium, restrained enough to stay out of the data's way.

### Aesthetic Direction — "Ladder"

> The system was refreshed (2026-06) to the **"Ladder"** direction: data is the hero,
> color *encodes* data, a vivid red is reserved for actions (gold was retired as the
> accent and now only encodes tier-S/gold-rank data). Tokens are oklch in Tailwind v4
> (`apps/web/app/globals.css`, `@theme inline`); themes flip via a `.dark` class. The
> token + primitive layer is authoritative app-wide; some pages are still being migrated
> to the compact-toolbar structure — see [[frontend-refresh-ladder]] memory.

- **Visual tone**: Light **and** dark (system-aware + a header toggle; FOUC-safe head script). A precise, data-forward "command deck" — **flat, solid surfaces** with 1px borders and restrained shadow, *not* glass. Backdrop-blur is reserved for genuinely floating layers (sticky table head, command palette), never decorative.
- **Typography**: **Bricolage Grotesque** for display/headings (characterful, editorial-technical) + **Hanken Grotesk** for body/UI (clean at small sizes, true tabular figures). JetBrains Mono **only** for real codes/IDs (admin job IDs, key prefixes) — never as decorative "technical" flavor. Hierarchy through size and weight.
- **Color palette**: oklch tokens, theme-flipping via `--t-*` vars. Cool-slate neutrals; off-white/ink text per theme. **Action red reserved for actions only** (`--t-primary`, a vivid scarlet-crimson, ~oklch hue 25) — primary buttons (solid fill, near-white text), active state, focus rings. Nothing decorative wears it, and it is kept more saturated than the muted data red (`danger`/`loss`/`wr-low`) so "act here" never reads as "a bad outcome". **Color encodes data**: a diverging win-rate scale (red↔neutral↔green, centered 50%), tier categorical (S/A/B/C/D, S is gold), rank categorical (iron→challenger), win/loss semantic. Gold is now a data color only (tier-S, gold rank, challenger), no longer the accent.
- **Signature elements**: (1) **Tier Spine** — a continuous tier-colored vertical rail the data hangs off (`components/ui/TierSpine.tsx`); (2) **Diverging win-rate bars** — every win rate renders as a `DataBar` centered on 50%; (3) **Tables lead** — compact sticky `Toolbar` headers replace tall heroes (answer-first, op.gg/u.gg-style).
- **Surfaces**: Solid `surface`/`surface-2` with a 1px `border` and a tight `shadow-card`/`shadow-soft`. Depth through layering and spacing, not gradients or glow. Tighter radii than before (control `.625rem` → hero `1.375rem`).
- **Aurora background**: A barely-perceptible radial wash on the body, theme-aware. Invisible until you look for it.
- **Animation**: Subtle and fast (140–160ms). Flat skeleton placeholders (no shimmer). One well-orchestrated staggered reveal on data load is permitted (`framer-motion`), fully `prefers-reduced-motion`-gated. Transform/opacity only — never animate layout props.
- **Interactive elements**: Solid red primary buttons (`bg-primary` + near-white `text-primary-fg`), outline/ghost secondaries. Filters use the `SegmentedControl`/`Select` primitives (Radix-backed) for keyboard a11y. `rounded-control`/`rounded-lg`, not pill clusters.
- **References**: Linear, Raycast (craft, restraint), Stripe Dashboard (data density), Vercel/Geist (flat precise surfaces). Information density from op.gg/u.gg with far higher polish.
- **Anti-references**: Cyan+purple schemes, gradient text, noise textures, heavy outer glow, frosted-glass-over-images, "hero metric" layouts, rounded-full pill clusters, decorative shimmer, **glassmorphism as decoration**, dark-mode-with-glow as a substitute for design decisions. These are AI fingerprints — avoid them.

### Design Principles

1. **Data density over decoration** — Tables, stats, and rankings are the product. Visual atmosphere enhances the experience but never competes with readability. If a decorative element obscures or distracts from data, remove it.
2. **Depth through layers, not effects** — Use translucent surfaces, subtle borders, and spacing to create hierarchy. Glass is a structural tool for organizing information, not a visual effect to admire. Every layer should serve a purpose.
3. **One accent, used with intent** — A vivid action red highlights what you can act on: primary actions, active states, focus. Everything else is grayscale. When the action red appears, it should mean "act here"; it stays more saturated than the muted data red (loss/danger) so the two never blur. Semantic colors (green for wins, the muted red for losses, gold for tier-S) encode data and are the only exceptions.
4. **Progressive disclosure** — Show the answer first, then offer depth. Tier lists show grades at a glance. Click to see stats. Click again for matchup details. Each layer adds information without requiring it.
5. **Earn every element** — If a component, badge, or decorative element doesn't help users find answers faster, delete it. No placeholder features, no redundant labels, no decorative borders that carry no information.
6. **Inclusive by default** — Good contrast ratios, keyboard navigability, readable type sizes. Respect `prefers-reduced-motion`. Accessibility is a baseline, not a feature.
