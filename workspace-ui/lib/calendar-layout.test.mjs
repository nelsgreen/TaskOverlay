import assert from "node:assert/strict"
import test from "node:test"
import {
  DAY_END_MIN,
  DAY_START_MIN,
  DEFAULT_WORKDAY_END_MIN,
  DEFAULT_WORKDAY_START_MIN,
  GRID_HEIGHT,
  MIN_DURATION_MIN,
  PX_PER_MIN,
  calendarNavigation,
  clampStartForDuration,
  clipRangeToDay,
  deadlineMarkerMinute,
  durationFits,
  effectiveCreationDuration,
  initialScrollMinute,
  initialScrollTop,
  markerMinute,
  meetingRangeForDay,
  minuteFromPointer,
  normalizeWorkingHours,
  rangeForMove,
  resizeRange,
  workdayBandGeometry,
} from "./calendar-layout.ts"
import { isoFromLocalDateTime, localSlotFromIso } from "./calendar-date.ts"

test("full-day and workday constants stay independent", () => {
  assert.equal(DAY_START_MIN, 0)
  assert.equal(DAY_END_MIN, 1440)
  assert.equal(DEFAULT_WORKDAY_START_MIN, 540)
  assert.equal(DEFAULT_WORKDAY_END_MIN, 1080)
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
  assert.deepEqual(clipRangeToDay(1380, 1500), { startMin: 1380, endMin: 1440 })
  assert.equal(clipRangeToDay(600, 600), null)
  assert.equal(clipRangeToDay(700, 600), null)
  assert.equal(clipRangeToDay(1440, 1500), null)
  assert.equal(durationFits(1380, 60), true)
  assert.equal(durationFits(1395, 60), false)
  assert.equal(durationFits(1425, 15), true)
  assert.equal(durationFits(1440, 15), false)
})

test("empty-slot creation shortens defaults near midnight", () => {
  assert.equal(effectiveCreationDuration(1380, 60), 60)
  assert.equal(effectiveCreationDuration(1410, 60), 30)
  assert.equal(effectiveCreationDuration(1425, 60), 15)
  assert.equal(effectiveCreationDuration(1425, 30), 15)
  assert.equal(effectiveCreationDuration(1430, 30), null)
  assert.equal(effectiveCreationDuration(1440, 30), null)
})

test("markers keep real full-day minutes while untimed deadlines stay at 18:00", () => {
  assert.equal(markerMinute(8 * 60), 480)
  assert.equal(markerMinute(19 * 60), 1140)
  assert.equal(deadlineMarkerMinute(23 * 60 + 45), 1425)
  assert.equal(deadlineMarkerMinute(null), 1080)
})

test("MEET ranges distinguish same-day, clipped cross-midnight, and zero-length data", () => {
  assert.deepEqual(meetingRangeForDay(600, 30, 660), { endMin: 660, durationMin: 60 })
  assert.deepEqual(meetingRangeForDay(1380, 30, 60), { endMin: 1440, durationMin: 120 })
  assert.deepEqual(meetingRangeForDay(1380, 60, null), { endMin: 1440, durationMin: 60 })
  assert.equal(meetingRangeForDay(600, 30, 600), null)
})

test("24:00 serializes as the following local midnight and never as a start slot", () => {
  const endUtc = isoFromLocalDateTime("2026-07-19", 24, 0)
  assert.deepEqual(localSlotFromIso(endUtc), { dateKey: "2026-07-20", minutes: 0 })
  assert.equal(durationFits(1440, 15), false)
})

test("initial scroll policy uses now only for the visible current day or week", () => {
  assert.equal(initialScrollMinute("day", "2026-07-19", "2026-07-19", false, 750), 750)
  assert.equal(initialScrollMinute("day", "2026-07-20", "2026-07-19", false, 750), 540)
  assert.equal(initialScrollMinute("week", "2026-07-14", "2026-07-19", true, 750), 750)
  assert.equal(initialScrollMinute("week", "2026-07-21", "2026-07-19", false, 750), 540)
  assert.equal(initialScrollMinute("day", "2026-07-20", "2026-07-19", false, 750, 480, 1200), 480)
  assert.equal(initialScrollMinute("week", "2026-07-21", "2026-07-19", false, 750, 480, 1200), 480)
  assert.equal(initialScrollMinute("day", "2026-07-19", "2026-07-19", false, 750, 480, 1200), 750)
})

test("work band uses supplied validated settings without changing full-day bounds", () => {
  assert.deepEqual(normalizeWorkingHours(480, 1200), { startMin: 480, endMin: 1200 })
  assert.deepEqual(workdayBandGeometry(480, 1200), {
    top: 480 * PX_PER_MIN,
    height: 720 * PX_PER_MIN,
  })
  assert.deepEqual(normalizeWorkingHours(1200, 480), { startMin: 540, endMin: 1080 })
  assert.deepEqual(normalizeWorkingHours(487, 1200), { startMin: 540, endMin: 1080 })
  assert.equal(DAY_START_MIN, 0)
  assert.equal(DAY_END_MIN, 1440)
})

test("initial scroll places the target near one quarter of the viewport", () => {
  const viewportHeight = 600
  assert.equal(initialScrollTop(0, viewportHeight), 0)
  assert.equal(initialScrollTop(600, viewportHeight), 600 * PX_PER_MIN - viewportHeight * 0.25)
  assert.equal(initialScrollTop(1440, viewportHeight), GRID_HEIGHT - viewportHeight)
})

test("initial scroll navigation identity ignores snapshots, selections, and clock ticks", () => {
  const first = calendarNavigation("week", "2026-07-14")
  const afterSnapshot = calendarNavigation("week", "2026-07-14")
  const afterClockTick = calendarNavigation("week", "2026-07-14")
  assert.equal(first.key, afterSnapshot.key)
  assert.equal(first.key, afterClockTick.key)
  assert.notEqual(first.key, calendarNavigation("day", "2026-07-14").key)
  assert.notEqual(first.key, calendarNavigation("week", "2026-07-21").key)
  assert.notEqual(first.key, calendarNavigation("week", "2026-07-14", 480, 1200).key)
  assert.equal(
    calendarNavigation("week", "2026-07-14", 480, 1200).key,
    calendarNavigation("week", "2026-07-14", 480, 1200).key,
  )
})

test("quick durations are enabled only when the exact duration fits", () => {
  assert.equal(durationFits(1320, 120), true)
  assert.equal(durationFits(1335, 120), false)
  assert.equal(durationFits(1410, 30), true)
  assert.equal(durationFits(1425, 30), false)
})
