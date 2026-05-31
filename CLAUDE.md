# Transcendence

League of Legends and TFT analytics platform. Monorepo with .NET 10 backend and Next.js 16 frontend.

## Design Context

### Users
Transcendence serves both competitive climbers and casual players. Competitive players come for tier lists, matchups, and optimal builds — they need fast, trustworthy data. Casual players browse profiles, check post-game stats, and explore champions — they want it to feel approachable, not overwhelming. The interface uses progressive depth: surface-level answers are immediate, with drill-down available for those who want it. Users arrive with intent and want answers fast.

### Brand Personality
**Immersive, precise, confident.** Transcendence feels like a high-tech command center for gaming data — layered surfaces, subtle atmospheric depth, and a restrained-but-present sci-fi sensibility. It earns trust through craft and information density, not flash. The aesthetic is deliberate: immersive enough to feel premium, restrained enough to stay out of the data's way.

### Aesthetic Direction
- **Visual tone**: Dark-mode-only. Layered, translucent surfaces with subtle glass effects — restrained backdrop-blur and translucent backgrounds provide depth without competing with content. Glass is felt, not seen. Think high-end dev tool meets immersive data visualization.
- **Typography**: Space Grotesk for headings (geometric, technical, distinctive) + Plus Jakarta Sans for body (clean, readable at all sizes). Type hierarchy through size and weight. Tabular numerals for data columns.
- **Color palette**: Deep dark base (`222 30% 7%`). Neutral-cool gray surfaces (`220 24% 11%`, `218 20% 15%`). Off-white text (`210 20% 96%`). **Single accent color**: a warm gold (`42 82% 56%`) used for interactive elements, active states, S-tier, and primary actions, with a closely-related warm secondary (`18 65% 55%`) held in reserve and used sparingly. Tier colors and semantic colors (win/loss, success/danger) provide all additional variety. Muted text at `215 12% 55%`.
- **Surfaces**: Translucent, layered cards with subtle `backdrop-filter: blur` at low intensity. Depth comes from 1px borders at low opacity, gentle inset highlights (`inset 0 1px 0 hsl(0 0% 100% / 0.03)`), and linear gradient backgrounds from surface to slightly darker. No heavy outer glows. Box-shadows are tight and dark, not diffused color halos.
- **Aurora background**: A faint radial gradient wash on the body — barely perceptible, providing atmosphere without distraction. Keep opacity very low (0.04–0.06 range). Should be invisible until you look for it.
- **Animation**: Subtle and fast. Transitions at 150–180ms ease for interactive state changes. Opacity fades for loading. No shimmer loaders in data areas — use flat skeleton placeholders. No hover scale/lift transforms on cards. The only allowed keyframe animations are for overlay entry (command palette, modals). Motion should be invisible.
- **Interactive elements**: Buttons use solid accent backgrounds (primary) or transparent with underline borders (secondary). Hover states use subtle opacity/color shifts, not glow rings or brightness filters. No `rounded-full` pill buttons — use `rounded-lg` or contextual radii.
- **References**: Raycast (restrained glass, craft quality), Stripe Dashboard (data density done right), Arc Browser (layered translucent surfaces), Figma (quiet UI). Draw information density inspiration from op.gg/u.gg but with far higher visual polish and no clutter.
- **Anti-references**: Cyan+purple color schemes, gradient text, noise textures, heavy outer glow effects, frosted-glass-over-images, "hero metric" layouts with big glowing numbers, rounded-full pill button clusters, decorative shimmer on static elements. These are AI-generated fingerprints — avoid them.

### Design Principles

1. **Data density over decoration** — Tables, stats, and rankings are the product. Visual atmosphere enhances the experience but never competes with readability. If a decorative element obscures or distracts from data, remove it.
2. **Depth through layers, not effects** — Use translucent surfaces, subtle borders, and spacing to create hierarchy. Glass is a structural tool for organizing information, not a visual effect to admire. Every layer should serve a purpose.
3. **One accent, used with intent** — Gold highlights what matters: primary actions, active states, top-tier rankings. Everything else is grayscale. When gold appears, it should mean something. Semantic colors (green for wins, red for losses) are the only exceptions.
4. **Progressive disclosure** — Show the answer first, then offer depth. Tier lists show grades at a glance. Click to see stats. Click again for matchup details. Each layer adds information without requiring it.
5. **Earn every element** — If a component, badge, or decorative element doesn't help users find answers faster, delete it. No placeholder features, no redundant labels, no decorative borders that carry no information.
6. **Inclusive by default** — Good contrast ratios, keyboard navigability, readable type sizes. Respect `prefers-reduced-motion`. Accessibility is a baseline, not a feature.
