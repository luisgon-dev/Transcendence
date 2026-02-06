"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import { Button } from "@/components/ui/Button";

type SessionMe =
  | { authenticated: false }
  | {
      authenticated: true;
      subject: string | null;
      name: string | null;
      roles: string[];
      authType: string | null;
    };

export function AccountNav() {
  const [me, setMe] = useState<SessionMe | null>(null);
  const [busy, setBusy] = useState(false);

  async function refresh() {
    try {
      const res = await fetch("/api/session/me", { cache: "no-store" });
      const json = (await res.json()) as SessionMe;
      setMe(json);
    } catch {
      setMe({ authenticated: false });
    }
  }

  useEffect(() => {
    void refresh();
  }, []);

  async function logout() {
    setBusy(true);
    try {
      await fetch("/api/session/logout", { method: "POST" });
    } finally {
      setBusy(false);
      await refresh();
    }
  }

  if (!me) {
    return <div className="h-10 w-[200px]" />;
  }

  if (!me.authenticated) {
    return (
      <div className="flex items-center gap-2">
        <Link className="text-sm text-fg/80 hover:text-fg" href="/account/login">
          Login
        </Link>
        <Link
          className="text-sm text-fg/80 hover:text-fg"
          href="/account/register"
        >
          Register
        </Link>
      </div>
    );
  }

  return (
    <div className="flex items-center gap-3">
      <Link
        className="text-sm text-fg/80 hover:text-fg"
        href="/account/favorites"
      >
        Favorites
      </Link>
      <Button variant="ghost" size="sm" onClick={logout} disabled={busy}>
        Logout
      </Button>
    </div>
  );
}

