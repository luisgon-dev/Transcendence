import Link from "next/link";

import { logoutAction } from "@/app/account/actions";
import { Button } from "@/components/ui/Button";
import { hasAdminRole } from "@/lib/authz";
import { getSessionMe } from "@/lib/session";

export async function AccountNav() {
  const me = await getSessionMe();

  if (!me.authenticated) {
    return (
      <div className="inline-flex items-center">
        <Link
          className="type-ui rounded-full px-3 py-2 font-semibold text-fg/74 transition hover:bg-white/[0.05] hover:text-fg"
          href="/account/login"
        >
          Sign in
        </Link>
      </div>
    );
  }

  return (
    <div className="inline-flex items-center gap-1 sm:gap-2">
      {hasAdminRole(me.roles) ? (
        <Link
          className="type-ui rounded-full px-3 py-2 font-medium text-fg/70 transition hover:bg-white/[0.05] hover:text-fg"
          href="/admin"
        >
          Admin
        </Link>
      ) : null}
      <Link
        className="type-ui rounded-full px-3 py-2 font-medium text-fg/70 transition hover:bg-white/[0.05] hover:text-fg"
        href="/account/favorites"
      >
        Favorites
      </Link>
      <form action={logoutAction}>
        <Button
          variant="ghost"
          size="sm"
          type="submit"
          className="h-9 rounded-full px-3.5 text-[0.875rem] text-fg/74 hover:bg-white/[0.05] hover:text-fg"
        >
          Log out
        </Button>
      </form>
    </div>
  );
}
