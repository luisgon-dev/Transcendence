"use client";

import { Bricolage_Grotesque, Hanken_Grotesk, JetBrains_Mono } from "next/font/google";

import "@/app/globals.css";
import { RouteError } from "@/components/RouteError";

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

// global-error replaces the root layout, so it must resolve the theme itself to
// avoid a flash of the wrong palette (mirrors the script in app/layout.tsx).
const themeScript = `(function(){try{var p=localStorage.getItem("theme");var d=p?p==="dark":window.matchMedia("(prefers-color-scheme: dark)").matches;document.documentElement.classList.toggle("dark",d);document.documentElement.style.colorScheme=d?"dark":"light";}catch(e){}})();`;

export default function GlobalError({
  error,
  reset
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  return (
    <html
      lang="en"
      suppressHydrationWarning
      className={`${displayFont.variable} ${bodyFont.variable} ${monoFont.variable}`}
    >
      <body className="antialiased">
        <script dangerouslySetInnerHTML={{ __html: themeScript }} />
        <main className="mx-auto flex w-full max-w-[1440px] flex-1 flex-col px-4 py-8 md:px-6 md:py-10 lg:px-8">
          <RouteError
            title="Something went wrong"
            message="The application hit an unexpected error and couldn't render."
            error={error}
            reset={reset}
          />
        </main>
      </body>
    </html>
  );
}
