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
      <header className="page-hero ops-hero p-6">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <p className="type-kicker text-muted">Operations Console</p>
            <h1 className="type-page-title mt-3 text-fg">Admin Pipeline Dashboard</h1>
            <p className="type-ui mt-3 max-w-2xl text-fg/70">
              Signed in as {session.name ?? "admin"}. Monitor queue pressure, ingestion health, and operator actions
              from one place.
            </p>
          </div>
          <div className="ops-console-note rounded-card px-4 py-3">
            <p className="type-kicker text-info/78">Session</p>
            <p className="type-ui mt-2 text-fg/82">Admin role active</p>
          </div>
        </div>
        <nav className="mt-5 flex flex-wrap gap-x-4 gap-y-2 border-t border-border/20 pt-3">
          {links.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              className="control-tab type-ui"
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
