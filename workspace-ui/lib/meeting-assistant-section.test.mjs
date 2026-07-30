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
  assert.match(details, /label="Open call link"/)
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

// ---------------------------------------------------------------------------
// Regression: Transcription range was hidden for any recording that wasn't
// Imported audio (sourceKind === "Imported"), so a normal recorded MEET
// whose full audio the provider rejected (too long) had no way to retry
// with a shorter range. Fixed by gating on completion/no-usable-transcript
// state instead of origin.
// ---------------------------------------------------------------------------

function recordingCardBlock() {
  return assistant.slice(
    assistant.indexOf("function RecordingCard"),
    assistant.indexOf("function AnalysisReview"),
  )
}

test("Transcription range visibility is driven by completion/transcript state (showTranscriptionRange), not recording origin", () => {
  const cardBlock = recordingCardBlock()
  assert.match(cardBlock, /const showTranscriptionRange = canTranscribe && \(!recording\.hasTranscript \|\| recording\.state === "Failed"\)/)
  assert.match(cardBlock, /\{showTranscriptionRange && \(/)
  // No more direct sourceKind gate on the range block itself.
  assert.doesNotMatch(cardBlock, /\{recording\.sourceKind === "Imported" && \(\s*\n\s*<div className="space-y-2">\s*\n\s*<div className="flex items-center justify-between gap-2">\s*\n\s*<span className="text-\[10px\] font-semibold uppercase tracking-wide text-muted-foreground">\s*\n\s*Transcription range/)
})

test("the range condition covers: completed audio with no transcript yet, and failed transcription - both true regardless of sourceKind", () => {
  const cardBlock = recordingCardBlock()
  assert.match(cardBlock, /const canTranscribe = !recording\.keepLocalOnly\s*\n\s*&& recording\.hasMixedAudio\s*\n\s*&& \["Recorded", "TranscriptReady", "Ready", "Failed"\]\.includes\(recording\.state\)/)
  // showTranscriptionRange = canTranscribe && (no transcript yet OR failed) -
  // both disjuncts are reachable independent of "Imported".
  assert.match(cardBlock, /!recording\.hasTranscript \|\| recording\.state === "Failed"/)
})

test("existing successful-transcript behavior is unchanged: a completed, non-Failed recording that already has a usable transcript does not show the range block", () => {
  // showTranscriptionRange is false whenever hasTranscript is true and
  // state isn't Failed, because canTranscribe's own state list already
  // excludes "Processing"/"Transcribing"/"Analyzing", and the (!hasTranscript
  // || Failed) clause is false once a transcript exists and nothing failed.
  const cardBlock = recordingCardBlock()
  assert.match(cardBlock, /showTranscriptionRange = canTranscribe && \(!recording\.hasTranscript \|\| recording\.state === "Failed"\)/)
})

test("the origin-only 'Imported' provenance line is untouched - only the range block's gate changed", () => {
  const cardBlock = recordingCardBlock()
  assert.match(cardBlock, /\{recording\.sourceKind === "Imported" && \(\s*\n\s*<p className="break-words">\s*\n\s*Origin: <strong className="text-foreground">Imported<\/strong>/)
})

test("Transcribe now / retry, Save range, and Use full recording still target the same recording id and range fields - unchanged wiring", () => {
  const cardBlock = recordingCardBlock()
  assert.match(cardBlock, /type: "setImportedAudioRange",\s*\n\s*recordingId: recording\.id,\s*\n\s*fromSeconds: null,\s*\n\s*untilSeconds: null,/)
  assert.match(cardBlock, /type: "setImportedAudioRange",\s*\n\s*recordingId: recording\.id,\s*\n\s*fromSeconds: from,\s*\n\s*untilSeconds: until,/)
  // Range inputs still validate against the recording's real duration.
  assert.match(cardBlock, /max=\{recording\.durationSeconds \|\| undefined\}/)
})
