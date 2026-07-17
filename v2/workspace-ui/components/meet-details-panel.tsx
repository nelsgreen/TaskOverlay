"use client"

import { useCallback, useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react"
import { CalendarDays, Check, ExternalLink, MapPin, RefreshCw, Trash2, Video, X } from "lucide-react"
import type {
  MeetDuration,
  MeetItem,
  MeetingAnalysisSnapshot,
  MeetingOperationSnapshot,
  MeetingRecordingPolicy,
  MeetingRecordingSnapshot,
  MeetingScreenshotSnapshot,
  MeetingTranscriptSnapshot,
  Project,
  Section,
  Task,
  WorkspaceContextHubCommand,
  WorkspaceContextItemSnapshot,
  WorkspaceContextSourceSnapshot,
  WorkspaceMeetingAssistantCommand,
} from "@/lib/types"
import { cn } from "@/lib/utils"
import { MeetingAutosaveQueue, type MeetSaveMode, type MeetSaveStatus } from "@/lib/meet-autosave"
import type { MeetEditableField } from "@/lib/meeting-edit"
import { applyMeetingTitleInput, generatedTitleForMeeting } from "@/lib/meeting-title"
import {
  isUntouchedNewMeeting,
  shouldCloseMeetModal,
  type MeetModalCloseReason,
} from "@/lib/meet-modal-policy"
import {
  MEET_WORKSPACE_TABS,
  shouldShowMeetDetailsActions,
  type MeetWorkspaceTab,
} from "@/lib/meet-workspace-policy"
import {
  buildMeetSecondaryLine,
  meetTabButtonId,
  meetTabLabel,
  meetTabPanelId,
  nextMeetTab,
} from "@/lib/meet-shell"
import { MeetContextBlock } from "./task-context-block"
import { MeetingReviewWorkspace, MeetingSourcesWorkspace } from "./meet-sources-review"

interface Props {
  meet: MeetItem | null
  projects: Project[]
  tasks: Task[]
  /** Called on every draft change — auto-apply */
  onPersist: (
    meet: MeetItem,
    fields: ReadonlySet<MeetEditableField>,
    mutationSequence: number,
  ) => Promise<void>
  onDelete: (id: string) => Promise<boolean>
  onClose: () => void
  isNewlyCreated?: boolean
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
  meetingTranscripts?: MeetingTranscriptSnapshot[]
  meetingScreenshots?: MeetingScreenshotSnapshot[]
  meetingAnalyses?: MeetingAnalysisSnapshot[]
  meetingOperations?: MeetingOperationSnapshot[]
  activeRecording?: MeetingRecordingSnapshot | null
  activeRecordingOwnerTitle?: string
  meetingAssistantError?: string | null
  meetingAssistantNotice?: string | null
  onClearMeetingAssistantError?: () => void
  onClearMeetingAssistantNotice?: () => void
  onMeetingAssistantCommand?: (command: WorkspaceMeetingAssistantCommand) => boolean
  defaultRecordingPolicy?: Exclude<MeetingRecordingPolicy, "Inherit">
}

const durationOptions: { value: MeetDuration; label: string; short: string }[] = [
  { value: "15m", label: "15 min", short: "15m" },
  { value: "30m", label: "30 min", short: "30m" },
  { value: "45m", label: "45 min", short: "45m" },
  { value: "1h", label: "1 hour", short: "1h" },
  { value: "90m", label: "1.5 hours", short: "1.5h" },
  { value: "2h", label: "2 hours", short: "2h" },
]

// Shared field styling — kept legible on the user's low-contrast work monitor:
// visible input borders (via the .meet-shell token scope), 13px body text, and
// a restrained violet focus ring.
const labelClass =
  "mb-1 block text-[11px] font-medium uppercase tracking-wide text-muted-foreground"
const inputClass =
  "w-full rounded-md border border-input bg-background px-3 py-2 text-[13px] text-foreground outline-none transition-colors placeholder:text-muted-foreground/60 focus-visible:border-status-meet/60 focus-visible:ring-2 focus-visible:ring-status-meet/25"

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
  onPersist,
  onDelete,
  onClose,
  isNewlyCreated = false,
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
  meetingTranscripts = [],
  meetingScreenshots = [],
  meetingAnalyses = [],
  meetingOperations = [],
  activeRecording = null,
  activeRecordingOwnerTitle,
  meetingAssistantError = null,
  meetingAssistantNotice = null,
  onClearMeetingAssistantError,
  onClearMeetingAssistantNotice,
  onMeetingAssistantCommand,
  defaultRecordingPolicy = "Manual",
}: Props) {
  const [draft, setDraft] = useState<MeetItem | null>(meet)
  const [activeTab, setActiveTab] = useState<MeetWorkspaceTab>("details")
  const [saveStatus, setSaveStatus] = useState<MeetSaveStatus>("saved")
  const sessionBaseRef = useRef<MeetItem | null>(meet)
  const draftRef = useRef<MeetItem | null>(meet)
  const titleInputRef = useRef<HTMLInputElement>(null)
  const sourceCommandSentRef = useRef(false)
  const contextCommandSentRef = useRef(false)
  const persistRef = useRef(onPersist)
  persistRef.current = onPersist
  const autosaveRef = useRef<MeetingAutosaveQueue<MeetItem, MeetEditableField> | null>(null)
  if (!autosaveRef.current && meet) {
    autosaveRef.current = new MeetingAutosaveQueue(
      meet,
      (value, fields, mutationSequence) => persistRef.current(value, fields, mutationSequence),
      setSaveStatus,
    )
  }

  useEffect(() => () => autosaveRef.current?.dispose(), [])

  useEffect(() => {
    const current = draftRef.current
    if (current && meet && current.id === meet.id && autosaveRef.current?.hasPending()) {
      const merged: MeetItem = { ...current, activeTranscriptId: meet.activeTranscriptId }
      setDraft(merged)
      draftRef.current = merged
      return
    }

    setDraft(meet)
    draftRef.current = meet
  }, [meet])

  useEffect(() => {
    setActiveTab("details")
  }, [meet?.id])

  useEffect(() => {
    if (!focusTitle || !meet || meet.id !== draft?.id) return
    window.requestAnimationFrame(() => {
      titleInputRef.current?.focus()
      titleInputRef.current?.select()
      onTitleFocused?.()
    })
  }, [focusTitle, meet, draft?.id, onTitleFocused])

  const flushAutosave = useCallback(
    () => autosaveRef.current?.flush() ?? Promise.resolve(true),
    [],
  )
  const requestClose = useCallback(async (reason: MeetModalCloseReason) => {
    if (!shouldCloseMeetModal(reason)) return false
    if (!await flushAutosave()) return false

    const current = draftRef.current
    const initial = sessionBaseRef.current
    if (isNewlyCreated && current && initial && isUntouchedNewMeeting({
      initial,
      current,
      hasRecordingOrSource:
        sourceCommandSentRef.current ||
        meetingRecordings.some((recording) => recording.meetingId === current.id) ||
        meetingTranscripts.some((transcript) => transcript.meetingId === current.id) ||
        meetingScreenshots.some((screenshot) => screenshot.meetingId === current.id),
      hasContextLink:
        contextCommandSentRef.current ||
        contextSources.some((source) => source.linkedMeetingIds.includes(current.id)) ||
        contextItems.some((item) => item.linkedMeetingIds.includes(current.id)),
    })) {
      if (!await onDelete(current.id)) return false
    }

    onClose()
    return true
  }, [
    contextItems,
    contextSources,
    flushAutosave,
    isNewlyCreated,
    meetingRecordings,
    meetingScreenshots,
    meetingTranscripts,
    onClose,
    onDelete,
  ])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") void requestClose("escape")
    }
    window.addEventListener("keydown", onKeyDown)
    return () => window.removeEventListener("keydown", onKeyDown)
  }, [requestClose])

  if (!draft) {
    return null
  }

  const updateDraft = (
    changes: Partial<MeetItem>,
    fields: MeetEditableField[],
    mode: MeetSaveMode,
  ) => {
    if (readOnly) return
    const current = draftRef.current
    if (!current) return
    const next = { ...current, ...changes }
    if ((next.titleIsGenerated ?? false) &&
        fields.some((field) => field === "projectId" || field === "date" || field === "startTime")) {
      next.title = generatedTitleForMeeting(next, projects)
    }
    draftRef.current = next
    setDraft(next)
    autosaveRef.current?.enqueue(next, fields, mode)
  }

  const updateTitle = (value: string) => {
    const current = draftRef.current
    if (!current) return
    const next = applyMeetingTitleInput(current, value, projects)
    updateDraft(
      { title: next.title, titleIsGenerated: next.titleIsGenerated },
      ["title", "titleIsGenerated"],
      "debounced",
    )
  }

  const datePresets = getDatePresets()
  const endTime = draft.endTime || (draft.duration !== "custom" ? computeEndTime(draft.startTime, draft.duration) : "")
  const linkedTask = draft.linkedTaskId ? tasks.find((t) => t.id === draft.linkedTaskId) : null
  const linkedProject = linkedTask ? projects.find((p) => p.id === linkedTask.projectId) : null
  const hasMissingLinkedTask = !!draft.linkedTaskId && !linkedTask
  const generatedTitle = generatedTitleForMeeting(draft, projects)
  const sendContextCommand = (command: WorkspaceContextHubCommand) => {
    const sent = onContextCommand?.(command) ?? false
    if (sent) contextCommandSentRef.current = true
    return sent
  }
  const sendMeetingAssistantCommand = (command: WorkspaceMeetingAssistantCommand) => {
    const sent = onMeetingAssistantCommand?.(command) ?? false
    if (sent && command.type !== "openMeetingLink") sourceCommandSentRef.current = true
    return sent
  }

  // Header uses the real connected MEET data (generated or user-authored),
  // never a generic "MEET details" label when a title is available.
  const projectName = projects.find((project) => project.id === draft.projectId)?.name
  const headerTitle = draft.title?.trim() ? draft.title : "Untitled MEET"
  const secondaryLine = buildMeetSecondaryLine([
    projectName ?? "No project",
    formatDate(draft.date),
    draft.startTime,
  ])
  const recordingActive = !!activeRecording && activeRecording.meetingId === draft.id
  const isDetails = shouldShowMeetDetailsActions(activeTab)

  const onTabsKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return
    event.preventDefault()
    const target = nextMeetTab(activeTab, event.key === "ArrowRight" ? "next" : "prev")
    setActiveTab(target)
    window.requestAnimationFrame(() => {
      document.getElementById(meetTabButtonId(target))?.focus()
    })
  }

  return (
    // Scrim never closes the modal (backdrop click is a no-op by product decision).
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/70 p-4 backdrop-blur-sm">
      {/*
        Fixed, viewport-clamped geometry (≈1180×720) so the shell keeps a constant
        size across Details / Sources / Review — height is viewport-derived, never
        content-derived. `.meet-shell` scopes the improved MEET-only palette.
      */}
      <div
        className="meet-shell flex h-[min(720px,calc(100dvh-2rem))] w-[min(1180px,calc(100vw-2rem))] flex-col overflow-hidden rounded-xl border border-border bg-sidebar text-foreground shadow-2xl shadow-black/60"
        role="dialog"
        aria-modal="true"
        aria-labelledby="meet-details-title"
      >
        {/* ── Header ── */}
        <header className="flex shrink-0 items-center gap-3 border-b border-border px-4 py-3">
          <span className="flex size-8 shrink-0 items-center justify-center rounded-md bg-status-meet/15 text-status-meet">
            <Video className="size-4" />
          </span>
          <div className="min-w-0 flex-1">
            <h2 id="meet-details-title" className="truncate text-sm font-semibold text-foreground">
              {headerTitle}
            </h2>
            <p className="truncate text-[11px] text-muted-foreground">{secondaryLine}</p>
          </div>
          {recordingActive && (
            <span className="flex shrink-0 items-center gap-1.5 rounded-full border border-destructive/40 bg-destructive/12 px-2 py-0.5 text-[11px] font-semibold text-destructive">
              <span className="size-1.5 animate-pulse rounded-full bg-destructive motion-reduce:animate-none" />
              REC
            </span>
          )}
          <span className="flex shrink-0 items-center gap-1.5 rounded-md bg-status-meet/15 px-2 py-0.5 text-[11px] font-semibold uppercase tracking-wide text-status-meet">
            <Video className="size-3" />
            Meet
          </span>
          <button
            type="button"
            onClick={() => void requestClose("explicit")}
            aria-label="Close MEET details"
            className="flex size-7 shrink-0 items-center justify-center rounded text-muted-foreground outline-none transition-colors hover:bg-accent hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
          >
            <X className="size-4" />
          </button>
        </header>

        {/* ── Tabs — full-width, equal, keyboard-navigable ── */}
        <div
          role="tablist"
          aria-label="MEET sections"
          onKeyDown={onTabsKeyDown}
          className="flex shrink-0 border-b border-border bg-background/40"
        >
          {MEET_WORKSPACE_TABS.map((tab) => {
            const active = activeTab === tab
            return (
              <button
                key={tab}
                type="button"
                id={meetTabButtonId(tab)}
                role="tab"
                aria-selected={active}
                aria-controls={meetTabPanelId(tab)}
                tabIndex={active ? 0 : -1}
                onClick={() => setActiveTab(tab)}
                className={cn(
                  "relative flex-1 px-3 py-2.5 text-[12px] font-medium outline-none transition-colors",
                  "focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring",
                  active ? "text-foreground" : "text-muted-foreground hover:bg-accent/60 hover:text-foreground",
                )}
              >
                {active && (
                  <span aria-hidden className="absolute inset-0 bg-[var(--meet-selected)]" />
                )}
                <span className="relative">{meetTabLabel(tab)}</span>
                {active && (
                  <span aria-hidden className="absolute inset-x-0 bottom-0 h-0.5 bg-[var(--meet-active)]" />
                )}
              </button>
            )
          })}
        </div>

        {/* ── Content region — fixed height, only inner columns scroll ── */}
        <div
          role="tabpanel"
          id={meetTabPanelId(activeTab)}
          aria-labelledby={meetTabButtonId(activeTab)}
          className="flex min-h-0 flex-1 flex-col"
        >
          {isDetails && (
            <fieldset
              disabled={readOnly}
              className="grid min-h-0 w-full min-w-0 flex-1 grid-cols-1 disabled:opacity-80 lg:grid-cols-2"
            >
              {/* LEFT: MEET fields */}
              <div className="flex min-h-0 flex-col gap-3 overflow-y-auto border-b border-border p-4 [scrollbar-gutter:stable] lg:border-b-0 lg:border-r">
                {/* Title + Project */}
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-[1fr_minmax(150px,220px)]">
                  <div>
                    <label htmlFor="meet-title-input" className={labelClass}>Meeting title</label>
                    <input
                      id="meet-title-input"
                      ref={titleInputRef}
                      value={draft.titleIsGenerated ? "" : draft.title}
                      onChange={(e) => updateTitle(e.target.value)}
                      placeholder={generatedTitle}
                      className={inputClass}
                    />
                  </div>
                  <div>
                    <label htmlFor="meet-project-select" className={labelClass}>Project</label>
                    <select
                      id="meet-project-select"
                      value={draft.projectId}
                      onChange={(e) => updateDraft({ projectId: e.target.value }, ["projectId"], "immediate")}
                      className={cn(inputClass, "appearance-none pr-8")}
                    >
                      {projects.map((p) => (
                        <option key={p.id} value={p.id}>{p.name}</option>
                      ))}
                    </select>
                  </div>
                </div>

                {/* Scheduling — one compact card */}
                <div className="rounded-lg border border-border bg-card/50">
                  <div className="flex items-center justify-between gap-2 border-b border-border/70 px-3 py-2">
                    <span className="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide text-foreground">
                      <CalendarDays className="size-3.5 text-status-meet" />
                      Schedule
                    </span>
                    <span className="font-mono text-[11px] text-status-meet">
                      {draft.startTime}{endTime ? ` – ${endTime}` : ""}
                    </span>
                  </div>
                  <div className="space-y-2.5 p-3">
                    {/* Quick date presets */}
                    <div className="grid grid-cols-3 gap-1.5">
                      {datePresets.map((p) => (
                        <button
                          key={`${p.label}:${p.value}`}
                          type="button"
                          onClick={() => updateDraft({ date: p.value }, ["date"], "immediate")}
                          className={cn(
                            "rounded border px-1 py-1.5 text-center text-[11px] font-medium outline-none transition-colors focus-visible:ring-2 focus-visible:ring-ring",
                            draft.date === p.value
                              ? "border-status-meet/45 bg-status-meet/10 text-status-meet"
                              : "border-border text-muted-foreground hover:bg-accent hover:text-foreground",
                          )}
                        >
                          {p.label}
                        </button>
                      ))}
                    </div>
                    {/* Date / start / end in one row */}
                    <div className="grid grid-cols-[1.5fr_1fr_1fr] gap-1.5">
                      <label className="flex flex-col gap-1">
                        <span className="text-[11px] uppercase tracking-wide text-muted-foreground">Date</span>
                        <input
                          type="date"
                          value={draft.date}
                          onChange={(e) => updateDraft({ date: e.target.value }, ["date"], "immediate")}
                          className="w-full rounded border border-input bg-background px-2 py-1.5 text-[12px] text-foreground outline-none focus-visible:border-status-meet/60 focus-visible:ring-2 focus-visible:ring-status-meet/25"
                        />
                      </label>
                      <label className="flex flex-col gap-1">
                        <span className="text-[11px] uppercase tracking-wide text-muted-foreground">Start</span>
                        <input
                          type="time"
                          value={draft.startTime}
                          onChange={(e) => updateDraft({ startTime: e.target.value }, ["startTime"], "immediate")}
                          className="w-full rounded border border-input bg-background px-2 py-1.5 text-[12px] text-foreground outline-none focus-visible:border-status-meet/60 focus-visible:ring-2 focus-visible:ring-status-meet/25"
                        />
                      </label>
                      <label className="flex flex-col gap-1">
                        <span className="text-[11px] uppercase tracking-wide text-muted-foreground">End</span>
                        <input
                          type="time"
                          value={draft.endTime ?? ""}
                          onChange={(e) => updateDraft(
                            { endTime: e.target.value || undefined },
                            ["endTime"],
                            "immediate",
                          )}
                          placeholder={endTime}
                          className="w-full rounded border border-input bg-background px-2 py-1.5 text-[12px] text-foreground outline-none placeholder:text-muted-foreground/50 focus-visible:border-status-meet/60 focus-visible:ring-2 focus-visible:ring-status-meet/25"
                        />
                      </label>
                    </div>
                    {/* Duration chips in one row */}
                    <div>
                      <span className="mb-1 block text-[11px] uppercase tracking-wide text-muted-foreground">Duration</span>
                      <div className="grid grid-cols-6 gap-1.5">
                        {durationOptions.map((d) => (
                          <button
                            key={d.value}
                            type="button"
                            title={d.label}
                            aria-pressed={draft.duration === d.value && !draft.endTime}
                            onClick={() => updateDraft(
                              { duration: d.value, endTime: undefined },
                              ["duration", "endTime"],
                              "immediate",
                            )}
                            className={cn(
                              "rounded border px-1 py-1.5 text-center text-[11px] font-medium outline-none transition-colors focus-visible:ring-2 focus-visible:ring-ring",
                              draft.duration === d.value && !draft.endTime
                                ? "border-status-meet/45 bg-status-meet/10 text-status-meet"
                                : "border-border text-muted-foreground hover:bg-accent hover:text-foreground",
                            )}
                          >
                            {d.short}
                          </button>
                        ))}
                      </div>
                    </div>
                  </div>
                </div>

                {/* Location + Link side by side */}
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <div>
                    <label htmlFor="meet-location-input" className={cn(labelClass, "flex items-center gap-1.5")}>
                      <MapPin className="size-3" />
                      Location
                    </label>
                    <input
                      id="meet-location-input"
                      value={draft.location ?? ""}
                      placeholder="Room 4, Zoom, Google Meet…"
                      onChange={(e) => updateDraft(
                        { location: e.target.value || undefined },
                        ["location"],
                        "debounced",
                      )}
                      className={inputClass}
                    />
                  </div>
                  <div>
                    <label htmlFor="meet-link-input" className={cn(labelClass, "flex items-center gap-1.5")}>
                      <ExternalLink className="size-3" />
                      Link
                    </label>
                    <input
                      id="meet-link-input"
                      value={draft.link ?? ""}
                      placeholder="meet.example.com/…"
                      onChange={(e) => updateDraft(
                        { link: e.target.value || undefined },
                        ["link"],
                        "debounced",
                      )}
                      className={inputClass}
                    />
                  </div>
                </div>

                {/* Linked task (optional) */}
                <div>
                  <label htmlFor="meet-linked-task-select" className={labelClass}>Linked task</label>
                  <select
                    id="meet-linked-task-select"
                    value={draft.linkedTaskId ?? ""}
                    onChange={(e) => updateDraft(
                      { linkedTaskId: e.target.value || undefined },
                      ["linkedTaskId"],
                      "immediate",
                    )}
                    className={cn(inputClass, "appearance-none pr-8")}
                  >
                    <option value="">None</option>
                    {tasks.map((t) => (
                      <option key={t.id} value={t.id}>{t.title}</option>
                    ))}
                  </select>
                  {linkedTask ? (
                    <div className="mt-2 flex items-center gap-2 rounded-md border border-border bg-card/50 px-2.5 py-1.5">
                      <div className="min-w-0 flex-1">
                        <p className="truncate text-[12px] font-medium text-foreground">{linkedTask.title}</p>
                        <p className="truncate text-[11px] text-muted-foreground">
                          {linkedProject?.name ?? "Unknown project"}
                        </p>
                      </div>
                      {onOpenLinkedTask && (
                        <button
                          type="button"
                          onClick={async () => {
                            if (await requestClose("navigate")) onOpenLinkedTask(linkedTask.id)
                          }}
                          className="inline-flex h-7 shrink-0 items-center gap-1.5 rounded-md border border-border px-2 text-[11px] font-medium text-muted-foreground outline-none transition-colors hover:bg-accent hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
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

                {/* Notes / Agenda — fills remaining height */}
                <div className="flex min-h-0 flex-1 flex-col">
                  <label htmlFor="meet-notes-input" className={labelClass}>Notes / Agenda</label>
                  <textarea
                    id="meet-notes-input"
                    value={draft.notes ?? ""}
                    placeholder="Agenda, context, links…"
                    onChange={(e) => updateDraft(
                      { notes: e.target.value || undefined },
                      ["notes"],
                      "debounced",
                    )}
                    className="min-h-[120px] w-full flex-1 resize-none rounded-md border border-input bg-background px-3 py-2 text-[13px] leading-relaxed text-foreground outline-none transition-colors placeholder:text-muted-foreground/60 focus-visible:border-status-meet/60 focus-visible:ring-2 focus-visible:ring-status-meet/25"
                  />
                </div>
              </div>

              {/* RIGHT: Context — equal-height column */}
              <div className="flex min-h-0 flex-col gap-3 overflow-y-auto p-4 [scrollbar-gutter:stable]">
                {onContextCommand && onOpenContextHub && (
                  <MeetContextBlock
                    meet={draft}
                    projects={projects}
                    sections={sections}
                    tasks={tasks}
                    meetItems={meetItems}
                    contextSources={contextSources}
                    contextItems={contextItems}
                    onCommand={sendContextCommand}
                    onOpenContextHub={async () => {
                      if (await requestClose("navigate")) onOpenContextHub()
                    }}
                    locked={readOnly}
                  />
                )}
                <p className="mt-auto text-[11px] leading-relaxed text-muted-foreground/80 text-pretty">
                  MEET is a calendar-like item — not a task. It has no TODO / FOCUS / WAIT / DONE status.
                </p>
              </div>
            </fieldset>
          )}

          {activeTab === "sources" && (
            <MeetingSourcesWorkspace
              meet={draft}
              projects={projects}
              recordings={meetingRecordings}
              transcripts={meetingTranscripts}
              screenshots={meetingScreenshots}
              analyses={meetingAnalyses}
              operations={meetingOperations}
              activeRecording={activeRecording}
              activeRecordingOwnerTitle={activeRecordingOwnerTitle}
              readOnly={readOnly}
              commandError={meetingAssistantError}
              commandNotice={meetingAssistantNotice}
              onClearError={onClearMeetingAssistantError}
              onClearNotice={onClearMeetingAssistantNotice}
              onCommand={onMeetingAssistantCommand ? sendMeetingAssistantCommand : undefined}
              defaultRecordingPolicy={defaultRecordingPolicy}
              onRecordingPolicyChange={(policy) => updateDraft(
                { recordingPolicy: policy },
                ["recordingPolicy"],
                "immediate",
              )}
              onBeforeRecordingStart={flushAutosave}
            />
          )}

          {activeTab === "review" && (
            <MeetingReviewWorkspace
              meet={draft}
              projects={projects}
              recordings={meetingRecordings}
              transcripts={meetingTranscripts}
              screenshots={meetingScreenshots}
              analyses={meetingAnalyses}
              operations={meetingOperations}
              activeRecording={activeRecording}
              activeRecordingOwnerTitle={activeRecordingOwnerTitle}
              readOnly={readOnly}
              commandError={meetingAssistantError}
              commandNotice={meetingAssistantNotice}
              onClearError={onClearMeetingAssistantError}
              onClearNotice={onClearMeetingAssistantNotice}
              onCommand={onMeetingAssistantCommand ? sendMeetingAssistantCommand : undefined}
            />
          )}
        </div>

        {/* ── Footer — one stable bar across every tab (autosave, no Save/Revert) ── */}
        <footer className="flex shrink-0 items-center justify-between gap-3 border-t border-border px-4 py-2.5">
          <div className="flex items-center gap-2">
            {isDetails && (
              <button
                type="button"
                disabled={readOnly}
                onClick={async () => {
                  if (!window.confirm(`Delete meeting "${draft.title || "Untitled"}"?`)) return
                  if (!await flushAutosave()) return
                  if (await onDelete(draft.id)) onClose()
                }}
                className="flex items-center gap-1.5 rounded-lg border border-destructive/40 px-3 py-1.5 text-[13px] font-medium text-destructive outline-none transition-colors hover:bg-destructive/10 focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-45"
              >
                <Trash2 className="size-4" />
                Delete meeting
              </button>
            )}
          </div>
          <div className="flex items-center gap-3">
            {!readOnly && (
              <span className="flex items-center gap-1.5 text-[11px]" aria-live="polite">
                {saveStatus === "saving" && (
                  <>
                    <span className="size-1.5 animate-pulse rounded-full bg-status-meet motion-reduce:animate-none" />
                    <span className="text-muted-foreground">Saving…</span>
                  </>
                )}
                {saveStatus === "saved" && (
                  <>
                    <Check className="size-3 text-status-focus" />
                    <span className="text-muted-foreground">Saved</span>
                  </>
                )}
                {saveStatus === "failed" && (
                  <button
                    type="button"
                    onClick={() => void autosaveRef.current?.retry()}
                    className="inline-flex items-center gap-1 rounded text-destructive outline-none hover:underline focus-visible:ring-2 focus-visible:ring-ring"
                  >
                    <RefreshCw className="size-3" />
                    Save failed · Retry
                  </button>
                )}
              </span>
            )}
            <button
              type="button"
              onClick={() => void requestClose("explicit")}
              className="rounded-lg border border-border px-3 py-1.5 text-[13px] font-medium text-muted-foreground outline-none transition-colors hover:bg-accent hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
            >
              Close
            </button>
          </div>
        </footer>
      </div>
    </div>
  )
}
