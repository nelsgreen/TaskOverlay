"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { CalendarDays, Clock, ExternalLink, MapPin, Save, Trash2, UndoDot, Video, X } from "lucide-react"
import type {
  MeetDuration,
  MeetItem,
  MeetingAnalysisSnapshot,
  MeetingRecordingSnapshot,
  Project,
  Section,
  Task,
  WorkspaceContextHubCommand,
  WorkspaceContextItemSnapshot,
  WorkspaceContextSourceSnapshot,
  WorkspaceMeetingAssistantCommand,
} from "@/lib/types"
import { cn } from "@/lib/utils"
import { shouldCloseMeetModal, type MeetModalCloseReason } from "@/lib/meet-modal-policy"
import { MeetContextBlock } from "./task-context-block"
import { MeetingAssistantSection } from "./meeting-assistant-section"

interface Props {
  meet: MeetItem | null
  projects: Project[]
  tasks: Task[]
  /** Called on every draft change — auto-apply */
  onApply: (meet: MeetItem) => void
  onDelete: (id: string) => void
  onClose: () => void
  onOpenLinkedTask?: (taskId: string) => void
  focusTitle?: boolean
  onTitleFocused?: () => void
  readOnly?: boolean
  /** ContextHUB records for the Context block. Absent/empty renders the block's empty state. */
  contextSources?: WorkspaceContextSourceSnapshot[]
  contextItems?: WorkspaceContextItemSnapshot[]
  /** Sends a link/unlink ContextHUB command through the bridge (mock fallback handled by the caller). */
  onContextCommand?: (command: WorkspaceContextHubCommand) => boolean
  /** Switches Workspace to the ContextHUB tab. */
  onOpenContextHub?: () => void
  /** Sections + all MEETs, for the Context block's Context Pack export (linked task path, related MEETs). */
  sections?: Section[]
  meetItems?: MeetItem[]
  meetingRecordings?: MeetingRecordingSnapshot[]
  meetingAnalyses?: MeetingAnalysisSnapshot[]
  activeRecording?: MeetingRecordingSnapshot | null
  activeRecordingOwnerTitle?: string
  meetingAssistantError?: string | null
  onClearMeetingAssistantError?: () => void
  onMeetingAssistantCommand?: (command: WorkspaceMeetingAssistantCommand) => boolean
}

const durationOptions: { value: MeetDuration; label: string }[] = [
  { value: "15m", label: "15 min" },
  { value: "30m", label: "30 min" },
  { value: "45m", label: "45 min" },
  { value: "1h", label: "1 hour" },
  { value: "90m", label: "1.5 h" },
  { value: "2h", label: "2 hours" },
]

function getDatePresets(): { label: string; value: string }[] {
  const pad = (n: number) => String(n).padStart(2, "0")
  const fmt = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
  const today = new Date()
  const tomorrow = new Date(today); tomorrow.setDate(today.getDate() + 1)
  const daysToMonday = (8 - today.getDay()) % 7 || 7
  const nextMonday = new Date(today); nextMonday.setDate(today.getDate() + daysToMonday)
  return [
    { label: "Today", value: fmt(today) },
    { label: "Tomorrow", value: fmt(tomorrow) },
    { label: "Next Monday", value: fmt(nextMonday) },
  ]
}

function formatDate(isoDate: string): string {
  if (!isoDate) return ""
  try {
    const d = new Date(isoDate + "T00:00:00")
    const today = new Date(); today.setHours(0, 0, 0, 0)
    const diff = Math.round((d.getTime() - today.getTime()) / 86400000)
    if (diff === 0) return "Today"
    if (diff === 1) return "Tomorrow"
    return d.toLocaleDateString("en-GB", { weekday: "short", day: "numeric", month: "short" })
  } catch {
    return isoDate
  }
}

/** Compute end time string from start + duration */
function computeEndTime(start: string, dur: MeetDuration): string {
  const m = start.match(/^(\d{1,2}):(\d{2})$/)
  if (!m) return ""
  const totalMins = parseInt(m[1]) * 60 + parseInt(m[2])
  const durMins: Record<MeetDuration, number> = {
    "15m": 15, "30m": 30, "45m": 45,
    "1h": 60, "90m": 90, "2h": 120, custom: 0,
  }
  const end = totalMins + (durMins[dur] ?? 0)
  const h = Math.floor(end / 60) % 24
  const min = end % 60
  return `${String(h).padStart(2, "0")}:${String(min).padStart(2, "0")}`
}

export function MeetDetailsModal({
  meet,
  projects,
  tasks,
  onApply,
  onDelete,
  onClose,
  onOpenLinkedTask,
  focusTitle = false,
  onTitleFocused,
  readOnly = false,
  contextSources = [],
  contextItems = [],
  onContextCommand,
  onOpenContextHub,
  sections = [],
  meetItems = [],
  meetingRecordings = [],
  meetingAnalyses = [],
  activeRecording = null,
  activeRecordingOwnerTitle,
  meetingAssistantError = null,
  onClearMeetingAssistantError,
  onMeetingAssistantCommand,
}: Props) {
  const [draft, setDraft] = useState<MeetItem | null>(meet)
  const sessionBaseRef = useRef<MeetItem | null>(meet)
  const draftRef = useRef<MeetItem | null>(meet)
  const titleInputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    const current = draftRef.current
    const base = sessionBaseRef.current
    const sameMeeting = current?.id === meet?.id && base?.id === meet?.id
    const hasLocalChanges = sameMeeting && JSON.stringify(current) !== JSON.stringify(base)
    if (hasLocalChanges && JSON.stringify(current) !== JSON.stringify(meet)) return

    setDraft(meet)
    draftRef.current = meet
    sessionBaseRef.current = meet
  }, [meet])

  useEffect(() => {
    if (!focusTitle || !meet || meet.id !== draft?.id) return
    window.requestAnimationFrame(() => {
      titleInputRef.current?.focus()
      titleInputRef.current?.select()
      onTitleFocused?.()
    })
  }, [focusTitle, meet, draft?.id, onTitleFocused])

  const dirty = draft !== null && JSON.stringify(draft) !== JSON.stringify(sessionBaseRef.current)
  const requestClose = useCallback((reason: MeetModalCloseReason) => {
    const canClose = shouldCloseMeetModal(
      reason,
      dirty,
      () => window.confirm("Discard unsaved MEET changes?"),
    )
    if (canClose) onClose()
    return canClose
  }, [dirty, onClose])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") requestClose("escape")
    }
    window.addEventListener("keydown", onKeyDown)
    return () => window.removeEventListener("keydown", onKeyDown)
  }, [requestClose])

  if (!draft) {
    return null
  }

  const set = <K extends keyof MeetItem>(key: K, value: MeetItem[K]) =>
    setDraft((current) => {
      const next = current ? { ...current, [key]: value } : current
      draftRef.current = next
      return next
    })

  const datePresets = getDatePresets()
  const endTime = draft.endTime || (draft.duration !== "custom" ? computeEndTime(draft.startTime, draft.duration) : "")
  const linkedTask = draft.linkedTaskId ? tasks.find((t) => t.id === draft.linkedTaskId) : null
  const linkedProject = linkedTask ? projects.find((p) => p.id === linkedTask.projectId) : null
  const hasMissingLinkedTask = !!draft.linkedTaskId && !linkedTask

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-background/75 p-3 backdrop-blur-sm sm:p-6"
      role="dialog"
      aria-modal="true"
      aria-labelledby="meet-details-title"
    >
      <div className="flex max-h-[calc(100vh-1.5rem)] w-full max-w-[1180px] flex-col overflow-hidden rounded-xl border border-border bg-sidebar shadow-2xl shadow-black/60 sm:max-h-[calc(100vh-3rem)]">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <div className="min-w-0">
          <h2 id="meet-details-title" className="truncate text-sm font-semibold text-foreground">
            {draft.id === "meeting-draft" ? "Create MEET" : "MEET details"}
          </h2>
          <p className="mt-0.5 truncate text-[11px] text-muted-foreground">
            Calendar item, context, recording, and Meeting Assistant
          </p>
        </div>
        <div className="ml-3 flex shrink-0 items-center gap-2">
          <span className="flex items-center gap-1.5 rounded-md bg-status-meet/15 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-status-meet">
            <Video className="size-3" />
            Meet
          </span>
          <button
            type="button"
            onClick={() => requestClose("explicit")}
            aria-label="Close MEET details"
            className="flex size-7 items-center justify-center rounded text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
          >
            <X className="size-4" />
          </button>
        </div>
      </div>

      {/* scrollbar-gutter:stable — same fix as Task Details: reserve the scrollbar's
          width so hovering/expanding a card never reflows the panel horizontally. */}
      <fieldset disabled={readOnly} className="min-h-0 flex-1 overflow-y-auto px-4 py-4 [scrollbar-gutter:stable] disabled:opacity-70">
        <div className="grid min-w-0 items-start gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(340px,0.9fr)]">
          <div className="min-w-0 space-y-3">

        {/* ── Title ── */}
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
            Meeting title
          </label>
          <input
            ref={titleInputRef}
            value={draft.title}
            onChange={(e) => set("title", e.target.value)}
            placeholder="Untitled MEET"
            className="w-full rounded-lg border border-input bg-background px-3 py-2 text-sm text-foreground outline-none transition-colors focus:border-status-meet/50 focus:ring-2 focus:ring-status-meet/15"
          />
        </div>

        {/* ── Project ── */}
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
            Project
          </label>
          <div className="relative">
            <select
              value={draft.projectId}
              onChange={(e) => set("projectId", e.target.value)}
              className="w-full appearance-none rounded-lg border border-input bg-background px-3 py-2 pr-8 text-sm text-foreground outline-none transition-colors focus:border-status-meet/50"
            >
              {projects.map((p) => (
                <option key={p.id} value={p.id}>{p.name}</option>
              ))}
            </select>
          </div>
        </div>

        {/* ── Date ── */}
        <div className="rounded-lg border border-border bg-card/40">
          <button
            type="button"
            className="flex w-full items-center gap-2.5 px-3 py-2.5 text-left"
          >
            <CalendarDays className="size-3.5 shrink-0 text-status-meet" />
            <span className="text-[11px] font-bold uppercase tracking-widest text-foreground">Date</span>
            <span className="ml-auto text-[11px] text-status-meet">{formatDate(draft.date)}</span>
          </button>
          <div className="space-y-2 border-t border-border/50 px-3 pb-3 pt-2.5">
            <div className="flex gap-1.5">
              {datePresets.map((p) => (
                <button
                  key={`${p.label}:${p.value}`}
                  onClick={() => set("date", p.value)}
                  className={cn(
                    "flex-1 rounded border px-1 py-1.5 text-center text-[10px] font-medium transition-colors",
                    draft.date === p.value
                      ? "border-status-meet/40 bg-status-meet/10 text-status-meet"
                      : "border-border text-muted-foreground hover:bg-accent",
                  )}
                >
                  {p.label}
                </button>
              ))}
            </div>
            <input
              type="date"
              value={draft.date}
              onChange={(e) => set("date", e.target.value)}
              className="w-full rounded border border-input bg-background px-2 py-1.5 text-[11px] text-foreground outline-none focus:border-status-meet/50"
            />
          </div>
        </div>

        {/* ── Time + Duration ── */}
        <div className="rounded-lg border border-border bg-card/40">
          <div className="flex items-center gap-2.5 px-3 py-2.5">
            <Clock className="size-3.5 shrink-0 text-status-meet" />
            <span className="text-[11px] font-bold uppercase tracking-widest text-foreground">Time</span>
            <span className="ml-auto font-mono text-[11px] text-status-meet">
              {draft.startTime}{endTime ? ` – ${endTime}` : ""}
            </span>
          </div>
          <div className="space-y-2.5 border-t border-border/50 px-3 pb-3 pt-2.5">
            <div className="grid grid-cols-2 gap-1.5">
              <label className="flex flex-col gap-1">
                <span className="text-[10px] uppercase tracking-wide text-muted-foreground">Start</span>
                <input
                  type="time"
                  value={draft.startTime}
                  onChange={(e) => set("startTime", e.target.value)}
                  className="w-full rounded border border-input bg-background px-2 py-1.5 text-[11px] text-foreground outline-none focus:border-status-meet/50"
                />
              </label>
              <label className="flex flex-col gap-1">
                <span className="text-[10px] uppercase tracking-wide text-muted-foreground">End (override)</span>
                <input
                  type="time"
                  value={draft.endTime ?? ""}
                  onChange={(e) => set("endTime", e.target.value || undefined)}
                  placeholder={endTime}
                  className="w-full rounded border border-input bg-background px-2 py-1.5 text-[11px] text-foreground outline-none placeholder:text-muted-foreground/50 focus:border-status-meet/50"
                />
              </label>
            </div>
            {/* Duration presets */}
            <div>
              <span className="mb-1.5 block text-[10px] uppercase tracking-wide text-muted-foreground">Duration</span>
              <div className="grid grid-cols-3 gap-1.5">
                {durationOptions.map((d) => (
                  <button
                    key={d.value}
                    onClick={() => { set("duration", d.value); set("endTime", undefined) }}
                    className={cn(
                      "rounded border px-1 py-1.5 text-[11px] font-medium transition-colors",
                      draft.duration === d.value && !draft.endTime
                        ? "border-status-meet/40 bg-status-meet/10 text-status-meet"
                        : "border-border text-muted-foreground hover:bg-accent",
                    )}
                  >
                    {d.label}
                  </button>
                ))}
              </div>
            </div>
          </div>
        </div>

        {/* ── Location ── */}
        <div>
          <label className="mb-1.5 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
            <MapPin className="size-3" />
            Location
          </label>
          <input
            value={draft.location ?? ""}
            placeholder="Room 4, Zoom, Google Meet…"
            onChange={(e) => set("location", e.target.value || undefined)}
            className="w-full rounded-lg border border-input bg-background px-3 py-2 text-sm text-foreground outline-none transition-colors placeholder:text-muted-foreground/50 focus:border-status-meet/50"
          />
        </div>

        {/* ── Link ── */}
        <div>
          <label className="mb-1.5 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
            <ExternalLink className="size-3" />
            Link
          </label>
          <input
            value={draft.link ?? ""}
            placeholder="meet.example.com/…"
            onChange={(e) => set("link", e.target.value || undefined)}
            className="w-full rounded-lg border border-input bg-background px-3 py-2 text-sm text-foreground outline-none transition-colors placeholder:text-muted-foreground/50 focus:border-status-meet/50"
          />
        </div>

        {/* ── Notes / Agenda ── */}
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
            Notes / Agenda
          </label>
          <textarea
            value={draft.notes ?? ""}
            placeholder="Agenda, context, links…"
            rows={3}
            onChange={(e) => set("notes", e.target.value || undefined)}
            className="w-full resize-none rounded-lg border border-input bg-background px-3 py-2 text-sm text-foreground outline-none transition-colors placeholder:text-muted-foreground/50 focus:border-status-meet/50"
          />
        </div>

        {/* ── Linked task (optional) ── */}
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
            Linked task
          </label>
          <select
            value={draft.linkedTaskId ?? ""}
            onChange={(e) => set("linkedTaskId", e.target.value || undefined)}
            className="w-full appearance-none rounded-lg border border-input bg-background px-3 py-2 text-sm text-foreground outline-none transition-colors focus:border-status-meet/50"
          >
            <option value="">None</option>
            {tasks.map((t) => (
              <option key={t.id} value={t.id}>{t.title}</option>
            ))}
          </select>
          {linkedTask ? (
            <div className="mt-2 rounded-lg border border-border bg-card/40 p-2">
              <div className="min-w-0">
                <p className="truncate text-[12px] font-medium text-foreground">{linkedTask.title}</p>
                <p className="mt-0.5 truncate text-[11px] text-muted-foreground">
                  {linkedProject?.name ?? "Unknown project"}
                </p>
              </div>
              {onOpenLinkedTask && (
                <button
                  type="button"
                  onClick={() => {
                    if (requestClose("navigate")) onOpenLinkedTask(linkedTask.id)
                  }}
                  className="mt-2 inline-flex h-7 items-center gap-1.5 rounded-md border border-border px-2 text-[11px] font-medium text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
                >
                  <ExternalLink className="size-3" />
                  Open task
                </button>
              )}
            </div>
          ) : hasMissingLinkedTask && (
            <p className="mt-1 text-[11px] text-muted-foreground">
              Linked task is no longer available.
            </p>
          )}
        </div>

          </div>
          <div className="min-w-0 space-y-3">

        {/* ── Recording assistant and Context ── */}
        {onMeetingAssistantCommand && (
          <MeetingAssistantSection
            meet={draft}
            projects={projects}
            recordings={meetingRecordings}
            analyses={meetingAnalyses}
            unclassifiedRecordings={meetingRecordings.filter((recording) => !recording.meetingId)}
            activeRecording={activeRecording}
            activeRecordingOwnerTitle={activeRecordingOwnerTitle}
            readOnly={readOnly}
            commandError={meetingAssistantError}
            onClearError={onClearMeetingAssistantError}
            onCommand={onMeetingAssistantCommand}
          />
        )}

        {onContextCommand && onOpenContextHub && (
          <MeetContextBlock
            meet={draft}
            projects={projects}
            sections={sections}
            tasks={tasks}
            meetItems={meetItems}
            contextSources={contextSources}
            contextItems={contextItems}
            onCommand={onContextCommand}
            onOpenContextHub={() => {
              if (requestClose("navigate")) onOpenContextHub()
            }}
            locked={readOnly}
          />
        )}

        {/* ── Info note ── */}
        <p className="text-[11px] text-muted-foreground/70 text-pretty">
          MEET is a calendar-like item — not a task. It has no TODO / FOCUS / WAIT / DONE status.
        </p>

          </div>
        </div>
      </fieldset>

      {/* Bottom actions */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-4 py-3">
        <button
          disabled={readOnly}
          onClick={() => {
            if (window.confirm(`Delete meeting "${draft.title || "Untitled"}"?`)) {
              onDelete(draft.id)
            }
          }}
          className="flex items-center gap-1.5 rounded-lg border border-destructive/30 px-3 py-1.5 text-sm font-medium text-destructive transition-colors hover:bg-destructive/10"
        >
          <Trash2 className="size-4" />
          Delete meeting
        </button>
        <div className="flex items-center gap-2">
          <button
            disabled={!dirty || readOnly}
            onClick={() => {
              draftRef.current = sessionBaseRef.current
              setDraft(sessionBaseRef.current)
            }}
            className={cn(
              "flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors",
              dirty && !readOnly
                ? "border-border text-foreground hover:bg-accent"
                : "cursor-not-allowed border-border/40 text-muted-foreground/40",
            )}
          >
            <UndoDot className="size-4" />
            Revert
          </button>
          <button
            type="button"
            onClick={() => requestClose("explicit")}
            className="rounded-lg border border-border px-3 py-1.5 text-sm font-medium text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
          >
            Close
          </button>
          <button
            disabled={!dirty || !draft.title.trim() || readOnly}
            onClick={() => onApply(draft)}
            className="flex items-center gap-1.5 rounded-lg bg-status-meet px-3 py-1.5 text-sm font-semibold text-background transition-opacity disabled:cursor-not-allowed disabled:opacity-40"
          >
            <Save className="size-4" />
            Save
          </button>
        </div>
      </div>
      </div>
    </div>
  )
}
