"use client";

import Link from "next/link";
import { useActionState } from "react";

import { loginAction } from "@/app/account/actions";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import { PasswordInput } from "@/components/ui/PasswordInput";
import { RiotAuthNotice } from "@/components/RiotAuthNotice";
import { RiotRsoButton } from "@/components/RiotRsoButton";

export default function LoginPage() {
  const [state, formAction, pending] = useActionState(
    loginAction,
    { error: null as string | null }
  );

  return (
    <div className="auth-shell">
      <section className="grid gap-6 lg:grid-cols-[minmax(0,1.05fr)_minmax(22rem,0.82fr)] lg:items-stretch">
        <Card className="auth-card w-full rounded-panel lg:col-start-2 lg:row-start-1 p-6 sm:p-7">
          <div className="relative z-[1]">
            <h1 className="type-title">
              Sign in
            </h1>

            <RiotAuthNotice />
            <div className="mt-6">
              <RiotRsoButton mode="login" returnTo="/account/favorites" label="Continue with Riot" />
            </div>
            <p className="mt-2 text-xs leading-5 text-muted">
              Riot shares your verified Riot ID and account identifier. Transcendence uses the
              authorization once and discards Riot’s access and refresh tokens.
            </p>
            <div className="my-5 flex items-center gap-3 text-xs uppercase tracking-[0.16em] text-muted">
              <span className="h-px flex-1 bg-border" />
              <span>or use email</span>
              <span className="h-px flex-1 bg-border" />
            </div>

            <form action={formAction} className="grid gap-4">
              <label className="grid gap-1.5">
                <span className="field-label">Email</span>
                <Input
                  name="email"
                  type="email"
                  autoComplete="email"
                  required
                  placeholder="name@example.com"
                />
              </label>
              <div className="grid gap-1.5">
                <div className="flex items-baseline justify-between gap-3">
                  <label htmlFor="password" className="field-label">
                    Password
                  </label>
                  <Link
                    href="/account/forgot-password"
                    className="type-meta rounded-sm font-semibold text-muted transition hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/24 focus-visible:ring-offset-2 focus-visible:ring-offset-bg"
                  >
                    Forgot password?
                  </Link>
                </div>
                <PasswordInput
                  id="password"
                  name="password"
                  autoComplete="current-password"
                  required
                  placeholder="Your password"
                />
              </div>

              {state.error ? (
                <p className="field-error type-ui" aria-live="polite">
                  {state.error}
                </p>
              ) : null}

              <Button type="submit" disabled={pending} className="mt-1">
                {pending ? "Signing in..." : "Sign in"}
              </Button>
            </form>

            <p className="type-ui mt-5 text-muted">
              New here?{" "}
              <Link
                className="font-semibold text-primary transition hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/24 focus-visible:ring-offset-2 focus-visible:ring-offset-bg"
                href="/account/register"
              >
                Create an account
              </Link>
            </p>
          </div>
        </Card>

        <div className="page-panel grid content-between lg:col-start-1 lg:row-start-1 gap-8 p-6 sm:p-8">
          <div>
            <h2 className="type-panel-title max-w-xl">
              Keep your favorite players handy
            </h2>
            <p className="type-lead mt-4 max-w-2xl">
              Sign in to save players you look up often.
            </p>
          </div>

          <div className="grid gap-3 md:grid-cols-3 lg:grid-cols-1 xl:grid-cols-3">
            <div className="surface-subtle rounded-card p-4">
              <p className="type-kicker text-muted">Favorites</p>
              <p className="type-ui mt-2 text-fg/84">Add a player to Favorites from their profile.</p>
            </div>
            <div className="surface-subtle rounded-card p-4">
              <p className="type-kicker text-muted">Live games</p>
              <p className="type-ui mt-2 text-fg/84">Check whether a saved player is in a game from Favorites.</p>
            </div>
            <div className="surface-subtle rounded-card p-4">
              <p className="type-kicker text-muted">Riot sign-in</p>
              <p className="type-ui mt-2 text-fg/84">Continue with Riot to sign in without sharing your Riot password with Transcendence.</p>
            </div>
          </div>

          <div className="surface-subtle rounded-panel p-4">
            <p className="type-kicker text-primary">League stats</p>
            <p className="type-ui mt-2 text-fg/78">
              Check champion rankings on the tier list.
            </p>
            <Link
              href="/lol/tierlist"
              className="type-ui mt-4 inline-flex items-center border-b border-border/45 px-0.5 pb-1 font-semibold text-fg/80 transition hover:border-primary/38 hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/24 focus-visible:ring-offset-2 focus-visible:ring-offset-bg"
            >
              View tier list
            </Link>
          </div>
        </div>
      </section>
    </div>
  );
}
