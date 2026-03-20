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
    <div className="grid place-items-center py-8">
      <Card className="w-full max-w-md rounded-3xl p-6">
        <p className="type-kicker text-primary">Account</p>
        <h1 className="type-title mt-3">
          Create account
        </h1>
        <p className="type-ui mt-3 text-fg/75">
          Save favorite players and jump back into the pages you check most.
        </p>

        <form action={formAction} className="mt-6 grid gap-3">
          <label className="grid gap-1.5">
            <span className="type-meta text-fg/78">Email</span>
            <Input
              name="email"
              type="email"
              autoComplete="email"
              required
            />
          </label>
          <label className="grid gap-1.5">
            <span className="type-meta text-fg/78">Password</span>
            <Input
              name="password"
              type="password"
              autoComplete="new-password"
              required
            />
            <span className="type-ui text-muted">Minimum 12 characters.</span>
          </label>

          {state.error ? <p className="type-ui text-danger">{state.error}</p> : null}

          <Button type="submit" disabled={pending}>
            {pending ? "Creating..." : "Create account"}
          </Button>
        </form>

        <p className="type-ui mt-4 text-muted">
          Already have an account?{" "}
          <Link className="font-semibold text-primary hover:underline" href="/account/login">
            Sign in
          </Link>
        </p>
      </Card>
    </div>
  );
}
