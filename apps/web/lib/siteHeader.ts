export type GameSwitcherItem = {
  label: "LoL" | "TFT";
  href?: string;
  isActive: boolean;
  isDisabled: boolean;
  comingSoon: boolean;
};

export function getGameSwitcherItems(
  pathname: string | null,
  tftPublicEnabled: boolean
): [GameSwitcherItem, GameSwitcherItem] {
  const isTftPath = pathname?.startsWith("/tft") ?? false;

  return [
    {
      label: "LoL",
      href: "/lol",
      isActive: !isTftPath,
      isDisabled: false,
      comingSoon: false
    },
    {
      label: "TFT",
      href: tftPublicEnabled ? "/tft" : undefined,
      isActive: tftPublicEnabled && isTftPath,
      isDisabled: !tftPublicEnabled,
      comingSoon: !tftPublicEnabled
    }
  ];
}
