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
  const gameName = String(formData.get("gameName") ?? "").trim();
  const tagLine = String(formData.get("tagLine") ?? "").trim();
  const platformRegion = String(formData.get("platformRegion") ?? "").trim();
  if (!gameName || !tagLine || !platformRegion) {
    return { error: "gameName, tagLine, and platformRegion are required." };
  }
  const type = String(formData.get("type") ?? "pro");
  const body = {
    gameName,
    tagLine,
    platformRegion,
    proName: String(formData.get("proName") ?? "").trim() || null,
    teamName: String(formData.get("teamName") ?? "").trim() || null,
    isPro: type === "pro",
    isHighEloOtp: type === "otp",
    isActive: true
  };
  try {
    await adminPost("/api/admin/pro-summoners", body);
  } catch (e) {
    return { error: e instanceof Error ? e.message : "Failed to create pro summoner." };
  }
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
  const requiredCols = ["gamename", "tagline", "platformregion"];
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
    const gameName = cols[colIndex("gamename")] ?? "";
    const tagLine = cols[colIndex("tagline")] ?? "";
    const platformRegion = cols[colIndex("platformregion")] ?? "";
    if (!gameName || !tagLine || !platformRegion) {
      errors.push(`Row ${i + 1}: gameName, tagLine, and platformRegion are required.`);
      continue;
    }

    const type = (cols[colIndex("type")] ?? "pro").toLowerCase();
    const body = {
      gameName,
      tagLine,
      platformRegion,
      proName: cols[colIndex("proname")] || null,
      teamName: cols[colIndex("teamname")] || null,
      isPro: type !== "otp",
      isHighEloOtp: type === "otp",
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
