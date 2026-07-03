export type Status = "TODO" | "FOCUS" | "WAIT" | "DONE"

export type ReminderPreset =
  | "none"
  | "30m"
  | "1h"
  | "morning"
  | "afternoon"
  | "next-morning"
  | "custom"

/** Lifecycle state of a reminder, independent from the chosen preset */
export type ReminderState = "none" | "active" | "scheduled"

export type RepeatInterval = "daily" | "weekly" | "monthly" | "custom"

export interface Project {
  id: string
  name: string
  color: string // hsl/oklch string used inline for the color dot
}

export interface Section {
  id: string
  projectId: string
  name: string
}

export interface Task {
  id: string
  projectId: string
  sectionId: string
  parentId: string | null
  title: string
  status: Status
  pinned: boolean
  reminder: ReminderPreset
  /** whether the reminder repeats */
  reminderRepeat?: boolean
  /** repeat cadence when reminderRepeat is on */
  reminderInterval?: RepeatInterval
  /** custom reminder date, e.g. "2026-07-12" or "Jul 12" */
  reminderDate?: string
  /** custom reminder time, e.g. "09:30" */
  reminderTime?: string
  /** free-text metadata shown inline, e.g. "ответа от Мадины" */
  waitingFor?: string
  /** structured deadline date, e.g. "2026-07-12" */
  deadlineDate?: string
  /** structured deadline time, e.g. "18:00" — only set when date+time mode */
  deadlineTime?: string
  /** legacy/display-only deadline label kept for inline display */
  deadline?: string
  notes?: string
}

export type TimelineKind = "MEET" | "REMIND" | "DEADLINE"

export interface TimelineItem {
  id: string
  kind: TimelineKind
  title: string
  projectPath: string
  /** secondary context: duration, room, day label etc. */
  meta?: string
  /** projectId used to look up color dot */
  projectId?: string
  time: string
  bucket: "today" | "tomorrow" | "week" | "later"
}

/** Duration options for a MEET */
export type MeetDuration = "15m" | "30m" | "45m" | "1h" | "90m" | "2h" | "custom"

/**
 * MEET is a first-class calendar-like item.
 * It is NOT a task. It has no TODO/FOCUS/WAIT/DONE status.
 * It is NOT REMIND and NOT DEADLINE.
 * Fields: Project · Title · Notes · Date · Time · Duration · Location · Link · (Participants later)
 */
export interface MeetItem {
  id: string
  projectId: string
  title: string
  notes?: string
  /** ISO date string e.g. "2026-07-04" */
  date: string
  /** "HH:MM" start time */
  startTime: string
  /** preset duration label */
  duration: MeetDuration
  /** optional explicit end time "HH:MM" overrides duration */
  endTime?: string
  location?: string
  link?: string
  /** optional id of a related Task */
  linkedTaskId?: string
}

/** Which panel mode is active in the right panel */
export type SelectionMode = "task" | "meet" | "none"

export type TreeFilter = "all" | "active" | "active-path"
export type TabKey = "tree" | "status" | "timeline" | "calendar" | "workstreams"

/** How the workspace project scope is selected */
export interface ProjectScope {
  /** ids of currently selected projects (>=1) */
  ids: string[]
}
