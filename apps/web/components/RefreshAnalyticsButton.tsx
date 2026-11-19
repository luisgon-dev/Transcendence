"use client";

import { useRouter } from "next/navigation";
import { useTransition } from "react";

export function RefreshAnalyticsButton() {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  return (
    <button
      type="button"
      disabled={pending}
      onClick={() => startTransition(() => router.refresh())}
      className="font-semibold text-primary hover:underline disabled:text-muted"
    >
      {pending ? "Checking…" : "Check again"}
    </button>
  );
}
