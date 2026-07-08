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
  /** source timestamp supplied by the read-only WPF bridge */
  remindAtUtc?: string
  /** true only while the current reminder occurrence needs attention */
  reminderActive?: boolean
  /** source deadline timestamp supplied by the read-only WPF bridge */
  deadlineAtUtc?: string
  /** Calendar planned work block start (UTC ISO), independent of REMIND/DEADLINE */
  plannedStartAtUtc?: string
  /** Calendar planned work block duration in minutes */
  plannedDurationMinutes?: number
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
  /** linked mock task selected by REMIND and DEADLINE rows */
  linkedTaskId?: string
  /** linked mock meeting selected by MEET rows */
  linkedMeetId?: string
  time: string
  bucket: "today" | "tomorrow" | "week" | "later"
  /** local calendar day (YYYY-MM-DD) this item occurs on, when known */
  dateKey?: string
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
export type StatusFilterKey = "all" | "panel" | Status | "remind"
export type TabKey = "tree" | "status" | "timeline" | "calendar" | "workstreams"

/**
 * Derived workstream activity state, computed from the real tasks in the
 * section (v0 also had "blocked"/"stale", which need data the model does not
 * track yet). null = workstream has no tasks.
 */
export type WorkstreamState = "active" | "waiting" | "todo" | "done"
export type WorkstreamFilter = "all" | "active" | "waiting" | "done"

/** How the workspace project scope is selected */
export interface ProjectScope {
  /** ids of currently selected projects (>=1) */
  ids: string[]
}

export interface WorkspaceSnapshotContract {
  schemaVersion: 1
  generatedAtUtc: string
  mode: "readonly" | "connected"
  projects: WorkspaceProjectSnapshot[]
  sections: WorkspaceSectionSnapshot[]
  tasks: WorkspaceTaskSnapshot[]
  activeNow: WorkspaceActiveNowSnapshot[]
  timelineItems: WorkspaceTimelineSnapshot[]
  context: WorkspaceContextSnapshot
}

export interface WorkspaceContextSnapshot {
  activeTab: TabKey
  selectedProjectIds: string[]
  selectedTaskId: string | null
  selectedTimelineItemId: string | null
  selectedWorkstreamId: string | null
  filter: TreeFilter
}

export interface WorkspaceProjectSnapshot {
  id: string
  name: string
  color: string
  sortOrder: number
}

export interface WorkspaceSectionSnapshot {
  id: string
  projectId: string
  name: string
  sortOrder: number
  isProjectRoot: boolean
}

export interface WorkspaceTaskSnapshot {
  id: string
  projectId: string
  sectionId: string
  parentId: string | null
  title: string
  description: string
  status: Status
  waitingFor: string
  pinToPanel: boolean
  sortOrder: number
  createdAtUtc: string
  updatedAtUtc: string
  reminderAtUtc: string | null
  reminderEveryMinutes: number | null
  reminderActive: boolean
  deadlineAtUtc: string | null
  plannedStartAtUtc: string | null
  plannedDurationMinutes: number | null
}

export interface WorkspaceActiveNowSnapshot {
  taskId: string
  kind: "FOCUS" | "REMIND"
}

export interface WorkspaceTimelineSnapshot {
  id: string
  kind: "REMIND" | "DEADLINE"
  title: string
  projectId: string
  projectPath: string
  linkedTaskId: string
  occursAtUtc: string
  meta: string | null
}

export type WorkspaceTaskCommand =
  | { type: "updateTaskStatus"; taskId: string; status: Status }
  | { type: "updateTaskPinToPanel"; taskId: string; pinToPanel: boolean }
  | { type: "updateTaskNotes"; taskId: string; notes: string }
  | { type: "updateTaskTitle"; taskId: string; title: string }
  | {
      type: "updateTaskPlannedWork"
      taskId: string
      plannedStartAtUtc: string | null
      plannedDurationMinutes: number | null
    }
  | { type: "updateTaskWaitingFor"; taskId: string; waitingFor: string }
  | {
      type: "updateTaskReminder"
      taskId: string
      remindAtUtc: string | null
      remindEveryMinutes?: number | null
    }
  | { type: "updateTaskDeadline"; taskId: string; deadlineAtUtc: string | null }

export type WorkspaceContextCommand = {
  type: "updateWorkspaceContext"
  activeTab: TabKey
  selectedProjectIds: string[]
  selectedTaskId: string | null
  selectedTimelineItemId: string | null
  selectedWorkstreamId: string | null
  filter: TreeFilter
}

/** Creates a task directly from Workspace. No taskId yet — the bridge returns one. */
export type WorkspaceCreateTaskCommand = {
  type: "createTask"
  title: string
  projectId: string
  sectionId?: string | null
}

/** Creates a top-level section (workstream) under a project. The bridge returns the section id. */
export type WorkspaceCreateSectionCommand = {
  type: "createSection"
  title: string
  projectId: string
}

export type WorkspaceCommand =
  | WorkspaceTaskCommand
  | WorkspaceContextCommand
  | WorkspaceCreateTaskCommand
  | WorkspaceCreateSectionCommand

export type WorkspaceCommandPayload = WorkspaceCommand extends infer Command
  ? Command extends { type: string }
    ? Omit<Command, "type">
    : never
  : never

export interface WorkspaceCommandEnvelope {
  schemaVersion: 1
  commandId: string
  type: WorkspaceCommand["type"]
  payload: WorkspaceCommandPayload
}

export interface WorkspaceCommandResult {
  schemaVersion: 1
  messageType: "commandResult"
  commandId: string
  success: boolean
  errorCode: string | null
  errorMessage: string | null
  createdTaskId?: string | null
  createdSectionId?: string | null
}

export interface PendingWorkspaceCommand {
  commandId: string
  taskId: string
  type: WorkspaceTaskCommand["type"]
}
