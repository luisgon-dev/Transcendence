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
