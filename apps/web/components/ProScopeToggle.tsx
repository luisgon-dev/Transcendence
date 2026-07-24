import Link from "next/link";

export function ProScopeToggle({
  checked,
  href
}: {
  checked: boolean;
  href: string;
}) {
  return (
    <Link
      href={href}
      role="switch"
      aria-checked={checked}
      className="type-ui inline-flex min-h-11 items-center gap-2.5 rounded-control px-1.5 py-1.5 text-fg/78 transition hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/35"
    >
      <span
        aria-hidden="true"
        className="relative h-6 w-10 rounded-full border border-border/70 bg-surface-2 transition data-[checked=true]:border-primary/50 data-[checked=true]:bg-primary/18"
        data-checked={checked}
      >
        <span
          className="absolute left-0.5 top-0.5 size-[18px] rounded-full bg-fg/55 transition-transform duration-150 ease-[cubic-bezier(0.25,1,0.5,1)] data-[checked=true]:translate-x-4 data-[checked=true]:bg-primary"
          data-checked={checked}
        />
      </span>
      <span>Include high-elo one-tricks</span>
    </Link>
  );
}
