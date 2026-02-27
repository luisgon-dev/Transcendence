"use server";

import { revalidatePath } from "next/cache";

import { adminDelete, adminPost } from "@/lib/adminBackend";

export async function triggerRecurringJobAction(formData: FormData) {
  const id = String(formData.get("id") ?? "").trim();
  if (!id) return;
  await adminPost(`/api/admin/jobs/recurring/${encodeURIComponent(id)}/trigger`);
  revalidatePath("/admin/jobs");
  revalidatePath("/admin");
}

export async function retryFailedJobAction(formData: FormData) {
  const id = String(formData.get("jobId") ?? "").trim();
  if (!id) return;
  await adminPost(`/api/admin/jobs/failed/${encodeURIComponent(id)}/retry`);
  revalidatePath("/admin/jobs");
  revalidatePath("/admin");
}

export async function invalidateAnalyticsCacheAction() {
  await adminPost("/api/admin/cache/invalidate");
  revalidatePath("/admin");
}

export async function revokeApiKeyAction(formData: FormData) {
  const id = String(formData.get("id") ?? "").trim();
  if (!id) return;
  await adminPost(`/api/auth/keys/${encodeURIComponent(id)}/revoke`);
  revalidatePath("/admin/api-keys");
}

export async function rotateApiKeyAction(formData: FormData) {
  const id = String(formData.get("id") ?? "").trim();
  if (!id) return;
  await adminPost(`/api/auth/keys/${encodeURIComponent(id)}/rotate`);
  revalidatePath("/admin/api-keys");
}

export async function deleteProSummonerAction(formData: FormData) {
  const id = String(formData.get("id") ?? "").trim();
  if (!id) return;
  await adminDelete(`/api/admin/pro-summoners/${encodeURIComponent(id)}`);
  revalidatePath("/admin/pro-summoners");
}

export async function createProSummonerAction(formData: FormData) {
  const body = {
    puuid: String(formData.get("puuid") ?? "").trim(),
    platformRegion: String(formData.get("platformRegion") ?? "").trim(),
    gameName: String(formData.get("gameName") ?? "").trim() || null,
    tagLine: String(formData.get("tagLine") ?? "").trim() || null,
    proName: String(formData.get("proName") ?? "").trim() || null,
    teamName: String(formData.get("teamName") ?? "").trim() || null,
    isPro: formData.get("isPro") === "on",
    isHighEloOtp: formData.get("isHighEloOtp") === "on",
    isActive: true
  };
  if (!body.puuid || !body.platformRegion) {
    return { error: "puuid and platformRegion are required." };
  }
  await adminPost("/api/admin/pro-summoners", body);
  revalidatePath("/admin/pro-summoners");
  return { error: null };
}

export async function refreshProSummonerAction(formData: FormData) {
  const id = String(formData.get("id") ?? "").trim();
  if (!id) return;
  await adminPost(`/api/admin/pro-summoners/${encodeURIComponent(id)}/refresh`);
  revalidatePath("/admin/pro-summoners");
}

export type BulkImportResult = {
  created: number;
  errors: string[];
};

export async function bulkCreateProSummonersAction(
  formData: FormData
): Promise<BulkImportResult> {
  const file = formData.get("file") as File | null;
  if (!file) return { created: 0, errors: ["No file provided."] };

  const text = await file.text();
  const lines = text.split(/\r?\n/).filter((l) => l.trim());
  if (lines.length < 2) return { created: 0, errors: ["CSV must have a header row and at least one data row."] };

  const header = lines[0].split(",").map((h) => h.trim().toLowerCase());
  const requiredCols = ["puuid", "platformregion"];
  for (const col of requiredCols) {
    if (!header.includes(col)) {
      return { created: 0, errors: [`Missing required CSV column: ${col}`] };
    }
  }

  const colIndex = (name: string) => header.indexOf(name);
  let created = 0;
  const errors: string[] = [];

  for (let i = 1; i < lines.length; i++) {
    const cols = lines[i].split(",").map((c) => c.trim());
    const puuid = cols[colIndex("puuid")] ?? "";
    const platformRegion = cols[colIndex("platformregion")] ?? "";
    if (!puuid || !platformRegion) {
      errors.push(`Row ${i + 1}: puuid and platformRegion are required.`);
      continue;
    }

    const body = {
      puuid,
      platformRegion,
      gameName: cols[colIndex("gamename")] || null,
      tagLine: cols[colIndex("tagline")] || null,
      proName: cols[colIndex("proname")] || null,
      teamName: cols[colIndex("teamname")] || null,
      isPro: (cols[colIndex("ispro")] ?? "true").toLowerCase() !== "false",
      isHighEloOtp: (cols[colIndex("ishighelootp")] ?? "false").toLowerCase() === "true",
      isActive: true
    };

    try {
      await adminPost("/api/admin/pro-summoners", body);
      created++;
    } catch (e) {
      errors.push(`Row ${i + 1}: ${e instanceof Error ? e.message : "Unknown error"}`);
    }
  }

  revalidatePath("/admin/pro-summoners");
  return { created, errors };
}
