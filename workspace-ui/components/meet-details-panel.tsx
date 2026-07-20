"use client"

import { useCallback, useEffect, useRef, useState } from "react"
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
  WorkspaceCommandResult,
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
  closeMeetEditor,
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
} from "@/lib/meet-shell"
import { isValidMeetingLinkUrl } from "@/lib/meeting-link"
import { Tabs, TabList, Tab, TabPanel } from "@/components/ui/tabs"
import { MeetContextBlock } from "./task-context-block"
import {
  MeetingReviewWorkspace,
  MeetingSourcesWorkspace,
  type TranscriptEditGuard,
} from "./meet-sources-review"

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
  /** Tracked sender for the transcript revision-save command (Review edit mode). */
  onSaveTranscriptRevision?: (
    command: WorkspaceMeetingAssistantCommand,
  ) => Promise<WorkspaceCommandResult>
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
  onSaveTranscriptRevision,
  defaultRecordingPolicy = "Manual",
}: Props) {
  const [draft, setDraft] = useState<MeetItem | null>(meet)
  const [activeTab, setActiveTab] = useState<MeetWorkspaceTab>("details")
  // Registered by the Review transcript editor while a draft exists; close,
  // tab switches, and Escape route through it so dirty edits are never lost
  // without the one "Discard unsaved transcript edits?" confirmation.
  const transcriptEditGuardRef = useRef<TranscriptEditGuard | null>(null)
  const requestCloseAfterDiscardRef = useRef<((reason: MeetModalCloseReason) => Promise<boolean>) | null>(null)
  const registerTranscriptEditGuard = useCallback(
    (guard: TranscriptEditGuard | null) => {
      transcriptEditGuardRef.current = guard
    },
    [],
  )
  const confirmLeaveTranscriptEditor = useCallback((onDiscard?: () => void): boolean => {
    const guard = transcriptEditGuardRef.current
    if (!guard?.isEditing) {
      onDiscard?.()
      return true
    }
    return guard.requestExit(onDiscard)
  }, [])
  const switchTab = useCallback((tab: MeetWorkspaceTab) => {
    if (tab === "review") {
      setActiveTab(tab)
      return true
    }
    if (!confirmLeaveTranscriptEditor(() => setActiveTab(tab))) return false
    return true
  }, [confirmLeaveTranscriptEditor])
  const [saveStatus, setSaveStatus] = useState<MeetSaveStatus>("saved")
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
    if (!confirmLeaveTranscriptEditor(() => { void requestCloseAfterDiscardRef.current?.(reason) })) return false
    return closeMeetEditor(reason, flushAutosave, onClose)
  }, [
    confirmLeaveTranscriptEditor,
    flushAutosave,
    onClose,
  ])
  requestCloseAfterDiscardRef.current = requestClose

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return
      // While the transcript editor is open, Escape cancels the edit (with the
      // dirty-draft confirmation) instead of closing the whole MEET modal.
      const guard = transcriptEditGuardRef.current
      if (guard?.isEditing) {
        guard.requestExit()
        return
      }
      void requestClose("escape")
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

  return (
    // Scrim never closes the modal (backdrop click is a no-op by product decision).
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/70 p-2 backdrop-blur-sm">
      {/*
        Bounded, clearly intentional modal — capped well below the viewport so a
        normal desktop keeps visible Workspace margins on every side, and scaled
        down on smaller Workspace sizes. Height stays viewport-derived (not
        content-derived) so geometry is constant across Details / Sources /
        Review. `.meet-shell` scopes the improved MEET-only palette.
      */}
      <div
        className="meet-shell flex h-[min(820px,88dvh)] w-[min(1280px,90vw)] flex-col overflow-hidden rounded-xl border border-border bg-sidebar text-foreground shadow-2xl shadow-black/60"
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

        {/* ── Tabs — keyboard-navigable via the canonical Tabs primitive ── */}
        <Tabs
          value={activeTab}
          onValueChange={(value, eventDetails) => {
            if (!switchTab(value as MeetWorkspaceTab)) eventDetails.cancel()
          }}
          className="min-h-0 flex-1"
        >
          <TabList activateOnFocus aria-label="MEET sections" className="bg-background/40">
            {MEET_WORKSPACE_TABS.map((tab) => (
              <Tab key={tab} value={tab} id={meetTabButtonId(tab)}>
                {meetTabLabel(tab)}
              </Tab>
            ))}
          </TabList>

          {/* ── Content region — fixed height, only inner columns scroll ── */}
          <TabPanel
            value="details"
            id={meetTabPanelId("details")}
            className="flex min-h-0 flex-1 flex-col bg-[var(--meet-content)]"
          >
            <fieldset
              disabled={readOnly}
              className="grid min-h-0 w-full min-w-0 flex-1 grid-cols-1 disabled:opacity-80 lg:grid-cols-[minmax(0,1.65fr)_minmax(320px,0.85fr)]"
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
                      tabIndex={1}
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

                {/* Location + Link + Linked task in one compact row (stacks when narrow) */}
                <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
                  <div>
                    <label htmlFor="meet-location-input" className={cn(labelClass, "flex items-center gap-1.5")}>
                      <MapPin className="size-3" />
                      Location
                    </label>
                    <input
                      id="meet-location-input"
                      tabIndex={2}
                      value={draft.location ?? ""}
                      placeholder="Room, Zoom, Meet…"
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
                    <div className="flex items-stretch gap-1.5">
                      <input
                        id="meet-link-input"
                        tabIndex={3}
                        value={draft.link ?? ""}
                        placeholder="meet.example.com/…"
                        onChange={(e) => updateDraft(
                          { link: e.target.value || undefined },
                          ["link"],
                          "debounced",
                        )}
                        className={cn(inputClass, "min-w-0 flex-1")}
                      />
                      {isValidMeetingLinkUrl(draft.link) && onMeetingAssistantCommand && (
                        <button
                          type="button"
                          aria-label="Open call link"
                          title="Open call link"
                          onClick={() => sendMeetingAssistantCommand({ type: "openMeetingLink", meetingId: draft.id })}
                          className="flex shrink-0 items-center justify-center rounded-md border border-border px-2.5 text-muted-foreground outline-none transition-colors hover:bg-accent hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
                        >
                          <ExternalLink className="size-4" />
                        </button>
                      )}
                    </div>
                    {draft.link?.trim() && !isValidMeetingLinkUrl(draft.link) && (
                      <p className="mt-1 text-[11px] text-muted-foreground">
                        Not a link the OS can open (needs http:// or https://).
                      </p>
                    )}
                  </div>
                  {/* Compact linked task: select + inline Open action, no duplicated card */}
                  <div>
                    <label htmlFor="meet-linked-task-select" className={labelClass}>Linked task</label>
                    <div className="flex items-stretch gap-1.5">
                      <select
                        id="meet-linked-task-select"
                        value={draft.linkedTaskId ?? ""}
                        onChange={(e) => updateDraft(
                          { linkedTaskId: e.target.value || undefined },
                          ["linkedTaskId"],
                          "immediate",
                        )}
                        className={cn(inputClass, "min-w-0 flex-1 appearance-none pr-8")}
                      >
                        <option value="">None</option>
                        {tasks.map((t) => (
                          <option key={t.id} value={t.id}>{t.title}</option>
                        ))}
                      </select>
                      {linkedTask && onOpenLinkedTask && (
                        <button
                          type="button"
                          aria-label="Open linked task"
                          title="Open task"
                          onClick={async () => {
                            if (await requestClose("navigate")) onOpenLinkedTask(linkedTask.id)
                          }}
                          className="flex shrink-0 items-center justify-center rounded-md border border-border px-2.5 text-muted-foreground outline-none transition-colors hover:bg-accent hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
                        >
                          <ExternalLink className="size-4" />
                        </button>
                      )}
                    </div>
                    {linkedTask ? (
                      <p className="mt-1 truncate text-[11px] text-muted-foreground">
                        {linkedProject?.name ?? "Unknown project"}
                      </p>
                    ) : hasMissingLinkedTask ? (
                      <p className="mt-1 text-[11px] text-muted-foreground">
                        Linked task is no longer available.
                      </p>
                    ) : null}
                  </div>
                </div>

                {/* Notes / Agenda — fills remaining height */}
                <div className="flex min-h-0 flex-1 flex-col">
                  <label htmlFor="meet-notes-input" className={labelClass}>Notes / Agenda</label>
                  <textarea
                    id="meet-notes-input"
                    tabIndex={4}
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

              {/* RIGHT: Context — narrower column, open by default for MEET */}
              <div className="flex min-h-0 flex-col overflow-y-auto p-4 [scrollbar-gutter:stable]">
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
                    defaultOpenWhenEmpty
                  />
                )}
              </div>
            </fieldset>
          </TabPanel>

          <TabPanel
            value="sources"
            id={meetTabPanelId("sources")}
            className="flex min-h-0 flex-1 flex-col bg-[var(--meet-content)]"
          >
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
          </TabPanel>

          <TabPanel
            value="review"
            id={meetTabPanelId("review")}
            className="flex min-h-0 flex-1 flex-col bg-[var(--meet-content)]"
          >
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
              onSaveTranscriptRevision={onSaveTranscriptRevision}
              registerTranscriptEditGuard={registerTranscriptEditGuard}
            />
          </TabPanel>
        </Tabs>

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
