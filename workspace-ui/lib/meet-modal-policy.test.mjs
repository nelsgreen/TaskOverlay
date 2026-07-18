import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import test from "node:test"
import {
  isUntouchedNewMeeting,
  MEET_MODAL_ACTIONS,
  shouldCloseMeetModal,
} from "./meet-modal-policy.ts"

const initial = {
  projectId: "project-a",
  titleIsGenerated: true,
  date: "2026-07-17",
  startTime: "09:00",
  duration: "30m",
  recordingPolicy: "Inherit",
}

test("MEET modal never closes from its backdrop", () => {
  assert.equal(shouldCloseMeetModal("backdrop"), false)
})

test("autosaved MEET modal closes explicitly, by Escape, or for navigation", () => {
  for (const reason of ["explicit", "escape", "navigate"]) {
    assert.equal(shouldCloseMeetModal(reason), true)
  }
})

test("MEET modal exposes Close and Delete without Save or Revert", () => {
  assert.deepEqual(Object.values(MEET_MODAL_ACTIONS), ["Close", "Delete meeting"])
  assert.equal(Object.values(MEET_MODAL_ACTIONS).includes("Save"), false)
  assert.equal(Object.values(MEET_MODAL_ACTIONS).includes("Revert"), false)

  const component = readFileSync(
    new URL("../components/meet-details-panel.tsx", import.meta.url),
    "utf8",
  )
  assert.doesNotMatch(component, />\s*Save\s*</)
  assert.doesNotMatch(component, />\s*Revert\s*</)
  assert.equal(component.includes("onApply"), false)
})

test("only a completely untouched generated MEET is eligible for close cleanup", () => {
  assert.equal(isUntouchedNewMeeting({
    initial,
    current: initial,
    hasRecordingOrSource: false,
    hasContextLink: false,
  }), true)

  for (const current of [
    { ...initial, titleIsGenerated: false },
    { ...initial, notes: "Agenda" },
    { ...initial, date: "2026-07-18" },
    { ...initial, linkedTaskId: "task-a" },
    { ...initial, recordingPolicy: "AutoRecord" },
  ]) {
    assert.equal(isUntouchedNewMeeting({
      initial,
      current,
      hasRecordingOrSource: false,
      hasContextLink: false,
    }), false)
  }

  assert.equal(isUntouchedNewMeeting({
    initial,
    current: initial,
    hasRecordingOrSource: true,
    hasContextLink: false,
  }), false)
  assert.equal(isUntouchedNewMeeting({
    initial,
    current: initial,
    hasRecordingOrSource: false,
    hasContextLink: true,
  }), false)
})
