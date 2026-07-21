import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import test from "node:test"

const meetDetailsSrc = readFileSync(new URL("../components/meet-details-panel.tsx", import.meta.url), "utf8")
const modalShellSrc = readFileSync(new URL("../components/ui/modal-shell.tsx", import.meta.url), "utf8")
const meetSourcesReviewSrc = readFileSync(new URL("../components/meet-sources-review.tsx", import.meta.url), "utf8")
const meetingAssistantSrc = readFileSync(new URL("../components/meeting-assistant-section.tsx", import.meta.url), "utf8")
const taskContextBlockSrc = readFileSync(new URL("../components/task-context-block.tsx", import.meta.url), "utf8")

// ---------------------------------------------------------------------------
// 1. Top-level MEET tabs: centered, equal-width, prominent
// ---------------------------------------------------------------------------

test("MEET tabs are a centered, bounded-width nav region with equal-width, larger tabs - not tiny left-aligned text links", () => {
  assert.match(meetDetailsSrc, /<TabList[\s\S]{0,200}mx-auto w-full max-w-md justify-center/)
  assert.match(meetDetailsSrc, /<Tab[\s\S]{0,200}flex flex-1 items-center justify-center[\s\S]{0,120}px-4 py-2\.5/)
})

test("the active MEET tab has a real raised/selected treatment (same contract as the canonical SegmentedControl), not just an underline", () => {
  assert.match(meetDetailsSrc, /aria-selected:bg-surface-raised/)
  assert.match(meetDetailsSrc, /aria-selected:shadow-\[var\(--shadow-1\),inset_0_0_0_1px_color-mix\(in_oklch,var\(--selection\)_40%,transparent\)\]/)
})

test("MEET tabs still delegate real aria-selected/keyboard semantics to the canonical Tabs primitive - no hand-rolled tablist/tab/tabpanel roles", () => {
  assert.doesNotMatch(meetDetailsSrc, /role="tablist"/)
  assert.doesNotMatch(meetDetailsSrc, /role="tab"/)
  assert.doesNotMatch(meetDetailsSrc, /role="tabpanel"/)
  assert.match(meetDetailsSrc, /from ["']@\/components\/ui\/tabs["']/)
})

// ---------------------------------------------------------------------------
// 2. ModalShell geometry is unchanged by the visual-rescue pass
// ---------------------------------------------------------------------------

test("ModalShell's geometry contract (typed numeric props -> min()/min() inline style) is unchanged", () => {
  assert.match(modalShellSrc, /maxWidthPx: number/)
  assert.match(modalShellSrc, /maxHeightPx: number/)
  assert.match(modalShellSrc, /width: `min\(\$\{maxWidthPx\}px, \$\{viewportWidthPercent\}vw\)`/)
  assert.match(modalShellSrc, /height: `min\(\$\{maxHeightPx\}px, \$\{viewportHeightPercent\}dvh\)`/)
  assert.match(meetDetailsSrc, /<ModalShell titleId="meet-details-title" \{\.\.\.MEET_SHELL_GEOMETRY\}>/)
})

// ---------------------------------------------------------------------------
// 3. Context block badges share canonical geometry
// ---------------------------------------------------------------------------

test("Source/Item/Status context badges share one geometry class (height/radius/padding/font/baseline) - only color differs", () => {
  assert.match(taskContextBlockSrc, /const contextBadgeClass =\s*\n?\s*"mt-0\.5 inline-flex shrink-0 items-center rounded-md border px-1\.5 py-0\.5 text-\[10px\] font-semibold leading-none"/)
  const sourceBadge = taskContextBlockSrc.slice(taskContextBlockSrc.indexOf("function SourceBadge"), taskContextBlockSrc.indexOf("function ItemBadge"))
  const itemBadge = taskContextBlockSrc.slice(taskContextBlockSrc.indexOf("function ItemBadge"), taskContextBlockSrc.indexOf("function StatusLabel"))
  const statusLabel = taskContextBlockSrc.slice(taskContextBlockSrc.indexOf("function StatusLabel"), taskContextBlockSrc.indexOf("function LinkExistingModal"))
  for (const badgeFn of [sourceBadge, itemBadge, statusLabel]) {
    assert.match(badgeFn, /contextBadgeClass/)
  }
  // StatusLabel no longer uses a different radius/padding/font-family (it
  // previously used `rounded border px-1 py-0.5 font-mono text-[9px]`,
  // visibly unlike its ItemBadge sibling on the same row).
  assert.doesNotMatch(statusLabel, /font-mono/)
})

// ---------------------------------------------------------------------------
// 4/5. Transcript cards: distinct resting/selected, never disabled-looking
// ---------------------------------------------------------------------------

test("transcript cards use the canonical row-selected pair for the selected state - never --surface-sunken/--border-strong (which read as sunken/disabled), and resting/selected are visibly distinct", () => {
  const cardFn = meetSourcesReviewSrc.slice(
    meetSourcesReviewSrc.indexOf("function TranscriptSourceCard"),
    meetSourcesReviewSrc.indexOf("function transcriptOriginBadgeClass") === -1
      ? meetSourcesReviewSrc.indexOf("function TranscriptSourceCard") + 3000
      : meetSourcesReviewSrc.indexOf("function transcriptOriginBadgeClass"),
  )
  assert.match(cardFn, /border-row-selected-border bg-row-selected/)
  assert.match(cardFn, /border-border bg-card/)
  assert.doesNotMatch(cardFn, /transcript\.isActive\s*\n?\s*\?\s*"border-border-strong bg-surface-sunken"/)
})

test("resting transcript-card hover lightens (bg-surface-raised), matching the app-wide 'hover must read as lifted, never sunken' convention - not the old bg-secondary (sunken) hover", () => {
  assert.match(meetSourcesReviewSrc, /hover:border-border-strong hover:bg-surface-raised/)
  assert.doesNotMatch(meetSourcesReviewSrc, /hover:border-border-strong hover:bg-secondary/)
})

test("the Transcripts/Screenshots section trays sit one tier below their nested transcript/screenshot cards (bg-surface-sunken tray containing bg-card cards) - cards no longer share their container's exact surface", () => {
  const sourcesFn = meetSourcesReviewSrc.slice(
    meetSourcesReviewSrc.indexOf("export function MeetingSourcesWorkspace"),
    meetSourcesReviewSrc.indexOf("export function MeetingReviewWorkspace"),
  )
  assert.equal((sourcesFn.match(/rounded-lg border border-border bg-surface-sunken p-3/g) ?? []).length, 2)
})

test("Active Transcript / Meeting Assistant / Visual references stay peer panels in Review (all bg-card, not one sunken)", () => {
  const reviewFn = meetSourcesReviewSrc.slice(meetSourcesReviewSrc.indexOf("export function MeetingReviewWorkspace"))
  assert.equal((reviewFn.match(/rounded-lg border border-border bg-card p-3/g) ?? []).length, 2)
  assert.match(reviewFn, /rounded-lg border border-border bg-card"/)
})

// ---------------------------------------------------------------------------
// 6. Read-only device/status information is not presented as an editable field
// ---------------------------------------------------------------------------

test("System/Microphone track-health status uses a distinct bg-surface-sunken status chip, not the --field surface real inputs use", () => {
  const trackHealthFn = meetingAssistantSrc.slice(
    meetingAssistantSrc.indexOf("function TrackHealth"),
    meetingAssistantSrc.indexOf("function RecordingOperationStatus"),
  )
  assert.match(trackHealthFn, /bg-surface-sunken/)
  assert.doesNotMatch(trackHealthFn, /bg-field\b/)
})

// ---------------------------------------------------------------------------
// 7. Footer uses normal canonical action sizing
// ---------------------------------------------------------------------------

test("ModalFooter has generous padding (py-4) proportionate to a large modal, not a thin strip", () => {
  assert.match(modalShellSrc, /border-t border-border bg-surface-sunken px-5 py-4/)
})

test("MEET footer's Close button uses the Button primitive's normal default size (no size=\"sm\" override) - an adequate target for the modal's primary exit action", () => {
  const footerBlock = meetDetailsSrc.slice(meetDetailsSrc.indexOf("<ModalFooter>"), meetDetailsSrc.indexOf("</ModalFooter>"))
  assert.match(footerBlock, /<Button type="button" tone="secondary" onClick=\{\(\) => void requestClose\("explicit"\)\}>/)
})

// ---------------------------------------------------------------------------
// 8. Apply selected actions: bounded canonical primary treatment
// ---------------------------------------------------------------------------

test("Apply selected actions uses the canonical Button primary tone, bounded to its natural width (not a w-full slab filling the entire card)", () => {
  const applyBlock = meetingAssistantSrc.slice(
    meetingAssistantSrc.indexOf("Apply selected actions") - 1100,
    meetingAssistantSrc.indexOf("Apply selected actions") + 50,
  )
  assert.match(applyBlock, /<Button/)
  assert.match(applyBlock, /tone="primary"/)
  assert.doesNotMatch(applyBlock, /w-full items-center justify-center gap-1\.5 rounded-md bg-primary/)
})

// ---------------------------------------------------------------------------
// 9. MEET identity, recording, destructive semantics stay separated
// ---------------------------------------------------------------------------

test("recording and destructive tones are untouched by the visual-rescue pass - still their own dedicated roles", () => {
  assert.match(meetingAssistantSrc, /recording\.state === "Recording" \? "animate-pulse bg-red-500"/)
  assert.match(meetingAssistantSrc, /recording\.state === "Failed" \? "bg-destructive"/)
  assert.match(meetDetailsSrc, /tone="destructive"/)
})

test("MEET identity markers (badges/icons) are untouched by the visual-rescue pass", () => {
  assert.match(meetDetailsSrc, /bg-status-meet\/15 px-2 py-0\.5 text-\[11px\] font-semibold uppercase tracking-wide text-status-meet/)
  assert.match(meetSourcesReviewSrc, /<Bot className="size-4 text-status-meet" \/>/)
})

// ---------------------------------------------------------------------------
// 10. Existing behavior wiring is unchanged
// ---------------------------------------------------------------------------

test("tab switching still routes through the transcript-edit-exit guard, and ActionButton/recording-policy call sites keep their existing props/behavior", () => {
  assert.match(meetDetailsSrc, /onValueChange=\{\(value, eventDetails\) => \{\s*\n\s*if \(!switchTab\(value as MeetWorkspaceTab\)\) eventDetails\.cancel\(\)/)
  // ActionButton keeps the same public prop shape (label/icon/onClick/
  // disabled/primary/danger/busy) even though its internals now delegate to
  // Button - no call site needed to change.
  assert.match(meetingAssistantSrc, /function ActionButton\(\{\s*\n\s*label,\s*\n\s*icon: Icon,\s*\n\s*onClick,\s*\n\s*disabled = false,\s*\n\s*primary = false,\s*\n\s*danger = false,\s*\n\s*busy = false,/)
  assert.match(meetingAssistantSrc, /<SegmentedControl\s*\n\s*value=\{meet\.recordingPolicy \?\? "Inherit"\}\s*\n\s*onValueChange=\{\(value\) => onRecordingPolicyChange\?\.\(value\)\}/)
})

test("recording select and transcription-range fields keep their existing state/handlers, just via canonical Select/Input/Button primitives", () => {
  assert.match(meetingAssistantSrc, /<Select\s*\n\s*value=\{selectedRecording\?\.id \?\? ""\}\s*\n\s*onChange=\{\(event\) => setSelectedRecordingId\(event\.target\.value\)\}/)
  assert.match(meetingAssistantSrc, /value=\{rangeFrom\}\s*\n\s*onChange=\{\(event\) => setRangeFrom\(event\.target\.value\)\}/)
  assert.match(meetSourcesReviewSrc, /onClick=\{\(\) => \{\s*\n\s*const action = isPlaying \? "pause" : "play"/)
})
