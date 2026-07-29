"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";

import { adminDelete, adminPost } from "@/lib/adminBackend";

function revalidateAdminSurfaces() {
  revalidatePath("/admin");
  revalidatePath("/admin/jobs");
}

async function simpleAdminAction(
  formData: FormData,
  formKey: string,
  pathFn: (encodedId: string) => string,
  revalidateFn: () => void,
  method: "POST" | "DELETE" = "POST"
) {
  const id = String(formData.get(formKey) ?? "").trim();
  if (!id) return;
  const path = pathFn(encodeURIComponent(id));
  if (method === "DELETE") {
    await adminDelete(path);
  } else {
    await adminPost(path);
  }
  revalidateFn();
}

export async function triggerRecurringJobAction(formData: FormData) {
  return simpleAdminAction(formData, "id",
    (id) => `/api/admin/jobs/recurring/${id}/trigger`, revalidateAdminSurfaces);
}

export async function pauseRecurringJobAction(formData: FormData) {
  return simpleAdminAction(formData, "id",
    (id) => `/api/admin/jobs/recurring/${id}/pause`, revalidateAdminSurfaces);
}

export async function resumeRecurringJobAction(formData: FormData) {
  return simpleAdminAction(formData, "id",
    (id) => `/api/admin/jobs/recurring/${id}/resume`, revalidateAdminSurfaces);
}

export async function retryFailedJobAction(formData: FormData) {
  return simpleAdminAction(formData, "jobId",
    (id) => `/api/admin/jobs/failed/${id}/retry`, revalidateAdminSurfaces);
}

export async function deleteJobAction(formData: FormData) {
  const jobId = String(formData.get("jobId") ?? "").trim();
  if (!jobId) return;
  const expectedState = String(formData.get("expectedState") ?? "").trim() || null;
  const reason = String(formData.get("reason") ?? "").trim() || null;
  await adminPost(`/api/admin/jobs/inspect/${encodeURIComponent(jobId)}/delete`, {
    expectedState,
    reason
  });
  revalidateAdminSurfaces();
  revalidatePath(`/admin/jobs/${encodeURIComponent(jobId)}`);
}

export async function bulkDeleteJobsAction(formData: FormData) {
  const state = String(formData.get("state") ?? "").trim();
  if (!state) return;

  const queue = String(formData.get("queue") ?? "").trim();
  const type = String(formData.get("type") ?? "").trim();
  const region = String(formData.get("region") ?? "").trim();
  const query = String(formData.get("q") ?? "").trim();
  const olderThanMinutesRaw = String(formData.get("olderThanMinutes") ?? "").trim();
  const olderThanMinutes = olderThanMinutesRaw ? Number.parseInt(olderThanMinutesRaw, 10) : null;
  const limitRaw = String(formData.get("limit") ?? "").trim();
  const limit = limitRaw ? Number.parseInt(limitRaw, 10) : 500;
  const dryRun = String(formData.get("dryRun") ?? "").trim().toLowerCase() === "true";

  await adminPost("/api/admin/jobs/bulk-delete", {
    states: [state],
    queues: queue ? [queue] : null,
    jobType: type || null,
    region: region || null,
    query: query || null,
    olderThanMinutes: Number.isFinite(olderThanMinutes) ? olderThanMinutes : null,
    limit: Number.isFinite(limit) ? limit : 500,
    scanLimit: 20000,
    dryRun
  });

  revalidateAdminSurfaces();
}

export async function invalidateAnalyticsCacheAction() {
  await adminPost("/api/admin/cache/invalidate");
  revalidatePath("/admin");
}

const BUILD_LAB_PATH = "/admin/analytics/build-lab";

// adminBackend collapses a failed call into one Error whose message carries the upstream status. A
// 409 on promote/rollback is the expected concurrent-promotion (or failed-gate) outcome, not a bug
// worth an error page, so it is reported on the page instead of thrown.
function generationActionMessage(error: unknown, fallback: string) {
  const message = error instanceof Error ? error.message : "";
  if (message.includes("(409)")) {
    return "Another generation owns the active pointer, or this one did not pass its gates. Refresh and retry.";
  }
  if (message.includes("(404)")) return "That generation no longer exists.";
  return fallback;
}

async function generationAction(
  formData: FormData,
  path: (encodedId: string) => string,
  fallbackError: string,
  body?: unknown
) {
  const id = String(formData.get("generationId") ?? "").trim();
  if (!id) return;

  let failure: string | null = null;
  try {
    await adminPost(path(encodeURIComponent(id)), body);
  } catch (error) {
    failure = generationActionMessage(error, fallbackError);
  }

  revalidatePath(BUILD_LAB_PATH);
  // redirect() throws its own control-flow signal, so it must stay outside the try above.
  if (failure) redirect(`${BUILD_LAB_PATH}?error=${encodeURIComponent(failure)}`);
}

export async function promoteBuildLabGenerationAction(formData: FormData) {
  return generationAction(
    formData,
    (id) => `/api/admin/analytics/build-lab/generations/${id}/promote`,
    "The generation could not be promoted."
  );
}

export async function rollbackBuildLabGenerationAction(formData: FormData) {
  return generationAction(
    formData,
    (id) => `/api/admin/analytics/build-lab/generations/${id}/rollback`,
    "The generation could not be made active."
  );
}

export async function failBuildLabGenerationAction(formData: FormData) {
  const reason = String(formData.get("reason") ?? "").trim();
  return generationAction(
    formData,
    (id) => `/api/admin/analytics/build-lab/generations/${id}/fail`,
    "The generation could not be abandoned.",
    { reason: reason || null }
  );
}

export async function revokeApiKeyAction(formData: FormData) {
  return simpleAdminAction(formData, "id",
    (id) => `/api/auth/keys/${id}/revoke`, () => revalidatePath("/admin/api-keys"));
}

export async function rotateApiKeyAction(formData: FormData) {
  return simpleAdminAction(formData, "id",
    (id) => `/api/auth/keys/${id}/rotate`, () => revalidatePath("/admin/api-keys"));
}

export async function deleteProSummonerAction(formData: FormData) {
  return simpleAdminAction(formData, "id",
    (id) => `/api/admin/pro-summoners/${id}`,
    () => revalidatePath("/admin/pro-summoners"), "DELETE");
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
    puuid: String(formData.get("puuid") ?? "").trim() || null,
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
  return simpleAdminAction(formData, "id",
    (id) => `/api/admin/pro-summoners/${id}/refresh`,
    () => revalidatePath("/admin/pro-summoners"));
}

export async function approveProCandidateAction(formData: FormData) {
  const id = String(formData.get("id") ?? "").trim();
  const gameName = String(formData.get("gameName") ?? "").trim();
  const tagLine = String(formData.get("tagLine") ?? "").trim();
  const platformRegion = String(formData.get("platformRegion") ?? "").trim();
  if (!id || !gameName || !tagLine || !platformRegion) {
    return { error: "Riot game name, tag line, and region are required." };
  }

  try {
    await adminPost(`/api/admin/pro-summoners/candidates/${encodeURIComponent(id)}/approve`, {
      gameName,
      tagLine,
      platformRegion,
      puuid: String(formData.get("puuid") ?? "").trim() || null
    });
  } catch (e) {
    return { error: e instanceof Error ? e.message : "Failed to approve candidate." };
  }

  revalidatePath("/admin/pro-summoners");
  return { error: null };
}

export async function rejectProCandidateAction(formData: FormData) {
  return simpleAdminAction(
    formData,
    "id",
    (id) => `/api/admin/pro-summoners/candidates/${id}/reject`,
    () => revalidatePath("/admin/pro-summoners")
  );
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
      puuid: cols[colIndex("puuid")] || null,
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
