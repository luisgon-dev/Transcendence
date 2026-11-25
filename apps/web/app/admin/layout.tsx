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
            <p className="type-kicker text-muted">Administration</p>
            <h1 className="type-page-title mt-3 text-fg">Data and worker operations</h1>
            <p className="type-ui mt-3 max-w-2xl text-fg/70">
              Signed in as {session.name ?? "admin"}. Monitor job queues, match collection, and service health.
            </p>
          </div>
          <div className="ops-console-note rounded-card px-4 py-3">
            <p className="type-kicker text-info">Session</p>
            <p className="type-ui mt-2 text-fg/82">Administrator access</p>
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
