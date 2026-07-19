import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import test from "node:test"
import {
  closeMeetEditor,
  MEET_MODAL_ACTIONS,
  shouldCloseMeetModal,
} from "./meet-modal-policy.ts"

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

test("closing the MEET editor flushes once and sends no destructive mutation", async () => {
  let flushes = 0
  let closes = 0
  const closed = await closeMeetEditor(
    "explicit",
    async () => {
      flushes += 1
      return true
    },
    () => { closes += 1 },
  )

  assert.equal(closed, true)
  assert.equal(flushes, 1)
  assert.equal(closes, 1)

  const component = readFileSync(
    new URL("../components/meet-details-panel.tsx", import.meta.url),
    "utf8",
  )
  const closePath = component.slice(
    component.indexOf("const requestClose ="),
    component.indexOf("requestCloseAfterDiscardRef.current = requestClose"),
  )
  assert.doesNotMatch(closePath, /onDelete|deleteMeeting/)
})

test("failed autosave keeps the MEET editor open without a command loop", async () => {
  let closes = 0
  let flushes = 0
  const closed = await closeMeetEditor(
    "explicit",
    async () => {
      flushes += 1
      return false
    },
    () => { closes += 1 },
  )

  assert.equal(closed, false)
  assert.equal(flushes, 1)
  assert.equal(closes, 0)
})
