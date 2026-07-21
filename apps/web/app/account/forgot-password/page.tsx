"use client";

import Link from "next/link";
import { useActionState } from "react";

import { requestPasswordResetAction } from "@/app/account/actions";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";

export default function ForgotPasswordPage() {
  const [state, formAction, pending] = useActionState(requestPasswordResetAction, {
    error: null,
    message: null
  });

  return (
    <div className="auth-shell">
      <Card className="auth-card mx-auto w-full max-w-xl rounded-panel p-6 sm:p-8">
        <p className="type-kicker text-muted">Account recovery</p>
        <h1 className="type-title mt-3">Reset your password</h1>
        <p className="type-ui mt-3 text-fg/75">
          Enter the email on your account. For privacy, the result is the same whether or not an account exists.
        </p>

        <form action={formAction} className="mt-6 grid gap-4">
          <label className="grid gap-1.5">
            <span className="field-label">Email</span>
            <Input name="email" type="email" autoComplete="email" required placeholder="name@example.com" />
          </label>
          {state.error ? <p className="field-error type-ui" aria-live="polite">{state.error}</p> : null}
          {state.message ? <p className="type-ui rounded-card bg-success/10 p-3 text-success" aria-live="polite">{state.message}</p> : null}
          <Button type="submit" disabled={pending}>
            {pending ? "Sending reset link..." : "Send reset link"}
          </Button>
        </form>

        <Link
          href="/account/login"
          className="type-ui mt-5 inline-flex font-semibold text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/24"
        >
          Back to sign in
        </Link>
      </Card>
    </div>
  );
}
