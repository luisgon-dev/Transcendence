"use client";

import Link from "next/link";
import { useActionState, useState } from "react";

import { completePasswordResetAction } from "@/app/account/actions";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { PasswordInput } from "@/components/ui/PasswordInput";

export function ResetPasswordForm({ token }: { token: string }) {
  const [state, formAction, pending] = useActionState(completePasswordResetAction, {
    error: null,
    message: null
  });
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const mismatch = confirm.length > 0 && password !== confirm;

  return (
    <div className="auth-shell">
      <Card className="auth-card mx-auto w-full max-w-xl rounded-panel p-6 sm:p-8">
        <p className="type-kicker text-muted">Account recovery</p>
        <h1 className="type-title mt-3">Choose a new password</h1>
        <p className="type-ui mt-3 text-fg/75">
          Use at least 12 characters. Completing this reset signs out every existing session.
        </p>

        {!token ? (
          <div className="mt-6 grid gap-3">
            <p className="field-error type-ui">This reset link is incomplete.</p>
            <Link href="/account/forgot-password" className="type-ui font-semibold text-primary hover:underline">
              Request a new reset link
            </Link>
          </div>
        ) : (
          <form action={formAction} className="mt-6 grid gap-4">
            <input type="hidden" name="token" value={token} />
            <label className="grid gap-1.5">
              <span className="field-label">New password</span>
              <PasswordInput
                name="password"
                autoComplete="new-password"
                required
                minLength={12}
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            </label>
            <label className="grid gap-1.5">
              <span className="field-label">Confirm new password</span>
              <PasswordInput
                name="confirmPassword"
                autoComplete="new-password"
                required
                value={confirm}
                onChange={(event) => setConfirm(event.target.value)}
                aria-invalid={mismatch || undefined}
              />
              {mismatch ? <span className="field-error type-ui">Passwords do not match.</span> : null}
            </label>
            {state.error ? <p className="field-error type-ui" aria-live="polite">{state.error}</p> : null}
            {state.message ? (
              <div className="grid gap-3 rounded-card bg-success/10 p-3">
                <p className="type-ui text-success" aria-live="polite">{state.message}</p>
                <Link href="/account/login" className="type-ui font-semibold text-primary hover:underline">Sign in</Link>
              </div>
            ) : null}
            {!state.message ? (
              <Button type="submit" disabled={pending || mismatch}>
                {pending ? "Updating password..." : "Update password"}
              </Button>
            ) : null}
          </form>
        )}
      </Card>
    </div>
  );
}
