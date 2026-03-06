"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import {
  type Dispatch,
  type SetStateAction,
  useState,
  useTransition
} from "react";

import { Button } from "@/components/ui/Button";
import type {
  AdminBulkDeleteJobsResult,
  AdminDeleteJobResult,
  AdminJobDetail,
  AdminJobListItem,
  AdminRecurringJob
} from "@/lib/adminTypes";

type MutationStatus =
  | { tone: "success" | "error" | "info"; text: string }
  | null;

type BulkDeleteRequest = {
  state: string;
  queue: string;
  type: string;
  region: string;
  q: string;
  olderThanMinutes: string;
  limit: number;
};

function statusClassName(tone: NonNullable<MutationStatus>["tone"]) {
  switch (tone) {
    case "success":
      return "border-emerald-400/20 bg-emerald-500/10 text-emerald-100";
    case "error":
      return "border-rose-400/20 bg-rose-500/10 text-rose-100";
    default:
      return "border-border/60 bg-black/15 text-fg/80";
  }
}

function StatusMessage({ status }: { status: MutationStatus }) {
  if (!status) return null;

  return (
    <div className={`rounded-2xl border px-4 py-3 text-sm ${statusClassName(status.tone)}`}>
      {status.text}
    </div>
  );
}

async function readResponseJson(response: Response) {
  const text = await response.text();
  if (!text) return null;

  try {
    return JSON.parse(text) as Record<string, unknown>;
  } catch {
    return { message: text };
  }
}

async function adminRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api/trn/admin${path}`, {
    ...init,
    headers: {
      ...(init?.body ? { "content-type": "application/json" } : {}),
      ...init?.headers
    },
    cache: "no-store"
  });

  const payload = await readResponseJson(response);
  if (!response.ok) {
    const message =
      typeof payload?.message === "string"
        ? payload.message
        : `Admin request failed (${response.status}).`;
    const detail =
      typeof payload?.detail === "string" ? ` ${payload.detail}` : "";
    throw new Error(`${message}${detail}`);
  }

  return payload as T;
}

function buildLogsHref(job: {
  exceptionType: string | null;
  stateChangedAtUtc: string | null;
  region: string | null;
}) {
  const params = new URLSearchParams({
    service: "service",
    limit: "200"
  });

  if (job.exceptionType) params.set("q", job.exceptionType);
  if (job.region) params.set("q", `${job.exceptionType ?? ""} ${job.region}`.trim());
  if (job.stateChangedAtUtc) {
    const at = new Date(job.stateChangedAtUtc);
    const since = new Date(at.getTime() - 15 * 60 * 1000);
    const until = new Date(at.getTime() + 15 * 60 * 1000);
    params.set("sinceUtc", since.toISOString().slice(0, 16));
    params.set("untilUtc", until.toISOString().slice(0, 16));
  }

  return `/admin/logs?${params.toString()}`;
}

type MutationStatusSetter = Dispatch<SetStateAction<MutationStatus>>;
type BooleanSetter = Dispatch<SetStateAction<boolean>>;

export function AdminRecurringJobsTable({ jobs }: { jobs: AdminRecurringJob[] }) {
  const router = useRouter();
  const [rows, setRows] = useState(jobs);
  const [status, setStatus] = useState<MutationStatus>(null);

  // eslint-disable-next-line no-unused-vars
  function updateRow(id: string, recipe: (currentJob: AdminRecurringJob) => AdminRecurringJob) {
    setRows((current) =>
      current.map((row) => (row.id === id ? recipe(row) : row))
    );
  }

  function refresh() {
    router.refresh();
  }

  return (
    <>
      <StatusMessage status={status} />
      <div className="mt-4 overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="text-fg/60">
            <tr>
              <th className="py-2">Id</th>
              <th className="py-2">Queue</th>
              <th className="py-2">Status</th>
              <th className="py-2">Next</th>
              <th className="py-2 text-right">Actions</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((job) => (
              <RecurringJobRow
                key={job.id}
                job={job}
                onStatus={setStatus}
                onRefresh={refresh}
                onPauseStateChange={(isPaused) =>
                  updateRow(job.id, (row) => ({
                    ...row,
                    isPaused,
                    isPresentInStorage: !isPaused
                  }))
                }
              />
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}

function RecurringJobRow({
  job,
  onStatus,
  onRefresh,
  onPauseStateChange
}: {
  job: AdminRecurringJob;
  onStatus: MutationStatusSetter;
  onRefresh: () => void;
  onPauseStateChange: BooleanSetter;
}) {
  const [pending, startTransition] = useTransition();

  function submit(kind: "trigger" | "pause" | "resume") {
    startTransition(async () => {
      try {
        const result = await adminRequest<{ message?: string }>(
          `/jobs/recurring/${encodeURIComponent(job.id)}/${kind}`,
          { method: "POST" }
        );

        if (kind === "pause") onPauseStateChange(true);
        if (kind === "resume") onPauseStateChange(false);

        onStatus({
          tone: "success",
          text: result.message ?? `Recurring job ${job.id} updated.`
        });
        onRefresh();
      } catch (error) {
        onStatus({
          tone: "error",
          text: error instanceof Error ? error.message : "Recurring job action failed."
        });
      }
    });
  }

  return (
    <tr className="border-t border-border/40">
      <td className="py-2 font-mono text-xs">{job.id}</td>
      <td className="py-2">{job.queue || "default"}</td>
      <td className="py-2">
        {job.isPaused ? (
          <span className="rounded-full bg-amber-500/15 px-2.5 py-1 text-xs uppercase tracking-wide text-amber-200">
            paused
          </span>
        ) : job.isEnabledByConfiguration ? (
          <span className="rounded-full bg-emerald-500/15 px-2.5 py-1 text-xs uppercase tracking-wide text-emerald-200">
            active
          </span>
        ) : (
          <span className="rounded-full bg-white/10 px-2.5 py-1 text-xs uppercase tracking-wide text-fg/60">
            disabled
          </span>
        )}
      </td>
      <td className="py-2">{job.nextExecution ? new Date(job.nextExecution).toLocaleString() : "-"}</td>
      <td className="py-2">
        <div className="flex justify-end gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={pending}
            onClick={() => submit("trigger")}
          >
            {pending ? "Working..." : "Trigger"}
          </Button>
          {job.isPausable ? (
            job.isPaused ? (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="bg-emerald-500/15 text-emerald-100 shadow-none hover:brightness-100"
                disabled={pending}
                onClick={() => submit("resume")}
              >
                {pending ? "Working..." : "Resume"}
              </Button>
            ) : (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="bg-amber-500/15 text-amber-100 shadow-none hover:brightness-100"
                disabled={pending}
                onClick={() => submit("pause")}
              >
                {pending ? "Working..." : "Pause"}
              </Button>
            )
          ) : null}
        </div>
      </td>
    </tr>
  );
}

export function AdminBulkDeleteButton({
  request,
  totalMatched,
  disabled
}: {
  request: BulkDeleteRequest;
  totalMatched: number;
  disabled: boolean;
}) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const [status, setStatus] = useState<MutationStatus>(null);

  return (
    <div className="grid gap-2">
      <StatusMessage status={status} />
      <Button
        type="button"
        variant="ghost"
        size="sm"
        className="rounded-full border border-amber-300/30 bg-amber-500/10 px-4 py-2 text-xs font-medium text-amber-100 transition hover:bg-amber-500/20"
        disabled={disabled || pending}
        onClick={() => {
          if (!window.confirm(`Delete up to ${totalMatched.toLocaleString()} matching jobs?`)) {
            return;
          }

          startTransition(async () => {
            try {
              const result = await adminRequest<AdminBulkDeleteJobsResult>("/jobs/bulk-delete", {
                method: "POST",
                body: JSON.stringify({
                  states: [request.state],
                  queues: request.queue ? [request.queue] : null,
                  jobType: request.type || null,
                  region: request.region || null,
                  query: request.q || null,
                  olderThanMinutes: request.olderThanMinutes
                    ? Number.parseInt(request.olderThanMinutes, 10)
                    : null,
                  limit: request.limit,
                  scanLimit: Math.max(5000, request.limit),
                  dryRun: false
                })
              });

              setStatus({
                tone: result.failed > 0 ? "info" : "success",
                text: `Bulk delete finished: deleted ${result.deleted} of ${result.matched} matched jobs${result.failed > 0 ? `, ${result.failed} failed` : ""}.`
              });
              router.refresh();
            } catch (error) {
              setStatus({
                tone: "error",
                text: error instanceof Error ? error.message : "Bulk delete failed."
              });
            }
          });
        }}
      >
        {pending ? "Clearing..." : `Clear Filtered ${request.state}`}
      </Button>
    </div>
  );
}

export function AdminJobsTable({
  jobs
}: {
  jobs: AdminJobListItem[];
}) {
  const router = useRouter();
  const [rows, setRows] = useState(jobs);
  const [status, setStatus] = useState<MutationStatus>(null);

  function removeRow(jobId: string) {
    setRows((current) => current.filter((row) => row.jobId !== jobId));
  }

  return (
    <>
      <StatusMessage status={status} />
      <div className="mt-4 overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="text-fg/60">
            <tr>
              <th className="py-2">Job</th>
              <th className="py-2">Type</th>
              <th className="py-2">Queue</th>
              <th className="py-2">Region</th>
              <th className="py-2">When</th>
              <th className="py-2">Detail</th>
              <th className="py-2">Logs</th>
              <th className="py-2 text-right">Action</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((job) => (
              <JobRow
                key={job.jobId}
                job={job}
                onDeleted={() => removeRow(job.jobId)}
                onStatus={setStatus}
                onRefresh={() => router.refresh()}
              />
            ))}
            {rows.length === 0 ? (
              <tr>
                <td className="py-3 text-fg/60" colSpan={8}>
                  No jobs matched the current filters.
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
    </>
  );
}

function JobRow({
  job,
  onDeleted,
  onStatus,
  onRefresh
}: {
  job: AdminJobListItem;
  onDeleted: () => void;
  onStatus: MutationStatusSetter;
  onRefresh: () => void;
}) {
  const [pending, startTransition] = useTransition();

  function handleDelete() {
    if (!window.confirm(`Delete job ${job.jobId}?`)) return;

    startTransition(async () => {
      try {
        const result = await adminRequest<AdminDeleteJobResult>(
          `/jobs/inspect/${encodeURIComponent(job.jobId)}/delete`,
          {
            method: "POST",
            body: JSON.stringify({
              expectedState: job.state,
              reason: "Deleted from admin jobs table"
            })
          }
        );

        onStatus({
          tone: result.deleted ? "success" : "info",
          text: result.message
        });

        if (result.deleted) onDeleted();
        onRefresh();
      } catch (error) {
        onStatus({
          tone: "error",
          text: error instanceof Error ? error.message : "Delete failed."
        });
      }
    });
  }

  function handleRetry() {
    startTransition(async () => {
      try {
        const result = await adminRequest<{ message?: string }>(
          `/jobs/failed/${encodeURIComponent(job.jobId)}/retry`,
          { method: "POST" }
        );

        onStatus({
          tone: "success",
          text: result.message ?? `Job ${job.jobId} re-queued.`
        });
        onDeleted();
        onRefresh();
      } catch (error) {
        onStatus({
          tone: "error",
          text: error instanceof Error ? error.message : "Retry failed."
        });
      }
    });
  }

  return (
    <tr className="border-t border-border/40">
      <td className="py-2 font-mono text-xs">{job.jobId}</td>
      <td className="py-2">{job.jobType?.split(".").pop() ?? "-"}</td>
      <td className="py-2">{job.queue}</td>
      <td className="py-2">{job.region ?? "-"}</td>
      <td className="py-2">
        {job.stateChangedAtUtc ? new Date(job.stateChangedAtUtc).toLocaleString() : "-"}
      </td>
      <td className="py-2">
        <Link
          href={`/admin/jobs/${encodeURIComponent(job.jobId)}`}
          className="rounded-full border border-border/80 px-3 py-1 text-xs text-fg/85 transition hover:bg-white/10"
        >
          Inspect
        </Link>
      </td>
      <td className="py-2">
        <Link
          href={buildLogsHref(job)}
          className="rounded-full border border-border/80 px-3 py-1 text-xs text-fg/85 transition hover:bg-white/10"
        >
          Nearby Logs
        </Link>
      </td>
      <td className="py-2">
        <div className="flex justify-end gap-2">
          {job.state === "Failed" ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={pending}
              onClick={handleRetry}
            >
              {pending ? "Working..." : "Retry"}
            </Button>
          ) : null}
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="bg-rose-500/15 text-rose-100 shadow-none hover:brightness-100"
            disabled={pending}
            onClick={handleDelete}
          >
            {pending ? "Working..." : "Delete"}
          </Button>
        </div>
      </td>
    </tr>
  );
}

export function AdminJobDetailActionBar({ detail }: { detail: AdminJobDetail }) {
  const router = useRouter();
  const [status, setStatus] = useState<MutationStatus>(null);
  const [pending, startTransition] = useTransition();

  function handleDelete() {
    if (!window.confirm(`Delete job ${detail.jobId}?`)) return;

    startTransition(async () => {
      try {
        const result = await adminRequest<AdminDeleteJobResult>(
          `/jobs/inspect/${encodeURIComponent(detail.jobId)}/delete`,
          {
            method: "POST",
            body: JSON.stringify({
              expectedState: detail.currentState,
              reason: "Deleted from admin job detail"
            })
          }
        );

        setStatus({
          tone: result.deleted ? "success" : "info",
          text: result.message
        });
        router.refresh();
      } catch (error) {
        setStatus({
          tone: "error",
          text: error instanceof Error ? error.message : "Delete failed."
        });
      }
    });
  }

  function handleRetry() {
    startTransition(async () => {
      try {
        const result = await adminRequest<{ message?: string }>(
          `/jobs/failed/${encodeURIComponent(detail.jobId)}/retry`,
          { method: "POST" }
        );

        setStatus({
          tone: "success",
          text: result.message ?? `Job ${detail.jobId} re-queued.`
        });
        router.refresh();
      } catch (error) {
        setStatus({
          tone: "error",
          text: error instanceof Error ? error.message : "Retry failed."
        });
      }
    });
  }

  return (
    <div className="grid gap-3">
      <StatusMessage status={status} />
      <div className="flex flex-wrap items-center gap-2">
        {detail.currentState === "Failed" ? (
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={pending}
            onClick={handleRetry}
          >
            {pending ? "Working..." : "Retry"}
          </Button>
        ) : null}
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="bg-rose-500/15 text-rose-100 shadow-none hover:brightness-100"
          disabled={pending}
          onClick={handleDelete}
        >
          {pending ? "Working..." : "Delete"}
        </Button>
      </div>
    </div>
  );
}
