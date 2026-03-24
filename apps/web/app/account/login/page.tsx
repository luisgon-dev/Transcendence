"use client";

import Link from "next/link";
import { useActionState } from "react";

import { loginAction } from "@/app/account/actions";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";

export default function LoginPage() {
  const [state, formAction, pending] = useActionState(
    loginAction,
    { error: null as string | null }
  );

  return (
    <div className="grid place-items-center py-8">
      <Card className="w-full max-w-md rounded-panel p-6">
        <p className="type-kicker text-muted">Account</p>
        <h1 className="type-title mt-3">
          Sign in
        </h1>
        <p className="type-ui mt-3 text-fg/75">
          Sign in to keep favorite players and saved pages close.
        </p>

        <form action={formAction} className="mt-6 grid gap-3">
          <label className="grid gap-1.5">
            <span className="type-meta text-fg/78">Email</span>
            <Input
              name="email"
              type="email"
              autoComplete="email"
              required
              placeholder="name@example.com"
            />
          </label>
          <label className="grid gap-1.5">
            <span className="type-meta text-fg/78">Password</span>
            <Input
              name="password"
              type="password"
              autoComplete="current-password"
              required
              placeholder="Your password"
            />
          </label>

          {state.error ? <p className="type-ui text-danger">{state.error}</p> : null}

          <Button type="submit" disabled={pending}>
            {pending ? "Signing in..." : "Sign in"}
          </Button>
        </form>

        <p className="type-ui mt-4 text-muted">
          New here?{" "}
          <Link className="font-semibold text-primary hover:underline" href="/account/register">
            Create an account
          </Link>
        </p>
      </Card>
    </div>
  );
}
