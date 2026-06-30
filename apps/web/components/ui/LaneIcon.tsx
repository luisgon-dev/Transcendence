type LaneGlyph = "ALL" | "TOP" | "JUNGLE" | "MIDDLE" | "BOTTOM" | "UTILITY";

function normalizeLane(role: string | null | undefined): LaneGlyph {
  const normalized = (role ?? "").trim().toUpperCase();
  switch (normalized) {
    case "TOP":
      return "TOP";
    case "JUNGLE":
      return "JUNGLE";
    case "MIDDLE":
    case "MID":
      return "MIDDLE";
    case "BOTTOM":
    case "BOT":
    case "ADC":
      return "BOTTOM";
    case "UTILITY":
    case "SUPPORT":
      return "UTILITY";
    default:
      return "ALL";
  }
}

// Official League position icons (paths from Community Dragon's
// rcp-fe-lol-static-assets/svg/position-*.svg, viewBox 0 0 34 34). Each lane
// pairs a dim "frame" path with a bright "active" shape; both use currentColor
// so the icon inherits the action accent on the active lane tab and stays
// grayscale otherwise. Top/Jungle render the position bracket; "All" uses a
// neutral quad glyph since Riot ships no all-roles icon.
function LaneGlyphPaths({ lane }: { lane: LaneGlyph }) {
  switch (lane) {
    case "TOP":
      return (
        <>
          <path
            fill="currentColor"
            fillOpacity={0.4}
            fillRule="evenodd"
            d="M21,14H14v7h7V14Zm5-3V26L11.014,26l-4,4H30V7.016Z"
          />
          <polygon fill="currentColor" points="4 4 4.003 28.045 9 23 9 9 23 9 28.045 4.003 4 4" />
        </>
      );
    case "JUNGLE":
      return (
        <path
          fill="currentColor"
          fillRule="evenodd"
          d="M25,3c-2.128,3.3-5.147,6.851-6.966,11.469A42.373,42.373,0,0,1,20,20a27.7,27.7,0,0,1,1-3C21,12.023,22.856,8.277,25,3ZM13,20c-1.488-4.487-4.76-6.966-9-9,3.868,3.136,4.422,7.52,5,12l3.743,3.312C14.215,27.917,16.527,30.451,17,31c4.555-9.445-3.366-20.8-8-28C11.67,9.573,13.717,13.342,13,20Zm8,5a15.271,15.271,0,0,1,0,2l4-4c0.578-4.48,1.132-8.864,5-12C24.712,13.537,22.134,18.854,21,25Z"
        />
      );
    case "MIDDLE":
      return (
        <>
          <path
            fill="currentColor"
            fillOpacity={0.4}
            fillRule="evenodd"
            d="M30,12.968l-4.008,4L26,26H17l-4,4H30ZM16.979,8L21,4H4V20.977L8,17,8,8h8.981Z"
          />
          <polygon fill="currentColor" points="25 4 4 25 4 30 9 30 30 9 30 4 25 4" />
        </>
      );
    case "BOTTOM":
      return (
        <>
          <path
            fill="currentColor"
            fillOpacity={0.4}
            fillRule="evenodd"
            d="M13,20h7V13H13v7ZM4,4V26.984l3.955-4L8,8,22.986,8l4-4H4Z"
          />
          <polygon fill="currentColor" points="29.997 5.955 25 11 25 25 11 25 5.955 29.997 30 30 29.997 5.955" />
        </>
      );
    case "UTILITY":
      return (
        <path
          fill="currentColor"
          fillRule="evenodd"
          d="M26,13c3.535,0,8-4,8-4H23l-3,3,2,7,5-2-3-4h2ZM22,5L20.827,3H13.062L12,5l5,6Zm-5,9-1-1L13,28l4,3,4-3L18,13ZM11,9H0s4.465,4,8,4h2L7,17l5,2,2-7Z"
        />
      );
    case "ALL":
    default:
      return (
        <>
          <rect x="6" y="6" width="9" height="9" rx="2" fill="currentColor" fillOpacity={0.4} />
          <rect x="19" y="6" width="9" height="9" rx="2" fill="currentColor" />
          <rect x="6" y="19" width="9" height="9" rx="2" fill="currentColor" />
          <rect x="19" y="19" width="9" height="9" rx="2" fill="currentColor" fillOpacity={0.4} />
        </>
      );
  }
}

export function LaneIcon({
  role,
  className
}: {
  role: string | null | undefined;
  className?: string;
}) {
  return (
    <svg viewBox="0 0 34 34" aria-hidden="true" className={className}>
      <LaneGlyphPaths lane={normalizeLane(role)} />
    </svg>
  );
}
