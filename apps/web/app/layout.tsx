import type { Metadata } from "next";
import { Bricolage_Grotesque, Hanken_Grotesk, JetBrains_Mono } from "next/font/google";

import "@/app/globals.css";
import { GlobalCommandPaletteLoader } from "@/components/GlobalCommandPaletteLoader";
import { SiteFooter } from "@/components/SiteFooter";
import { SiteHeader } from "@/components/SiteHeader";
import { getMetadataBase, SITE_NAME, socialImageUrl } from "@/lib/seo";

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
  metadataBase: getMetadataBase(),
  title: {
    default: SITE_NAME,
    template: `%s | ${SITE_NAME}`
  },
  description: "League of Legends stats, builds, runes, and player profiles.",
  applicationName: SITE_NAME,
  alternates: {
    canonical: "/"
  },
  openGraph: {
    type: "website",
    siteName: SITE_NAME,
    title: SITE_NAME,
    description: "League of Legends stats, builds, runes, and player profiles.",
    url: "/",
    images: [
      {
        url: socialImageUrl(
          "League analytics you can trust",
          SITE_NAME,
          "Tier lists, builds, matchups, and player profiles"
        ),
        width: 1200,
        height: 630,
        alt: "Transcendence League of Legends analytics"
      }
    ]
  },
  twitter: {
    card: "summary_large_image",
    title: SITE_NAME,
    description: "League of Legends stats, builds, runes, and player profiles.",
    images: [
      socialImageUrl(
        "League analytics you can trust",
        SITE_NAME,
        "Tier lists, builds, matchups, and player profiles"
      )
    ]
  },
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
        <GlobalCommandPaletteLoader />
      </body>
    </html>
  );
}
