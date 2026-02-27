import "server-only";

import { redirect } from "next/navigation";

import { hasAdminRole } from "@/lib/authz";
import { logEvent } from "@/lib/serverLog";
import { getSessionMe } from "@/lib/session";
import { getAccessTokenOrRefresh } from "@/lib/sessionToken";

export type AdminSession = {
  userId: string | null;
  name: string | null;
  roles: string[];
  accessToken: string;
};

export async function requireAdminSession(): Promise<AdminSession> {
  const me = await getSessionMe();
  if (!me.authenticated) {
    logEvent("warn", "admin: user not authenticated, redirecting to login");
    redirect("/account/login");
  }

  if (!hasAdminRole(me.roles)) {
    logEvent(
      "warn",
      "admin: user lacks admin role, redirecting to home",
      { name: me.name, roles: me.roles }
    );
    redirect("/");
  }

  const token = await getAccessTokenOrRefresh();
  if (!token) {
    redirect("/account/login");
  }

  return {
    userId: me.subject,
    name: me.name,
    roles: me.roles,
    accessToken: token
  };
}
