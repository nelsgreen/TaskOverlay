export type Status = "TODO" | "FOCUS" | "WAIT" | "DONE"

export type ReminderPreset =
  | "none"
  | "30m"
  | "1h"
  | "2h"
  | "morning"
  | "afternoon"
  | "next-morning"
  | "custom"

/** Lifecycle state of a reminder, independent from the chosen preset */
export type ReminderState = "none" | "active" | "scheduled"

export type RepeatInterval = "every2h" | "daily" | "weekly" | "monthly" | "custom"

export interface Project {
  id: string
  name: string
  color: string // hsl/oklch string used inline for the color dot
}

export interface Section {
  id: string
  projectId: string
  name: string
  isProjectRoot?: boolean
}

/**
 * One lightweight execution step ("Steps" in the UI). Deliberately NOT a task:
 * no status/REMIND/DEADLINE/WAIT/pin/priority of its own. Step state is
 * independent of the parent task's status.
 */
export interface TaskCheckpoint {
  id: string
  title: string
  done: boolean
  /** stable list position, 0-based */
  order: number
  completedAtUtc?: string | null
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
  /** lightweight execution steps; absent/empty both mean "no steps" */
  checkpoints?: TaskCheckpoint[]
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
export type TabKey = "tree" | "status" | "timeline" | "calendar" | "workstreams" | "contexthub"

// ─── ContextHUB (project memory) ────────────────────────────────────────────

export type ContextSourceType =
  | "meetingSummary"
  | "meetingTranscript"
  | "chatSummary"
  | "manualNote"
  | "clientRequest"
  | "documentSummary"
  | "statusUpdate"
  | "telegramCapture"
  | "other"

export type ContextSourceApp =
  | "chatgpt"
  | "claude"
  | "codex"
  | "telegram"
  | "manual"
  | "other"

export type ContextItemType =
  | "decision"
  | "requirement"
  | "constraint"
  | "blocker"
  | "openQuestion"
  | "actionItem"
  | "projectFact"
  | "risk"
  | "note"

export type ContextItemStatus = "active" | "resolved" | "deprecated" | "superseded"

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
  meetings: WorkspaceMeetingSnapshot[]
  contextSources?: WorkspaceContextSourceSnapshot[]
  contextItems?: WorkspaceContextItemSnapshot[]
  activeNow: WorkspaceActiveNowSnapshot[]
  timelineItems: WorkspaceTimelineSnapshot[]
  context: WorkspaceContextSnapshot
}

/** ContextHUB source document as exposed by the snapshot. Ids are snapshot-format ("N" guids). */
export interface WorkspaceContextSourceSnapshot {
  id: string
  projectId: string
  sourceType: ContextSourceType
  sourceApp: ContextSourceApp | null
  title: string
  body: string
  summary: string
  sourceDateUtc: string
  linkedTaskIds: string[]
  linkedMeetingIds: string[]
  createdAtUtc: string
  updatedAtUtc: string
}

/** ContextHUB context item as exposed by the snapshot. */
export interface WorkspaceContextItemSnapshot {
  id: string
  projectId: string
  itemType: ContextItemType
  status: ContextItemStatus
  title: string
  body: string
  sourceDocumentIds: string[]
  linkedTaskIds: string[]
  linkedMeetingIds: string[]
  createdAtUtc: string
  updatedAtUtc: string
  resolvedAtUtc: string | null
}

export interface WorkspaceContextSnapshot {
  activeTab: TabKey
  selectedProjectIds: string[]
  selectedTaskId: string | null
  selectedTimelineItemId: string | null
  selectedWorkstreamId: string | null
  filter: TreeFilter
  activeNowCollapsed: boolean
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
  checkpoints?: WorkspaceCheckpointSnapshot[] | null
}

export interface WorkspaceCheckpointSnapshot {
  id: string
  title: string
  done: boolean
  sortOrder: number
  completedAtUtc: string | null
}

export interface WorkspaceActiveNowSnapshot {
  taskId: string
  kind: "FOCUS" | "REMIND"
}

export interface WorkspaceMeetingSnapshot {
  id: string
  projectId: string
  title: string
  notes: string
  startsAtUtc: string
  durationMinutes: number
  location: string
  link: string
  linkedTaskId: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export interface WorkspaceTimelineSnapshot {
  id: string
  kind: "MEET" | "REMIND" | "DEADLINE"
  title: string
  projectId: string
  projectPath: string
  linkedTaskId: string | null
  linkedMeetingId: string | null
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
  | { type: "moveTask"; taskId: string; sectionId: string }
  | { type: "deleteTask"; taskId: string }
  /** Batch add — single add sends a one-item array; multiline paste sends all lines. */
  | { type: "addTaskCheckpoints"; taskId: string; titles: string[] }
  | { type: "updateTaskCheckpointTitle"; taskId: string; checkpointId: string; title: string }
  | { type: "toggleTaskCheckpoint"; taskId: string; checkpointId: string; done: boolean }
  | { type: "deleteTaskCheckpoint"; taskId: string; checkpointId: string }
  | { type: "reorderTaskCheckpoint"; taskId: string; checkpointId: string; targetIndex: number }

export type WorkspaceSectionCommand =
  | { type: "renameSection"; sectionId: string; title: string }
  | { type: "deleteSection"; sectionId: string }

export type WorkspaceContextCommand = {
  type: "updateWorkspaceContext"
  activeTab: TabKey
  selectedProjectIds: string[]
  selectedTaskId: string | null
  selectedTimelineItemId: string | null
  selectedWorkstreamId: string | null
  filter: TreeFilter
  activeNowCollapsed: boolean
}

export type WorkspaceMeetingCommand =
  | {
      type: "createMeeting"
      projectId: string
      title: string
      startsAtUtc: string
      durationMinutes: number
      notes?: string | null
      location?: string | null
      link?: string | null
      linkedTaskId?: string | null
    }
  | {
      type: "updateMeeting"
      meetingId: string
      projectId?: string
      title?: string
      startsAtUtc?: string
      durationMinutes?: number
      notes?: string | null
      location?: string | null
      link?: string | null
      linkedTaskId?: string | null
    }
  | { type: "deleteMeeting"; meetingId: string }

/** Creates a task directly from Workspace. No taskId yet — the bridge returns one. */
export type WorkspaceCreateTaskCommand = {
  type: "createTask"
  title: string
  /** Explicit connected draft: permits an empty title until Details commits a real one. */
  draft?: boolean
  projectId?: string
  sectionId?: string | null
  /** When set, the new task becomes a subtask of this task and inherits its project/section. */
  parentTaskId?: string
}

/** Creates a top-level section (workstream) under a project. The bridge returns the section id. */
export type WorkspaceCreateSectionCommand = {
  type: "createSection"
  title: string
  projectId: string
}

/**
 * ContextHUB commands. create/update payloads are patches on the C# side:
 * absent fields keep existing values. Link/unlink commands are idempotent.
 */
export type WorkspaceContextHubCommand =
  | {
      type: "createContextSource"
      projectId: string
      sourceType: ContextSourceType
      sourceApp?: ContextSourceApp | null
      title: string
      body?: string
      summary?: string
      sourceDateUtc?: string
      linkedTaskIds?: string[]
      linkedMeetingIds?: string[]
    }
  | {
      type: "updateContextSource"
      sourceId: string
      projectId?: string
      sourceType?: ContextSourceType
      sourceApp?: ContextSourceApp | null
      title?: string
      body?: string
      summary?: string
      sourceDateUtc?: string
      linkedTaskIds?: string[]
      linkedMeetingIds?: string[]
    }
  | { type: "deleteContextSource"; sourceId: string }
  | {
      type: "createContextItem"
      projectId: string
      itemType: ContextItemType
      status?: ContextItemStatus
      title: string
      body?: string
      sourceDocumentIds?: string[]
      linkedTaskIds?: string[]
      linkedMeetingIds?: string[]
    }
  | {
      type: "updateContextItem"
      itemId: string
      projectId?: string
      itemType?: ContextItemType
      status?: ContextItemStatus
      title?: string
      body?: string
      sourceDocumentIds?: string[]
      linkedTaskIds?: string[]
      linkedMeetingIds?: string[]
    }
  | { type: "deleteContextItem"; itemId: string }
  | { type: "linkContextItemToTask"; itemId: string; taskId: string }
  | { type: "unlinkContextItemFromTask"; itemId: string; taskId: string }
  | { type: "linkContextItemToMeeting"; itemId: string; meetingId: string }
  | { type: "unlinkContextItemFromMeeting"; itemId: string; meetingId: string }
  | { type: "linkSourceToTask"; sourceId: string; taskId: string }
  | { type: "unlinkSourceFromTask"; sourceId: string; taskId: string }
  | { type: "linkSourceToMeeting"; sourceId: string; meetingId: string }
  | { type: "unlinkSourceFromMeeting"; sourceId: string; meetingId: string }

export type WorkspaceCommand =
  | WorkspaceTaskCommand
  | WorkspaceSectionCommand
  | WorkspaceContextCommand
  | WorkspaceCreateTaskCommand
  | WorkspaceCreateSectionCommand
  | WorkspaceMeetingCommand
  | WorkspaceContextHubCommand

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
  createdMeetingId?: string | null
  createdContextSourceId?: string | null
  createdContextItemId?: string | null
}

export interface PendingWorkspaceCommand {
  commandId: string
  taskId: string
  type: WorkspaceTaskCommand["type"]
}
