import type { ReactNode } from "react";

import { Card } from "@/components/ui/Card";

export type BackendErrorCardProps = {
  title: string;
  message: string;
  requestId?: string | null;
  detail?: string | null;
  children?: ReactNode;
};

export function BackendErrorCard({
  title,
  message,
  requestId,
  detail,
  children
}: BackendErrorCardProps) {
  return (
    <Card className="p-6">
      <h1 className="font-[var(--font-sora)] text-2xl font-semibold">{title}</h1>
      <p className="mt-2 text-sm text-fg/75">{message}</p>
      {requestId ? (
        <p className="mt-3 text-xs text-muted">
          Request ID: <code>{requestId}</code>
        </p>
      ) : null}
      {detail ? (
        <pre className="mt-3 max-w-full overflow-x-auto rounded-lg border border-border/60 bg-black/25 p-3 text-xs text-fg/80">
          {detail}
        </pre>
      ) : null}
      {children ? <div className="mt-4">{children}</div> : null}
    </Card>
  );
}
