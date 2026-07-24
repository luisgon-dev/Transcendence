"use client";

import { useRef, useState, useTransition } from "react";

import {
  approveProCandidateAction,
  bulkCreateProSummonersAction,
  createProSummonerAction,
  deleteProSummonerAction,
  rejectProCandidateAction,
  refreshProSummonerAction,
  type BulkImportResult
} from "@/app/admin/actions";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import type { ProPlayerDiscoveryCandidate, ProSummoner } from "@/lib/adminTypes";

const PLATFORM_REGIONS = [
  "NA1",
  "EUW1",
  "EUN1",
  "KR",
  "BR1",
  "LA1",
  "LA2",
  "OC1",
  "JP1",
  "TR1",
  "RU"
] as const;

export function ProSummonersPanel({
  rows,
  candidates
}: {
  rows: ProSummoner[];
  candidates: ProPlayerDiscoveryCandidate[];
}) {
  const [showAddForm, setShowAddForm] = useState(false);
  const [showCsvImport, setShowCsvImport] = useState(false);

  return (
    <section className="space-y-4">
      <div className="flex items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          onClick={() => setShowAddForm((v) => !v)}
        >
          {showAddForm ? "Hide Form" : "Add Summoner"}
        </Button>
        <Button
          variant="outline"
          size="sm"
          onClick={() => setShowCsvImport((v) => !v)}
        >
          {showCsvImport ? "Hide Import" : "CSV Import"}
        </Button>
      </div>

      {showAddForm && <AddSummonerForm />}
      {showCsvImport && <CsvImportForm />}
      <DiscoveryCandidates candidates={candidates} />

      <div className="page-panel p-4">
        <h2 className="text-lg font-semibold">
          Tracked Pro Summoners ({rows.length})
        </h2>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-fg/65">
              <tr>
                <th className="py-2">Identity</th>
                <th className="py-2">Region</th>
                <th className="py-2">Profile</th>
                <th className="py-2">Updated</th>
                <th className="py-2 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <SummonerRow key={row.id} row={row} />
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </section>
  );
}

function DiscoveryCandidates({ candidates }: { candidates: ProPlayerDiscoveryCandidate[] }) {
  return (
    <div className="page-panel p-4">
      <div>
        <h2 className="text-lg font-semibold">Pro account candidates ({candidates.length})</h2>
        <p className="mt-1 text-sm text-fg/60">
          Leaguepedia names are staged here. Confirm the current Riot ID before adding an account.
        </p>
      </div>
      {candidates.length === 0 ? (
        <p className="mt-4 text-sm text-fg/65">No candidates need review.</p>
      ) : (
        <div className="mt-4 grid gap-3">
          {candidates.map((candidate) => (
            <CandidateRow key={candidate.id} candidate={candidate} />
          ))}
        </div>
      )}
    </div>
  );
}

function CandidateRow({ candidate }: { candidate: ProPlayerDiscoveryCandidate }) {
  const [pending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);

  return (
    <div className="rounded-xl border border-border/50 bg-surface-2/25 p-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <p className="font-semibold">{candidate.proName}</p>
          <p className="text-xs text-fg/60">
            {[candidate.teamName, candidate.role].filter(Boolean).join(" · ") || "No current team"}
          </p>
          <p className="mt-1 whitespace-pre-wrap text-xs text-fg/75">
            Source accounts: {candidate.soloQueueIds || "None listed"}
          </p>
        </div>
        <form
          action={(formData) => {
            startTransition(async () => {
              await rejectProCandidateAction(formData);
            });
          }}
        >
          <input type="hidden" name="id" value={candidate.id} />
          <Button type="submit" size="sm" variant="ghost" disabled={pending}>
            Reject
          </Button>
        </form>
      </div>
      <form
        action={(formData) => {
          setError(null);
          startTransition(async () => {
            const result = await approveProCandidateAction(formData);
            setError(result?.error ?? null);
          });
        }}
        className="mt-3 grid gap-2 sm:grid-cols-[1fr_8rem_8rem_1fr_auto]"
      >
        <input type="hidden" name="id" value={candidate.id} />
        <Input name="gameName" placeholder="Riot game name" required />
        <Input name="tagLine" placeholder="Tag" required />
        <select
          name="platformRegion"
          required
          aria-label={`Region for ${candidate.proName}`}
          className="control-select h-11 w-full bg-surface/50 px-3 text-sm text-fg focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
        >
          <option value="">Region</option>
          {PLATFORM_REGIONS.map((region) => (
            <option key={region} value={region}>{region}</option>
          ))}
        </select>
        <Input name="puuid" placeholder="PUUID (optional)" />
        <Button type="submit" size="sm" disabled={pending}>
          {pending ? "Adding..." : "Approve"}
        </Button>
      </form>
      {error ? <p className="mt-2 text-xs text-danger">{error}</p> : null}
    </div>
  );
}

function AddSummonerForm() {
  const formRef = useRef<HTMLFormElement>(null);
  const [pending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);

  function handleSubmit(formData: FormData) {
    setError(null);
    startTransition(async () => {
      const result = await createProSummonerAction(formData);
      if (result?.error) {
        setError(result.error);
      } else {
        formRef.current?.reset();
      }
    });
  }

  return (
    <div className="page-panel p-4">
      <h3 className="mb-3 text-sm font-semibold">Add Pro Summoner</h3>
      <form ref={formRef} action={handleSubmit} className="space-y-3">
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          <Input name="gameName" placeholder="Game Name *" required />
          <Input name="tagLine" placeholder="Tag Line *" required />
          <select
            name="platformRegion"
            required
            className="control-select h-11 w-full bg-surface/50 px-3 text-sm text-fg focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
          >
            <option value="">Region *</option>
            {PLATFORM_REGIONS.map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </select>
          <Input name="puuid" placeholder="PUUID (optional)" />
          <Input name="proName" placeholder="Pro Name" />
          <Input name="teamName" placeholder="Team Name" />
        </div>
        <div className="flex items-center gap-4">
          <label className="flex items-center gap-2 text-sm">
            <input
              type="radio"
              name="type"
              value="pro"
              defaultChecked
              className="accent-primary"
            />
            Pro
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="radio"
              name="type"
              value="otp"
              className="accent-primary"
            />
            High Elo OTP
          </label>
          <Button type="submit" size="sm" disabled={pending}>
            {pending ? "Adding..." : "Add"}
          </Button>
        </div>
        {error && <p className="text-sm text-danger">{error}</p>}
      </form>
    </div>
  );
}

function CsvImportForm() {
  const formRef = useRef<HTMLFormElement>(null);
  const [pending, startTransition] = useTransition();
  const [result, setResult] = useState<BulkImportResult | null>(null);

  function handleSubmit(formData: FormData) {
    setResult(null);
    startTransition(async () => {
      const res = await bulkCreateProSummonersAction(formData);
      setResult(res);
      formRef.current?.reset();
    });
  }

  return (
    <div className="page-panel p-4">
      <h3 className="mb-3 text-sm font-semibold">CSV Import</h3>
      <p className="mb-2 text-xs text-fg/60">
        Required columns: gameName, tagLine, platformRegion. Optional: puuid,
        proName, teamName, type (pro|otp, defaults to pro)
      </p>
      <form ref={formRef} action={handleSubmit} className="flex items-center gap-3">
        <input
          type="file"
          name="file"
          accept=".csv"
          required
          className="text-sm text-fg/80"
        />
        <Button type="submit" size="sm" disabled={pending}>
          {pending ? "Importing..." : "Import"}
        </Button>
      </form>
      {result && (
        <div className="mt-3 text-sm">
          <p className="text-fg/80">
            Created: {result.created}
            {result.errors.length > 0 && `, Errors: ${result.errors.length}`}
          </p>
          {result.errors.length > 0 && (
            <ul className="mt-1 list-inside list-disc text-xs text-danger">
              {result.errors.map((err, i) => (
                <li key={i}>{err}</li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

function SummonerRow({ row }: { row: ProSummoner }) {
  const [refreshPending, startRefresh] = useTransition();
  const [deletePending, startDelete] = useTransition();
  const canRefresh = !!row.gameName && !!row.tagLine;

  return (
    <tr className="border-t border-border/40">
      <td className="py-2">
        {row.gameName && row.tagLine
          ? `${row.gameName}#${row.tagLine}`
          : <span className="text-fg/65">{row.puuid.slice(0, 12)}...</span>}
      </td>
      <td className="py-2">{row.platformRegion}</td>
      <td className="py-2">
        <p>{[row.proName, row.teamName].filter(Boolean).join(" / ") || "-"}</p>
        <p className="text-xs text-fg/55">
          {row.isPro ? "Pro" : "One-trick"}
          {row.isHighEloOtp && row.otpGames && row.otpSampleSize
            ? ` · ${row.otpGames}/${row.otpSampleSize} games`
            : ""}
          {` · ${row.source}`}
        </p>
      </td>
      <td className="py-2">{new Date(row.updatedAtUtc).toLocaleString()}</td>
      <td className="py-2">
        <div className="flex justify-end gap-2">
          <form
            action={(formData) => {
              startRefresh(async () => {
                await refreshProSummonerAction(formData);
              });
            }}
          >
            <input type="hidden" name="id" value={row.id} />
            <button
              type="submit"
              disabled={!canRefresh || refreshPending}
              className="rounded-full border border-primary/60 px-3 py-1 text-xs text-primary transition hover:bg-primary/10 disabled:opacity-50 disabled:pointer-events-none"
            >
              {refreshPending ? "..." : "Refresh"}
            </button>
          </form>
          <form
            action={(formData) => {
              startDelete(async () => {
                await deleteProSummonerAction(formData);
              });
            }}
          >
            <input type="hidden" name="id" value={row.id} />
            <button
              type="submit"
              disabled={deletePending}
              className="rounded-full border border-danger/60 px-3 py-1 text-xs text-danger transition hover:bg-danger/10 disabled:opacity-50"
            >
              {deletePending ? "..." : "Delete"}
            </button>
          </form>
        </div>
      </td>
    </tr>
  );
}
