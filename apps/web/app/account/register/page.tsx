"use client";

import Link from "next/link";
import { useActionState } from "react";

import { registerAction } from "@/app/account/actions";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";

export default function RegisterPage() {
  const [state, formAction, pending] = useActionState(
    registerAction,
    { error: null as string | null }
  );

  return (
    <div className="auth-shell">
      <section className="grid gap-6 lg:grid-cols-[minmax(0,1.05fr)_minmax(22rem,0.82fr)] lg:items-stretch">
        <div className="page-panel grid content-between gap-8 p-6 sm:p-8">
          <div>
            <p className="type-kicker text-muted">Saved Workflow</p>
            <h1 className="type-panel-title mt-3 max-w-xl">
              Set up a lightweight account and keep your research path intact.
            </h1>
            <p className="type-lead mt-4 max-w-2xl">
              Create an account to save favorite players, keep route shortcuts nearby, and return to the same working set faster.
            </p>
          </div>

          <div className="grid gap-3 md:grid-cols-3 lg:grid-cols-1 xl:grid-cols-3">
            <div className="surface-subtle rounded-card p-4">
              <p className="type-kicker text-fg/56">Quick return</p>
              <p className="type-ui mt-2 text-fg/84">Your saved pages stay close when you are bouncing between games, patch notes, and profiles.</p>
            </div>
            <div className="surface-subtle rounded-card p-4">
              <p className="type-kicker text-fg/56">Private scope</p>
              <p className="type-ui mt-2 text-fg/84">The account is for site preferences and favorites. It does not require Riot sign-in.</p>
            </div>
            <div className="surface-subtle rounded-card p-4">
              <p className="type-kicker text-fg/56">Built for reuse</p>
              <p className="type-ui mt-2 text-fg/84">Useful when the same players, champions, and routes come up repeatedly during ranked sessions.</p>
            </div>
          </div>

          <div className="surface-subtle rounded-panel p-4">
            <p className="type-kicker text-primary/84">Security baseline</p>
            <p className="field-note mt-2">
              Use a password with at least 12 characters. Longer passphrases are easier to remember and harder to guess.
            </p>
          </div>
        </div>

        <Card className="auth-card w-full rounded-panel p-6 sm:p-7">
          <div className="relative z-[1]">
            <p className="type-kicker text-muted">Account</p>
            <h2 className="type-title mt-3">
              Create account
            </h2>
            <p className="type-ui mt-3 text-fg/75">
              Create an account to save favorite players and get back to them faster.
            </p>

            <form action={formAction} className="mt-6 grid gap-4">
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
              <label className="grid gap-1.5">
                <span className="field-label">Password</span>
                <Input
                  name="password"
                  type="password"
                  autoComplete="new-password"
                  required
                  placeholder="At least 12 characters"
                />
                <span className="field-note">Use at least 12 characters.</span>
              </label>

              {state.error ? (
                <p className="field-error type-ui" aria-live="polite">
                  {state.error}
                </p>
              ) : null}

              <Button type="submit" disabled={pending} className="mt-1">
                {pending ? "Creating account..." : "Create account"}
              </Button>
            </form>

            <p className="type-ui mt-5 text-fg/68">
              Already have an account?{" "}
              <Link
                className="font-semibold text-primary transition hover:text-primary/84 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/24 focus-visible:ring-offset-2 focus-visible:ring-offset-bg"
                href="/account/login"
              >
                Sign in
              </Link>
            </p>
          </div>
        </Card>
      </section>
    </div>
  );
}
