import type { Metadata } from "next";
import { Bricolage_Grotesque, Hanken_Grotesk, JetBrains_Mono } from "next/font/google";

import "@/app/globals.css";
import { GlobalCommandPalette } from "@/components/GlobalCommandPalette";
import { SiteFooter } from "@/components/SiteFooter";
import { SiteHeader } from "@/components/SiteHeader";

const displayFont = Bricolage_Grotesque({
  subsets: ["latin"],
  variable: "--font-display-face",
  display: "swap"
});

const bodyFont = Hanken_Grotesk({
  subsets: ["latin"],
  variable: "--font-body",
  display: "swap"
});

const monoFont = JetBrains_Mono({
  subsets: ["latin"],
  variable: "--font-mono-face",
  display: "swap"
});

export const metadata: Metadata = {
  title: "Transcendence",
  description: "League of Legends and TFT stats, builds, comps, and player profiles.",
  icons: {
    icon: [
      { url: "/favicon.ico" },
      { url: "/favicon-96x96.png", sizes: "96x96", type: "image/png" },
      { url: "/favicon.svg", type: "image/svg+xml" }
    ],
    shortcut: "/favicon.ico",
    apple: "/apple-touch-icon.png"
  },
  manifest: "/site.webmanifest",
  appleWebApp: {
    title: "Transcendence"
  }
};

// Resolve the theme before first paint to avoid a flash. Runs synchronously as
// the first thing in <body>: reads the saved preference, else system setting.
const themeScript = `(function(){try{var p=localStorage.getItem("theme");var d=p?p==="dark":window.matchMedia("(prefers-color-scheme: dark)").matches;document.documentElement.classList.toggle("dark",d);document.documentElement.style.colorScheme=d?"dark":"light";}catch(e){}})();`;

export default function RootLayout({
  children
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="en"
      suppressHydrationWarning
      className={`${displayFont.variable} ${bodyFont.variable} ${monoFont.variable}`}
    >
      <body className="antialiased">
        <script dangerouslySetInnerHTML={{ __html: themeScript }} />
        <SiteHeader />
        <main className="site-main mx-auto flex w-full max-w-[1440px] flex-1 flex-col px-4 py-8 md:px-6 md:py-10 lg:px-8">
          {children}
        </main>
        <SiteFooter />
        <GlobalCommandPalette />
      </body>
    </html>
  );
}
