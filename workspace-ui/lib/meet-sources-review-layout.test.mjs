import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import test from "node:test"

const source = readFileSync(
  new URL("../components/meet-sources-review.tsx", import.meta.url),
  "utf8",
)
const details = readFileSync(
  new URL("../components/meet-details-panel.tsx", import.meta.url),
  "utf8",
)
const globals = readFileSync(
  new URL("../app/globals.css", import.meta.url),
  "utf8",
)

const sourcesBody = source.slice(
  source.indexOf("export function MeetingSourcesWorkspace"),
  source.indexOf("export function MeetingReviewWorkspace"),
)

test("Sources keeps only the two column headings, no page heading or prose", () => {
  assert.doesNotMatch(sourcesBody, />MEET sources</)
  assert.doesNotMatch(sourcesBody, /Record locally or add durable managed copies/)
  assert.match(sourcesBody, /Recording &amp; audio/)
  assert.match(sourcesBody, /Transcripts &amp; screenshots/)
})

test("Sources uses a two-column layout: recording/audio left, transcripts/screenshots right", () => {
  assert.match(sourcesBody, /grid-cols-\[minmax\(0,1fr\)_minmax\(320px,0\.9fr\)\]/)
  const importAudioIndex = sourcesBody.indexOf('"importMeetingAudio"')
  const importTranscriptIndex = sourcesBody.indexOf('"importMeetingTranscript"')
  const captureScreenshotIndex = sourcesBody.indexOf('"captureMeetingScreenshot"')
  const recordingAssistantIndex = sourcesBody.indexOf("<MeetingAssistantSection")
  assert.ok(importAudioIndex < recordingAssistantIndex)
  assert.ok(importTranscriptIndex > recordingAssistantIndex)
  assert.ok(captureScreenshotIndex > recordingAssistantIndex)
})

test("transcript cards select as a whole card, with no Set active button", () => {
  assert.doesNotMatch(source, /Set active/)
  // Whole-card selection: click plus Enter/Space on the focusable card itself.
  assert.match(source, /role="radio"/)
  assert.match(source, /tabIndex=\{0\}/)
  assert.match(source, /onClick=\{activate\}/)
  assert.match(source, /event\.key !== "Enter" && event\.key !== " "/)
  // Nested actions never bubble into selection.
  assert.match(source, /event\.stopPropagation\(\)[\s\S]{0,80}onClick\(\)/)
})

test("active and inactive transcript cards keep identical geometry", () => {
  // The Active label occupies a reserved header slot in both states instead of
  // appearing and disappearing, and no state-dependent action is rendered.
  assert.match(source, /!transcript\.isActive && "invisible"/)
  assert.doesNotMatch(source, /\{!transcript\.isActive && \(\s*<SourceAction/)
  // Selection uses the same canonical row-selected pair Tree row selection
  // already uses (--row-selected/--row-selected-border): mixing --selection
  // into --surface reads as raised/lighter in Dark and tinted/slightly
  // darker in Light - never sunken or disabled in either theme - with the
  // violet check only as a secondary cue.
  assert.match(source, /border-row-selected-border bg-row-selected/)
})

test("Sources and Review no longer rely on a page-level scroll wrapper", () => {
  assert.doesNotMatch(source, /overflow-y-auto px-4 py-4/)
})

test("Review keeps exactly one scroll region per column and drops the stale viewport-relative cap", () => {
  const reviewBody = source.slice(source.indexOf("export function MeetingReviewWorkspace"))
  assert.doesNotMatch(reviewBody, /calc\(100vh-14rem\)/)
  assert.equal((reviewBody.match(/overflow-y-auto/g) ?? []).length, 2)
})

test("Review keeps its existing information architecture: transcript left, assistant stack right", () => {
  const reviewBody = source.slice(source.indexOf("export function MeetingReviewWorkspace"))
  assert.match(reviewBody, /Active transcript/)
  assert.match(reviewBody, /Meeting Assistant/)
  assert.match(reviewBody, /Visual references/)
  assert.match(reviewBody, /Project context updates/)
})

test("transcript speaker controls are transcript-level and use the internal discard dialog", () => {
  assert.match(source, /aria-label="Current user speaker"/)
  assert.match(source, /aria-label="Merge source speaker"/)
  assert.match(source, /The source speaker disappears from this revision; the target speaker remains\./)
  assert.doesNotMatch(source, /Mark as You/)
  assert.doesNotMatch(source, /window\.confirm\("Discard unsaved transcript edits\?"\)/)
  assert.match(source, /Discard unsaved transcript edits\?/)
  assert.match(source, />\s*Keep editing\s*</)
  assert.match(source, />\s*Discard\s*</)
})

test("Sources groups immutable transcript revisions and removes duplicate metadata", () => {
  assert.match(source, /function groupTranscriptLineages/)
  assert.match(source, /Latest edited revision/)
  assert.match(source, /Previous revisions \(\{lineage\.previousEdits\.length\}\)/)
  assert.match(source, /function transcriptMetadata/)
  assert.match(source, /parts\.join\(" · "\)/)
})

test("MEET no longer scopes a dark-only .meet-shell token override - it follows the canonical Light/Dark surface tokens like every other Workspace modal", () => {
  assert.doesNotMatch(globals, /\.meet-shell\b/)
  assert.doesNotMatch(globals, /--meet-content:/)
  assert.doesNotMatch(globals, /--meet-border-strong:/)
  assert.doesNotMatch(globals, /--meet-active:/)
  assert.doesNotMatch(globals, /--meet-selected:/)
  assert.doesNotMatch(globals, /--meet-selected-surface:/)
  // Note: `from "@/lib/meet-shell"` (the unrelated tab-id/geometry helper
  // module) legitimately still contains the substring "meet-shell".
  assert.doesNotMatch(details, /className="meet-shell\b/)
  assert.doesNotMatch(details, /var\(--meet-/)
  assert.doesNotMatch(source, /var\(--meet-/)
  // Cards are solid surfaces, not near-invisible alpha tints of the background.
  assert.doesNotMatch(source, /bg-card\/40|bg-card\/30/)
})

test("Details Link field surfaces inline feedback for an invalid URL instead of a global Sources error", () => {
  assert.match(details, /isValidMeetingLinkUrl\(draft\.link\)/)
  assert.match(details, /needs http:\/\/ or https:\/\//)
})
