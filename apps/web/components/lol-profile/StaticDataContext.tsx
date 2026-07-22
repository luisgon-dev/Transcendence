"use client";

import { createContext, useContext, type ReactNode } from "react";

import type {
  ChampionStatic,
  ItemStatic,
  RuneStatic,
  SpellStatic
} from "@/components/lol-profile/shared";

export type ProfileStaticData = {
  championStatic: ChampionStatic | null;
  itemStatic: ItemStatic | null;
  spellStatic: SpellStatic | null;
  runeStatic: RuneStatic | null;
};

const EMPTY_STATIC_DATA: ProfileStaticData = {
  championStatic: null,
  itemStatic: null,
  spellStatic: null,
  runeStatic: null
};

const StaticDataContext = createContext<ProfileStaticData>(EMPTY_STATIC_DATA);

export function StaticDataProvider({
  value,
  children
}: {
  value: ProfileStaticData;
  children: ReactNode;
}) {
  return <StaticDataContext.Provider value={value}>{children}</StaticDataContext.Provider>;
}

export function useStaticData(): ProfileStaticData {
  return useContext(StaticDataContext);
}
