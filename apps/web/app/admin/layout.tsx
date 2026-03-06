import Link from "next/link";

import { requireAdminSession } from "@/lib/adminSession";

const links = [
  { href: "/admin", label: "Pipeline" },
  { href: "/admin/jobs?state=enqueued", label: "Jobs" },
  { href: "/admin/logs", label: "Service Logs" },
  { href: "/admin/pro-summoners", label: "Pro Summoners" },
  { href: "/admin/api-keys", label: "API Keys" },
  { href: "/admin/audit", label: "Audit Log" }
];

export default async function AdminLayout({
  children
}: Readonly<{
  children: React.ReactNode;
}>) {
  const session = await requireAdminSession();

  return (
    <section className="grid gap-6">
      <header className="overflow-hidden rounded-[2rem] border border-border/70 bg-[radial-gradient(circle_at_top_left,rgba(144,205,244,0.16),transparent_40%),radial-gradient(circle_at_bottom_right,rgba(244,114,182,0.14),transparent_35%),rgba(17,24,39,0.62)] p-6 shadow-[0_25px_80px_rgba(0,0,0,0.28)]">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <p className="text-[11px] uppercase tracking-[0.24em] text-fg/55">Operations Console</p>
            <h1 className="mt-2 font-[var(--font-sora)] text-3xl font-semibold text-fg">Admin Pipeline Dashboard</h1>
            <p className="mt-2 max-w-2xl text-sm text-fg/70">
              Signed in as {session.name ?? "admin"}. Monitor queue pressure, ingestion health, and operator actions
              from one place.
            </p>
          </div>
          <div className="rounded-2xl border border-border/60 bg-black/15 px-4 py-3 text-sm text-fg/75">
            Admin role active
          </div>
        </div>
        <nav className="mt-5 flex flex-wrap gap-2">
          {links.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              className="rounded-full border border-border/70 bg-white/5 px-3 py-1.5 text-sm text-fg/75 transition hover:bg-white/10 hover:text-fg"
            >
              {link.label}
            </Link>
          ))}
        </nav>
      </header>
      {children}
    </section>
  );
}
