import type { MeetItem } from "@/lib/types"
import { isoFromLocalDateTime } from "@/lib/calendar-date"

export type MeetEditableField =
  | "projectId"
  | "title"
  | "titleIsGenerated"
  | "date"
  | "startTime"
  | "duration"
  | "endTime"
  | "notes"
  | "location"
  | "link"
  | "linkedTaskId"
  | "recordingPolicy"

export function meetStartIso(meeting: MeetItem): string {
  const [hour, minute] = meeting.startTime.split(":").map(Number)
  return isoFromLocalDateTime(meeting.date, hour || 0, minute || 0)
}

export function meetDurationMinutes(meeting: MeetItem): number {
  const presets: Record<MeetItem["duration"], number> = {
    "15m": 15,
    "30m": 30,
    "45m": 45,
    "1h": 60,
    "90m": 90,
    "2h": 120,
    custom: 30,
  }
  if (!meeting.endTime) return presets[meeting.duration]
  const [startHour, startMinute] = meeting.startTime.split(":").map(Number)
  const [endHour, endMinute] = meeting.endTime.split(":").map(Number)
  const difference = (endHour * 60 + endMinute) - (startHour * 60 + startMinute)
  return difference > 0 ? difference : presets[meeting.duration]
}

export function meetDurationFields(
  startMinutes: number,
  durationMinutes: number,
): Pick<MeetItem, "duration" | "endTime"> {
  const preset = Object.entries({
    "15m": 15,
    "30m": 30,
    "45m": 45,
    "1h": 60,
    "90m": 90,
    "2h": 120,
  } as const).find(([, minutes]) => minutes === durationMinutes)?.[0] as
    | MeetItem["duration"]
    | undefined
  if (preset) return { duration: preset, endTime: undefined }
  const endMinutes = startMinutes + durationMinutes
  return {
    duration: "custom",
    endTime: `${String(Math.floor((endMinutes % 1440) / 60)).padStart(2, "0")}:${String(endMinutes % 60).padStart(2, "0")}`,
  }
}
