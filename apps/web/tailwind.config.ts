import type { Config } from "tailwindcss";

export default {
  content: ["./app/**/*.{ts,tsx}", "./components/**/*.{ts,tsx}"],
  theme: {
    extend: {
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
        loss: "hsl(var(--loss))"
      },
      boxShadow: {
        glass: "0 1px 0 hsl(0 0% 100% / 0.05) inset, 0 0 0 1px hsl(var(--border) / 0.65), 0 14px 40px hsl(230 42% 5% / 0.65)",
        glow: "0 0 0 1px hsl(var(--primary) / 0.35), 0 0 24px hsl(var(--primary) / 0.28)"
      },
      backgroundImage: {
        "aurora":
          "radial-gradient(1200px 600px at 20% -10%, hsl(var(--primary) / 0.35), transparent 60%), radial-gradient(900px 500px at 80% 0%, hsl(var(--primary-2) / 0.22), transparent 55%), radial-gradient(1000px 700px at 40% 110%, hsl(220 100% 60% / 0.12), transparent 60%)"
      },
      keyframes: {
        shimmer: {
          "0%": { backgroundPosition: "200% 0" },
          "100%": { backgroundPosition: "-200% 0" }
        },
        floaty: {
          "0%, 100%": { transform: "translateY(0)" },
          "50%": { transform: "translateY(-5px)" }
        }
      },
      animation: {
        shimmer: "shimmer 1.2s linear infinite",
        floaty: "floaty 6s ease-in-out infinite"
      }
    }
  },
  plugins: []
} satisfies Config;

