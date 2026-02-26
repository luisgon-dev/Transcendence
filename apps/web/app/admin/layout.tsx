import Link from "next/link";

import { requireAdminSession } from "@/lib/adminSession";

const links = [
  { href: "/admin", label: "Overview" },
  { href: "/admin/jobs", label: "Jobs" },
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
      <header className="rounded-2xl border border-border/70 bg-surface/60 p-4">
        <h1 className="font-[var(--font-sora)] text-2xl font-semibold">Admin Dashboard</h1>
        <p className="mt-1 text-sm text-fg/70">
          Signed in as {session.name ?? "admin"}.
        </p>
        <nav className="mt-3 flex flex-wrap gap-2">
          {links.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              className="rounded-full border border-border/70 px-3 py-1.5 text-sm text-fg/75 transition hover:bg-white/10 hover:text-fg"
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
