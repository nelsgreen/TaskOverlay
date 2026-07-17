/**
 * Pure helpers for the MEET modal shell (phase 1 visual migration).
 *
 * Kept dependency-free so it can be unit-tested with `node --test` and so the
 * shell geometry, tab identity, keyboard navigation, and header summary logic
 * live outside the React component. The tab order is asserted against
 * `meet-workspace-policy` in the test suite to prevent drift.
 */

export type MeetShellTab = "details" | "sources" | "review"

export const MEET_SHELL_TAB_ORDER: readonly MeetShellTab[] = [
  "details",
  "sources",
  "review",
]

/**
 * Fixed desktop-oriented modal geometry. The rendered element clamps these to
 * the viewport (`min(<px>, calc(100vw|100dvh - margin))`) so the modal keeps a
 * constant size across Details / Sources / Review yet stays safe on small
 * screens. Height is viewport-derived, never content-derived.
 */
export const MEET_SHELL_GEOMETRY = {
  maxWidthPx: 1180,
  maxHeightPx: 720,
  viewportMarginRem: 2,
} as const

export function meetTabButtonId(tab: MeetShellTab): string {
  return `meet-tab-${tab}`
}

export function meetTabPanelId(tab: MeetShellTab): string {
  return `meet-panel-${tab}`
}

export function meetTabLabel(tab: MeetShellTab): string {
  return tab.charAt(0).toUpperCase() + tab.slice(1)
}

/** Roving tab navigation for ArrowLeft/ArrowRight, wrapping at both ends. */
export function nextMeetTab(
  current: MeetShellTab,
  direction: "next" | "prev",
): MeetShellTab {
  const order = MEET_SHELL_TAB_ORDER
  const index = order.indexOf(current)
  if (index < 0) return order[0]
  const count = order.length
  const target = direction === "next"
    ? (index + 1) % count
    : (index - 1 + count) % count
  return order[target]
}

/**
 * Builds the compact header secondary line, e.g. "PLHIV · Today · 09:00".
 * Blank/whitespace parts are dropped so a MEET without a project or time still
 * renders a clean line rather than stray separators.
 */
export function buildMeetSecondaryLine(
  parts: ReadonlyArray<string | null | undefined>,
): string {
  return parts
    .map((part) => part?.trim())
    .filter((part): part is string => Boolean(part && part.length > 0))
    .join(" · ")
}
