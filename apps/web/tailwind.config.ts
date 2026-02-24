import type { Config } from "tailwindcss";

export default {
  content: ["./app/**/*.{ts,tsx}", "./components/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        bg: "hsl(var(--bg))",
        surface: "hsl(var(--surface))",
        border: "hsl(var(--border))",
        fg: "hsl(var(--fg))",
        muted: "hsl(var(--muted))",
        primary: "hsl(var(--primary))",
        "primary-2": "hsl(var(--primary-2))",
        "tier-s": "hsl(var(--tier-s))",
        "tier-a": "hsl(var(--tier-a))",
        "tier-b": "hsl(var(--tier-b))",
        "tier-c": "hsl(var(--tier-c))",
        "tier-d": "hsl(var(--tier-d))",
        "wr-high": "hsl(var(--wr-high))",
        "wr-low": "hsl(var(--wr-low))",
        win: "hsl(var(--win))",
        loss: "hsl(var(--loss))"
      },
      boxShadow: {
        glass: "0 1px 0 hsl(var(--border) / 0.65) inset, 0 0 0 1px hsl(var(--border) / 0.6), 0 12px 40px hsl(240 50% 5% / 0.65)"
      },
      backgroundImage: {
        "aurora":
          "radial-gradient(1200px 600px at 20% -10%, hsl(var(--primary) / 0.35), transparent 60%), radial-gradient(900px 500px at 80% 0%, hsl(var(--primary-2) / 0.28), transparent 55%), radial-gradient(1000px 700px at 40% 110%, hsl(280 100% 65% / 0.12), transparent 60%)"
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

