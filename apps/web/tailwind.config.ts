import type { Config } from "tailwindcss";

export default {
  content: ["./app/**/*.{ts,tsx}", "./components/**/*.{ts,tsx}"],
  theme: {
    extend: {
      borderRadius: {
        control: "var(--radius-control)",
        card: "var(--radius-card)",
        panel: "var(--radius-panel)",
        hero: "var(--radius-hero)"
      },
      colors: {
        bg: "hsl(var(--bg))",
        surface: "hsl(var(--surface))",
        "surface-2": "hsl(var(--surface-2))",
        border: "hsl(var(--border))",
        "border-strong": "hsl(var(--border-strong))",
        fg: "hsl(var(--fg))",
        muted: "hsl(var(--muted))",
        primary: "hsl(var(--primary))",
        "primary-2": "hsl(var(--primary-2))",
        success: "hsl(var(--success))",
        danger: "hsl(var(--danger))",
        warning: "hsl(var(--warning))",
        "tier-s": "hsl(var(--tier-s))",
        "tier-a": "hsl(var(--tier-a))",
        "tier-b": "hsl(var(--tier-b))",
        "tier-c": "hsl(var(--tier-c))",
        "tier-d": "hsl(var(--tier-d))",
        "wr-high": "hsl(var(--wr-high))",
        "wr-low": "hsl(var(--wr-low))",
        win: "hsl(var(--win))",
        loss: "hsl(var(--loss))",
        info: "hsl(var(--info))",
        "rank-iron": "hsl(var(--rank-iron))",
        "rank-bronze": "hsl(var(--rank-bronze))",
        "rank-silver": "hsl(var(--rank-silver))",
        "rank-gold": "hsl(var(--rank-gold))",
        "rank-platinum": "hsl(var(--rank-platinum))",
        "rank-emerald": "hsl(var(--rank-emerald))",
        "rank-diamond": "hsl(var(--rank-diamond))",
        "rank-master": "hsl(var(--rank-master))",
        "rank-grandmaster": "hsl(var(--rank-grandmaster))",
        "rank-challenger": "hsl(var(--rank-challenger))",
        "team-blue": "hsl(var(--team-blue))",
        "team-red": "hsl(var(--team-red))"
      },
      boxShadow: {
        glass: "0 1px 0 hsl(210 20% 100% / 0.04) inset, 0 0 0 1px hsl(var(--border) / 0.5), 0 10px 28px hsl(222 30% 4% / 0.45)",
        soft: "var(--elevation-soft)",
        card: "var(--elevation-card)",
        media: "var(--elevation-media)",
        overlay: "var(--elevation-overlay)",
        inset: "var(--hairline-inset)"
      },
      keyframes: {
        shimmer: {
          "0%": { backgroundPosition: "200% 0" },
          "100%": { backgroundPosition: "-200% 0" }
        }
      },
      animation: {
        shimmer: "shimmer 1.2s linear infinite"
      }
    }
  },
  plugins: []
} satisfies Config;

