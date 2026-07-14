"use client"

import { useEffect, useRef, useState } from "react"
import { ArrowDown, ArrowUp, Bell, Check, ChevronRight, Flag, ListChecks, MapPin, Pin, Repeat, Trash2, UndoDot, X } from "lucide-react"
import type {
  MeetItem,
  Project,
  ReminderPreset,
  ReminderState,
  RepeatInterval,
  Section,
  Status,
  Task,
  TaskCheckpoint,
  WorkspaceContextHubCommand,
  WorkspaceContextItemSnapshot,
  WorkspaceContextSourceSnapshot,
  WorkspaceTaskCommand,
} from "@/lib/types"
import { cn } from "@/lib/utils"
import { isoFromLocalDateTime } from "@/lib/calendar-date"
import { statusConfig } from "./status-badge"
import { TaskContextBlock } from "./task-context-block"

type BridgeEditField = "title" | "status" | "pinToPanel" | "notes" | "waitingFor" | "reminder" | "deadline"
/** The bridge's updateTaskReminder command always replaces both fields at once (the C# side has
 * no partial-patch form), so every reminder push must carry the instant and the repeat cadence
 * together — otherwise an unrelated edit silently wipes whichever half it omits. */
type ReminderBridgeValue = { remindAtUtc: string | null; remindEveryMinutes: number | null }
type BridgeEditValue = string | boolean | null | ReminderBridgeValue

interface Props {
  task: Task | null
  projects: Project[]
  sections: Section[]
  /** Called on every draft change — auto-apply, no Save button */
  onApply: (task: Task) => void
  onDelete: (id: string) => void
  /** Move the task to a snapshot section id (group:{id} or project:{id}:root). */
  onMoveTask?: (taskId: string, sectionId: string) => void
  focusTitle?: boolean
  onTitleFocused?: () => void
  editMode?: "full" | "connected" | "readonly"
  pendingFields?: Set<string>
  bridgeError?: string | null
  onBridgeEdit?: (
    taskId: string,
    field: BridgeEditField,
    value: BridgeEditValue,
  ) => boolean
  /** Sends a fully-shaped checkpoint ("Steps") command through the bridge when connected. */
  onCheckpointCommand?: (command: WorkspaceTaskCommand) => boolean
  onClearBridgeError?: () => void
  /** ContextHUB records for the Context block. Absent/empty renders the block's empty state. */
  contextSources?: WorkspaceContextSourceSnapshot[]
  contextItems?: WorkspaceContextItemSnapshot[]
  /** Sends a link/unlink ContextHUB command through the bridge (mock fallback handled by the caller). */
  onContextCommand?: (command: WorkspaceContextHubCommand) => boolean
  /** Switches Workspace to the ContextHUB tab. */
  onOpenContextHub?: () => void
  /** All tasks/MEETs, for the Context block's Context Pack export (linked-record title lookups). */
  allTasks?: Task[]
  meetItems?: MeetItem[]
}

/** Combines local date+time fields into a UTC ISO instant, or null when incomplete/absent. */
function computeReminderIso(t: Task): string | null {
  if (!t.reminderDate || !t.reminderTime) return null
  const [h, m] = t.reminderTime.split(":").map(Number)
  return isoFromLocalDateTime(t.reminderDate, h, m)
}

/**
 * Concrete UTC instant for a one-shot reminder preset, computed fresh from "now" — presets don't
 * write into the custom date/time fields (kept as a separate entry path, matching the v0
 * reference). Tomorrow morning/afternoon anchor to 10:00/14:00 local; 10:00 matches the existing
 * WPF ReminderService.GetTomorrowMorning precedent. Next workday morning skips Sat/Sun.
 */
function computePresetReminderIso(preset: ReminderPreset, now: Date): string | null {
  const localAt = (daysAhead: number, hour: number) => {
    const d = new Date(now)
    d.setDate(d.getDate() + daysAhead)
    d.setHours(hour, 0, 0, 0)
    return d.toISOString()
  }
  switch (preset) {
    case "30m": return new Date(now.getTime() + 30 * 60_000).toISOString()
    case "1h": return new Date(now.getTime() + 60 * 60_000).toISOString()
    case "2h": return new Date(now.getTime() + 120 * 60_000).toISOString()
    case "morning": return localAt(1, 10)
    case "afternoon": return localAt(1, 14)
    case "next-morning": {
      const d = new Date(now)
      d.setDate(d.getDate() + 1)
      while (d.getDay() === 0 || d.getDay() === 6) d.setDate(d.getDate() + 1)
      d.setHours(10, 0, 0, 0)
      return d.toISOString()
    }
    default: return null
  }
}

/**
 * Flat repeat cadence in minutes for the bridge's remindEveryMinutes. Monthly has no flat-minute
 * equivalent the backend can represent (calendar months vary in length), so it intentionally
 * returns null here and stays mock-only — see the "Later" hint on the Monthly button below.
 */
function repeatIntervalMinutes(interval: RepeatInterval | undefined): number | null {
  switch (interval) {
    case "every2h": return 120
    case "daily": return 1440
    case "weekly": return 10080
    default: return null
  }
}

/** Date-only deadlines are treated as due by end of that local day. */
function computeDeadlineIso(t: Task, withTime: boolean): string | null {
  if (!t.deadlineDate) return null
  if (withTime) {
    if (!t.deadlineTime) return null
    const [h, m] = t.deadlineTime.split(":").map(Number)
    return isoFromLocalDateTime(t.deadlineDate, h, m)
  }
  return isoFromLocalDateTime(t.deadlineDate, 23, 59)
}

const statuses: Status[] = ["TODO", "FOCUS", "WAIT", "DONE"]

// UI-only: the Pin-to-panel toggle is hidden in Details for now (the feature
// isn't currently relevant on this surface). The data model, bridge command,
// and pinned-task/overlay semantics are untouched — flip this to re-expose it.
const SHOW_PIN_TO_PANEL = false

const advancedPresets: { value: ReminderPreset; label: string }[] = [
  { value: "30m", label: "In 30m" },
  { value: "1h", label: "In 1h" },
  { value: "2h", label: "In 2h" },
  { value: "morning", label: "Tomorrow morning" },
  { value: "afternoon", label: "Tomorrow afternoon" },
  { value: "next-morning", label: "Next workday morning" },
]

const repeatIntervals: { value: RepeatInterval; label: string }[] = [
  { value: "every2h", label: "Every 2h" },
  { value: "daily", label: "Daily" },
  { value: "weekly", label: "Weekly" },
  { value: "monthly", label: "Monthly" },
]

/** Steps in stable order; absent and empty both mean "no steps". */
function sortedCheckpoints(t: Task): TaskCheckpoint[] {
  return [...(t.checkpoints ?? [])].sort((a, b) => a.order - b.order)
}

function checkpointProgress(t: Task): { done: number; total: number; summary: string } {
  const items = t.checkpoints ?? []
  const total = items.length
  const done = items.filter((c) => c.done).length
  const summary = total === 0 ? "No steps" : done === total ? "All steps ready" : `${done}/${total}`
  return { done, total, summary }
}

/** Splits pasted text into step titles: one per non-empty line. */
function splitPastedSteps(text: string): string[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
}

/** Local id for mock/dev-mode steps (connected mode gets real ids from the snapshot). */
function newCheckpointId(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") return crypto.randomUUID()
  return `step-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

function deriveReminderState(t: Task): ReminderState {
  if (t.reminder === "none" && !t.reminderDate && !t.reminderTime) return "none"
  if (t.reminderDate || t.reminderTime || t.reminder === "custom") return "scheduled"
  return "active"
}

function reminderSummary(t: Task): string {
  if (deriveReminderState(t) === "none") return "No reminder"
  if (t.reminderDate) {
    const d = new Date(t.reminderDate)
    const dateStr = d.toLocaleDateString("en-GB", { day: "numeric", month: "short" })
    return t.reminderTime ? `${dateStr} ${t.reminderTime}` : dateStr
  }
  const labels: Record<ReminderPreset, string> = {
    none: "No reminder",
    "30m": "In 30 minutes",
    "1h": "In 1 hour",
    "2h": "In 2 hours",
    morning: "Tomorrow morning",
    afternoon: "Tomorrow afternoon",
    "next-morning": "Next workday morning",
    custom: "Custom",
  }
  return labels[t.reminder] ?? "Active"
}

function deadlineSummary(t: Task): string {
  if (!t.deadlineDate && !t.deadline) return "No deadline"
  if (t.deadlineDate) {
    const d = new Date(t.deadlineDate)
    const today = new Date()
    today.setHours(0, 0, 0, 0)
    const diff = Math.round((d.getTime() - today.getTime()) / 86400000)
    let label: string
    if (diff === 0) label = "Today"
    else if (diff === 1) label = "Tomorrow"
    else label = d.toLocaleDateString("en-GB", { weekday: "short", day: "numeric", month: "short" })
    return t.deadlineTime ? `${label} ${t.deadlineTime}` : label
  }
  return t.deadline ?? "No deadline"
}

type ActiveField = "title" | "notes" | "waitingFor" | null

/**
 * Merges persisted fields from a fresh snapshot into the local draft, skipping
 * whichever field the user is actively typing in and the Reminder/Deadline data
 * while those editors are open (their own onChange path owns those fields until
 * the section closes). Used both for the normal same-task snapshot reconcile
 * and to selectively recover from a failed bridge command.
 */
function mergeTaskFields(
  current: Task,
  incoming: Task,
  activeField: ActiveField,
  reminderOpen: boolean,
  deadlineOpen: boolean,
): Task {
  const next: Task = {
    ...current,
    status: incoming.status,
    pinned: incoming.pinned,
    projectId: incoming.projectId,
    sectionId: incoming.sectionId,
    parentId: incoming.parentId,
    remindAtUtc: incoming.remindAtUtc,
    reminderActive: incoming.reminderActive,
    deadlineAtUtc: incoming.deadlineAtUtc,
    plannedStartAtUtc: incoming.plannedStartAtUtc,
    plannedDurationMinutes: incoming.plannedDurationMinutes,
    // Checkpoints always take the authoritative snapshot: in-progress row edits
    // and the add-step input live in separate local buffers, never in the draft.
    checkpoints: incoming.checkpoints,
  }
  if (activeField !== "title") next.title = incoming.title
  if (activeField !== "notes") next.notes = incoming.notes
  if (activeField !== "waitingFor") next.waitingFor = incoming.waitingFor
  if (!reminderOpen) {
    next.reminder = incoming.reminder
    next.reminderDate = incoming.reminderDate
    next.reminderTime = incoming.reminderTime
    next.reminderRepeat = incoming.reminderRepeat
    next.reminderInterval = incoming.reminderInterval
  }
  if (!deadlineOpen) {
    next.deadline = incoming.deadline
    next.deadlineDate = incoming.deadlineDate
    next.deadlineTime = incoming.deadlineTime
  }
  return next
}

function getDeadlinePresets() {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, "0")
  const fmt = (date: Date) => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
  const today = new Date(d)
  const tomorrow = new Date(d)
  tomorrow.setDate(d.getDate() + 1)
  const friday = new Date(d)
  const daysToFriday = (5 - d.getDay() + 7) % 7 || 7
  friday.setDate(d.getDate() + daysToFriday)
  const nextWeek = new Date(d)
  const daysToMonday = (8 - d.getDay()) % 7 || 7
  nextWeek.setDate(d.getDate() + daysToMonday)
  return [
    { label: "Today", value: fmt(today) },
    { label: "Tomorrow", value: fmt(tomorrow) },
    { label: "This Friday", value: fmt(friday) },
    { label: "Next week", value: fmt(nextWeek) },
  ]
}

export function DetailsPanel({
  task,
  projects,
  sections,
  onApply,
  onDelete,
  onMoveTask,
  focusTitle,
  onTitleFocused,
  editMode = "full",
  pendingFields = new Set(),
  bridgeError,
  onBridgeEdit,
  onCheckpointCommand,
  onClearBridgeError,
  contextSources = [],
  contextItems = [],
  onContextCommand,
  onOpenContextHub,
  allTasks = [],
  meetItems = [],
}: Props) {
  const [draft, setDraft] = useState<Task | null>(task)
  // Reminder/Deadline/Location cards are collapsed to one row and expand on hover
  // (CSS) or when pinned open via click (these flags). Reminder/Deadline also
  // track focus-within as state (*Focused) so the same "being edited" signal both
  // reveals the editor for keyboard users and guards an in-progress custom
  // date/time from a snapshot reconcile — see mergeTaskFields below.
  const [reminderOpen, setReminderOpen] = useState(false)
  const [reminderFocused, setReminderFocused] = useState(false)
  const [deadlineOpen, setDeadlineOpen] = useState(false)
  const [deadlineFocused, setDeadlineFocused] = useState(false)
  const [locationOpen, setLocationOpen] = useState(false)
  const [deadlineWithTime, setDeadlineWithTime] = useState(false)
  // Which plain-text field the user is currently typing in (between focus and
  // blur/commit). A fresh snapshot for the same task must not stomp this field,
  // even though it reconciles every other field. Reminder/Deadline use their
  // *Open/*Focused flags for the same purpose.
  const [activeField, setActiveField] = useState<"title" | "notes" | "waitingFor" | null>(null)
  // Steps editor state — all separate from the draft so snapshot reconciliation
  // can always take the authoritative checkpoint list without stomping typing.
  const [stepsOpen, setStepsOpen] = useState(false)
  const [newStepText, setNewStepText] = useState("")
  const [editingStepId, setEditingStepId] = useState<string | null>(null)
  const [editingStepText, setEditingStepText] = useState("")

  // Track the snapshot at the start of the editing session for "Revert"
  const sessionBaseRef = useRef<Task | null>(task)
  const titleInputRef = useRef<HTMLInputElement>(null)
  const notesInputRef = useRef<HTMLTextAreaElement>(null)
  // Last task id this panel rendered for — distinguishes "selection changed"
  // (full reset) from "same task, fresh snapshot" (merge) below.
  const lastTaskIdRef = useRef<string | null>(task?.id ?? null)

  // Single reconciliation effect for the connected draft/snapshot relationship:
  // - Selecting a different task fully resets the draft and editor UI state.
  // - A fresh snapshot for the *same* task merges every persisted field into the
  //   draft except the one field the user is actively typing in, and except the
  //   Reminder/Deadline data while those editors are open. This is what lets
  //   Status/Pin/Notes/Waiting-for catch up after a bridge command completes
  //   without re-collapsing Reminder/Deadline or discarding an in-progress edit.
  useEffect(() => {
    const idChanged = (task?.id ?? null) !== lastTaskIdRef.current
    lastTaskIdRef.current = task?.id ?? null

    if (idChanged) {
      setDraft(task)
      sessionBaseRef.current = task
      setReminderOpen(false)
      setReminderFocused(false)
      setDeadlineOpen(false)
      setDeadlineFocused(false)
      setLocationOpen(false)
      setDeadlineWithTime(task?.deadlineTime ? true : false)
      setActiveField(null)
      setStepsOpen(false)
      setNewStepText("")
      setEditingStepId(null)
      setEditingStepText("")
      return
    }

    // Mock/full mode has no independent authoritative snapshot: `task` there is
    // just an echo of our own onApply write-back, so merging it back in would
    // fight the very state it was derived from. Reconciliation only applies to
    // the bridged (connected/read-only) snapshot flow.
    if (!task || editMode === "full") return
    setDraft((current) => current
      ? mergeTaskFields(current, task, activeField, reminderOpen || reminderFocused, deadlineOpen || deadlineFocused)
      : task)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [task, reminderOpen, reminderFocused, deadlineOpen, deadlineFocused, activeField, editMode])

  useEffect(() => {
    if (!focusTitle || !task || task.id !== draft?.id) return
    const frame = requestAnimationFrame(() => {
      titleInputRef.current?.focus()
      titleInputRef.current?.select()
      onTitleFocused?.()
    })
    return () => cancelAnimationFrame(frame)
  }, [focusTitle, task, draft?.id, onTitleFocused])

  // Auto-apply: push every draft change up to parent immediately (mock mode only)
  useEffect(() => {
    if (!draft || editMode !== "full") return
    onApply(draft)
  }, [draft, editMode])

  // A failed bridge command recovers by merging the (unchanged) authoritative
  // task back in — same field-level rule as above, so an error on one field
  // (e.g. Status) can't wipe out an in-progress edit on another (e.g. Notes).
  useEffect(() => {
    if (!bridgeError || !task) return
    setDraft((current) => current
      ? mergeTaskFields(current, task, activeField, reminderOpen || reminderFocused, deadlineOpen || deadlineFocused)
      : task)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bridgeError, task])

  if (!draft) {
    return (
      <aside className="flex h-full w-full flex-col border-l border-border bg-sidebar">
        <div className="flex flex-1 flex-col items-center justify-center gap-2 px-6 text-center">
          <div className="flex size-12 items-center justify-center rounded-full bg-accent">
            <MapPin className="size-5 text-muted-foreground" />
          </div>
          <p className="text-sm font-medium text-foreground">Nothing selected</p>
          <p className="text-xs text-muted-foreground">Select a task in the tree to see details.</p>
        </div>
      </aside>
    )
  }

  const dirty = JSON.stringify(draft) !== JSON.stringify(sessionBaseRef.current)
  const projectSections = sections.filter((s) => s.projectId === draft.projectId)
  const locationSummary =
    [
      projects.find((p) => p.id === draft.projectId)?.name,
      sections.find((s) => s.id === draft.sectionId)?.name,
    ]
      .filter(Boolean)
      .join(" / ") || "—"
  const set = <K extends keyof Task>(key: K, value: Task[K]) => setDraft((d) => d ? { ...d, [key]: value } : d)
  const hasReminder = deriveReminderState(draft) !== "none"
  const hasDeadline = !!(draft.deadlineDate || draft.deadline)
  const bridged = editMode !== "full"
  const connected = editMode === "connected"
  const locked = editMode === "readonly"
  const taskId = draft.id
  const sourceTitle = task?.title ?? draft.title
  const sourceNotes = task?.notes ?? ""
  const sourceWaitingFor = task?.waitingFor ?? ""

  function sendBridgeEdit(
    field: BridgeEditField,
    value: BridgeEditValue,
  ) {
    if (!connected || pendingFields.has(field)) return
    onClearBridgeError?.()
    onBridgeEdit?.(taskId, field, value)
  }

  function clearReminder() {
    if (connected) sendBridgeEdit("reminder", { remindAtUtc: null, remindEveryMinutes: null })
    setDraft((d) => d ? {
      ...d,
      reminder: "none",
      reminderDate: undefined,
      reminderTime: undefined,
      reminderRepeat: false,
      reminderInterval: undefined,
    } : d)
    setReminderOpen(false)
  }

  function clearDeadline() {
    if (connected) sendBridgeEdit("deadline", null)
    setDraft((d) => d ? { ...d, deadlineDate: undefined, deadlineTime: undefined, deadline: undefined } : d)
    setDeadlineOpen(false)
  }

  function applyReminderPatch(patch: Partial<Pick<Task, "reminder" | "reminderDate" | "reminderTime">>) {
    if (!draft) return
    const next = { ...draft, ...patch }
    setDraft(next)
    // A partial date-only or time-only entry must not send a clear (null) to the
    // bridge — only push once both date and time make a complete instant. The
    // existing repeat cadence rides along unchanged so editing the date/time
    // doesn't silently wipe an already-active repeat (SetSchedule replaces both
    // fields at once — see ReminderBridgeValue).
    if (connected) {
      const iso = computeReminderIso(next)
      if (iso) {
        sendBridgeEdit("reminder", {
          remindAtUtc: iso,
          remindEveryMinutes: next.reminderRepeat ? repeatIntervalMinutes(next.reminderInterval) : null,
        })
      }
    }
  }

  /** One-shot preset pick — always turns Repeat off, matching the WPF ReminderEditor precedent
   * of resetting recurrence when a fresh preset is chosen. */
  function applyReminderPreset(preset: ReminderPreset) {
    setDraft((d) => d ? {
      ...d,
      reminder: preset,
      reminderDate: undefined,
      reminderTime: undefined,
      reminderRepeat: false,
      reminderInterval: undefined,
    } : d)
    if (connected) {
      const iso = computePresetReminderIso(preset, new Date())
      if (iso) sendBridgeEdit("reminder", { remindAtUtc: iso, remindEveryMinutes: null })
    }
  }

  /** Effective one-shot instant to carry alongside a repeat change: the custom date/time when
   * set, else a freshly recomputed instant for the active preset, else null (the bridge then
   * auto-schedules the first occurrence from now + interval). */
  function effectiveReminderIso(t: Task): string | null {
    return computeReminderIso(t) ??
      (t.reminder !== "none" && t.reminder !== "custom" ? computePresetReminderIso(t.reminder, new Date()) : null)
  }

  function applyReminderRepeatToggle() {
    if (!draft) return
    const turningOn = !draft.reminderRepeat
    const currentInterval = draft.reminderInterval
    const interval: RepeatInterval =
      currentInterval && currentInterval !== "monthly" && currentInterval !== "custom" ? currentInterval : "weekly"
    const next: Task = {
      ...draft,
      reminderRepeat: turningOn,
      reminderInterval: turningOn ? interval : draft.reminderInterval,
    }
    setDraft(next)
    if (connected) {
      const iso = effectiveReminderIso(next)
      const repeatMinutes = turningOn ? repeatIntervalMinutes(interval) : null
      if (iso || repeatMinutes) sendBridgeEdit("reminder", { remindAtUtc: iso, remindEveryMinutes: repeatMinutes })
    }
  }

  function applyReminderInterval(interval: RepeatInterval) {
    if (!draft) return
    if (connected && interval === "monthly") return
    const next: Task = { ...draft, reminderInterval: interval }
    setDraft(next)
    if (connected) {
      const iso = effectiveReminderIso(next)
      const repeatMinutes = repeatIntervalMinutes(interval)
      if (iso || repeatMinutes) sendBridgeEdit("reminder", { remindAtUtc: iso, remindEveryMinutes: repeatMinutes })
    }
  }

  function applyDeadlinePatch(patch: Partial<Pick<Task, "deadline" | "deadlineDate" | "deadlineTime">>) {
    if (!draft) return
    const next = { ...draft, ...patch }
    setDraft(next)
    // Same rule as reminders: an incomplete date/time combination must not clear
    // the deadline on the bridge.
    if (connected) {
      const iso = computeDeadlineIso(next, deadlineWithTime)
      if (iso) sendBridgeEdit("deadline", iso)
    }
  }

  // ── Steps (checkpoints) ──
  // Connected: send the granular command, update the draft optimistically, and
  // let the fresh snapshot reconcile (same pattern as Status/Pin). Mock/full:
  // the draft mutation itself is the persistence via onApply.
  const checkpoints = sortedCheckpoints(draft)
  const progress = checkpointProgress(draft)
  const checkpointsPending = pendingFields.has("checkpoints")

  function sendCheckpoint(command: WorkspaceTaskCommand) {
    if (!connected || checkpointsPending) return
    onClearBridgeError?.()
    onCheckpointCommand?.(command)
  }

  function setDraftCheckpoints(update: (items: TaskCheckpoint[]) => TaskCheckpoint[]) {
    setDraft((d) => {
      if (!d) return d
      const next = update(sortedCheckpoints(d)).map((item, index) => ({ ...item, order: index }))
      return { ...d, checkpoints: next }
    })
  }

  function addSteps(titles: string[]) {
    const clean = titles.map((t) => t.trim()).filter(Boolean)
    if (clean.length === 0) return
    if (connected) sendCheckpoint({ type: "addTaskCheckpoints", taskId, titles: clean })
    // Optimistic ids are placeholders until the snapshot reconciles; while the
    // command is pending, all step controls are disabled, so a placeholder id
    // can never be sent back to the bridge.
    setDraftCheckpoints((items) => [
      ...items,
      ...clean.map((title, index) => ({
        id: connected ? `pending-${items.length + index}` : newCheckpointId(),
        title,
        done: false,
        order: items.length + index,
      })),
    ])
    setNewStepText("")
  }

  function toggleStep(checkpointId: string, done: boolean) {
    if (connected) sendCheckpoint({ type: "toggleTaskCheckpoint", taskId, checkpointId, done })
    setDraftCheckpoints((items) => items.map((c) => c.id === checkpointId ? { ...c, done } : c))
  }

  function commitStepEdit() {
    const checkpointId = editingStepId
    const title = editingStepText.trim()
    setEditingStepId(null)
    setEditingStepText("")
    if (!checkpointId || !title) return
    const current = checkpoints.find((c) => c.id === checkpointId)
    if (!current || current.title === title) return
    if (connected) sendCheckpoint({ type: "updateTaskCheckpointTitle", taskId, checkpointId, title })
    setDraftCheckpoints((items) => items.map((c) => c.id === checkpointId ? { ...c, title } : c))
  }

  function deleteStep(checkpointId: string) {
    if (connected) sendCheckpoint({ type: "deleteTaskCheckpoint", taskId, checkpointId })
    setDraftCheckpoints((items) => items.filter((c) => c.id !== checkpointId))
  }

  function moveStep(checkpointId: string, delta: -1 | 1) {
    const index = checkpoints.findIndex((c) => c.id === checkpointId)
    if (index < 0) return
    const targetIndex = Math.min(Math.max(index + delta, 0), checkpoints.length - 1)
    if (targetIndex === index) return
    if (connected) sendCheckpoint({ type: "reorderTaskCheckpoint", taskId, checkpointId, targetIndex })
    setDraftCheckpoints((items) => {
      const next = [...items]
      const [moved] = next.splice(index, 1)
      next.splice(targetIndex, 0, moved)
      return next
    })
  }

  function revert() {
    setDraft(sessionBaseRef.current)
    setReminderOpen(false)
    setDeadlineOpen(false)
    setDeadlineWithTime(sessionBaseRef.current?.deadlineTime ? true : false)
    setActiveField(null)
    setNewStepText("")
    setEditingStepId(null)
    setEditingStepText("")
  }

  return (
    <aside className="flex h-full w-full flex-col border-l border-border bg-sidebar">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <h2 className="text-sm font-semibold text-foreground">Details</h2>
        <div className="flex items-center gap-1.5">
          {bridged && (
            <span className="rounded-md border border-primary/30 bg-primary/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-primary">
              {connected ? "Connected" : "Read-only"}
            </span>
          )}
          <span className="rounded-md bg-accent px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
            Task
          </span>
        </div>
      </div>

      <div className="contents">
      {bridgeError && (
        <div className="mx-4 mt-3 rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-xs text-destructive">
          {bridgeError}
        </div>
      )}
      <div className="flex-1 space-y-3 overflow-y-auto px-4 py-4">

        {/* ── Title ── */}
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
            Task title
          </label>
          <input
            ref={titleInputRef}
            value={draft.title}
            placeholder="Task title"
            onChange={(e) => set("title", e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Tab" && !e.shiftKey) {
                e.preventDefault()
                notesInputRef.current?.focus()
              }
            }}
            onFocus={() => setActiveField("title")}
            onBlur={() => {
              if (connected && draft.title !== sourceTitle) sendBridgeEdit("title", draft.title)
              setActiveField(null)
            }}
            disabled={locked || pendingFields.has("title")}
            className="w-full rounded-lg border border-input bg-background px-3 py-2 text-sm text-foreground outline-none transition-colors focus:border-primary/60 focus:ring-2 focus:ring-primary/20"
          />
        </div>

        {/* ── Status chips ── */}
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
            Status
          </label>
          <div className="grid grid-cols-4 gap-1.5">
            {statuses.map((s) => {
              const c = statusConfig[s]
              const active = draft.status === s
              return (
                <button
                  key={s}
                  onClick={() => {
                    // Optimistic: reflect immediately in Details, then the
                    // bridge command's snapshot reconciles/corrects it. Tree
                    // reads the same snapshot directly, so the two can no
                    // longer disagree after the round-trip.
                    if (connected) sendBridgeEdit("status", s)
                    set("status", s)
                  }}
                  disabled={locked || pendingFields.has("status")}
                  className={cn(
                    "flex items-center justify-center gap-1 rounded-md border px-1 py-2 text-[10px] font-semibold uppercase tracking-wide transition-colors",
                    active
                      ? cn(c.soft, c.text, "border-current/50 ring-1 ring-inset", c.ring)
                      : "border-border text-muted-foreground hover:bg-accent hover:text-foreground",
                  )}
                >
                  <span className={cn("size-1.5 rounded-full", active ? c.dot : "bg-muted-foreground/40")} />
                  {c.label}
                </button>
              )
            })}
          </div>
        </div>

        {/* ── Waiting for — directly under status ── */}
        {/* Compact like Notes but one-line collapsed: label lives inside as a
            faint placeholder (shown only while empty, never saved). A textarea
            (not input) so multiline paste/typing works; Enter inserts a newline
            and never submits (there is no form). Height follows content via
            field-sizing:content, clamped to ~1 line idle and expanded on
            hover/focus. Save path (onBlur -> bridge) is unchanged. */}
        {draft.status === "WAIT" && (
          <div>
            <textarea
              value={draft.waitingFor ?? ""}
              placeholder="Waiting for"
              rows={1}
              onChange={(e) => set("waitingFor", e.target.value || undefined)}
              onFocus={() => setActiveField("waitingFor")}
              onBlur={() => {
                const waitingFor = draft.waitingFor ?? ""
                if (connected && waitingFor !== sourceWaitingFor) sendBridgeEdit("waitingFor", waitingFor)
                setActiveField(null)
              }}
              disabled={locked || pendingFields.has("waitingFor")}
              className={cn(
                // leading-5 pins line-height to 1.25rem so the collapsed max-height
                // is an exact line count: py-2 (1rem) + border (2px) + 1×1.25rem.
                // With overflow-hidden this clips cleanly at the 1st line boundary —
                // no partial 2nd line peeking through.
                "w-full resize-none rounded-lg border border-status-wait/40 bg-background px-3 py-2 text-sm leading-5 text-foreground outline-none [field-sizing:content]",
                "transition-[max-height,border-color,box-shadow] duration-150",
                "placeholder:text-[11px] placeholder:font-semibold placeholder:uppercase placeholder:tracking-wide placeholder:text-muted-foreground/40",
                "max-h-[calc(2.25rem_+_2px)] overflow-hidden hover:max-h-32 hover:overflow-y-auto focus:max-h-32 focus:overflow-y-auto",
                "focus:border-status-wait/60 focus:ring-2 focus:ring-status-wait/15",
              )}
            />
          </div>
        )}

        {/* ── REMINDER — full-width collapsible ── */}
        {/* mt-3 is applied on the card itself, not via the parent's space-y-3:
            the wrapping fieldset is display:contents, so a margin on it (or the
            parent's inter-child margin) generates no box and is dropped. */}
        <fieldset disabled={locked} className="contents">
        <div
          className={cn("group/card mt-3 rounded-lg border border-border bg-card/40", locked && "opacity-60")}
          onFocus={() => setReminderFocused(true)}
          onBlur={(e) => { if (!e.currentTarget.contains(e.relatedTarget)) setReminderFocused(false) }}
        >
          {/* Header — one collapsed row; the editor below reveals on hover (CSS),
              focus-within (reminderFocused), or an explicit click (reminderOpen). */}
          <button
            type="button"
            onClick={() => setReminderOpen((o) => !o)}
            className="flex w-full items-center gap-2.5 px-3 py-2.5 text-left"
            aria-expanded={reminderOpen || reminderFocused}
          >
            <Bell
              className={cn(
                "size-3.5 shrink-0",
                hasReminder ? "text-status-remind" : "text-muted-foreground",
              )}
            />
            <span className="text-[11px] font-bold uppercase tracking-widest text-foreground">Reminder</span>

            {/* Summary */}
            <span className="flex flex-1 items-center justify-end gap-1.5 overflow-hidden">
              {hasReminder && draft.reminderRepeat && (
                <Repeat className="size-3 shrink-0 text-status-remind/70" />
              )}
              <span
                className={cn(
                  "truncate text-[11px]",
                  hasReminder ? "text-status-remind" : "text-muted-foreground",
                )}
              >
                {reminderSummary(draft)}
              </span>
              {hasReminder && (
                <span
                  role="button"
                  onClick={(e) => { e.stopPropagation(); if (!locked) clearReminder() }}
                  aria-label="Clear reminder"
                  className="shrink-0 rounded p-0.5 text-muted-foreground transition-colors hover:text-destructive"
                >
                  <X className="size-3" />
                </span>
              )}
            </span>

            <ChevronRight
              className={cn(
                "size-3.5 shrink-0 text-muted-foreground transition-transform group-hover/card:rotate-90",
                (reminderOpen || reminderFocused) && "rotate-90",
              )}
            />
          </button>

          {/* Editor — hidden while collapsed so its controls stay out of the tab
              order; revealed on hover / focus-within / click. */}
          <div
            className={cn(
              "space-y-3 border-t border-border/50 px-3 pb-3 pt-3 group-hover/card:block",
              (reminderOpen || reminderFocused) ? "block" : "hidden",
            )}
          >
              {/* Relative presets — computed client-side and pushed through the bridge when connected */}
              <div className="grid grid-cols-2 gap-1.5">
                {advancedPresets.map((r, i) => {
                  const active = draft.reminder === r.value && !draft.reminderDate
                  return (
                    <button
                      key={`${r.value}-${i}`}
                      onClick={() => applyReminderPreset(r.value)}
                      disabled={pendingFields.has("reminder")}
                      className={cn(
                        "rounded border px-2 py-1.5 text-left text-[11px] font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-50",
                        active
                          ? "border-status-remind/40 bg-status-remind/10 text-status-remind"
                          : "border-border text-muted-foreground hover:bg-accent",
                      )}
                    >
                      {r.label}
                    </button>
                  )
                })}
              </div>

              {/* Custom date + time — persists through the bridge when connected */}
              <div className="grid grid-cols-2 gap-1.5">
                <label className="flex flex-col gap-1">
                  <span className="text-[10px] uppercase tracking-wide text-muted-foreground">Date</span>
                  <input
                    type="date"
                    value={draft.reminderDate ?? ""}
                    onChange={(e) => applyReminderPatch({ reminderDate: e.target.value || undefined, reminder: "custom" })}
                    disabled={pendingFields.has("reminder")}
                    className="w-full rounded border border-input bg-background px-2 py-1.5 text-[11px] text-foreground outline-none focus:border-status-remind/60"
                  />
                </label>
                <label className="flex flex-col gap-1">
                  <span className="text-[10px] uppercase tracking-wide text-muted-foreground">Time</span>
                  <input
                    type="time"
                    value={draft.reminderTime ?? ""}
                    onChange={(e) => applyReminderPatch({ reminderTime: e.target.value || undefined, reminder: "custom" })}
                    disabled={pendingFields.has("reminder")}
                    className="w-full rounded border border-input bg-background px-2 py-1.5 text-[11px] text-foreground outline-none focus:border-status-remind/60"
                  />
                </label>
              </div>
              {connected && draft.reminderDate && !draft.reminderTime && (
                <p className="text-[10px] text-muted-foreground">Pick a time to save this reminder.</p>
              )}

              {/* Repeat — cadence pushed through the bridge as remindEveryMinutes when connected */}
              <div className="flex items-center gap-2 border-t border-border/50 pt-2.5">
                <Repeat
                  className={cn(
                    "size-3.5 shrink-0",
                    draft.reminderRepeat ? "text-status-remind" : "text-muted-foreground",
                  )}
                />
                <span className="flex-1 text-[11px] font-medium text-foreground">Repeat</span>
                <Switch
                  checked={!!draft.reminderRepeat}
                  activeColor="bg-status-remind"
                  disabled={pendingFields.has("reminder")}
                  onChange={applyReminderRepeatToggle}
                />
              </div>

              {/* Repeat intervals — only when Repeat ON. Monthly has no flat-minute equivalent
                  the backend can represent yet, so it's disabled (not hidden) with a "Later"
                  hint when connected; it still works in mock/dev mode. */}
              {draft.reminderRepeat && (
                <div className="grid grid-cols-2 gap-1.5">
                  {repeatIntervals.map((iv, i) => {
                    const active = draft.reminderInterval === iv.value
                    const monthlyDisabled = connected && iv.value === "monthly"
                    return (
                      <button
                        key={`${iv.value}-${i}`}
                        type="button"
                        onClick={() => applyReminderInterval(iv.value)}
                        disabled={monthlyDisabled || pendingFields.has("reminder")}
                        title={monthlyDisabled ? "Needs a calendar-aware recurrence model — coming later" : undefined}
                        className={cn(
                          "rounded border px-2 py-1.5 text-[11px] font-medium transition-colors",
                          monthlyDisabled
                            ? "cursor-not-allowed border-border/50 text-muted-foreground/40"
                            : active
                              ? "border-status-remind/40 bg-status-remind/10 text-status-remind"
                              : "border-border text-muted-foreground hover:bg-accent disabled:cursor-not-allowed disabled:opacity-50",
                        )}
                      >
                        {iv.label}
                        {monthlyDisabled && <span className="ml-1 text-[9px] uppercase tracking-wide">Later</span>}
                      </button>
                    )
                  })}
                </div>
              )}
            </div>
        </div>
        </fieldset>

        {/* ── DEADLINE — full-width collapsible ── */}
        <fieldset disabled={locked} className="contents">
        <div
          className={cn("group/card mt-3 rounded-lg border border-border bg-card/40", locked && "opacity-60")}
          onFocus={() => setDeadlineFocused(true)}
          onBlur={(e) => { if (!e.currentTarget.contains(e.relatedTarget)) setDeadlineFocused(false) }}
        >
          {/* Header — one collapsed row; the editor below reveals on hover (CSS),
              focus-within (deadlineFocused), or an explicit click (deadlineOpen). */}
          <button
            type="button"
            onClick={() => setDeadlineOpen((o) => !o)}
            className="flex w-full items-center gap-2.5 px-3 py-2.5 text-left"
            aria-expanded={deadlineOpen || deadlineFocused}
          >
            <Flag
              className={cn(
                "size-3.5 shrink-0",
                hasDeadline ? "fill-current text-status-deadline" : "text-muted-foreground",
              )}
            />
            <span className="text-[11px] font-bold uppercase tracking-widest text-foreground">Deadline</span>

            {/* Summary */}
            <span className="flex flex-1 items-center justify-end gap-1.5 overflow-hidden">
              <span
                className={cn(
                  "truncate text-[11px]",
                  hasDeadline ? "text-status-deadline" : "text-muted-foreground",
                )}
              >
                {deadlineSummary(draft)}
              </span>
              {hasDeadline && (
                <span
                  role="button"
                  onClick={(e) => { e.stopPropagation(); if (!locked) clearDeadline() }}
                  aria-label="Clear deadline"
                  className="shrink-0 rounded p-0.5 text-muted-foreground transition-colors hover:text-destructive"
                >
                  <X className="size-3" />
                </span>
              )}
            </span>

            <ChevronRight
              className={cn(
                "size-3.5 shrink-0 text-muted-foreground transition-transform group-hover/card:rotate-90",
                (deadlineOpen || deadlineFocused) && "rotate-90",
              )}
            />
          </button>

          {/* Editor — hidden while collapsed so its controls stay out of the tab
              order; revealed on hover / focus-within / click. */}
          <div
            className={cn(
              "space-y-2.5 border-t border-border/50 px-3 pb-3 pt-3 group-hover/card:block",
              (deadlineOpen || deadlineFocused) ? "block" : "hidden",
            )}
          >
              {/* Quick presets */}
              <div className="grid grid-cols-2 gap-1.5">
                {getDeadlinePresets().map((p) => {
                  const active = draft.deadlineDate === p.value
                  return (
                    <button
                      key={`${p.label}:${p.value}`}
                      onClick={() => applyDeadlinePatch({
                        deadlineDate: p.value,
                        deadline: p.label,
                        deadlineTime: deadlineWithTime ? draft.deadlineTime : undefined,
                      })}
                      disabled={pendingFields.has("deadline")}
                      className={cn(
                        "rounded border px-2 py-1.5 text-left text-[11px] font-medium transition-colors",
                        active
                          ? "border-status-deadline/40 bg-status-deadline/10 text-status-deadline"
                          : "border-border text-muted-foreground hover:bg-accent",
                      )}
                    >
                      {p.label}
                    </button>
                  )
                })}
              </div>

              {/* Date / Date+time mode toggle */}
              <div className="flex items-center gap-1 rounded border border-border p-0.5">
                <button
                  onClick={() => {
                    setDeadlineWithTime(false)
                    applyDeadlinePatch({ deadlineTime: undefined })
                  }}
                  className={cn(
                    "flex-1 rounded px-2 py-1 text-[11px] font-medium transition-colors",
                    !deadlineWithTime ? "bg-accent text-foreground" : "text-muted-foreground hover:text-foreground",
                  )}
                >
                  Date only
                </button>
                <button
                  onClick={() => setDeadlineWithTime(true)}
                  className={cn(
                    "flex-1 rounded px-2 py-1 text-[11px] font-medium transition-colors",
                    deadlineWithTime ? "bg-accent text-foreground" : "text-muted-foreground hover:text-foreground",
                  )}
                >
                  Date + time
                </button>
              </div>

              {/* Inputs — persist through the bridge when connected */}
              <div className={cn("grid gap-1.5", deadlineWithTime ? "grid-cols-2" : "grid-cols-1")}>
                <label className="flex flex-col gap-1">
                  <span className="text-[10px] uppercase tracking-wide text-muted-foreground">Date</span>
                  <input
                    type="date"
                    value={draft.deadlineDate ?? ""}
                    onChange={(e) => applyDeadlinePatch({ deadlineDate: e.target.value || undefined, deadline: e.target.value || undefined })}
                    disabled={pendingFields.has("deadline")}
                    className="w-full rounded border border-input bg-background px-2 py-1.5 text-[11px] text-foreground outline-none focus:border-status-deadline/60"
                  />
                </label>
                {deadlineWithTime && (
                  <label className="flex flex-col gap-1">
                    <span className="text-[10px] uppercase tracking-wide text-muted-foreground">Time</span>
                    <input
                      type="time"
                      value={draft.deadlineTime ?? ""}
                      onChange={(e) => applyDeadlinePatch({ deadlineTime: e.target.value || undefined })}
                      disabled={pendingFields.has("deadline")}
                      className="w-full rounded border border-input bg-background px-2 py-1.5 text-[11px] text-foreground outline-none focus:border-status-deadline/60"
                    />
                  </label>
                )}
              </div>
              {connected && !deadlineWithTime && draft.deadlineDate && (
                <p className="text-[10px] text-muted-foreground">Date-only deadlines are due by end of that day.</p>
              )}
            </div>
        </div>
        </fieldset>

        {/* ── PIN TO PANEL — single compact row (hidden for now; see SHOW_PIN_TO_PANEL) ── */}
        {SHOW_PIN_TO_PANEL && (
          <div className="flex items-center gap-2.5 px-1 py-1">
            <Pin
              className={cn(
                "size-3.5 shrink-0",
                draft.pinned ? "fill-current text-status-panel" : "text-muted-foreground",
              )}
            />
            <span className="flex-1 text-[11px] font-bold uppercase tracking-widest text-foreground">Pin to panel</span>
            <Switch
              checked={draft.pinned}
              activeColor="bg-status-panel"
              disabled={locked || pendingFields.has("pinToPanel")}
              onChange={() => {
                // Same optimistic-then-reconciled pattern as Status: update the
                // draft immediately, let the fresh snapshot confirm/correct it.
                const next = !draft.pinned
                if (connected) sendBridgeEdit("pinToPanel", next)
                set("pinned", next)
              }}
            />
          </div>
        )}

        {/* ── LOCATION ── */}
        {/* Connected: changing project/section sends moveTask; the fresh snapshot
            reconciles the draft. Mock: updates the local draft directly.
            Changing project moves the task to that project's root by default. */}
        <fieldset disabled={locked || pendingFields.has("location")} className="contents">
        <div className={cn("group/card mt-3 rounded-lg border border-border bg-card/40", locked && "opacity-60")}>
          {/* Header — one collapsed row; selectors reveal on hover / focus-within / click */}
          <button
            type="button"
            onClick={() => setLocationOpen((o) => !o)}
            className="flex w-full items-center gap-2.5 px-3 py-2.5 text-left"
            aria-expanded={locationOpen}
          >
            <MapPin className="size-3.5 shrink-0 text-muted-foreground" />
            <span className="text-[11px] font-bold uppercase tracking-widest text-foreground">Location</span>
            <span className="flex-1 truncate text-right text-[11px] text-muted-foreground">{locationSummary}</span>
            <ChevronRight
              className={cn(
                "size-3.5 shrink-0 text-muted-foreground transition-transform group-hover/card:rotate-90 group-focus-within/card:rotate-90",
                locationOpen && "rotate-90",
              )}
            />
          </button>

          {/* Selectors — hidden while collapsed; revealed on hover / focus-within / click.
              A native <select> keeps focus while its dropdown is open, so focus-within
              holds the card open through the whole selection. */}
          <div
            className={cn(
              "space-y-2 border-t border-border/50 px-3 pb-3 pt-3 group-hover/card:block group-focus-within/card:block",
              locationOpen ? "block" : "hidden",
            )}
          >
            <LocationRow label="Project">
              <Select
                value={draft.projectId}
                onChange={(v) => {
                  if (v === draft.projectId) return
                  if (connected) {
                    const rootSectionId = `project:${v}:root`
                    setDraft((d) => d ? { ...d, projectId: v, sectionId: rootSectionId, parentId: null } : d)
                    onMoveTask?.(draft.id, rootSectionId)
                  } else {
                    const first = sections.find((s) => s.projectId === v)
                    setDraft((d) => d ? { ...d, projectId: v, sectionId: first ? first.id : d.sectionId, parentId: null } : d)
                  }
                }}
                options={projects.map((p) => ({ value: p.id, label: p.name }))}
              />
            </LocationRow>
            <LocationRow label="Section">
              <Select
                value={draft.sectionId}
                onChange={(v) => {
                  if (v === draft.sectionId) return
                  if (connected) {
                    const target = sections.find((s) => s.id === v)
                    setDraft((d) => d ? {
                      ...d,
                      sectionId: v,
                      projectId: target?.projectId ?? d.projectId,
                      parentId: null,
                    } : d)
                    onMoveTask?.(draft.id, v)
                  }
                  else set("sectionId", v)
                }}
                options={projectSections.map((s) => ({ value: s.id, label: s.name }))}
              />
            </LocationRow>
          </div>
        </div>
        </fieldset>

        {/* ── NOTES ── */}
        {/* Label lives inside as a faint placeholder (shown only while empty and
            never saved). Height follows content via field-sizing:content, clamped
            to ~2 lines while idle and expanded on hover/focus for comfortable
            editing — the save path (onBlur -> bridge) is unchanged. */}
        {/* mt-3 gives the same gap the cards use: Tailwind v4 space-y puts the gap
            as margin-bottom on the preceding sibling, which is lost on the
            display:contents Location fieldset, so Notes needs its own top margin. */}
        <div className="mt-3">
          <textarea
            ref={notesInputRef}
            value={draft.notes ?? ""}
            onChange={(e) => set("notes", e.target.value || undefined)}
            onFocus={() => setActiveField("notes")}
            onBlur={() => {
              const notes = draft.notes ?? ""
              if (connected && notes !== sourceNotes) sendBridgeEdit("notes", notes)
              setActiveField(null)
            }}
            disabled={locked || pendingFields.has("notes")}
            rows={2}
            placeholder="NOTES / CONTEXT"
            className={cn(
              // leading-5 pins line-height to 1.25rem so the collapsed max-height
              // is an exact line count: py-2 (1rem) + border (2px) + 2×1.25rem.
              // With overflow-hidden this clips cleanly at the 2nd line boundary —
              // no partial 3rd line peeking through.
              "w-full resize-none rounded-lg border border-input bg-background px-3 py-2 text-sm leading-5 text-foreground outline-none [field-sizing:content]",
              "transition-[max-height,border-color,box-shadow] duration-150",
              "placeholder:text-[11px] placeholder:font-semibold placeholder:uppercase placeholder:tracking-wide placeholder:text-muted-foreground/40",
              // Idle: clamp to exactly 2 lines and hide the overflow. Hover/focus:
              // expand to a reasonable max and scroll if the note is very long.
              "max-h-[calc(3.5rem_+_2px)] overflow-hidden hover:max-h-48 hover:overflow-y-auto focus:max-h-48 focus:overflow-y-auto",
              "focus:border-primary/60 focus:ring-2 focus:ring-primary/20",
            )}
          />
        </div>

        {/* ── STEPS — lightweight execution checkpoints, separate from Notes ── */}
        <fieldset disabled={locked} className="contents">
        <div className={cn("group/card mt-3 rounded-lg border border-border bg-card/40", locked && "opacity-60")}>
          {/* Header — one collapsed row (icon, label, summary); the editor below
              reveals on hover (CSS), focus-within, or an explicit click (stepsOpen). */}
          <button
            type="button"
            onClick={() => setStepsOpen((o) => !o)}
            className="flex w-full items-center gap-2.5 px-3 py-2.5 text-left"
            aria-expanded={stepsOpen}
          >
            <ListChecks
              className={cn(
                "size-3.5 shrink-0",
                progress.total > 0 ? "text-primary" : "text-muted-foreground",
              )}
            />
            <span className="text-[11px] font-bold uppercase tracking-widest text-foreground">Steps</span>
            <span className="flex flex-1 items-center justify-end gap-1.5 overflow-hidden">
              <span
                className={cn(
                  "truncate text-[11px]",
                  progress.total > 0 && progress.done === progress.total
                    ? "text-status-done"
                    : progress.total > 0
                      ? "text-foreground"
                      : "text-muted-foreground",
                )}
              >
                {progress.summary}
              </span>
            </span>
            <ChevronRight
              className={cn(
                "size-3.5 shrink-0 text-muted-foreground transition-transform group-hover/card:rotate-90 group-focus-within/card:rotate-90",
                stepsOpen && "rotate-90",
              )}
            />
          </button>

          {/* Editor — hidden while collapsed so its controls stay out of the tab
              order and take no vertical space; revealed on hover / focus-within /
              click. The progress bar lives inside so it doesn't show while collapsed. */}
          <div
            className={cn(
              "space-y-2 border-t border-border/50 px-3 pb-3 pt-2.5 group-hover/card:block group-focus-within/card:block",
              stepsOpen ? "block" : "hidden",
            )}
          >
              {/* Subtle progress bar — only when there are at least 2 steps */}
              {progress.total >= 2 && (
                <div
                  role="progressbar"
                  aria-valuemin={0}
                  aria-valuemax={progress.total}
                  aria-valuenow={progress.done}
                  aria-label={`${progress.done} of ${progress.total} steps done`}
                  className="h-1 overflow-hidden rounded-full bg-border/60"
                >
                  <div
                    className="h-full rounded-full bg-primary/60 transition-[width]"
                    style={{ width: `${(progress.done / progress.total) * 100}%` }}
                  />
                </div>
              )}

              {/* Step rows — quiet by default; actions appear on hover/focus */}
              {checkpoints.map((step, index) => (
                <div
                  key={step.id}
                  className="group/step flex items-center gap-2 rounded px-1 py-0.5 focus-within:bg-accent/40 hover:bg-accent/40"
                >
                  <button
                    type="button"
                    role="checkbox"
                    aria-checked={step.done}
                    aria-label={step.done ? `Reopen step: ${step.title}` : `Complete step: ${step.title}`}
                    disabled={checkpointsPending}
                    onClick={() => toggleStep(step.id, !step.done)}
                    className={cn(
                      "flex size-4 shrink-0 items-center justify-center rounded border transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-primary",
                      step.done
                        ? "border-status-done/60 bg-status-done/20 text-status-done"
                        : "border-input bg-background text-transparent hover:border-primary/50",
                    )}
                  >
                    <Check className="size-3" aria-hidden />
                  </button>

                  {editingStepId === step.id ? (
                    <input
                      autoFocus
                      value={editingStepText}
                      onChange={(e) => setEditingStepText(e.target.value)}
                      onBlur={commitStepEdit}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") {
                          e.preventDefault()
                          commitStepEdit()
                        } else if (e.key === "Escape") {
                          setEditingStepId(null)
                          setEditingStepText("")
                        }
                      }}
                      className="min-w-0 flex-1 rounded border border-input bg-background px-1.5 py-0.5 text-xs text-foreground outline-none focus:border-primary/60"
                    />
                  ) : (
                    <button
                      type="button"
                      onClick={() => {
                        if (checkpointsPending) return
                        setEditingStepId(step.id)
                        setEditingStepText(step.title)
                      }}
                      title="Edit step"
                      className={cn(
                        "min-w-0 flex-1 truncate rounded text-left text-xs focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-primary",
                        step.done ? "text-muted-foreground line-through" : "text-foreground",
                      )}
                    >
                      {step.title}
                    </button>
                  )}

                  {/* Row actions — hidden until hover or keyboard focus lands in the row */}
                  <span className="flex shrink-0 items-center gap-0.5 opacity-0 transition-opacity focus-within:opacity-100 group-focus-within/step:opacity-100 group-hover/step:opacity-100">
                    <button
                      type="button"
                      aria-label="Move step up"
                      disabled={checkpointsPending || index === 0}
                      onClick={() => moveStep(step.id, -1)}
                      className="rounded p-0.5 text-muted-foreground transition-colors hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary disabled:cursor-not-allowed disabled:opacity-30"
                    >
                      <ArrowUp className="size-3" />
                    </button>
                    <button
                      type="button"
                      aria-label="Move step down"
                      disabled={checkpointsPending || index === checkpoints.length - 1}
                      onClick={() => moveStep(step.id, 1)}
                      className="rounded p-0.5 text-muted-foreground transition-colors hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary disabled:cursor-not-allowed disabled:opacity-30"
                    >
                      <ArrowDown className="size-3" />
                    </button>
                    <button
                      type="button"
                      aria-label={`Delete step: ${step.title}`}
                      disabled={checkpointsPending}
                      onClick={() => deleteStep(step.id)}
                      className="rounded p-0.5 text-muted-foreground transition-colors hover:text-destructive focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary disabled:cursor-not-allowed disabled:opacity-30"
                    >
                      <X className="size-3" />
                    </button>
                  </span>
                </div>
              ))}

              {/* Add input — Enter adds one step; pasting a multiline list adds them all */}
              <input
                value={newStepText}
                onChange={(e) => setNewStepText(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault()
                    addSteps([newStepText])
                  } else if (e.key === "Escape") {
                    setNewStepText("")
                  }
                }}
                onPaste={(e) => {
                  const text = e.clipboardData.getData("text")
                  if (!text.includes("\n")) return
                  e.preventDefault()
                  addSteps(splitPastedSteps(text))
                }}
                disabled={checkpointsPending}
                placeholder="+ Next step… (paste a list to add several)"
                className="w-full rounded border border-input bg-background px-2 py-1.5 text-xs text-foreground outline-none transition-colors placeholder:text-muted-foreground/60 focus:border-primary/60"
              />

              {/* Footer — remaining count, or the explicit complete-task action */}
              {progress.total > 0 && progress.done === progress.total ? (
                <div className="flex items-center justify-between gap-2 pt-0.5">
                  <span className="text-[11px] font-medium text-status-done">All steps ready</span>
                  {draft.status !== "DONE" && (
                    <button
                      type="button"
                      disabled={pendingFields.has("status")}
                      onClick={() => {
                        // Completing the parent is always this explicit action —
                        // finishing the last step never auto-completes the task.
                        if (connected) sendBridgeEdit("status", "DONE")
                        set("status", "DONE")
                      }}
                      className="rounded border border-status-done/40 bg-status-done/10 px-2 py-1 text-[11px] font-medium text-status-done transition-colors hover:bg-status-done/20 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary disabled:cursor-not-allowed disabled:opacity-50"
                    >
                      Complete task
                    </button>
                  )}
                </div>
              ) : progress.total > 0 ? (
                <p className="pt-0.5 text-[11px] text-muted-foreground">
                  {progress.total - progress.done === 1
                    ? "1 step left"
                    : `${progress.total - progress.done} steps left`}
                </p>
              ) : null}
            </div>
        </div>
        </fieldset>

        {/* ── CONTEXT — linked ContextHUB SourceDocuments/ContextItems (Task-only MVP) ── */}
        {onContextCommand && onOpenContextHub && (
          <TaskContextBlock
            task={draft}
            projects={projects}
            sections={sections}
            tasks={allTasks}
            meetItems={meetItems}
            contextSources={contextSources}
            contextItems={contextItems}
            onCommand={onContextCommand}
            onOpenContextHub={onOpenContextHub}
            locked={locked}
          />
        )}
      </div>

      {/* ── Bottom actions: Delete (left) + Revert (right) ── */}
      <div className="flex items-center gap-2 border-t border-border px-4 py-3">
        <button
          onClick={() => onDelete(draft.id)}
          disabled={locked}
          className="flex items-center gap-1.5 rounded-lg px-2.5 py-2 text-sm font-medium text-destructive transition-colors hover:bg-destructive/10 disabled:cursor-not-allowed disabled:opacity-40"
        >
          <Trash2 className="size-3.5" />
          Delete task
        </button>
        <div className="flex-1" />
        <button
          onClick={revert}
          disabled={!dirty}
          title="Revert all changes made this session"
          className="flex items-center gap-1.5 rounded-lg border border-border px-2.5 py-2 text-sm font-medium text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-30"
        >
          <UndoDot className="size-3.5" />
          Revert
        </button>
      </div>
      </div>
    </aside>
  )
}

// ── Shared primitives ──

function Switch({
  checked,
  onChange,
  activeColor = "bg-primary",
  disabled,
}: {
  checked: boolean
  onChange: () => void
  activeColor?: string
  disabled?: boolean
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      onClick={onChange}
      disabled={disabled}
      className={cn(
        "relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors",
        checked ? activeColor : "bg-input",
        disabled && "cursor-not-allowed opacity-50",
      )}
    >
      <span
        className={cn(
          "inline-block size-4 rounded-full bg-background shadow transition-transform",
          checked ? "translate-x-[18px]" : "translate-x-0.5",
        )}
      />
    </button>
  )
}

function LocationRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center gap-2">
      <span className="w-14 shrink-0 text-xs text-muted-foreground">{label}</span>
      <div className="min-w-0 flex-1">{children}</div>
    </div>
  )
}

function Select({
  value,
  onChange,
  options,
}: {
  value: string
  onChange: (v: string) => void
  options: { value: string; label: string }[]
}) {
  return (
    <select
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className="w-full rounded-md border border-input bg-background px-2 py-1.5 text-sm text-foreground outline-none transition-colors focus:border-primary/60"
    >
      {options.map((o) => (
        <option key={o.value} value={o.value}>
          {o.label}
        </option>
      ))}
    </select>
  )
}
