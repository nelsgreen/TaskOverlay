import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import test from "node:test"
import {
  buildMeetSecondaryLine,
  MEET_SHELL_GEOMETRY,
  MEET_SHELL_TAB_ORDER,
  meetTabButtonId,
  meetTabLabel,
  meetTabPanelId,
  nextMeetTab,
} from "./meet-shell.ts"
import { MEET_WORKSPACE_TABS } from "./meet-workspace-policy.ts"

const component = readFileSync(
  new URL("../components/meet-details-panel.tsx", import.meta.url),
  "utf8",
)
const globals = readFileSync(
  new URL("../app/globals.css", import.meta.url),
  "utf8",
)

test("shell tab order matches the authoritative workspace tab order", () => {
  assert.deepEqual([...MEET_SHELL_TAB_ORDER], [...MEET_WORKSPACE_TABS])
  assert.deepEqual([...MEET_SHELL_TAB_ORDER], ["details", "sources", "review"])
})

test("tab identity helpers are stable and distinct", () => {
  assert.equal(meetTabButtonId("details"), "meet-tab-details")
  assert.equal(meetTabPanelId("review"), "meet-panel-review")
  assert.equal(meetTabLabel("sources"), "Sources")
  assert.notEqual(meetTabButtonId("details"), meetTabPanelId("details"))
})

test("arrow navigation wraps in both directions", () => {
  assert.equal(nextMeetTab("details", "next"), "sources")
  assert.equal(nextMeetTab("review", "next"), "details")
  assert.equal(nextMeetTab("details", "prev"), "review")
  assert.equal(nextMeetTab("sources", "prev"), "details")
})

test("secondary header line drops blank parts and joins with a middot", () => {
  assert.equal(
    buildMeetSecondaryLine(["PLHIV", "Today", "09:00"]),
    "PLHIV · Today · 09:00",
  )
  assert.equal(buildMeetSecondaryLine(["PLHIV", "", "  ", undefined, "09:00"]), "PLHIV · 09:00")
  assert.equal(buildMeetSecondaryLine([null, undefined, ""]), "")
})

test("geometry constants describe a fixed desktop shell", () => {
  assert.equal(MEET_SHELL_GEOMETRY.maxWidthPx, 1180)
  assert.equal(MEET_SHELL_GEOMETRY.maxHeightPx, 720)
})

test("modal geometry is fixed and viewport-clamped, not content-derived", () => {
  // The same width/height clamp must live on the shell box regardless of tab.
  assert.match(component, /min\(1180px,calc\(100vw-2rem\)\)/)
  assert.match(component, /min\(720px,calc\(100dvh-2rem\)\)/)
  // A single shell box carries the geometry.
  assert.equal(component.match(/w-\[min\(1180px/g)?.length, 1)
  assert.equal(component.match(/h-\[min\(720px/g)?.length, 1)
})

test("tabs use correct ARIA roles and a single tabpanel", () => {
  assert.match(component, /role="tablist"/)
  assert.match(component, /role="tab"/)
  assert.match(component, /aria-selected=\{active\}/)
  assert.match(component, /role="tabpanel"/)
  assert.match(component, /aria-controls=\{meetTabPanelId/)
})

test("exactly one autosave status region exists (no header/footer duplication)", () => {
  assert.equal(component.match(/saveStatus === "saving"/g)?.length, 1)
  assert.equal(component.match(/saveStatus === "saved"/g)?.length, 1)
  assert.equal(component.match(/saveStatus === "failed"/g)?.length, 1)
})

test("no Save or Revert controls, and no legacy onApply path", () => {
  assert.doesNotMatch(component, />\s*Save\s*</)
  assert.doesNotMatch(component, />\s*Revert\s*</)
  assert.equal(component.includes("onApply"), false)
})

test("one stable footer renders across every tab", () => {
  assert.equal(component.match(/<footer/g)?.length, 1)
  // Delete meeting is footer-scoped to Details only.
  assert.match(component, /isDetails && \(\s*\n\s*<button/)
})

test("migrated MEET surface avoids unreadable 8-9px metadata", () => {
  assert.doesNotMatch(component, /text-\[8px\]/)
  assert.doesNotMatch(component, /text-\[9px\]/)
})

test("visual foundation is MEET-scoped, not global", () => {
  assert.match(component, /className="meet-shell /)
  assert.match(globals, /\.meet-shell\s*\{/)
  // The scope raises border + metadata contrast without touching :root/.dark.
  assert.match(globals, /\.meet-shell[\s\S]*--border:\s*oklch\(1 0 0 \/ 14%\)/)
  assert.match(globals, /\.meet-shell[\s\S]*--muted-foreground:/)
})

test("recording start still flushes autosave before recording", () => {
  assert.match(component, /onBeforeRecordingStart=\{flushAutosave\}/)
})
