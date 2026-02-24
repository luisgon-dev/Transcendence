import { AccountNav } from "@/components/AccountNav";
import { SiteHeaderClient } from "@/components/SiteHeaderClient";
import { fetchChampionMap } from "@/lib/staticData";

export async function SiteHeader() {
  let patch: string | null = null;
  try {
    const { version } = await fetchChampionMap();
    patch = version.split(".").slice(0, 2).join(".");
  } catch {
    // ignore – header still renders without patch badge
  }

  return (
    <SiteHeaderClient patch={patch}>
      <AccountNav />
    </SiteHeaderClient>
  );
}
