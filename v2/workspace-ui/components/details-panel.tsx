"use client"

import { useEffect, useRef, useState } from "react"
import { Bell, ChevronDown, ChevronRight, Flag, MapPin, Pin, Repeat, Trash2, UndoDot, X } from "lucide-react"
import type { Project, ReminderPreset, ReminderState, RepeatInterval, Section, Status, Task } from "@/lib/types"
import { cn } from "@/lib/utils"
import { statusConfig } from "./status-badge"

interface Props {
  task: Task | null
  projects: Project[]
  sections: Section[]
  /** Called on every draft change — auto-apply, no Save button */
  onApply: (task: Task) => void
  onDelete: (id: string) => void
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

export function DetailsPanel({ task, projects, sections, onApply, onDelete }: Props) {
  const [draft, setDraft] = useState<Task | null>(task)
  const [reminderOpen, setReminderOpen] = useState(false)
  const [deadlineOpen, setDeadlineOpen] = useState(false)
  const [deadlineWithTime, setDeadlineWithTime] = useState(false)

  // Track the snapshot at the start of the editing session for "Revert"
  const sessionBaseRef = useRef<Task | null>(task)

  // When a new task is selected, reset the session base and accordion state
  useEffect(() => {
    setDraft(task)
    sessionBaseRef.current = task
    setReminderOpen(false)
    setDeadlineOpen(false)
    setDeadlineWithTime(task?.deadlineTime ? true : false)
  }, [task?.id])

  // Auto-apply: push every draft change up to parent immediately
  useEffect(() => {
    if (!draft) return
    onApply(draft)
  }, [draft])

  if (!draft) {
    return (
      <aside className="hidden w-72 shrink-0 flex-col border-l border-border bg-sidebar xl:flex">
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

  function clearReminder() {
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
    setDraft((d) => d ? { ...d, deadlineDate: undefined, deadlineTime: undefined, deadline: undefined } : d)
    setDeadlineOpen(false)
  }

  function revert() {
    setDraft(sessionBaseRef.current)
    setReminderOpen(false)
    setDeadlineOpen(false)
    setDeadlineWithTime(sessionBaseRef.current?.deadlineTime ? true : false)
  }

  return (
    <aside className="hidden w-72 shrink-0 flex-col border-l border-border bg-sidebar xl:flex">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <h2 className="text-sm font-semibold text-foreground">Details</h2>
        <span className="rounded-md bg-accent px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
          Task
        </span>
      </div>

      <div className="flex-1 space-y-3 overflow-y-auto px-4 py-4">

        {/* ── Title ── */}
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
            Task title
          </label>
          <input
            value={draft.title}
            onChange={(e) => set("title", e.target.value)}
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
                  onClick={() => set("status", s)}
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
        <div>
          <label
            className={cn(
              "mb-1.5 block text-[11px] font-semibold uppercase tracking-wide transition-colors",
              draft.status === "WAIT" ? "text-status-wait" : "text-muted-foreground/50",
            )}
          >
            Waiting for
          </label>
          <input
            value={draft.waitingFor ?? ""}
            placeholder={draft.status === "WAIT" ? "e.g. reply from Madina" : "—"}
            disabled={draft.status !== "WAIT"}
            onChange={(e) => set("waitingFor", e.target.value || undefined)}
            className={cn(
              "w-full rounded-lg border bg-background px-3 py-2 text-sm text-foreground outline-none transition-colors placeholder:text-muted-foreground/50",
              draft.status === "WAIT"
                ? "border-status-wait/40 focus:border-status-wait/60 focus:ring-2 focus:ring-status-wait/15"
                : "cursor-default border-input/40 opacity-40",
            )}
          />
        </div>

        {/* ── REMINDER — full-width collapsible ── */}
        <div className="rounded-lg border border-border bg-card/40">
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
                  onClick={(e) => { e.stopPropagation(); clearReminder() }}
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

          {/* Collapsed + no reminder: show 3 quick preset chips */}
          {!reminderOpen && !hasReminder && (
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
              {/* Presets grid */}
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

              {/* Custom date + time */}
              <div className="grid grid-cols-2 gap-1.5">
                <label className="flex flex-col gap-1">
                  <span className="text-[10px] uppercase tracking-wide text-muted-foreground">Date</span>
                  <input
                    type="date"
                    value={draft.reminderDate ?? ""}
                    onChange={(e) =>
                      setDraft((d) => d ? { ...d, reminderDate: e.target.value || undefined, reminder: "custom" } : d)
                    }
                    className="w-full rounded border border-input bg-background px-2 py-1.5 text-[11px] text-foreground outline-none focus:border-status-remind/60"
                  />
                </label>
                <label className="flex flex-col gap-1">
                  <span className="text-[10px] uppercase tracking-wide text-muted-foreground">Time</span>
                  <input
                    type="time"
                    value={draft.reminderTime ?? ""}
                    onChange={(e) =>
                      setDraft((d) => d ? { ...d, reminderTime: e.target.value || undefined, reminder: "custom" } : d)
                    }
                    className="w-full rounded border border-input bg-background px-2 py-1.5 text-[11px] text-foreground outline-none focus:border-status-remind/60"
                  />
                </label>
              </div>

              {/* Repeat toggle */}
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
            </div>
          )}
        </div>

        {/* ── DEADLINE — full-width collapsible ── */}
        <div className="rounded-lg border border-border bg-card/40">
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
                  onClick={(e) => { e.stopPropagation(); clearDeadline() }}
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
                      onClick={() =>
                        setDraft((d) => d ? {
                          ...d,
                          deadlineDate: p.value,
                          deadline: p.label,
                          deadlineTime: deadlineWithTime ? d.deadlineTime : undefined,
                        } : d)
                      }
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
                  onClick={() => { setDeadlineWithTime(false); setDraft((d) => d ? { ...d, deadlineTime: undefined } : d) }}
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

              {/* Inputs */}
              <div className={cn("grid gap-1.5", deadlineWithTime ? "grid-cols-2" : "grid-cols-1")}>
                <label className="flex flex-col gap-1">
                  <span className="text-[10px] uppercase tracking-wide text-muted-foreground">Date</span>
                  <input
                    type="date"
                    value={draft.deadlineDate ?? ""}
                    onChange={(e) =>
                      setDraft((d) => d ? { ...d, deadlineDate: e.target.value || undefined, deadline: e.target.value || undefined } : d)
                    }
                    className="w-full rounded border border-input bg-background px-2 py-1.5 text-[11px] text-foreground outline-none focus:border-status-deadline/60"
                  />
                </label>
                {deadlineWithTime && (
                  <label className="flex flex-col gap-1">
                    <span className="text-[10px] uppercase tracking-wide text-muted-foreground">Time</span>
                    <input
                      type="time"
                      value={draft.deadlineTime ?? ""}
                      onChange={(e) => set("deadlineTime", e.target.value || undefined)}
                      className="w-full rounded border border-input bg-background px-2 py-1.5 text-[11px] text-foreground outline-none focus:border-status-deadline/60"
                    />
                  </label>
                )}
              </div>
            </div>
          )}
        </div>

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
            onChange={() => set("pinned", !draft.pinned)}
          />
        </div>

        {/* ── LOCATION ── */}
        <div className="rounded-lg border border-border bg-card/40 p-3">
          <div className="mb-2 flex items-center gap-1.5">
            <MapPin className="size-3.5 text-muted-foreground" />
            <span className="text-[11px] font-bold uppercase tracking-widest text-muted-foreground">
              Location
            </span>
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

        {/* ── NOTES ── */}
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
            Notes / context
          </label>
          <textarea
            value={draft.notes ?? ""}
            onChange={(e) => set("notes", e.target.value || undefined)}
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
    </aside>
  )
}

// ── Shared primitives ──

function Switch({
  checked,
  onChange,
  activeColor = "bg-primary",
}: {
  checked: boolean
  onChange: () => void
  activeColor?: string
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      onClick={onChange}
      className={cn(
        "relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors",
        checked ? activeColor : "bg-input",
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
