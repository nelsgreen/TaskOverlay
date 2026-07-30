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

test("modal geometry is bounded, viewport-clamped, and stable across tabs - applied once through ModalShell's typed geometry props, not a raw className string", () => {
  assert.match(component, /import \{\s*\n?\s*buildMeetSecondaryLine,\s*\n?\s*MEET_SHELL_GEOMETRY,/)
  // Bounded clamp, applied once on the shell box independent of the tab,
  // via the authoritative MEET_SHELL_GEOMETRY constant (see the geometry
  // constants test above) spread onto ModalShell's typed numeric props.
  assert.equal(component.match(/<ModalShell titleId="meet-details-title" \{\.\.\.MEET_SHELL_GEOMETRY\}>/g)?.length, 1)
  // The rejected near-fullscreen geometry must be fully gone.
  assert.doesNotMatch(component, /1600px|calc\(100vw-16px\)|calc\(100dvh-16px\)/)
})

test("Details uses a wider editable column than Context (not 50/50)", () => {
  assert.match(component, /lg:grid-cols-\[minmax\(0,1\.65fr\)_minmax\(320px,0\.85fr\)\]/)
  assert.doesNotMatch(component, /lg:grid-cols-2/)
})

test("compact linked task exposes an inline Open action, not a duplicated card", () => {
  assert.match(component, /label="Open linked task"/)
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

test("MEET tabs delegate ARIA roles/keyboard nav to the canonical Tabs primitive, not a hand-rolled tablist", () => {
  assert.match(component, /import \{ Tabs, TabList, Tab, TabPanel \} from ["']@\/components\/ui\/tabs["']/)
  assert.match(component, /<Tabs\s*\n\s*value=\{activeTab\}/)
  assert.match(component, /<TabList\s*\n\s*activateOnFocus\s*\n\s*aria-label="MEET sections"/)
  // No hand-rolled ARIA attributes duplicating what Tabs/TabList/Tab/TabPanel
  // already own internally (role, aria-selected, aria-controls, tabIndex).
  assert.doesNotMatch(component, /role="tablist"/)
  assert.doesNotMatch(component, /role="tab"/)
  assert.doesNotMatch(component, /role="tabpanel"/)
})

test("all three MEET tabs are wired through matching Tab/TabPanel value pairs with stable ids", () => {
  for (const tab of ["details", "sources", "review"]) {
    assert.match(component, new RegExp(`<TabPanel\\s*\\n\\s*value="${tab}"\\s*\\n\\s*id=\\{meetTabPanelId\\("${tab}"\\)\\}`))
  }
  assert.match(component, /\{MEET_WORKSPACE_TABS.map\(\(tab\) => \(\s*\n\s*<Tab\s*\n\s*key=\{tab\}\s*\n\s*value=\{tab\}\s*\n\s*id=\{meetTabButtonId\(tab\)\}/)
})

test("tab switching still routes through the transcript-edit-exit guard (switchTab), and a canceled switch cancels the Tabs change event", () => {
  assert.match(component, /onValueChange=\{\(value, eventDetails\) => \{\s*\n\s*if \(!switchTab\(value as MeetWorkspaceTab\)\) eventDetails\.cancel\(\)/)
})

test("exactly one autosave status region exists (no header/footer duplication)", () => {
  // Migrated onto the shared SavedState primitive (components/ui/saved-state.tsx):
  // the saving/saved/failed rendering now lives there, so this checks for
  // exactly one SavedState usage bound to saveStatus instead of the old
  // inline saveStatus === "..." conditionals.
  assert.equal(component.match(/<SavedState\b/g)?.length, 1)
  assert.equal(component.match(/status=\{saveStatus\}/g)?.length, 1)
})

test("no Save or Revert controls, and no legacy onApply path", () => {
  assert.doesNotMatch(component, />\s*Save\s*</)
  assert.doesNotMatch(component, />\s*Revert\s*</)
  assert.equal(component.includes("onApply"), false)
})

test("one stable footer renders across every tab", () => {
  assert.equal(component.match(/<ModalFooter/g)?.length, 1)
  // Delete meeting is footer-scoped to Details only.
  assert.match(component, /isDetails && \(\s*\n\s*<Button/)
})

test("migrated MEET surface avoids unreadable 8-9px metadata", () => {
  assert.doesNotMatch(component, /text-\[8px\]/)
  assert.doesNotMatch(component, /text-\[9px\]/)
})

test("MEET modal composes the canonical ModalShell - no dark-only .meet-shell override remains", () => {
  assert.match(component, /import \{ ModalShell, ModalHeader, ModalBody, ModalFooter \} from ["']@\/components\/ui\/modal-shell["']/)
  assert.match(component, /<ModalShell titleId="meet-details-title"/)
  // Note: `from "@/lib/meet-shell"` (the unrelated tab-id/geometry helper
  // module) legitimately still contains the substring "meet-shell" - only
  // the CSS class usage/definition is checked here.
  assert.doesNotMatch(component, /className="meet-shell\b/)
  assert.doesNotMatch(globals, /\.meet-shell\b/)
})

test("recording start still flushes autosave before recording", () => {
  assert.match(component, /onBeforeRecordingStart=\{flushAutosave\}/)
})
