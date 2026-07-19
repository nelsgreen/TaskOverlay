export const DAY_START_MIN = 0
export const DAY_END_MIN = 24 * 60
export const WORKDAY_START_MIN = 9 * 60
export const WORKDAY_END_MIN = 18 * 60
export const UNTIMED_DEADLINE_MIN = WORKDAY_END_MIN
export const SNAP_MIN = 15
export const MIN_DURATION_MIN = 15
export const DEFAULT_TASK_DURATION_MIN = 60
export const DEFAULT_MEET_DURATION_MIN = 30
export const PX_PER_MIN = 1.15
export const GRID_HEIGHT = Math.round((DAY_END_MIN - DAY_START_MIN) * PX_PER_MIN)

export function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value))
}

export function snapMinute(value: number, snap = SNAP_MIN): number {
  return Math.round(value / snap) * snap
}

export function clampStartForDuration(startMin: number, durationMin: number): number {
  const safeDuration = clamp(durationMin, MIN_DURATION_MIN, DAY_END_MIN - DAY_START_MIN)
  return clamp(startMin, DAY_START_MIN, DAY_END_MIN - safeDuration)
}

export function rangeForMove(startMin: number, durationMin: number): { startMin: number; endMin: number } {
  const safeDuration = clamp(durationMin, MIN_DURATION_MIN, DAY_END_MIN - DAY_START_MIN)
  const safeStart = clampStartForDuration(startMin, safeDuration)
  return { startMin: safeStart, endMin: safeStart + safeDuration }
}

export function minuteFromPointer(
  rectTop: number,
  clientY: number,
  durationMin = MIN_DURATION_MIN,
): number {
  const rawMinute = DAY_START_MIN + (clientY - rectTop) / PX_PER_MIN
  return clampStartForDuration(snapMinute(rawMinute), durationMin)
}

export function resizeRange(
  startMin: number,
  endMin: number,
  edge: "top" | "bottom",
  deltaMin: number,
): { startMin: number; endMin: number } {
  if (edge === "top") {
    return {
      startMin: clamp(startMin + deltaMin, DAY_START_MIN, endMin - MIN_DURATION_MIN),
      endMin,
    }
  }
  return {
    startMin,
    endMin: clamp(endMin + deltaMin, startMin + MIN_DURATION_MIN, DAY_END_MIN),
  }
}

export function clipRangeToDay(startMin: number, endMin: number): { startMin: number; endMin: number } {
  const clippedStart = clamp(startMin, DAY_START_MIN, DAY_END_MIN - MIN_DURATION_MIN)
  const clippedEnd = clamp(endMin, clippedStart + MIN_DURATION_MIN, DAY_END_MIN)
  return { startMin: clippedStart, endMin: clippedEnd }
}

export function durationFits(startMin: number, durationMin: number): boolean {
  return startMin >= DAY_START_MIN && startMin + durationMin <= DAY_END_MIN
}

export function initialScrollMinute(
  viewMode: "day" | "week",
  selectedDate: string,
  today: string,
  weekContainsToday: boolean,
  currentMinute: number,
): number {
  const showCurrentTime = viewMode === "day" ? selectedDate === today : weekContainsToday
  return showCurrentTime ? clamp(currentMinute, DAY_START_MIN, DAY_END_MIN) : WORKDAY_START_MIN
}

export function initialScrollTop(targetMinute: number, viewportHeight: number): number {
  const targetTop = targetMinute * PX_PER_MIN - viewportHeight * 0.25
  const maxScrollTop = Math.max(0, GRID_HEIGHT - viewportHeight)
  return clamp(targetTop, 0, maxScrollTop)
}
