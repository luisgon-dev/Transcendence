import type { Metadata } from "next";
import { Plus_Jakarta_Sans, Space_Grotesk } from "next/font/google";

import "@/app/globals.css";
import { GlobalCommandPalette } from "@/components/GlobalCommandPalette";
import { SiteHeader } from "@/components/SiteHeader";

const headingFont = Space_Grotesk({
  subsets: ["latin"],
  variable: "--font-sora",
  display: "swap"
});

const bodyFont = Plus_Jakarta_Sans({
  subsets: ["latin"],
  variable: "--font-manrope",
  display: "swap"
});

export const metadata: Metadata = {
  title: "Transcendence",
  description: "LoL analytics: summoners, matches, and champion insights.",
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
      <body className="bg-aurora font-[var(--font-manrope)] antialiased">
        <SiteHeader />
        <main className="mx-auto w-full max-w-[1440px] px-4 py-8 md:px-6 md:py-10">
          {children}
        </main>
        <GlobalCommandPalette />
      </body>
    </html>
  );
}
