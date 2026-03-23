import Link from "next/link";

import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";

const suggestions = [
  { href: "/lol/tierlist", label: "LoL Tier List" },
  { href: "/lol/champions", label: "Champions" },
  { href: "/lol/pro-builds", label: "Pro Builds" },
  { href: "/tft/comps", label: "TFT Comps" },
];

export default function NotFound() {
  return (
    <div className="grid place-items-center">
      <Card className="w-full max-w-lg p-6">
        <h1 className="type-title">
          Page not found
        </h1>
        <p className="type-ui mt-3 text-fg/75">
          We couldn&apos;t find what you were looking for. It may have been moved or the URL might be incorrect.
        </p>

        <nav className="mt-5">
          <p className="type-meta text-muted">Try one of these instead</p>
          <ul className="mt-2 grid grid-cols-2 gap-2">
            {suggestions.map((s) => (
              <li key={s.href}>
                <Link
                  href={s.href}
                  className="block rounded-lg border border-border/50 px-3 py-2 text-sm font-medium text-fg/90 transition hover:border-primary/40 hover:text-primary"
                >
                  {s.label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>

        <div className="mt-5">
          <Link href="/">
            <Button variant="outline">Go home</Button>
          </Link>
        </div>
      </Card>
    </div>
  );
}

