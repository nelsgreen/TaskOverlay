import assert from "node:assert/strict"
import test from "node:test"
import { proposedActionLabel } from "./meeting-proposed-action.ts"

test("every production proposed-action type maps to a human-readable label", () => {
  assert.equal(proposedActionLabel("CreateTask"), "Create task")
  assert.equal(proposedActionLabel("CreateWaitingTask"), "Create waiting task")
  assert.equal(proposedActionLabel("CreateFollowUpTask"), "Create follow-up task")
  assert.equal(proposedActionLabel("AddMeetingContextNote"), "Add to context")
})

test("labels never expose the raw enum casing", () => {
  for (const label of [
    proposedActionLabel("CreateTask"),
    proposedActionLabel("CreateWaitingTask"),
    proposedActionLabel("CreateFollowUpTask"),
    proposedActionLabel("AddMeetingContextNote"),
  ]) {
    assert.doesNotMatch(label, /^[A-Z][a-zA-Z]*$/)
  }
})
