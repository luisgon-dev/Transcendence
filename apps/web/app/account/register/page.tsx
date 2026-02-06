"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";

import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";

export default function RegisterPage() {
  const router = useRouter();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);

    try {
      const res = await fetch("/api/session/register", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ email, password })
      });

      if (!res.ok) {
        const json = (await res.json().catch(() => null)) as { message?: string } | null;
        setError(json?.message ?? "Registration failed.");
        return;
      }

      router.push("/account/favorites");
      router.refresh();
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="grid place-items-center">
      <Card className="w-full max-w-md p-6">
        <h1 className="font-[var(--font-sora)] text-2xl font-semibold">
          Register
        </h1>
        <p className="mt-2 text-sm text-fg/75">
          Create an account to save favorites.
        </p>

        <form onSubmit={onSubmit} className="mt-6 grid gap-3">
          <label className="grid gap-1 text-sm">
            <span className="text-fg/85">Email</span>
            <Input
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              type="email"
              autoComplete="email"
              required
            />
          </label>
          <label className="grid gap-1 text-sm">
            <span className="text-fg/85">Password</span>
            <Input
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              type="password"
              autoComplete="new-password"
              required
            />
            <span className="text-xs text-muted">Minimum 8 characters.</span>
          </label>

          {error ? <p className="text-sm text-red-300">{error}</p> : null}

          <Button type="submit" disabled={busy}>
            {busy ? "Creating..." : "Create account"}
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

