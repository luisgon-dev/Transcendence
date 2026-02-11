import { AccountNav } from "@/components/AccountNav";
import { SiteHeaderClient } from "@/components/SiteHeaderClient";

export function SiteHeader() {
  return (
    <SiteHeaderClient>
      <AccountNav />
    </SiteHeaderClient>
  );
}
