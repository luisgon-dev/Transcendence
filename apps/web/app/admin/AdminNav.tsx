"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const links = [
  { href: "/admin", label: "Pipeline", match: (p: string) => p === "/admin" },
  { href: "/admin/jobs?state=enqueued", label: "Jobs", match: (p: string) => p.startsWith("/admin/jobs") },
  { href: "/admin/logs", label: "Service logs", match: (p: string) => p.startsWith("/admin/logs") },
  { href: "/admin/pro-summoners", label: "Tracked players", match: (p: string) => p.startsWith("/admin/pro-summoners") },
  { href: "/admin/api-keys", label: "API keys", match: (p: string) => p.startsWith("/admin/api-keys") },
  { href: "/admin/audit", label: "Audit log", match: (p: string) => p.startsWith("/admin/audit") }
];

export function AdminNav() {
  const pathname = usePathname() ?? "";

  return (
    <nav className="flex flex-wrap gap-x-4 gap-y-2 border-t border-border/40 pt-3">
      {links.map((link) => (
        <Link
          key={link.href}
          href={link.href}
          aria-current={link.match(pathname) ? "page" : undefined}
          data-active={link.match(pathname)}
          className="control-tab type-ui"
        >
          {link.label}
        </Link>
      ))}
    </nav>
  );
}
