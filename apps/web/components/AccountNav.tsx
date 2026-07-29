import Link from "next/link";

import { logoutAction } from "@/app/account/actions";
import { Button } from "@/components/ui/Button";
import { analyticsFeatureFlags } from "@/lib/analyticsFeatureFlags";
import { hasAdminRole } from "@/lib/authz";
import { getSessionMe } from "@/lib/session";

export async function AccountNav() {
  const [me, flags] = await Promise.all([getSessionMe(), analyticsFeatureFlags()]);

  if (!me.authenticated) {
    return (
      <div className="inline-flex items-center">
        <Link
          className="type-ui inline-flex min-h-11 items-center rounded-full px-3 py-2 font-semibold text-fg/74 transition-[color,background-color,transform] duration-200 ease-[cubic-bezier(0.25,1,0.5,1)] hover:-translate-y-px hover:bg-surface-2/55 hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/26 focus-visible:ring-offset-2 focus-visible:ring-offset-bg"
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
          className="type-ui inline-flex min-h-11 items-center rounded-full px-3 py-2 font-medium text-fg/70 transition-[color,background-color,transform] duration-200 ease-[cubic-bezier(0.25,1,0.5,1)] hover:-translate-y-px hover:bg-surface-2/55 hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/26 focus-visible:ring-offset-2 focus-visible:ring-offset-bg"
          href="/admin"
        >
          Admin
        </Link>
      ) : null}
      <Link
        className="type-ui inline-flex min-h-11 items-center rounded-full px-3 py-2 font-medium text-fg/70 transition-[color,background-color,transform] duration-200 ease-[cubic-bezier(0.25,1,0.5,1)] hover:-translate-y-px hover:bg-surface-2/55 hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/26 focus-visible:ring-offset-2 focus-visible:ring-offset-bg"
        href="/account/favorites"
      >
        Favorites
      </Link>
      {flags.buildLab ? (
        <Link
          className="type-ui hidden min-h-11 items-center rounded-full px-3 py-2 font-medium text-fg/70 transition-[color,background-color,transform] duration-200 ease-[cubic-bezier(0.25,1,0.5,1)] hover:-translate-y-px hover:bg-surface-2/55 hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/26 focus-visible:ring-offset-2 focus-visible:ring-offset-bg xl:inline-flex"
          href="/account/saved-builds"
        >
          Saved builds
        </Link>
      ) : null}
      <form action={logoutAction}>
        <Button
          variant="ghost"
          size="sm"
          type="submit"
          className="h-10 rounded-full border border-border/50 bg-surface/24 px-3.5 text-fg/74 shadow-inset hover:border-border/72 hover:bg-surface-2/55 hover:text-fg"
        >
          Log out
        </Button>
      </form>
    </div>
  );
}
