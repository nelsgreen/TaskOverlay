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

test("Sources uses a two-column layout: recording/audio left, transcripts/screenshots right", () => {
  const sourcesBody = source.slice(
    source.indexOf("export function MeetingSourcesWorkspace"),
    source.indexOf("export function MeetingReviewWorkspace"),
  )
  assert.match(sourcesBody, /grid-cols-\[minmax\(0,1fr\)_minmax\(320px,0\.9fr\)\]/)
  // Import audio lives with recording/audio (left); import transcript and capture
  // screenshot live with their own sections (right) — not one shared toolbar.
  const importAudioIndex = sourcesBody.indexOf('"importMeetingAudio"')
  const importTranscriptIndex = sourcesBody.indexOf('"importMeetingTranscript"')
  const captureScreenshotIndex = sourcesBody.indexOf('"captureMeetingScreenshot"')
  const recordingAssistantIndex = sourcesBody.indexOf("<MeetingAssistantSection")
  assert.ok(importAudioIndex < recordingAssistantIndex)
  assert.ok(importTranscriptIndex > recordingAssistantIndex)
  assert.ok(captureScreenshotIndex > recordingAssistantIndex)
})

test("Sources and Review no longer rely on a page-level scroll wrapper", () => {
  assert.doesNotMatch(source, /overflow-y-auto px-4 py-4/)
})

test("Review keeps exactly one scroll region per column and drops the stale viewport-relative cap", () => {
  const reviewBody = source.slice(source.indexOf("export function MeetingReviewWorkspace"))
  assert.doesNotMatch(reviewBody, /calc\(100vh-14rem\)/)
  // Left (transcript) and right (assistant/visual refs) each own one overflow-y-auto region.
  assert.equal((reviewBody.match(/overflow-y-auto/g) ?? []).length, 2)
})

test("Review keeps its existing information architecture: transcript left, assistant stack right", () => {
  const reviewBody = source.slice(source.indexOf("export function MeetingReviewWorkspace"))
  assert.match(reviewBody, /Active transcript/)
  assert.match(reviewBody, /Meeting Assistant/)
  assert.match(reviewBody, /Visual references/)
  assert.match(reviewBody, /Project context updates/)
})

test("Details Link field surfaces inline feedback for an invalid URL instead of a global Sources error", () => {
  assert.match(details, /isValidMeetingLinkUrl\(draft\.link\)/)
  assert.match(details, /needs http:\/\/ or https:\/\//)
})
