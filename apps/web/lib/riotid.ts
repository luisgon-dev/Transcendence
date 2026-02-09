export type RiotId = {
  gameName: string;
  tagLine: string;
};

export function parseRiotIdInput(input: string): RiotId | null {
  const trimmed = input.trim();
  if (!trimmed) return null;

  const hashIdx = trimmed.lastIndexOf("#");
  if (hashIdx < 1 || hashIdx === trimmed.length - 1) return null;

  const gameName = trimmed.slice(0, hashIdx).trim();
  const tagLine = trimmed.slice(hashIdx + 1).trim();
  if (!gameName || !tagLine) return null;

  return { gameName, tagLine };
}

export function encodeRiotIdPath({ gameName, tagLine }: RiotId) {
  // op.gg-like: /summoners/{region}/{gameName}-{tagLine}
  return `${encodeURIComponent(gameName)}-${encodeURIComponent(tagLine)}`;
}

export function decodeRiotIdPath(riotIdPath: string): RiotId | null {
  let decoded: string;
  try {
    decoded = decodeURIComponent(riotIdPath);
  } catch {
    // Malformed percent-encoding in user-supplied path.
    return null;
  }
  const dashIdx = decoded.lastIndexOf("-");
  if (dashIdx < 1 || dashIdx === decoded.length - 1) return null;

  const gameName = decoded.slice(0, dashIdx).trim();
  const tagLine = decoded.slice(dashIdx + 1).trim();
  if (!gameName || !tagLine) return null;

  return { gameName, tagLine };
}
