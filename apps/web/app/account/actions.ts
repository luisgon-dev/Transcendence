"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";

import { clearAuthCookies, getAuthCookies, setAuthCookies, type AuthTokenResponse } from "@/lib/authCookies";
import { logEvent } from "@/lib/serverLog";
import { getTrnClient } from "@/lib/trnClient";

type AuthActionState = {
  error: string | null;
};

export type PasswordResetActionState = {
  error: string | null;
  message: string | null;
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
  const actionLabel = endpoint === "/api/auth/register" ? "account setup" : "sign-in";
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
    return { error: `We couldn't reach ${actionLabel} right now. Try again.` };
  }

  if (!data) {
    const message =
      (error as { detail?: string; title?: string } | undefined)?.detail ??
      (error as { detail?: string; title?: string } | undefined)?.title ??
      (response.status >= 500 ? "We couldn't finish that request right now. Try again." : null) ??
      (endpoint === "/api/auth/register"
        ? "We couldn't create your account with those details."
        : "We couldn't sign you in with those details.");
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

export async function requestPasswordResetAction(
  _prevState: PasswordResetActionState,
  formData: FormData
): Promise<PasswordResetActionState> {
  const email = normalizeCredential(formData.get("email"));
  if (!email) return { error: "Email is required.", message: null };

  try {
    const { data, error, response } = await getTrnClient().POST("/api/auth/password-reset", {
      body: { email }
    });
    if (!data) {
      const detail = (error as { detail?: string; title?: string } | undefined)?.detail;
      return {
        error: detail ?? (response.status === 503
          ? "Password recovery is temporarily unavailable. Try again later."
          : "We couldn't start password recovery. Try again."),
        message: null
      };
    }
  } catch (caught: unknown) {
    logEvent("warn", "password reset request failed", { error: caught });
    return { error: "We couldn't reach password recovery right now. Try again.", message: null };
  }

  return {
    error: null,
    message: "If that account exists, a reset link is on its way. Check your inbox and spam folder."
  };
}

export async function completePasswordResetAction(
  _prevState: PasswordResetActionState,
  formData: FormData
): Promise<PasswordResetActionState> {
  const token = normalizeCredential(formData.get("token"));
  const newPassword = normalizeCredential(formData.get("password"));
  const confirmPassword = normalizeCredential(formData.get("confirmPassword"));

  if (!token) return { error: "This reset link is missing its token.", message: null };
  if (newPassword.length < 12) {
    return { error: "Password must be at least 12 characters.", message: null };
  }
  if (newPassword !== confirmPassword) {
    return { error: "Passwords do not match.", message: null };
  }

  try {
    const { response, error } = await getTrnClient().POST("/api/auth/password-reset/complete", {
      body: { token, newPassword }
    });
    if (!response.ok) {
      return {
        error: (error as { detail?: string; title?: string } | undefined)?.detail ??
          "This reset link is invalid or expired. Request a new one.",
        message: null
      };
    }
  } catch (caught: unknown) {
    logEvent("warn", "password reset completion failed", { error: caught });
    return { error: "We couldn't update your password right now. Try again.", message: null };
  }

  return {
    error: null,
    message: "Password updated. Existing sessions were signed out; you can now sign in with the new password."
  };
}

export async function logoutAction() {
  const { refreshToken } = await getAuthCookies();
  if (refreshToken) {
    try {
      const client = getTrnClient();
      await client.POST("/api/auth/logout", {
        body: { refreshToken }
      });
    } catch (caught: unknown) {
      logEvent("warn", "logout revoke request failed", { error: caught });
    }
  }

  await clearAuthCookies();
  revalidateAuthShell();
  redirect("/");
}
