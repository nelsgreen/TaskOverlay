import assert from "node:assert/strict"
import test from "node:test"
import { shouldCloseMeetModal } from "./meet-modal-policy.ts"

test("MEET modal never closes from its backdrop", () => {
  let confirmations = 0
  assert.equal(shouldCloseMeetModal("backdrop", false, () => {
    confirmations += 1
    return true
  }), false)
  assert.equal(confirmations, 0)
})

test("clean MEET modal closes explicitly without confirmation", () => {
  let confirmations = 0
  assert.equal(shouldCloseMeetModal("explicit", false, () => {
    confirmations += 1
    return false
  }), true)
  assert.equal(confirmations, 0)
})

test("unsaved MEET edits require confirmation for close, Escape, and navigation", () => {
  for (const reason of ["explicit", "escape", "navigate"]) {
    assert.equal(shouldCloseMeetModal(reason, true, () => false), false)
    assert.equal(shouldCloseMeetModal(reason, true, () => true), true)
  }
})
