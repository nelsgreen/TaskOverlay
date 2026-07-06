"use client"

import { useEffect, useRef, useState } from "react"
import { Bell, ChevronDown, ChevronRight, Flag, MapPin, Pin, Repeat, Trash2, UndoDot, X } from "lucide-react"
import type { Project, ReminderPreset, ReminderState, RepeatInterval, Section, Status, Task } from "@/lib/types"
import { cn } from "@/lib/utils"
import { isoFromLocalDateTime } from "@/lib/calendar-date"
import { statusConfig } from "./status-badge"

type BridgeEditField = "title" | "status" | "pinToPanel" | "notes" | "waitingFor" | "reminder" | "deadline"

interface Props {
  task: Task | null
  projects: Project[]
  sections: Section[]
  /** Called on every draft change — auto-apply, no Save button */
  onApply: (task: Task) => void
  onDelete: (id: string) => void
  editMode?: "full" | "connected" | "readonly"
  pendingFields?: Set<string>
  bridgeError?: string | null
  onBridgeEdit?: (
    taskId: string,
    field: BridgeEditField,
    value: string | boolean | null,
  ) => boolean
  onClearBridgeError?: () => void
}

/** Combines local date+time fields into a UTC ISO instant, or null when incomplete/absent. */
function computeReminderIso(t: Task): string | null {
  if (!t.reminderDate || !t.reminderTime) return null
  const [h, m] = t.reminderTime.split(":").map(Number)
  return isoFromLocalDateTime(t.reminderDate, h, m)
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

const quickPresets: { value: ReminderPreset; label: string }[] = [
  { value: "30m", label: "In 30m" },
  { value: "1h", label: "In 1h" },
  { value: "morning", label: "Tomorrow morning" },
]

const advancedPresets: { value: ReminderPreset; label: string }[] = [
  { value: "30m", label: "In 30m" },
  { value: "1h", label: "In 1h" },
  { value: "afternoon", label: "In 2h" },
  { value: "morning", label: "Tomorrow morning" },
  { value: "next-morning", label: "Tomorrow afternoon" },
  { value: "next-morning", label: "Next workday morning" },
]

const repeatIntervals: { value: RepeatInterval; label: string }[] = [
  { value: "daily", label: "Every 2h" },
  { value: "daily", label: "Daily" },
  { value: "weekly", label: "Weekly" },
  { value: "monthly", label: "Monthly" },
]

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
  editMode = "full",
  pendingFields = new Set(),
  bridgeError,
  onBridgeEdit,
  onClearBridgeError,
}: Props) {
  const [draft, setDraft] = useState<Task | null>(task)
  const [reminderOpen, setReminderOpen] = useState(false)
  const [deadlineOpen, setDeadlineOpen] = useState(false)
  const [deadlineWithTime, setDeadlineWithTime] = useState(false)
  // Which plain-text field the user is currently typing in (between focus and
  // blur/commit). A fresh snapshot for the same task must not stomp this field,
  // even though it reconciles every other field. Reminder/Deadline don't need an
  // entry here — reminderOpen/deadlineOpen already mark those as "being edited".
  const [activeField, setActiveField] = useState<"title" | "notes" | "waitingFor" | null>(null)

  // Track the snapshot at the start of the editing session for "Revert"
  const sessionBaseRef = useRef<Task | null>(task)
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
      setDeadlineOpen(false)
      setDeadlineWithTime(task?.deadlineTime ? true : false)
      setActiveField(null)
      return
    }

    // Mock/full mode has no independent authoritative snapshot: `task` there is
    // just an echo of our own onApply write-back, so merging it back in would
    // fight the very state it was derived from. Reconciliation only applies to
    // the bridged (connected/read-only) snapshot flow.
    if (!task || editMode === "full") return
    setDraft((current) => current ? mergeTaskFields(current, task, activeField, reminderOpen, deadlineOpen) : task)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [task, reminderOpen, deadlineOpen, activeField, editMode])

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
    setDraft((current) => current ? mergeTaskFields(current, task, activeField, reminderOpen, deadlineOpen) : task)
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
    value: string | boolean | null,
  ) {
    if (!connected || pendingFields.has(field)) return
    onClearBridgeError?.()
    onBridgeEdit?.(taskId, field, value)
  }

  function clearReminder() {
    if (connected) sendBridgeEdit("reminder", null)
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
    // bridge — only push once both date and time make a complete instant.
    if (connected) {
      const iso = computeReminderIso(next)
      if (iso) sendBridgeEdit("reminder", iso)
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

  function revert() {
    setDraft(sessionBaseRef.current)
    setReminderOpen(false)
    setDeadlineOpen(false)
    setDeadlineWithTime(sessionBaseRef.current?.deadlineTime ? true : false)
    setActiveField(null)
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
            value={draft.title}
            onChange={(e) => set("title", e.target.value)}
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
        {draft.status === "WAIT" && <div>
          <label
            className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wide text-status-wait"
          >
            Waiting for
          </label>
          <input
            value={draft.waitingFor ?? ""}
            placeholder="e.g. reply from Madina"
            onChange={(e) => set("waitingFor", e.target.value || undefined)}
            onFocus={() => setActiveField("waitingFor")}
            onBlur={() => {
              const waitingFor = draft.waitingFor ?? ""
              if (connected && waitingFor !== sourceWaitingFor) sendBridgeEdit("waitingFor", waitingFor)
              setActiveField(null)
            }}
            disabled={locked || pendingFields.has("waitingFor")}
            className="w-full rounded-lg border border-status-wait/40 bg-background px-3 py-2 text-sm text-foreground outline-none transition-colors placeholder:text-muted-foreground/50 focus:border-status-wait/60 focus:ring-2 focus:ring-status-wait/15"
          />
        </div>}

        {/* ── REMINDER — full-width collapsible ── */}
        <fieldset disabled={locked} className="contents">
        <div className={cn("rounded-lg border border-border bg-card/40", locked && "opacity-60")}>
          {/* Header — entire row is clickable */}
          <button
            type="button"
            onClick={() => setReminderOpen((o) => !o)}
            className="flex w-full items-center gap-2.5 px-3 py-2.5 text-left"
            aria-expanded={reminderOpen}
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

            {reminderOpen
              ? <ChevronDown className="size-3.5 shrink-0 text-muted-foreground" />
              : <ChevronRight className="size-3.5 shrink-0 text-muted-foreground" />
            }
          </button>

          {/* Collapsed + no reminder: relative quick presets — dev/full mode only (no bridge command for these) */}
          {!connected && !reminderOpen && !hasReminder && (
            <div className="flex gap-1.5 border-t border-border/50 px-3 pb-2.5 pt-2">
              {quickPresets.map((r) => (
                <button
                  key={r.value}
                  onClick={() => setDraft((d) => d ? { ...d, reminder: r.value, reminderDate: undefined, reminderTime: undefined } : d)}
                  className="flex-1 rounded border border-border px-1 py-1.5 text-center text-[10px] font-medium text-muted-foreground transition-colors hover:border-status-remind/40 hover:bg-status-remind/10 hover:text-status-remind"
                >
                  {r.label}
                </button>
              ))}
            </div>
          )}

          {/* Expanded editor */}
          {reminderOpen && (
            <div className="space-y-3 border-t border-border/50 px-3 pb-3 pt-3">
              {/* Relative presets — dev/full mode only (no bridge command for these) */}
              {!connected && (
                <div className="grid grid-cols-2 gap-1.5">
                  {advancedPresets.map((r, i) => {
                    const active = draft.reminder === r.value && !draft.reminderDate
                    return (
                      <button
                        key={`${r.value}-${i}`}
                        onClick={() =>
                          setDraft((d) => d ? { ...d, reminder: r.value, reminderDate: undefined, reminderTime: undefined } : d)
                        }
                        className={cn(
                          "rounded border px-2 py-1.5 text-left text-[11px] font-medium transition-colors",
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
              )}

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

              {/* Repeat — dev/full mode only in this release */}
              {!connected && (
                <>
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
                      onChange={() =>
                        setDraft((d) => d ? {
                          ...d,
                          reminderRepeat: !d.reminderRepeat,
                          reminderInterval: !d.reminderRepeat ? (d.reminderInterval ?? "weekly") : d.reminderInterval,
                        } : d)
                      }
                    />
                  </div>

                  {/* Repeat intervals — only when Repeat ON */}
                  {draft.reminderRepeat && (
                    <div className="grid grid-cols-2 gap-1.5">
                      {repeatIntervals.map((iv, i) => {
                        const active = draft.reminderInterval === iv.value
                        return (
                          <button
                            key={`${iv.value}-${i}`}
                            onClick={() => set("reminderInterval", iv.value)}
                            className={cn(
                              "rounded border px-2 py-1.5 text-[11px] font-medium transition-colors",
                              active
                                ? "border-status-remind/40 bg-status-remind/10 text-status-remind"
                                : "border-border text-muted-foreground hover:bg-accent",
                            )}
                          >
                            {iv.label}
                          </button>
                        )
                      })}
                    </div>
                  )}
                </>
              )}
            </div>
          )}
        </div>
        </fieldset>

        {/* ── DEADLINE — full-width collapsible ── */}
        <fieldset disabled={locked} className="contents">
        <div className={cn("rounded-lg border border-border bg-card/40", locked && "opacity-60")}>
          {/* Header — entire row is clickable */}
          <button
            type="button"
            onClick={() => setDeadlineOpen((o) => !o)}
            className="flex w-full items-center gap-2.5 px-3 py-2.5 text-left"
            aria-expanded={deadlineOpen}
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

            {deadlineOpen
              ? <ChevronDown className="size-3.5 shrink-0 text-muted-foreground" />
              : <ChevronRight className="size-3.5 shrink-0 text-muted-foreground" />
            }
          </button>

          {/* Expanded editor */}
          {deadlineOpen && (
            <div className="space-y-2.5 border-t border-border/50 px-3 pb-3 pt-3">
              {/* Quick presets */}
              <div className="grid grid-cols-2 gap-1.5">
                {getDeadlinePresets().map((p) => {
                  const active = draft.deadlineDate === p.value
                  return (
                    <button
                      key={p.value}
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
          )}
        </div>
        </fieldset>

        {/* ── PIN TO PANEL — single compact row ── */}
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

        {/* ── LOCATION ── */}
        {/* No move/update-location bridge command exists yet, so this stays
            disabled with an explicit reason in connected/read-only mode instead
            of silently no-op'ing. */}
        <fieldset disabled={bridged} className="contents">
        <div
          className={cn("rounded-lg border border-border bg-card/40 p-3", bridged && "opacity-60")}
          title={bridged ? "Moving tasks between projects/sections is not available in this build yet" : undefined}
        >
          <div className="mb-2 flex items-center gap-1.5">
            <MapPin className="size-3.5 text-muted-foreground" />
            <span className="text-[11px] font-bold uppercase tracking-widest text-muted-foreground">
              Location
            </span>
            {bridged && (
              <span className="rounded bg-accent px-1 py-0.5 text-[8px] font-semibold uppercase tracking-wide text-muted-foreground">
                Later
              </span>
            )}
          </div>
          <div className="space-y-2">
            <LocationRow label="Project">
              <Select
                value={draft.projectId}
                onChange={(v) => {
                  const first = sections.find((s) => s.projectId === v)
                  setDraft((d) => d ? { ...d, projectId: v, sectionId: first ? first.id : d.sectionId, parentId: null } : d)
                }}
                options={projects.map((p) => ({ value: p.id, label: p.name }))}
              />
            </LocationRow>
            <LocationRow label="Section">
              <Select
                value={draft.sectionId}
                onChange={(v) => set("sectionId", v)}
                options={projectSections.map((s) => ({ value: s.id, label: s.name }))}
              />
            </LocationRow>
          </div>
        </div>
        </fieldset>

        {/* ── NOTES ── */}
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
            Notes / context
          </label>
          <textarea
            value={draft.notes ?? ""}
            onChange={(e) => set("notes", e.target.value || undefined)}
            onFocus={() => setActiveField("notes")}
            onBlur={() => {
              const notes = draft.notes ?? ""
              if (connected && notes !== sourceNotes) sendBridgeEdit("notes", notes)
              setActiveField(null)
            }}
            disabled={locked || pendingFields.has("notes")}
            rows={3}
            placeholder="Add context…"
            className="w-full resize-none rounded-lg border border-input bg-background px-3 py-2 text-sm text-foreground outline-none transition-colors placeholder:text-muted-foreground/60 focus:border-primary/60 focus:ring-2 focus:ring-primary/20"
          />
        </div>
      </div>

      {/* ── Bottom actions: Delete (left) + Revert (right) ── */}
      <div className="flex items-center gap-2 border-t border-border px-4 py-3">
        <button
          onClick={() => onDelete(draft.id)}
          disabled={bridged}
          className="flex items-center gap-1.5 rounded-lg px-2.5 py-2 text-sm font-medium text-destructive transition-colors hover:bg-destructive/10"
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
