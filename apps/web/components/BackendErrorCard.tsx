import type { ReactNode } from "react";

import { Card } from "@/components/ui/Card";

export type BackendErrorCardProps = {
  title: string;
  message: string;
  hint?: string;
  requestId?: string | null;
  detail?: string | null;
  children?: ReactNode;
};

export function BackendErrorCard({
  title,
  message,
  hint,
  requestId,
  detail,
  children
}: BackendErrorCardProps) {
  const hasTechnicalInfo = requestId || detail;

  return (
    <Card className="page-panel p-6">
      <h1 className="type-title">{title}</h1>
      <p className="type-ui mt-3 text-fg/75">{message}</p>
      {hint ? <p className="mt-2 text-sm text-muted">{hint}</p> : null}
      {children ? <div className="mt-4">{children}</div> : null}
      {hasTechnicalInfo ? (
        <details className="mt-5 text-xs text-muted">
          <summary className="cursor-pointer select-none rounded hover:text-fg/65 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40">
            Request details
          </summary>
          <div className="mt-2 space-y-2">
            {requestId ? (
              <p>
                Request ID: <code className="text-fg/65">{requestId}</code>
              </p>
            ) : null}
            {detail ? (
              <pre className="max-w-full overflow-x-auto rounded-lg border border-border/60 bg-black/25 p-3 text-fg/65">
                {detail}
              </pre>
            ) : null}
          </div>
        </details>
      ) : null}
    </Card>
  );
}
