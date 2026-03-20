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
    <Card className="page-panel p-6">
      <p className="type-kicker text-primary">Status</p>
      <h1 className="type-title mt-3">{title}</h1>
      <p className="type-ui mt-3 text-fg/75">{message}</p>
      {requestId ? (
        <p className="type-ui mt-4 text-muted">
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
