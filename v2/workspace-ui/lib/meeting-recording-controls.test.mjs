import assert from "node:assert/strict"
import test from "node:test"
import { deriveMeetingRecordingControlState } from "./meeting-recording-controls.ts"

const selectedMeetingId = "meet-a"
const selectedRecording = { id: "recording-a", meetingId: selectedMeetingId }

test("fresh and manually selectable MEETs derive Start", () => {
  for (const scenario of ["manual-past", "inherit-current", "manual-future"]) {
    assert.deepEqual(
      deriveMeetingRecordingControlState(selectedMeetingId, null, null),
      { mode: "start", ownedActiveRecording: null },
      scenario,
    )
  }
})

test("pending Start and Stop states are mutually exclusive", () => {
  assert.equal(
    deriveMeetingRecordingControlState(selectedMeetingId, null, "start").mode,
    "starting",
  )
  assert.equal(
    deriveMeetingRecordingControlState(selectedMeetingId, selectedRecording, null).mode,
    "stop",
  )
  assert.equal(
    deriveMeetingRecordingControlState(selectedMeetingId, selectedRecording, "stop").mode,
    "stopping",
  )
})

test("another MEET or emergency recording derives conflict", () => {
  assert.equal(
    deriveMeetingRecordingControlState(
      selectedMeetingId,
      { id: "recording-b", meetingId: "meet-b" },
      null,
    ).mode,
    "conflict",
  )
  assert.equal(
    deriveMeetingRecordingControlState(
      selectedMeetingId,
      { id: "recording-emergency", meetingId: null },
      null,
    ).mode,
    "conflict",
  )
})
