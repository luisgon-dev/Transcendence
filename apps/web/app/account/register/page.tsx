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
    <div className="grid place-items-center">
      <Card className="w-full max-w-md p-6">
        <h1 className="font-[var(--font-sora)] text-2xl font-semibold">
          Register
        </h1>
        <p className="mt-2 text-sm text-fg/75">
          Create an account to save favorites.
        </p>

        <form action={formAction} className="mt-6 grid gap-3">
          <label className="grid gap-1 text-sm">
            <span className="text-fg/85">Email</span>
            <Input
              name="email"
              type="email"
              autoComplete="email"
              required
            />
          </label>
          <label className="grid gap-1 text-sm">
            <span className="text-fg/85">Password</span>
            <Input
              name="password"
              type="password"
              autoComplete="new-password"
              required
            />
            <span className="text-xs text-muted">Minimum 8 characters.</span>
          </label>

          {state.error ? <p className="text-sm text-red-300">{state.error}</p> : null}

          <Button type="submit" disabled={pending}>
            {pending ? "Creating..." : "Create account"}
          </Button>
        </form>

        <p className="mt-4 text-sm text-muted">
          Already have an account?{" "}
          <Link className="text-primary hover:underline" href="/account/login">
            Login
          </Link>
        </p>
      </Card>
    </div>
  );
}
