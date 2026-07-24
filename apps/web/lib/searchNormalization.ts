export function normalizeSearchText(value: string): string {
  return value
    .normalize("NFKD")
    .replace(/\p{M}/gu, "")
    .toLocaleLowerCase()
    .replace(/[^\p{L}\p{N}]/gu, "");
}

export function searchMatchScore(candidate: string, query: string): number | null {
  const normalizedCandidate = normalizeSearchText(candidate);
  const normalizedQuery = normalizeSearchText(query);
  if (!normalizedQuery) return 4;
  if (normalizedCandidate === normalizedQuery) return 0;
  if (normalizedCandidate.startsWith(normalizedQuery)) return 1;

  const tokenMatch = candidate
    .normalize("NFKD")
    .replace(/\p{M}/gu, "")
    .toLocaleLowerCase()
    .split(/[^\p{L}\p{N}]+/u)
    .some((token) => token.startsWith(normalizedQuery));
  if (tokenMatch) return 2;
  if (normalizedCandidate.includes(normalizedQuery)) return 3;
  return null;
}
