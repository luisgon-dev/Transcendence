import { ImageResponse } from "next/og";
import type { NextRequest } from "next/server";

export const runtime = "edge";

function readParam(request: NextRequest, name: string, fallback: string, maxLength: number): string {
  const value = request.nextUrl.searchParams.get(name)?.trim();
  return (value || fallback).slice(0, maxLength);
}

export async function GET(request: NextRequest) {
  const title = readParam(request, "title", "League analytics you can trust", 90);
  const eyebrow = readParam(request, "eyebrow", "Transcendence", 40);
  const detail = readParam(
    request,
    "detail",
    "Tier lists, builds, matchups, and player profiles",
    120
  );

  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          justifyContent: "space-between",
          padding: "72px 78px",
          // ImageResponse's server renderer does not support OKLCH functions, so this
          // isolated raster surface uses sRGB equivalents of the product tokens.
          background: "#171922",
          color: "#f2f3f7",
          fontFamily: "Arial, sans-serif"
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 18 }}>
          <div
            style={{
              width: 18,
              height: 18,
              borderRadius: 5,
              background: "#ef443a"
            }}
          />
          <div
            style={{
              fontSize: 24,
              fontWeight: 700,
              letterSpacing: "0.14em",
              textTransform: "uppercase",
              color: "#aeb2c0"
            }}
          >
            {eyebrow}
          </div>
        </div>
        <div style={{ display: "flex", flexDirection: "column", gap: 24, maxWidth: 1040 }}>
          <div style={{ fontSize: 76, lineHeight: 0.98, fontWeight: 750, letterSpacing: "-0.045em" }}>
            {title}
          </div>
          <div style={{ fontSize: 30, lineHeight: 1.25, color: "#bbc0cc" }}>
            {detail}
          </div>
        </div>
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            paddingTop: 22,
            borderTop: "1px solid #414552",
            fontSize: 22,
            color: "#9a9eac"
          }}
        >
          <span>transcend.kronic.one</span>
          <span>Current-patch League of Legends data</span>
        </div>
      </div>
    ),
    {
      width: 1200,
      height: 630,
      headers: {
        "Cache-Control": "public, max-age=86400, stale-while-revalidate=604800"
      }
    }
  );
}
