export const GLOBAL_SEARCH_OPEN_EVENT = "trn:open-command-palette";

export function isGlobalSearchShortcut(event: KeyboardEvent): boolean {
  const target = event.target;
  const editable = target instanceof HTMLElement && (target.isContentEditable ||
    ["input", "textarea", "select"].includes(target.tagName.toLowerCase()));
  return (event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k" &&
    !event.altKey && !event.shiftKey && !event.isComposing && !editable;
}

export type GlobalSearchOpenOrigin = {
  centerX: number;
  centerY: number;
  width: number;
  height: number;
};

export type GlobalSearchOpenDetail = {
  origin: GlobalSearchOpenOrigin | null;
};

function toOpenOrigin(element: HTMLElement): GlobalSearchOpenOrigin {
  const rect = element.getBoundingClientRect();
  return {
    centerX: rect.left + rect.width / 2,
    centerY: rect.top + rect.height / 2,
    width: rect.width,
    height: rect.height
  };
}

export function dispatchGlobalSearchOpen(element?: HTMLElement | null) {
  const detail: GlobalSearchOpenDetail = {
    origin: element ? toOpenOrigin(element) : null
  };

  window.dispatchEvent(
    new CustomEvent<GlobalSearchOpenDetail>(GLOBAL_SEARCH_OPEN_EVENT, {
      detail
    })
  );
}

export function getGlobalSearchOpenDetail(event: Event): GlobalSearchOpenDetail {
  if (!(event instanceof CustomEvent)) {
    return { origin: null };
  }

  const detail = event.detail as Partial<GlobalSearchOpenDetail> | null | undefined;
  const origin = detail?.origin;
  if (!origin) return { origin: null };

  if (
    !Number.isFinite(origin.centerX) ||
    !Number.isFinite(origin.centerY) ||
    !Number.isFinite(origin.width) ||
    !Number.isFinite(origin.height)
  ) {
    return { origin: null };
  }

  return {
    origin: {
      centerX: origin.centerX,
      centerY: origin.centerY,
      width: origin.width,
      height: origin.height
    }
  };
}
