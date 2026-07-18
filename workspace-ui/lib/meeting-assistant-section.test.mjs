import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import test from "node:test"

const assistant = readFileSync(
  new URL("../components/meeting-assistant-section.tsx", import.meta.url),
  "utf8",
)
const sourcesReview = readFileSync(
  new URL("../components/meet-sources-review.tsx", import.meta.url),
  "utf8",
)
const details = readFileSync(
  new URL("../components/meet-details-panel.tsx", import.meta.url),
  "utf8",
)

test("Sources has no call-link action; Details owns a compact validated open control", () => {
  assert.doesNotMatch(assistant, /Join call|Open meeting link/)
  assert.doesNotMatch(sourcesReview, /Join call|Open meeting link/)
  assert.doesNotMatch(assistant, /openMeetingLink/)
  // Details: icon-only control, labeled, shown only for a valid http(s) URL.
  assert.match(details, /aria-label="Open call link"/)
  assert.match(details, /isValidMeetingLinkUrl\(draft\.link\) && onMeetingAssistantCommand/)
  assert.match(details, /type: "openMeetingLink"/)
})

test("normal recording actions expose no local-only primary toggle", () => {
  assert.doesNotMatch(assistant, /Keep audio local|Audio stays local|Keep local only/)
  assert.doesNotMatch(assistant, /Cloud transcription is disabled/)
})

test("an existing keepLocalOnly recording shows blocked transcription with one Allow action", () => {
  assert.match(assistant, /recording\.keepLocalOnly && !isRuntimeActive/)
  assert.match(assistant, /Cloud transcription is blocked for this recording\./)
  assert.match(assistant, /Allow transcription/)
  // The action clears the existing flag through the current connected command.
  assert.match(assistant, /type: "setMeetingRecordingLocalOnly",[\s\S]{0,120}keepLocalOnly: false/)
  // Transcription stays gated on the persisted boolean.
  assert.match(assistant, /const canTranscribe = !recording\.keepLocalOnly/)
})

test("range-save success confirms in a fixed reserved slot, never a document-flow banner", () => {
  const controller = readFileSync(new URL("./meeting-operation-state.ts", import.meta.url), "utf8")
  assert.doesNotMatch(controller, /Range saved for the next transcription/)
  assert.match(assistant, /confirmRangeSaved/)
  // The slot keeps its dimensions whether or not the confirmation is showing.
  assert.match(assistant, /w-12 shrink-0[\s\S]{0,120}aria-live="polite"/)
  assert.match(assistant, /\{rangeSaved && \(/)
})

test("recording policy shows a compact effective state instead of a permanent paragraph", () => {
  assert.match(assistant, /Effective: \{defaultRecordingPolicy/)
  assert.doesNotMatch(assistant, /inherits the current global Settings preference/)
})

test("Sources keeps no permanent Recording & Meeting Assistant heading or capture prose", () => {
  assert.doesNotMatch(assistant, /Recording &amp; Meeting Assistant/)
  assert.doesNotMatch(assistant, /Local two-track capture/)
})

test("Review shows human-readable action labels and no raw enum or status strings", () => {
  assert.match(assistant, /proposedActionLabel\(action\.type\)/)
  assert.doesNotMatch(assistant, /<span>\{action\.type\}<\/span>/)
  // reviewState and analysis.state are workflow-internal — the checkbox and
  // apply/reject flow already represent review state.
  assert.doesNotMatch(assistant, /\{action\.reviewState\}/)
  assert.doesNotMatch(assistant, /\{analysis\.state\}/)
})

test("Review renders no model-confidence output", () => {
  assert.doesNotMatch(assistant, /confidence \* 100|model confidence|action\.confidence/)
  assert.doesNotMatch(sourcesReview, /confidence/)
})

test("proposed action project defaults to the MEET's real project name", () => {
  assert.match(assistant, /const meetProjectName = projects\.find/)
  assert.match(assistant, /<option value="">\{meetProjectName\}<\/option>/)
  assert.doesNotMatch(assistant, /<option value="">MEET project<\/option>/)
})

test("Sources and Review both pass the MEET into AnalysisReview for project labeling", () => {
  assert.equal((sourcesReview.match(/<AnalysisReview/g) ?? []).length, 1)
  assert.match(sourcesReview, /<AnalysisReview[\s\S]{0,80}meet=\{meet\}/)
  assert.match(assistant, /<AnalysisReview[\s\S]{0,80}meet=\{meet\}/)
})
