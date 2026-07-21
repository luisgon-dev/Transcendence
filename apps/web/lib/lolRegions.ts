export const LOL_REGION_OPTIONS = [
  { value: "na", label: "NA" },
  { value: "euw", label: "EUW" },
  { value: "eune", label: "EUNE" },
  { value: "kr", label: "KR" },
  { value: "br", label: "BR" },
  { value: "lan", label: "LAN" },
  { value: "las", label: "LAS" },
  { value: "oce", label: "OCE" },
  { value: "jp", label: "JP" },
  { value: "tr", label: "TR" },
  { value: "ru", label: "RU" }
] as const;

const PLATFORM_TO_SLUG: Record<string, string> = {
  NA1: "na",
  EUW1: "euw",
  EUN1: "eune",
  KR: "kr",
  BR1: "br",
  LA1: "lan",
  LA2: "las",
  OC1: "oce",
  JP1: "jp",
  TR1: "tr",
  RU: "ru"
};

export function normalizeLolRegionSlug(value: string | null | undefined): string {
  const candidate = value?.trim().toLowerCase() ?? "";
  return LOL_REGION_OPTIONS.some((option) => option.value === candidate) ? candidate : "na";
}

export function platformRegionToSlug(platformRegion: string): string {
  return PLATFORM_TO_SLUG[platformRegion.trim().toUpperCase()] ?? normalizeLolRegionSlug(platformRegion);
}
