import Link from "next/link";

import { logoutAction } from "@/app/account/actions";
import { Button } from "@/components/ui/Button";
import { getSessionMe } from "@/lib/session";

export async function AccountNav() {
  const me = await getSessionMe();
  if (!me.authenticated) {
    return (
      <div className="inline-flex items-center gap-1 rounded-full border border-border/70 bg-surface/40 p-1">
        <Link
          className="rounded-full px-3 py-1.5 text-sm text-fg/75 transition hover:bg-white/10 hover:text-fg"
          href="/account/login"
        >
          Login
        </Link>
        <Link
          className="rounded-full px-3 py-1.5 text-sm text-fg/75 transition hover:bg-white/10 hover:text-fg"
          href="/account/register"
        >
          Register
        </Link>
      </div>
    );
  }

  return (
    <div className="inline-flex items-center gap-1 rounded-full border border-border/70 bg-surface/40 p-1">
      <Link
        className="rounded-full px-3 py-1.5 text-sm text-fg/75 transition hover:bg-white/10 hover:text-fg"
        href="/account/favorites"
      >
        Favorites
      </Link>
      <form action={logoutAction}>
        <Button
          variant="ghost"
          size="sm"
          type="submit"
          className="h-8 rounded-full px-3 text-sm text-fg/75 hover:text-fg"
        >
          Logout
        </Button>
      </form>
    </div>
  );
}
