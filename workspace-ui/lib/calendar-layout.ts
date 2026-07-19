export const DAY_START_MIN = 0
export const DAY_END_MIN = 24 * 60
export const DEFAULT_WORKDAY_START_MIN = 9 * 60
export const DEFAULT_WORKDAY_END_MIN = 18 * 60
export const UNTIMED_DEADLINE_MIN = 18 * 60
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

export function clipRangeToDay(startMin: number, endMin: number): { startMin: number; endMin: number } | null {
  const clippedStart = clamp(startMin, DAY_START_MIN, DAY_END_MIN)
  const clippedEnd = clamp(endMin, DAY_START_MIN, DAY_END_MIN)
  if (!Number.isFinite(startMin) || !Number.isFinite(endMin) || clippedStart >= clippedEnd) return null
  return { startMin: clippedStart, endMin: clippedEnd }
}

export function normalizeWorkingHours(
  startMin: number,
  endMin: number,
): { startMin: number; endMin: number } {
  const valid = Number.isInteger(startMin) &&
    Number.isInteger(endMin) &&
    startMin >= DAY_START_MIN &&
    endMin <= DAY_END_MIN &&
    startMin < endMin &&
    endMin - startMin >= SNAP_MIN &&
    startMin % SNAP_MIN === 0 &&
    endMin % SNAP_MIN === 0
  return valid
    ? { startMin, endMin }
    : { startMin: DEFAULT_WORKDAY_START_MIN, endMin: DEFAULT_WORKDAY_END_MIN }
}

export function workdayBandGeometry(startMin: number, endMin: number): { top: number; height: number } {
  const normalized = normalizeWorkingHours(startMin, endMin)
  return {
    top: normalized.startMin * PX_PER_MIN,
    height: (normalized.endMin - normalized.startMin) * PX_PER_MIN,
  }
}

export function durationFits(startMin: number, durationMin: number): boolean {
  return startMin >= DAY_START_MIN &&
    startMin < DAY_END_MIN &&
    durationMin >= MIN_DURATION_MIN &&
    startMin + durationMin <= DAY_END_MIN
}

export function effectiveCreationDuration(startMin: number, defaultDurationMin: number): number | null {
  if (!Number.isFinite(startMin) || !Number.isFinite(defaultDurationMin) || startMin < DAY_START_MIN || startMin >= DAY_END_MIN) {
    return null
  }
  const durationMin = Math.min(defaultDurationMin, DAY_END_MIN - startMin)
  return durationMin >= MIN_DURATION_MIN ? durationMin : null
}

export function markerMinute(minute: number): number {
  return clamp(minute, DAY_START_MIN, DAY_END_MIN)
}

export function deadlineMarkerMinute(minute: number | null): number {
  return minute === null ? UNTIMED_DEADLINE_MIN : markerMinute(minute)
}

export function meetingRangeForDay(
  startMin: number,
  durationMin: number,
  explicitEndMin: number | null,
): { endMin: number; durationMin: number } | null {
  if (!Number.isFinite(startMin) || startMin < DAY_START_MIN || startMin >= DAY_END_MIN) return null
  if (explicitEndMin !== null) {
    if (!Number.isFinite(explicitEndMin) || explicitEndMin < DAY_START_MIN || explicitEndMin > DAY_END_MIN) return null
    if (explicitEndMin === startMin) return null
    return explicitEndMin < startMin
      ? { endMin: DAY_END_MIN, durationMin: DAY_END_MIN - startMin + explicitEndMin }
      : { endMin: explicitEndMin, durationMin: explicitEndMin - startMin }
  }
  if (!Number.isFinite(durationMin) || durationMin <= 0) return null
  return { endMin: startMin + durationMin, durationMin }
}

export interface CalendarNavigation {
  viewMode: "day" | "week"
  selectedDate: string
  workdayStartMin: number
  workdayEndMin: number
  key: string
}

export function calendarNavigation(
  viewMode: "day" | "week",
  selectedDate: string,
  workdayStartMin = DEFAULT_WORKDAY_START_MIN,
  workdayEndMin = DEFAULT_WORKDAY_END_MIN,
): CalendarNavigation {
  return {
    viewMode,
    selectedDate,
    workdayStartMin,
    workdayEndMin,
    key: `${viewMode}:${selectedDate}:${workdayStartMin}-${workdayEndMin}`,
  }
}

export function initialScrollMinute(
  viewMode: "day" | "week",
  selectedDate: string,
  today: string,
  weekContainsToday: boolean,
  currentMinute: number,
  workdayStartMin = DEFAULT_WORKDAY_START_MIN,
  workdayEndMin = DEFAULT_WORKDAY_END_MIN,
): number {
  const showCurrentTime = viewMode === "day" ? selectedDate === today : weekContainsToday
  const workingHours = normalizeWorkingHours(workdayStartMin, workdayEndMin)
  return showCurrentTime ? clamp(currentMinute, DAY_START_MIN, DAY_END_MIN) : workingHours.startMin
}

export function initialScrollTop(targetMinute: number, viewportHeight: number): number {
  const targetTop = targetMinute * PX_PER_MIN - viewportHeight * 0.25
  const maxScrollTop = Math.max(0, GRID_HEIGHT - viewportHeight)
  return clamp(targetTop, 0, maxScrollTop)
}
