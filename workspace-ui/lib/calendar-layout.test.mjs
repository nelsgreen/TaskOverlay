import assert from "node:assert/strict"
import test from "node:test"
import {
  DAY_END_MIN,
  DAY_START_MIN,
  GRID_HEIGHT,
  MIN_DURATION_MIN,
  PX_PER_MIN,
  WORKDAY_END_MIN,
  WORKDAY_START_MIN,
  clampStartForDuration,
  clipRangeToDay,
  durationFits,
  initialScrollMinute,
  initialScrollTop,
  minuteFromPointer,
  rangeForMove,
  resizeRange,
} from "./calendar-layout.ts"

test("full-day and workday constants stay independent", () => {
  assert.equal(DAY_START_MIN, 0)
  assert.equal(DAY_END_MIN, 1440)
  assert.equal(WORKDAY_START_MIN, 540)
  assert.equal(WORKDAY_END_MIN, 1080)
  assert.equal(GRID_HEIGHT, Math.round(1440 * PX_PER_MIN))
})

test("pointer snapping works across the complete day", () => {
  const top = 100
  assert.equal(minuteFromPointer(top, top), 0)
  assert.equal(minuteFromPointer(top, top + 8 * 60 * PX_PER_MIN), 480)
  assert.equal(minuteFromPointer(top, top + 19 * 60 * PX_PER_MIN), 1140)
  assert.equal(minuteFromPointer(top, top + 23.75 * 60 * PX_PER_MIN), 1425)
})

test("moving a block preserves duration at the end of day", () => {
  assert.deepEqual(rangeForMove(1430, 60), { startMin: 1380, endMin: 1440 })
  assert.deepEqual(rangeForMove(1400, 30), { startMin: 1400, endMin: 1430 })
  assert.equal(clampStartForDuration(1440, MIN_DURATION_MIN), 1425)
})

test("resize clamps to midnight boundaries and minimum duration", () => {
  assert.deepEqual(resizeRange(60, 120, "top", -120), { startMin: 0, endMin: 120 })
  assert.deepEqual(resizeRange(1380, 1425, "bottom", 120), { startMin: 1380, endMin: 1440 })
  assert.deepEqual(resizeRange(60, 120, "top", 120), { startMin: 105, endMin: 120 })
})

test("ranges never extend beyond the day", () => {
  assert.deepEqual(clipRangeToDay(-30, 1500), { startMin: 0, endMin: 1440 })
  assert.equal(durationFits(1380, 60), true)
  assert.equal(durationFits(1395, 60), false)
  assert.equal(durationFits(1425, 15), true)
})

test("initial scroll policy uses now only for the visible current day or week", () => {
  assert.equal(initialScrollMinute("day", "2026-07-19", "2026-07-19", false, 750), 750)
  assert.equal(initialScrollMinute("day", "2026-07-20", "2026-07-19", false, 750), 540)
  assert.equal(initialScrollMinute("week", "2026-07-14", "2026-07-19", true, 750), 750)
  assert.equal(initialScrollMinute("week", "2026-07-21", "2026-07-19", false, 750), 540)
})

test("initial scroll places the target near one quarter of the viewport", () => {
  const viewportHeight = 600
  assert.equal(initialScrollTop(0, viewportHeight), 0)
  assert.equal(initialScrollTop(600, viewportHeight), 600 * PX_PER_MIN - viewportHeight * 0.25)
  assert.equal(initialScrollTop(1440, viewportHeight), GRID_HEIGHT - viewportHeight)
})
