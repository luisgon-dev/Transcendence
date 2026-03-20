import Link from "next/link";

import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";

export default function NotFound() {
  return (
    <div className="grid place-items-center">
      <Card className="w-full max-w-lg p-6">
        <p className="type-kicker text-primary">Navigation</p>
        <h1 className="type-title mt-3">
          Not found
        </h1>
        <p className="type-ui mt-3 text-fg/75">
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

