import Link from "next/link";

import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";

export default function NotFound() {
  return (
    <div className="grid place-items-center">
      <Card className="w-full max-w-lg p-6">
        <h1 className="font-[var(--font-sora)] text-2xl font-semibold">
          Not found
        </h1>
        <p className="mt-2 text-sm text-fg/75">
          That page doesn&apos;t exist.
        </p>
        <div className="mt-5">
          <Link href="/">
            <Button>Go home</Button>
          </Link>
        </div>
      </Card>
    </div>
  );
}

