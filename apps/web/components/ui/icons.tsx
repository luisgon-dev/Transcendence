export function SearchIcon({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 16 16"
      aria-hidden="true"
      className={className}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <circle cx="7" cy="7" r="4.5" />
      <path d="m10.5 10.5 3 3" />
    </svg>
  );
}

export function SparkIcon({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 16 16"
      aria-hidden="true"
      className={className}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.4"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M8 1.75v2.5" />
      <path d="m12.42 3.58-1.77 1.77" />
      <path d="M14.25 8h-2.5" />
      <path d="m12.42 12.42-1.77-1.77" />
      <path d="M8 14.25v-2.5" />
      <path d="m3.58 12.42 1.77-1.77" />
      <path d="M1.75 8h2.5" />
      <path d="m3.58 3.58 1.77 1.77" />
      <circle cx="8" cy="8" r="1.75" />
    </svg>
  );
}

export function ArrowCornerIcon({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 16 16"
      aria-hidden="true"
      className={className}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M4.25 11.75 11.75 4.25" />
      <path d="M5.5 4.25h6.25V10.5" />
    </svg>
  );
}
