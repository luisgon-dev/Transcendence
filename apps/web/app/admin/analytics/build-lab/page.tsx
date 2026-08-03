import {
  failBuildLabGenerationAction,
  promoteBuildLabGenerationAction,
  rollbackBuildLabGenerationAction
} from "@/app/admin/actions";
import { AdminRefreshButton } from "@/app/admin/AdminRefreshButton";
import { adminGet } from "@/lib/adminBackend";
import type {
  AdminBuildLabGeneration,
  AdminBuildLabGenerationResponse,
  AdminBuildLabPromotionEntry
} from "@/lib/adminTypes";

const ABANDONABLE_STATUSES = ["PendingDataset", "Modeling", "Candidate"];
const LONG_RUN_MINUTES = 90;

type MetricTone = "success" | "danger" | "warning" | undefined;
type MetricRow = { label: string; value: string; tone?: MetricTone };

function percent(value: unknown) {
  return typeof value === "number" ? `${(value * 100).toFixed(2)}%` : "—";
}

function toneClass(tone: MetricTone) {
  if (tone === "success") return "text-success";
  if (tone === "danger") return "text-danger";
  if (tone === "warning") return "text-warning";
  return "";
}

// Backend timestamps are UTC; a payload without an explicit offset must not be read as local time
// or every age below would be wrong by the viewer's offset.
function parseUtc(value: string | null) {
  if (!value) return null;
  const normalized = /(Z|[+]\d\d:?\d\d|-\d\d:\d\d)$/.test(value) ? value : `${value}Z`;
  const parsed = Date.parse(normalized);
  return Number.isFinite(parsed) ? parsed : null;
}

function formatUtc(value: string | null) {
  const parsed = parseUtc(value);
  return parsed == null ? value || "—" : new Date(parsed).toLocaleString();
}

function minutesSince(value: string | null, nowMs: number) {
  const parsed = parseUtc(value);
  return parsed == null ? null : Math.round((nowMs - parsed) / 60_000);
}

function ageLabel(minutes: number) {
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  return hours < 48 ? `${hours} hr ago` : `${Math.floor(hours / 24)} days ago`;
}

function number(value: unknown) {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function validationRows(generation: AdminBuildLabGeneration): MetricRow[] {
  let metrics: Record<string, unknown>;
  try {
    metrics = (JSON.parse(generation.validationMetricsJson || "{}") ?? {}) as Record<string, unknown>;
  } catch {
    return [{ label: "Validation", value: "Metrics are missing or malformed", tone: "danger" }];
  }

  const brier = number(metrics.brierScore);
  const baselineBrier = number(metrics.baselineBrierScore);
  const logLoss = number(metrics.logLoss);
  const baselineLogLoss = number(metrics.baselineLogLoss);
  const splits = ["trainMatchCount", "calibrationMatchCount", "testMatchCount"]
    .map((key) => number(metrics[key]))
    .filter((value): value is number => value != null);

  const beats = (candidate: number | null, baseline: number | null) =>
    candidate == null || baseline == null ? undefined : candidate < baseline ? "success" : "danger";
  const versus = (candidate: number | null, baseline: number | null) =>
    candidate == null
      ? "—"
      : `${candidate.toFixed(4)}${baseline == null ? "" : ` vs ${baseline.toFixed(4)} baseline`}`;
  const gate = (value: unknown): MetricRow["tone"] =>
    value === true ? "success" : value === false ? "danger" : "warning";

  return [
    { label: "Overall ECE", value: percent(metrics.overallEce) },
    { label: "Worst time band ECE", value: percent(metrics.maxTimeBandEce) },
    { label: "Brier", value: versus(brier, baselineBrier), tone: beats(brier, baselineBrier) },
    {
      label: "Log loss",
      value: versus(logLoss, baselineLogLoss),
      tone: beats(logLoss, baselineLogLoss)
    },
    {
      label: "Held-out patch",
      value:
        metrics.heldOutPatchPassed === true
          ? `Passed${typeof metrics.heldOutPatch === "string" ? ` · ${metrics.heldOutPatch}` : ""}`
          : metrics.heldOutPatchPassed === false
            ? "Not passed"
            : "Not reported",
      tone: gate(metrics.heldOutPatchPassed)
    },
    {
      label: "Leakage",
      value:
        metrics.leakageCheckPassed === true
          ? "No split overlap"
          : metrics.leakageCheckPassed === false
            ? "Overlap detected"
            : "Not reported",
      tone: gate(metrics.leakageCheckPassed)
    },
    {
      label: "Split matches",
      value: splits.length === 3 ? splits.map((value) => value.toLocaleString()).join(" / ") : "—"
    },
    {
      label: "Design columns",
      value: number(metrics.designColumnCount)?.toLocaleString() ?? "—"
    }
  ];
}

// The worker serialises this history with default (PascalCase) naming while the API envelope around
// it is camelCase, so the opaque string can arrive in either casing.
type RawPromotionEntry = Partial<Record<string, unknown>>;

function entryField(entry: RawPromotionEntry, name: string) {
  const value = entry[name] ?? entry[`${name[0].toUpperCase()}${name.slice(1)}`];
  return typeof value === "string" && value.trim() ? value : null;
}

function promotionHistory(generation: AdminBuildLabGeneration): AdminBuildLabPromotionEntry[] {
  try {
    const parsed: unknown = JSON.parse(generation.promotionHistoryJson || "[]");
    if (!Array.isArray(parsed)) return [];
    return parsed
      .filter((entry): entry is RawPromotionEntry => typeof entry === "object" && entry !== null)
      .map((entry) => ({
        action: entryField(entry, "action") ?? "",
        atUtc: entryField(entry, "atUtc") ?? "",
        actor: entryField(entry, "actor"),
        reason: entryField(entry, "reason")
      }))
      .filter((entry) => Boolean(entry.action));
  } catch {
    return [];
  }
}

function statusTone(status: string, active: boolean) {
  if (active || status === "Ready") return "border-success/35 bg-success/10 text-success";
  if (status === "Failed") return "border-danger/35 bg-danger/10 text-danger";
  if (status === "Candidate") return "border-warning/35 bg-warning/10 text-warning";
  return "border-info/35 bg-info/10 text-info";
}

// The clock read lives here rather than in the component body: lease and heartbeat ages are
// measured against fetch time, and reading it during render is impure.
async function loadGenerations() {
  const response = await adminGet<AdminBuildLabGenerationResponse>("/api/admin/analytics/build-lab");
  return { response, nowMs: Date.now() };
}

export default async function AdminBuildLabAnalyticsPage(props: {
  searchParams?: Promise<{ error?: string }>;
}) {
  const searchParams = props.searchParams ? await props.searchParams : undefined;
  const actionError = searchParams?.error?.trim();
  const { response, nowMs } = await loadGenerations();
  const active = response.generations.find((generation) => generation.isActive);

  return (
    <div className="grid gap-6">
      {actionError ? (
        <p
          role="alert"
          className="rounded-card border border-danger/35 bg-danger/10 p-3 text-sm text-danger"
        >
          {actionError}
        </p>
      ) : null}

      <section className="grid gap-3 md:grid-cols-3">
        <div className="ops-stat-card" data-tone={active ? "success" : "warning"}>
          <p className="type-kicker text-fg/55">Active generation</p>
          <p className="mt-3 text-2xl font-semibold">{active?.patch ?? "None"}</p>
          <p className="mt-1 text-xs text-fg/55">{active?.modelVersion || "Waiting for promotion"}</p>
        </div>
        <div className="ops-stat-card" data-tone="info">
          <p className="type-kicker text-fg/55">Champion-role coverage</p>
          <p className="mt-3 text-2xl font-semibold text-info">{response.activeChampionRoleScopes}</p>
          <p className="mt-1 text-xs text-fg/55">Scopes with publishable estimates</p>
        </div>
        <div className="ops-stat-card" data-tone="primary">
          <p className="type-kicker text-fg/55">Matchup coverage</p>
          <p className="mt-3 text-2xl font-semibold text-primary">{response.activeMatchupScopes}</p>
          <p className="mt-1 text-xs text-fg/55">Qualified champion-role-opponent scopes</p>
        </div>
      </section>

      <section className="page-panel p-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <p className="type-kicker text-primary">Adjusted WPA platform</p>
            <h2 className="mt-2 text-xl font-semibold">Generation coverage and validation</h2>
            <p className="mt-2 max-w-3xl text-sm text-fg/65">
              Only atomically promoted generations reach public serving tables. Candidates remain
              isolated until calibration, leakage, evidence, artifact, and coverage checks pass. A
              generation whose lease has expired or whose heartbeat has gone quiet is stalled and can
              be abandoned here.
            </p>
          </div>
          <AdminRefreshButton label="Refresh Generations" />
        </div>
      </section>

      <section className="grid gap-4">
        {response.generations.length === 0 ? (
          <div className="page-panel p-8 text-center">
            <h2 className="text-lg font-semibold">No dataset generation yet</h2>
            <p className="mt-2 text-sm text-fg/60">
              Enable the offline analytics schedule after the one-minute timeline backfill is ready.
            </p>
          </div>
        ) : null}

        {response.generations.map((generation) => {
          const publishRate = generation.actionEstimateCount > 0
            ? generation.publishableActionCount / generation.actionEstimateCount
            : 0;
          const gatedCount = Math.max(
            0,
            generation.actionEstimateCount - generation.publishableActionCount
          );
          const inFlight = ABANDONABLE_STATUSES.includes(generation.status);
          // There is no heartbeat to go stale: a dead modeler drops its advisory lock and the worker
          // reaps the row on the next tick. A long run is only worth flagging as *long*, not as stuck.
          const runningMinutes = minutesSince(generation.createdAtUtc, nowMs);
          const longRunning =
            inFlight && runningMinutes != null && runningMinutes >= LONG_RUN_MINUTES;
          const history = promotionHistory(generation);

          return (
            <article key={generation.id} className="page-panel overflow-hidden">
              <div className="flex flex-wrap items-start justify-between gap-4 border-b border-border/40 p-5">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <h2 className="text-lg font-semibold">Patch {generation.patch}</h2>
                    <span className={`rounded-full border px-2.5 py-1 text-xs uppercase tracking-wide ${statusTone(generation.status, generation.isActive)}`}>
                      {generation.isActive ? "Active" : generation.status}
                    </span>
                    {longRunning ? (
                      <span className="ops-chip text-xs" data-tone="warning">
                        Long run
                      </span>
                    ) : null}
                  </div>
                  <p className="mt-2 text-sm text-fg/60">
                    {generation.rankScope.replaceAll("_", " ")} · {generation.datasetVersion} ·{" "}
                    {generation.modelVersion || "model pending"}
                  </p>
                  <p className="mt-1 font-mono text-xs text-fg/45">{generation.id}</p>
                </div>
                <div className="flex flex-wrap items-start gap-2">
                  {generation.status === "Candidate" ? (
                    <form action={promoteBuildLabGenerationAction}>
                      <input type="hidden" name="generationId" value={generation.id} />
                      <button className="rounded-full border border-primary/40 bg-primary/10 px-4 py-2 text-sm text-primary transition hover:bg-primary/20">
                        Validate and promote
                      </button>
                    </form>
                  ) : null}
                  {!generation.isActive && ["Ready", "Retired"].includes(generation.status) ? (
                    <form action={rollbackBuildLabGenerationAction}>
                      <input type="hidden" name="generationId" value={generation.id} />
                      <button className="surface-chip rounded-full px-4 py-2 text-sm text-fg/85 transition hover:bg-surface-2/72">
                        Roll back to this
                      </button>
                    </form>
                  ) : null}
                  {inFlight ? (
                    <details className="w-full max-w-xs sm:w-72">
                      <summary className="cursor-pointer rounded-full border border-danger/35 px-4 py-2 text-sm text-danger transition hover:bg-danger/10">
                        Fail / abandon
                      </summary>
                      <form
                        action={failBuildLabGenerationAction}
                        className="mt-2 grid gap-2 rounded-card border border-border/55 bg-surface-2/35 p-3"
                      >
                        <input type="hidden" name="generationId" value={generation.id} />
                        <label className="grid gap-1.5">
                          <span className="field-label">Reason</span>
                          <input
                            name="reason"
                            className="control-input"
                            placeholder="Modeler lease expired without a heartbeat"
                          />
                        </label>
                        <button className="rounded-control border border-danger/40 bg-danger/10 px-3 py-2 text-sm font-semibold text-danger transition hover:bg-danger/20">
                          Mark failed
                        </button>
                      </form>
                    </details>
                  ) : null}
                </div>
              </div>

              <div className="grid gap-5 p-5 xl:grid-cols-[1.1fr_1fr]">
                <div>
                  <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
                    <div>
                      <p className="type-kicker text-fg/45">Matches</p>
                      <p className="mt-2 text-lg font-semibold type-tabular">{generation.matchCount.toLocaleString()}</p>
                    </div>
                    <div>
                      <p className="type-kicker text-fg/45">Estimates</p>
                      <p className="mt-2 text-lg font-semibold type-tabular">{generation.actionEstimateCount.toLocaleString()}</p>
                    </div>
                    <div>
                      <p className="type-kicker text-fg/45">Published</p>
                      <p className="mt-2 text-lg font-semibold type-tabular text-success">{generation.publishableActionCount.toLocaleString()}</p>
                    </div>
                    <div>
                      <p className="type-kicker text-fg/45">Gated</p>
                      <p className="mt-2 text-lg font-semibold type-tabular">{gatedCount.toLocaleString()}</p>
                      <p className="mt-1 text-xs text-fg/45">Sample, ESS, interval</p>
                    </div>
                    <div>
                      <p className="type-kicker text-fg/45">Pass rate</p>
                      <p className="mt-2 text-lg font-semibold type-tabular">{(publishRate * 100).toFixed(1)}%</p>
                    </div>
                  </div>
                  <dl className="mt-5 grid gap-2 text-sm">
                    <div className="flex justify-between gap-3 border-t border-border/30 pt-2">
                      <dt className="text-fg/50">Source cutoff</dt>
                      <dd>{new Date(generation.sourceCutoffUtc).toLocaleString()}</dd>
                    </div>
                    <div className="flex justify-between gap-3 border-t border-border/30 pt-2">
                      <dt className="text-fg/50">Code revision</dt>
                      <dd className="font-mono text-xs">{generation.codeRevision || "unrecorded"}</dd>
                    </div>
                    <div className="flex justify-between gap-3 border-t border-border/30 pt-2">
                      <dt className="text-fg/50">Promoted</dt>
                      <dd>{generation.promotedAtUtc ? new Date(generation.promotedAtUtc).toLocaleString() : "—"}</dd>
                    </div>
                  </dl>

                  {generation.leaseOwner || inFlight ? (
                    <div className="mt-5 rounded-card border border-border/45 bg-surface-2/30 p-3">
                      <p className="type-kicker text-fg/45">Modeler</p>
                      <dl className="mt-2 grid gap-2 text-sm">
                        <div className="flex justify-between gap-3">
                          <dt className="text-fg/50">Owner</dt>
                          <dd className="truncate font-mono text-xs">{generation.leaseOwner || "unleased"}</dd>
                        </div>
                        <div className="flex justify-between gap-3 border-t border-border/25 pt-2">
                          <dt className="text-fg/50">Running for</dt>
                          <dd className={longRunning ? "text-warning" : ""}>
                            {runningMinutes == null ? "—" : ageLabel(runningMinutes)}
                          </dd>
                        </div>
                      </dl>
                      {longRunning ? (
                        <p className="mt-2 text-xs text-warning">
                          Running for over {LONG_RUN_MINUTES} minutes. That is not proof it is stuck —
                          a dead modeler releases its lock and is reaped automatically — but it is
                          worth checking the container logs.
                        </p>
                      ) : null}
                    </div>
                  ) : null}
                </div>

                <div>
                  <p className="type-kicker text-fg/45">Validation report</p>
                  <dl className="mt-3 grid gap-2 sm:grid-cols-2">
                    {validationRows(generation).map((row) => (
                      <div key={row.label} className="rounded-card bg-surface-2/35 p-3">
                        <dt className="text-xs text-fg/50">{row.label}</dt>
                        <dd className={`mt-1 text-sm font-medium ${toneClass(row.tone)}`}>{row.value}</dd>
                      </div>
                    ))}
                  </dl>
                  {generation.failureReason ? (
                    <p className="mt-3 rounded-card border border-danger/30 bg-danger/10 p-3 text-sm text-danger">
                      {generation.failureReason}
                    </p>
                  ) : null}

                  <p className="type-kicker mt-5 text-fg/45">Promotion history</p>
                  {history.length === 0 ? (
                    <p className="mt-2 text-sm text-fg/55">No promotion or rollback recorded.</p>
                  ) : (
                    <ol className="mt-2 grid gap-2">
                      {[...history].reverse().map((entry, index) => (
                        <li
                          key={`${entry.action}-${entry.atUtc}-${index}`}
                          className="rounded-card border border-border/40 bg-surface-2/25 p-3 text-sm"
                        >
                          <div className="flex flex-wrap items-baseline justify-between gap-2">
                            <span className="font-semibold capitalize">{entry.action}</span>
                            <span className="text-xs text-fg/50">{formatUtc(entry.atUtc)}</span>
                          </div>
                          <p className="mt-1 text-xs text-fg/55">
                            {entry.actor || "unattributed"}
                            {entry.reason ? ` · ${entry.reason}` : ""}
                          </p>
                        </li>
                      ))}
                    </ol>
                  )}
                </div>
              </div>
            </article>
          );
        })}
      </section>
    </div>
  );
}
