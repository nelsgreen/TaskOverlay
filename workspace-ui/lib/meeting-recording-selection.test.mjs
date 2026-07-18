import assert from "node:assert/strict"
import test from "node:test"
import { resolveMeetingRecordingSelection } from "./meeting-recording-selection.ts"

test("initial and newly created recordings select the deterministic latest item", () => {
  assert.equal(resolveMeetingRecordingSelection(null, null, "b", ["b", "a"]), "b")
  assert.equal(resolveMeetingRecordingSelection("b", "b", "c", ["c", "b", "a"]), "c")
})

test("manual older selection remains while the latest recording is unchanged", () => {
  assert.equal(resolveMeetingRecordingSelection("a", "b", "b", ["b", "a"]), "a")
})

test("missing selection falls back and an empty MEET clears selection", () => {
  assert.equal(resolveMeetingRecordingSelection("missing", "b", "b", ["b", "a"]), "b")
  assert.equal(resolveMeetingRecordingSelection("a", "a", null, []), null)
})
