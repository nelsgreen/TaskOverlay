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
  // Selection is a lighter neutral surface + strong neutral border, with the
  // violet check only as a secondary cue.
  assert.match(source, /border-\[var\(--meet-border-strong\)\] bg-\[var\(--meet-selected-surface\)\]/)
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

test("MEET surfaces use a layered charcoal hierarchy instead of black-on-black alphas", () => {
  // Scoped tokens define shell, content-column, card, and selected levels.
  assert.match(globals, /--meet-content:/)
  assert.match(globals, /--meet-selected-surface:/)
  // The shared content region sits on the lighter column surface for all tabs.
  assert.match(details, /bg-\[var\(--meet-content\)\]/)
  // Cards are solid surfaces, not near-invisible alpha tints of the background.
  assert.doesNotMatch(source, /bg-card\/40|bg-card\/30/)
})

test("Details Link field surfaces inline feedback for an invalid URL instead of a global Sources error", () => {
  assert.match(details, /isValidMeetingLinkUrl\(draft\.link\)/)
  assert.match(details, /needs http:\/\/ or https:\/\//)
})
