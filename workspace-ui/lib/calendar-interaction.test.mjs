import assert from "node:assert/strict"
import fs from "node:fs"
import test from "node:test"
import { CalendarActivationGuard } from "./calendar-interaction.ts"

test("normal pointer and keyboard activation open task and MEET details", () => {
  for (const itemKind of ["TASK", "MEET"]) {
    const guard = new CalendarActivationGuard()
    assert.equal(guard.shouldActivate(1), true, `${itemKind} pointer click should activate`)
    assert.equal(guard.shouldActivate(0), true, `${itemKind} keyboard click should activate`)
  }
})

test("task and MEET drag or resize suppress only their fall-through click", () => {
  for (const itemKind of ["TASK", "MEET"]) {
    for (const manipulation of ["drag", "resize"]) {
      const guard = new CalendarActivationGuard()
      guard.beginPointerInteraction()
      guard.completeManipulation()
      assert.equal(guard.shouldActivate(1), false, `${itemKind} ${manipulation} must not activate`)
      assert.equal(guard.shouldActivate(1), true, `${itemKind} next genuine click should activate`)
    }
  }

  const guard = new CalendarActivationGuard()
  guard.completeManipulation()
  guard.beginPointerInteraction()
  assert.equal(guard.shouldActivate(1), true)
})

test("day and week blocks share the manipulation guard", () => {
  const source = fs.readFileSync(new URL("../components/calendar-view.tsx", import.meta.url), "utf8")
  assert.match(source, /activationGuardRef\.current\.completeManipulation\(\)/)
  assert.match(source, /onBlockPointerDown=\{beginBlockPointer\}/)
  assert.match(source, /onBlockActivate=\{activateBlock\}/)
  assert.doesNotMatch(source, /onResizeBlock[\s\S]{0,220}onSelectMeet\(block\.meetId\)/)
})
