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

test("geometry constants describe a bounded, clearly intentional shell", () => {
  assert.equal(MEET_SHELL_GEOMETRY.maxWidthPx, 1280)
  assert.equal(MEET_SHELL_GEOMETRY.maxHeightPx, 820)
  assert.equal(MEET_SHELL_GEOMETRY.viewportWidthPercent, 90)
  assert.equal(MEET_SHELL_GEOMETRY.viewportHeightPercent, 88)
  // Must stay well below a near-fullscreen clamp.
  assert.ok(MEET_SHELL_GEOMETRY.maxWidthPx < 1600)
  assert.ok(MEET_SHELL_GEOMETRY.maxHeightPx < 1000)
})

test("modal geometry is bounded, viewport-clamped, and stable across tabs", () => {
  // Bounded clamp, applied once on the shell box independent of the tab.
  assert.match(component, /min\(1280px,90vw\)/)
  assert.match(component, /min\(820px,88dvh\)/)
  assert.equal(component.match(/w-\[min\(1280px/g)?.length, 1)
  assert.equal(component.match(/h-\[min\(820px/g)?.length, 1)
  // The rejected near-fullscreen geometry must be fully gone.
  assert.doesNotMatch(component, /1600px|calc\(100vw-16px\)|calc\(100dvh-16px\)/)
})

test("Details uses a wider editable column than Context (not 50/50)", () => {
  assert.match(component, /lg:grid-cols-\[minmax\(0,1\.65fr\)_minmax\(320px,0\.85fr\)\]/)
  assert.doesNotMatch(component, /lg:grid-cols-2/)
})

test("compact linked task exposes an inline Open action, not a duplicated card", () => {
  assert.match(component, /aria-label="Open linked task"/)
  // Open-task navigation still flushes autosave first via requestClose.
  assert.match(component, /if \(await requestClose\("navigate"\)\) onOpenLinkedTask/)
  // The missing-linked-task warning is preserved.
  assert.match(component, /Linked task is no longer available\./)
})

test("MEET Details keeps Context open by default without changing Task Details", () => {
  // MEET Details opts into default-open; the shared block feeds it into `open`.
  assert.match(component, /defaultOpenWhenEmpty/)
  const contextBlock = readFileSync(
    new URL("../components/task-context-block.tsx", import.meta.url),
    "utf8",
  )
  assert.match(contextBlock, /manualOpen \?\? \(totalLinked > 0 \|\| defaultOpenWhenEmpty\)/)
  assert.match(contextBlock, /defaultOpenWhenEmpty=\{defaultOpenWhenEmpty\}/)
  assert.match(contextBlock, /defaultOpenWhenEmpty = false/)
  // The Task wrapper must not pass the flag, so Task keeps collapse-when-empty.
  const taskWrapper = contextBlock.slice(
    contextBlock.indexOf("export function TaskContextBlock"),
    contextBlock.indexOf("interface MeetContextBlockProps"),
  )
  assert.equal(taskWrapper.includes("defaultOpenWhenEmpty"), false)
})

test("the permanent calendar-like-item explanation is removed", () => {
  assert.equal(component.includes("calendar-like item"), false)
})

test("Sources and Review keep their existing props and command wiring", () => {
  assert.match(component, /<MeetingSourcesWorkspace/)
  assert.match(component, /<MeetingReviewWorkspace/)
  assert.equal(component.match(/operations=\{meetingOperations\}/g)?.length, 2)
  assert.equal(
    component.match(/onCommand=\{onMeetingAssistantCommand \? sendMeetingAssistantCommand : undefined\}/g)?.length,
    2,
  )
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
