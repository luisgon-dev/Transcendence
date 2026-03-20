import type { Metadata } from "next";
import { Plus_Jakarta_Sans, Space_Grotesk } from "next/font/google";

import "@/app/globals.css";
import { GlobalCommandPalette } from "@/components/GlobalCommandPalette";
import { SiteFooter } from "@/components/SiteFooter";
import { SiteHeader } from "@/components/SiteHeader";

const headingFont = Space_Grotesk({
  subsets: ["latin"],
  variable: "--font-heading",
  display: "swap"
});

const bodyFont = Plus_Jakarta_Sans({
  subsets: ["latin"],
  variable: "--font-body",
  display: "swap"
});

export const metadata: Metadata = {
  title: "Transcendence",
  description: "League of Legends and TFT stats, builds, comps, and player profiles.",
  icons: {
    icon: "/icon.svg",
    shortcut: "/icon.svg"
  }
};

export default function RootLayout({
  children
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en" className={`${headingFont.variable} ${bodyFont.variable}`}>
      <body className="bg-aurora antialiased">
        <SiteHeader />
        <main className="mx-auto w-full max-w-[1440px] px-4 py-8 md:px-6 lg:px-8 md:py-12">
          {children}
        </main>
        <SiteFooter />
        <GlobalCommandPalette />
      </body>
    </html>
  );
}
