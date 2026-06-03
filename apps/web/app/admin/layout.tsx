import { AdminNav } from "@/app/admin/AdminNav";
import { requireAdminSession } from "@/lib/adminSession";

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
        <div className="mt-5">
          <AdminNav />
        </div>
      </header>
      {children}
    </section>
  );
}
