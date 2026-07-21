export const ANALYTICS_QUEUE_OPTIONS = [
  { value: "solo", label: "Ranked Solo/Duo", family: "RANKED_SOLO_DUO", hasRoles: true },
  { value: "aram", label: "ARAM", family: "ARAM", hasRoles: false },
  { value: "arena", label: "Arena", family: "ARENA", hasRoles: false },
  { value: "flex", label: "Ranked Flex", family: "RANKED_FLEX", hasRoles: true }
] as const;

export type AnalyticsQueue = (typeof ANALYTICS_QUEUE_OPTIONS)[number]["value"];

export function normalizeAnalyticsQueue(value: string | null | undefined): AnalyticsQueue {
  const candidate = value?.trim().toLowerCase();
  return ANALYTICS_QUEUE_OPTIONS.some((option) => option.value === candidate)
    ? candidate as AnalyticsQueue
    : "solo";
}

export function analyticsQueueOption(value: string | null | undefined) {
  const queue = normalizeAnalyticsQueue(value);
  return ANALYTICS_QUEUE_OPTIONS.find((option) => option.value === queue)!;
}
