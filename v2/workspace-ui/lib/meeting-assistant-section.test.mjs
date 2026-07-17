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

test("call link only offers Join call for a validated URL, not raw Link text", () => {
  assert.match(assistant, /isValidMeetingLinkUrl\(meet\.link\)/)
  assert.match(assistant, /label="Join call"/)
  assert.doesNotMatch(assistant, /Open meeting link/)
})

test("keeping audio local uses clear on/off wording and preserves the existing command", () => {
  assert.match(assistant, /"Audio stays local" : "Keep audio local"/)
  assert.doesNotMatch(assistant, /"Local only" : "Keep local only"/)
  // Still the same underlying boolean + command, just relabeled.
  assert.match(assistant, /keepLocalOnly: !recording\.keepLocalOnly/)
  assert.match(assistant, /type: "setMeetingRecordingLocalOnly"/)
  // Compact explanatory copy is contextual (only while local-only is on), not a permanent paragraph.
  assert.match(assistant, /\{recording\.keepLocalOnly && \(/)
  assert.match(assistant, /Cloud transcription is disabled for this recording/)
})

test("recording policy shows a compact effective state instead of a permanent paragraph", () => {
  assert.match(assistant, /Effective: \{defaultRecordingPolicy/)
  assert.doesNotMatch(assistant, /inherits the current global Settings preference/)
})

test("proposed actions show human-readable labels, never the raw enum", () => {
  assert.match(assistant, /proposedActionLabel\(action\.type\)/)
  assert.doesNotMatch(assistant, /<span>\{action\.type\}<\/span>/)
})

test("proposed action project defaults to the MEET's real project name", () => {
  assert.match(assistant, /const meetProjectName = projects\.find/)
  assert.match(assistant, /<option value="">\{meetProjectName\}<\/option>/)
  assert.doesNotMatch(assistant, /<option value="">MEET project<\/option>/)
})

test("confidence is demoted to secondary metadata, not a primary badge", () => {
  // No longer in the primary uppercase type/reviewState row.
  assert.doesNotMatch(assistant, /<span>\{Math\.round\(action\.confidence \* 100\)\}%<\/span>/)
  // Still available, but folded into the muted rationale line as plain text.
  assert.match(assistant, /model confidence: \$\{Math\.round\(action\.confidence \* 100\)\}%/)
})

test("transcription stays gated on keepLocalOnly, matching the preserved persistence model", () => {
  assert.match(assistant, /const canTranscribe = !recording\.keepLocalOnly/)
})

test("Sources and Review both pass the MEET into AnalysisReview for project labeling", () => {
  assert.equal((sourcesReview.match(/<AnalysisReview/g) ?? []).length, 1)
  assert.match(sourcesReview, /<AnalysisReview[\s\S]{0,80}meet=\{meet\}/)
  assert.match(assistant, /<AnalysisReview[\s\S]{0,80}meet=\{meet\}/)
})
