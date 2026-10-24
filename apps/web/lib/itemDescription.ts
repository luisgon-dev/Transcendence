export type ItemEffect = { kind: "passive" | "active"; name: string; text: string };
export type ItemDescription = { stats: string[]; effects: ItemEffect[] };

// Data Dragon descriptions are Riot's own markup (<stats>, <passive>, <keyword>, ...). Only the text is
// kept, so nothing from it is ever rendered as HTML.
function plainText(markup: string) {
  return markup
    .replace(/<br\s*\/?>/gi, " ")
    .replace(/<[^>]+>/g, "")
    .replace(/\s+/g, " ")
    .trim();
}

export function parseItemDescription(markup: string | null | undefined): ItemDescription {
  if (!markup) return { stats: [], effects: [] };
  const statsBlock = /<stats>([\s\S]*?)<\/stats>/i.exec(markup)?.[1] ?? "";
  const stats = statsBlock.split(/<br\s*\/?>/i).map(plainText).filter(Boolean);
  const effects: ItemEffect[] = [];
  const effectPattern = /<(passive|active)>([\s\S]*?)<\/\1>([\s\S]*?)(?=<(?:passive|active)>|<\/mainText>|$)/gi;
  for (const [, kind, name, body] of markup.matchAll(effectPattern)) {
    const text = plainText(body.replace(/<rules>[\s\S]*?<\/rules>|<flavorText>[\s\S]*?<\/flavorText>/gi, ""));
    effects.push({ kind: kind.toLowerCase() as ItemEffect["kind"], name: plainText(name), text });
  }
  return { stats, effects };
}
