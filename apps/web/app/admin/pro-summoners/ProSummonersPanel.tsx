"use client";

import { useRef, useState, useTransition } from "react";

import {
  bulkCreateProSummonersAction,
  createProSummonerAction,
  deleteProSummonerAction,
  refreshProSummonerAction,
  type BulkImportResult
} from "@/app/admin/actions";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import type { ProSummoner } from "@/lib/adminTypes";

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

export function ProSummonersPanel({ rows }: { rows: ProSummoner[] }) {
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

      <div className="rounded-2xl border border-border/70 bg-surface/40 p-4">
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
    <div className="rounded-2xl border border-border/70 bg-surface/40 p-4">
      <h3 className="mb-3 text-sm font-semibold">Add Pro Summoner</h3>
      <form ref={formRef} action={handleSubmit} className="space-y-3">
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          <Input name="gameName" placeholder="Game Name *" required />
          <Input name="tagLine" placeholder="Tag Line *" required />
          <select
            name="platformRegion"
            required
            className="h-11 w-full rounded-xl border border-border/80 bg-surface/50 px-3 text-sm text-fg shadow-glass outline-none focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
          >
            <option value="">Region *</option>
            {PLATFORM_REGIONS.map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </select>
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
    <div className="rounded-2xl border border-border/70 bg-surface/40 p-4">
      <h3 className="mb-3 text-sm font-semibold">CSV Import</h3>
      <p className="mb-2 text-xs text-fg/60">
        Required columns: gameName, tagLine, platformRegion. Optional: proName,
        teamName, type (pro|otp, defaults to pro)
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
          : <span className="text-fg/50">{row.puuid.slice(0, 12)}...</span>}
      </td>
      <td className="py-2">{row.platformRegion}</td>
      <td className="py-2">
        {[row.proName, row.teamName].filter(Boolean).join(" / ") || "-"}
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
