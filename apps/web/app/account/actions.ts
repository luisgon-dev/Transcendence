"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";

import { clearAuthCookies, setAuthCookies, type AuthTokenResponse } from "@/lib/authCookies";
import { logEvent } from "@/lib/serverLog";
import { getTrnClient } from "@/lib/trnClient";

type AuthActionState = {
  error: string | null;
};

function normalizeCredential(value: FormDataEntryValue | null) {
  return typeof value === "string" ? value.trim() : "";
}

function revalidateAuthShell() {
  revalidatePath("/", "layout");
  revalidatePath("/account/favorites");
  revalidatePath("/account/login");
  revalidatePath("/account/register");
}

async function authenticate(
  endpoint: "/api/auth/login" | "/api/auth/register",
  formData: FormData
): Promise<AuthActionState> {
  const email = normalizeCredential(formData.get("email"));
  const password = normalizeCredential(formData.get("password"));

  if (!email || !password) {
    return { error: "Email and password are required." };
  }

  const client = getTrnClient();
  let data: unknown;
  let error: unknown;
  let response: { status: number };

  try {
    const result = await client.POST(endpoint, {
      body: { email, password }
    });
    data = result.data;
    error = result.error;
    response = result.response;
  } catch (caught: unknown) {
    logEvent("warn", "auth action backend request failed", { endpoint, error: caught });
    return { error: "Authentication service unavailable." };
  }

  if (!data) {
    const message =
      (error as { detail?: string; title?: string } | undefined)?.detail ??
      (error as { detail?: string; title?: string } | undefined)?.title ??
      (response.status >= 500 ? "Authentication service unavailable." : null) ??
      "Authentication failed.";
    return { error: message };
  }

  const token = data as AuthTokenResponse;
  await setAuthCookies(token);
  revalidateAuthShell();
  redirect("/account/favorites");
}

export async function loginAction(
  _prevState: AuthActionState,
  formData: FormData
): Promise<AuthActionState> {
  return authenticate("/api/auth/login", formData);
}

export async function registerAction(
  _prevState: AuthActionState,
  formData: FormData
): Promise<AuthActionState> {
  return authenticate("/api/auth/register", formData);
}

export async function logoutAction() {
  await clearAuthCookies();
  revalidateAuthShell();
  redirect("/");
}
