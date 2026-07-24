type NamedGameResource = {
  name?: string | null;
};

function normalizedName(resource: NamedGameResource | null | undefined): string | null {
  const name = resource?.name?.trim();
  return name ? name : null;
}

export function championDisplayName(resource: NamedGameResource | null | undefined): string {
  return normalizedName(resource) ?? "Unknown champion";
}

export function itemDisplayName(resource: NamedGameResource | null | undefined): string {
  return normalizedName(resource) ?? "Unknown item";
}

export function runeDisplayName(resource: NamedGameResource | null | undefined): string {
  return normalizedName(resource) ?? "Unknown rune";
}

export function spellDisplayName(resource: NamedGameResource | null | undefined): string {
  return normalizedName(resource) ?? "Unknown summoner spell";
}
