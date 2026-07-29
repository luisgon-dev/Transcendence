import Link from "next/link";
import { notFound } from "next/navigation";

import { SavedBuildList } from "@/components/SavedBuildList";
import { Toolbar } from "@/components/ui/Toolbar";
import { buttonClassName } from "@/components/ui/buttonStyles";
import { fetchBackendJson } from "@/lib/backendCall";
import type { SavedBuildList as SavedBuildListResponse } from "@/lib/buildLab";
import { getBackendBaseUrl } from "@/lib/env";
import { getAccessTokenOrRefresh } from "@/lib/sessionToken";
import { analyticsFeatureFlags } from "@/lib/analyticsFeatureFlags";

const EMPTY_PAGE: SavedBuildListResponse = {
  items: [],
  page: 1,
  pageSize: 0,
  totalCount: 0,
  hasMore: false
};

function requestedPage(value: string | undefined) {
  const parsed = Number.parseInt(value ?? "", 10);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : 1;
}

async function loadSavedBuilds(page: number) {
  const token = await getAccessTokenOrRefresh();
  if (!token.ok) return { authenticated: false, list: EMPTY_PAGE };
  // pageSize is left to the server so its default (50) and cap (200) stay in one place.
  const result = await fetchBackendJson<SavedBuildListResponse>(
    `${getBackendBaseUrl()}/api/users/me/lol/saved-builds?page=${page}`,
    {
      cache: "no-store",
      headers: { authorization: `Bearer ${token.accessToken}` }
    }
  );
  return { authenticated: result.status !== 401, list: result.body ?? EMPTY_PAGE };
}

export default async function SavedBuildsPage(props: {
  searchParams?: Promise<{ page?: string }>;
}) {
  if (!(await analyticsFeatureFlags()).buildLab) notFound();
  const searchParams = props.searchParams ? await props.searchParams : undefined;
  const result = await loadSavedBuilds(requestedPage(searchParams?.page));
  const { items, page, pageSize, totalCount, hasMore } = result.list;
  const firstShown = items.length > 0 ? (page - 1) * pageSize + 1 : 0;
  const lastShown = firstShown > 0 ? firstShown + items.length - 1 : 0;

  return (
    <div className="grid gap-4">
      <Toolbar
        eyebrow="Account"
        title="Saved builds"
        meta={
          <>
            <span>Private configurations and revocable read-only links</span>
            {totalCount > 0 ? (
              <span className="type-tabular">
                {firstShown}–{lastShown} of {totalCount}
              </span>
            ) : null}
          </>
        }
      />
      <SavedBuildList initialBuilds={items} authenticated={result.authenticated} />
      {page > 1 || hasMore ? (
        <nav
          aria-label="Saved build pages"
          className="flex items-center justify-between gap-3 border-t border-border/45 pt-3"
        >
          {page > 1 ? (
            <Link
              href={`/account/saved-builds?page=${page - 1}`}
              rel="prev"
              className={buttonClassName({ variant: "outline", size: "sm" })}
            >
              Previous
            </Link>
          ) : (
            <span aria-hidden />
          )}
          <span className="type-caption type-tabular text-muted">Page {page}</span>
          {hasMore ? (
            <Link
              href={`/account/saved-builds?page=${page + 1}`}
              rel="next"
              className={buttonClassName({ variant: "outline", size: "sm" })}
            >
              Load more
            </Link>
          ) : (
            <span aria-hidden />
          )}
        </nav>
      ) : null}
    </div>
  );
}
