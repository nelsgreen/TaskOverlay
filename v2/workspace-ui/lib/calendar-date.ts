/**
 * Pure date-key helpers for the Workspace Calendar.
 * A "date key" is a local calendar date formatted as YYYY-MM-DD.
 * All parsing goes through Date's local getters, never UTC string slicing,
 * so a UTC timestamp lands on the correct local day.
 */

function pad(value: number): string {
  return String(value).padStart(2, "0")
}

export function dateKeyFromDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/** Parses an ISO/UTC timestamp and returns the local calendar day it falls on. */
export function dateKeyFromIso(isoUtc: string): string | null {
  const parsed = new Date(isoUtc)
  if (Number.isNaN(parsed.getTime())) return null
  return dateKeyFromDate(parsed)
}

/** Parses a YYYY-MM-DD key as a local midnight Date. */
export function parseDateKey(key: string): Date {
  const [year, month, day] = key.split("-").map(Number)
  return new Date(year, month - 1, day)
}

export function todayKey(): string {
  return dateKeyFromDate(new Date())
}

export function addDaysKey(key: string, delta: number): string {
  const date = parseDateKey(key)
  date.setDate(date.getDate() + delta)
  return dateKeyFromDate(date)
}

/** Monday (ISO week start) of the week containing the given key. */
export function mondayOfWeekKey(key: string): string {
  const date = parseDateKey(key)
  const isoDayIndex = (date.getDay() + 6) % 7 // Monday = 0 ... Sunday = 6
  date.setDate(date.getDate() - isoDayIndex)
  return dateKeyFromDate(date)
}

/** The seven date keys Monday..Sunday for the week starting at mondayKey. */
export function weekDayKeys(mondayKey: string): string[] {
  return Array.from({ length: 7 }, (_, index) => addDaysKey(mondayKey, index))
}

export function formatWeekLabel(mondayKey: string): string {
  const monday = parseDateKey(mondayKey)
  const sunday = parseDateKey(addDaysKey(mondayKey, 6))
  const mondayLabel = monday.toLocaleDateString(undefined, { month: "short", day: "numeric" })
  const sundayLabel = sunday.toLocaleDateString(undefined, { month: "short", day: "numeric" })
  return `${mondayLabel} - ${sundayLabel}`
}

export function formatDayLabel(key: string): string {
  return parseDateKey(key).toLocaleDateString(undefined, {
    weekday: "long",
    month: "short",
    day: "numeric",
  })
}

export function formatWeekdayShort(key: string): string {
  return parseDateKey(key).toLocaleDateString(undefined, { weekday: "short" })
}

export function formatDayNumber(key: string): string {
  return String(parseDateKey(key).getDate())
}

export function isSameKey(a: string, b: string): boolean {
  return a === b
}
