"use client"

import { useEffect, useState } from "react"

import {
  Bell,
  CalendarClock,
  CalendarDays,
  ChevronLeft,
  ChevronRight,
  ClipboardList,
  Database,
  FileText,
  Flag,
  FolderTree,
  Layers,
  ListChecks,
  Plus,
  Search,
  Square,
  Video,
} from "lucide-react"
import type { MeetingRecordingSnapshot, StatusFilterKey, TabKey, Task, TreeFilter, WorkstreamFilter } from "@/lib/types"
import { matchesStatusFilter } from "@/lib/status-filter"
import { cn } from "@/lib/utils"
import { Input } from "@/components/ui/input"
import { addDaysKey, formatWeekLabel, mondayOfWeekKey, parseDateKey, todayKey } from "@/lib/calendar-date"

interface Props {
  tab: TabKey
  onTabChange: (tab: TabKey) => void
  filter: TreeFilter
  onFilterChange: (filter: TreeFilter) => void
  treeProjectName: string
  treeProjectColor: string
  onAddTask: () => void
  addTaskDisabled?: boolean
  onAddSection: () => void
  addSectionDisabled?: boolean
  statusFilter: StatusFilterKey
  onStatusFilterChange: (filter: StatusFilterKey) => void
  hideDone: boolean
  onHideDoneChange: (hideDone: boolean) => void
  statusTasks: Task[]
  search: string
  onSearchChange: (value: string) => void
  calendarSelectedDate: string
  calendarViewMode: "day" | "week"
  calendarShowDone: boolean
  onCalendarToday: () => void
  onCalendarTomorrow: () => void
  onCalendarStep: (dir: number) => void
  onCalendarShowDoneChange: (showDone: boolean) => void
  onCalendarViewModeChange: (mode: "day" | "week") => void
  onCalendarAddMeeting: () => void
  calendarAddMeetingDisabled?: boolean
  calendarAddMeetingLabel?: string
  calendarAddMeetingError?: string | null
  wsFilter: WorkstreamFilter
  onWsFilterChange: (filter: WorkstreamFilter) => void
  wsSummary: Record<string, number>
  onAddWorkstream: () => void
  addWorkstreamDisabled?: boolean
  addWorkstreamHint?: string | null
  onAddContextItem: () => void
  onAddContextSource: () => void
  addContextDisabled?: boolean
  /** Read-only export/copy — always enabled, even when addContextDisabled (read-only mode). */
  onContextPack: () => void
  contextPackDisabled?: boolean
  contextPackHint?: string | null
  activeRecording?: MeetingRecordingSnapshot | null
  onStopRecording?: () => void
}

const tabs: { key: TabKey; label: string; icon: typeof FolderTree; later?: boolean }[] = [
  { key: "tree", label: "Tree", icon: FolderTree },
  { key: "status", label: "Status", icon: ListChecks },
  { key: "timeline", label: "Timeline", icon: CalendarClock },
  { key: "calendar", label: "Calendar", icon: CalendarDays },
  { key: "workstreams", label: "Workstreams", icon: Layers },
  { key: "contexthub", label: "ContextHUB", icon: Database },
]

const treeFilters: { key: TreeFilter; label: string }[] = [
  { key: "all", label: "All" },
  { key: "active", label: "Active only" },
  { key: "active-path", label: "Active + path" },
]

const statusFilters: {
  key: StatusFilterKey
  label: string
  dot?: string
  text?: string
}[] = [
  { key: "all", label: "All" },
  { key: "panel", label: "Panel", dot: "bg-status-panel", text: "text-status-panel" },
  { key: "FOCUS", label: "Focus", dot: "bg-status-focus", text: "text-status-focus" },
  { key: "WAIT", label: "Wait", dot: "bg-status-wait", text: "text-status-wait" },
  { key: "remind", label: "Remind", dot: "bg-status-remind", text: "text-status-remind" },
  { key: "TODO", label: "Todo", dot: "bg-status-todo", text: "text-status-todo" },
  { key: "DONE", label: "Done", dot: "bg-status-done", text: "text-status-done" },
]

const toolbar = {
  segment: "flex h-7 items-center rounded-md border border-border bg-card p-0.5",
  segmentItem: (active: boolean) =>
    cn(
      "h-6 rounded px-2.5 text-[11px] font-medium transition-colors",
      active ? "bg-accent text-foreground" : "text-muted-foreground hover:text-foreground",
    ),
}

export function WorkspaceHeader({
  tab,
  onTabChange,
  filter,
  onFilterChange,
  treeProjectName,
  treeProjectColor,
  onAddTask,
  addTaskDisabled = false,
  onAddSection,
  addSectionDisabled = false,
  statusFilter,
  onStatusFilterChange,
  hideDone,
  onHideDoneChange,
  statusTasks,
  search,
  onSearchChange,
  calendarSelectedDate,
  calendarViewMode,
  calendarShowDone,
  onCalendarToday,
  onCalendarTomorrow,
  onCalendarStep,
  onCalendarShowDoneChange,
  onCalendarViewModeChange,
  onCalendarAddMeeting,
  calendarAddMeetingDisabled = false,
  calendarAddMeetingLabel = "MEET",
  calendarAddMeetingError = null,
  wsFilter,
  onWsFilterChange,
  wsSummary,
  onAddWorkstream,
  addWorkstreamDisabled = false,
  addWorkstreamHint = null,
  onAddContextItem,
  onAddContextSource,
  addContextDisabled = false,
  onContextPack,
  contextPackDisabled = false,
  contextPackHint = null,
  activeRecording = null,
  onStopRecording,
}: Props) {
  const searchDisabled = tab === "calendar"
  const [clock, setClock] = useState(() => Date.now())

  useEffect(() => {
    if (!activeRecording) return
    setClock(Date.now())
    const timer = window.setInterval(() => setClock(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [activeRecording?.id])

  return (
    <header className="shrink-0 border-b border-border">
      <div className="flex h-12 items-stretch gap-4 px-4">
        <div className="flex shrink-0 items-center gap-2.5">
          <img
            src="./taskoverlay-mark-32.png"
            alt=""
            aria-hidden="true"
            className="size-6 shrink-0 object-contain"
          />
          <div className="min-w-0 leading-tight">
            <h1 className="text-[14px] font-semibold leading-tight text-foreground">Workspace</h1>
            <p className="max-w-44 truncate text-[10px] text-muted-foreground">
              Main task organization surface
            </p>
          </div>
        </div>

        <nav className="flex min-w-0 flex-1 items-stretch overflow-x-auto" aria-label="Workspace views">
          {tabs.map((item) => {
            const Icon = item.icon
            const active = tab === item.key
            return (
              <button
                type="button"
                key={item.key}
                onClick={() => onTabChange(item.key)}
                className={cn(
                  "flex h-full shrink-0 items-center gap-1.5 whitespace-nowrap border-b-2 px-2.5 text-[12px] font-medium transition-colors",
                  active
                    ? "border-primary text-foreground"
                    : "border-transparent text-muted-foreground hover:text-foreground",
                  item.later && !active && "opacity-65",
                )}
              >
                <Icon className="size-3.5" />
                {item.label}
                {item.later && (
                  <span className="rounded bg-accent px-1 py-0.5 text-[8px] font-semibold uppercase tracking-wide text-muted-foreground">
                    Later
                  </span>
                )}
              </button>
            )
          })}
        </nav>

        {activeRecording && (
          <button
            type="button"
            onClick={onStopRecording}
            title={activeRecording.plannedEndPassed
              ? "Planned MEET end passed. Recording continues until you stop it."
              : "Recording is active. Click to stop."}
            className="my-auto flex h-8 shrink-0 items-center gap-2 rounded-md border border-red-500/50 bg-red-500/10 px-2.5 text-[11px] font-semibold text-red-400"
          >
            <span className="size-2 animate-pulse rounded-full bg-red-500" />
            REC {formatRecordingElapsed(activeRecording.startedAtUtc, clock)}
            <Square className="size-3 fill-current" />
          </button>
        )}

        <div className="relative my-auto w-56 shrink-0 max-w-[28vw]">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={search}
            onChange={(event) => onSearchChange(event.target.value)}
            disabled={searchDisabled}
            placeholder={searchDisabled ? "Search unavailable in this view" : "Search or jump to..."}
            className="pl-8 pr-3 text-[12px]"
          />
        </div>
      </div>

      <div className="flex h-9 items-center gap-2 border-t border-border/40 px-4">
        {tab === "tree" && (
          <TreeToolbar
            filter={filter}
            onFilterChange={onFilterChange}
            treeProjectName={treeProjectName}
            treeProjectColor={treeProjectColor}
            onAddTask={onAddTask}
            addTaskDisabled={addTaskDisabled}
            onAddSection={onAddSection}
            addSectionDisabled={addSectionDisabled}
          />
        )}
        {tab === "status" && (
          <StatusToolbar
            tasks={statusTasks}
            filter={statusFilter}
            onFilterChange={onStatusFilterChange}
            hideDone={hideDone}
            onHideDoneChange={onHideDoneChange}
          />
        )}
        {tab === "timeline" && <TimelineToolbar />}
        {tab === "calendar" && (
          <CalendarToolbar
            selectedDate={calendarSelectedDate}
            viewMode={calendarViewMode}
            showDone={calendarShowDone}
            onToday={onCalendarToday}
            onTomorrow={onCalendarTomorrow}
            onStep={onCalendarStep}
            onShowDoneChange={onCalendarShowDoneChange}
            onViewModeChange={onCalendarViewModeChange}
            onAddMeeting={onCalendarAddMeeting}
            addMeetingDisabled={calendarAddMeetingDisabled}
            addMeetingLabel={calendarAddMeetingLabel}
            addMeetingError={calendarAddMeetingError}
          />
        )}
        {tab === "workstreams" && (
          <WorkstreamsToolbar
            wsFilter={wsFilter}
            onWsFilterChange={onWsFilterChange}
            wsSummary={wsSummary}
            onAddWorkstream={onAddWorkstream}
            addDisabled={addWorkstreamDisabled}
            addHint={addWorkstreamHint}
          />
        )}
        {tab === "contexthub" && (
          <ContextHubToolbar
            onAddContextItem={onAddContextItem}
            onAddContextSource={onAddContextSource}
            addDisabled={addContextDisabled}
            onContextPack={onContextPack}
            contextPackDisabled={contextPackDisabled}
            contextPackHint={contextPackHint}
          />
        )}
      </div>
    </header>
  )
}

function formatRecordingElapsed(startedAtUtc: string | null, now: number): string {
  const started = startedAtUtc ? Date.parse(startedAtUtc) : Number.NaN
  const seconds = Number.isFinite(started)
    ? Math.max(0, Math.floor((now - started) / 1000))
    : 0
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  const remainder = seconds % 60
  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, "0")}:${String(remainder).padStart(2, "0")}`
    : `${minutes}:${String(remainder).padStart(2, "0")}`
}

function ContextHubToolbar({
  onAddContextItem,
  onAddContextSource,
  addDisabled,
  onContextPack,
  contextPackDisabled,
  contextPackHint,
}: {
  onAddContextItem: () => void
  onAddContextSource: () => void
  addDisabled?: boolean
  onContextPack: () => void
  contextPackDisabled?: boolean
  contextPackHint?: string | null
}) {
  return (
    <>
      <span className="hidden text-[11px] text-muted-foreground lg:inline">
        Project memory: decisions, blockers, questions, and their sources
      </span>
      <div className="ml-auto flex items-center gap-1.5">
        <button
          type="button"
          onClick={onContextPack}
          disabled={contextPackDisabled}
          title={contextPackDisabled ? contextPackHint ?? undefined : "Copy a markdown context pack for Claude/ChatGPT/Codex"}
          className="flex h-6 items-center gap-1.5 rounded-md border border-border bg-card px-2.5 text-[11px] font-medium text-foreground transition-colors hover:bg-accent disabled:cursor-not-allowed disabled:opacity-50"
        >
          <ClipboardList className="size-3.5" />
          Context Pack
        </button>
        <button
          type="button"
          onClick={onAddContextSource}
          disabled={addDisabled}
          title={addDisabled ? "Read-only: connect to add sources" : undefined}
          className="flex h-6 items-center gap-1.5 rounded-md border border-border bg-card px-2.5 text-[11px] font-medium text-foreground transition-colors hover:bg-accent disabled:cursor-not-allowed disabled:opacity-50"
        >
          <FileText className="size-3.5" />
          Source
        </button>
        <button
          type="button"
          onClick={onAddContextItem}
          disabled={addDisabled}
          title={addDisabled ? "Read-only: connect to add context" : undefined}
          className="flex h-6 items-center gap-1.5 rounded-md bg-primary px-2.5 text-[11px] font-semibold text-primary-foreground transition-colors hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-50"
        >
          <Plus className="size-3.5" />
          Context
        </button>
      </div>
    </>
  )
}

function TreeToolbar({
  filter,
  onFilterChange,
  treeProjectName,
  treeProjectColor,
  onAddTask,
  addTaskDisabled,
  onAddSection,
  addSectionDisabled,
}: {
  filter: TreeFilter
  onFilterChange: (filter: TreeFilter) => void
  treeProjectName: string
  treeProjectColor: string
  onAddTask: () => void
  addTaskDisabled?: boolean
  onAddSection: () => void
  addSectionDisabled?: boolean
}) {
  return (
    <>
      <div className={toolbar.segment} role="group" aria-label="Tree filter">
        {treeFilters.map((item) => (
          <button
            type="button"
            key={item.key}
            onClick={() => onFilterChange(item.key)}
            className={toolbar.segmentItem(filter === item.key)}
          >
            {item.label}
          </button>
        ))}
      </div>

      {/* Right: create target + actions — follows v0 TreeToolbar (New in: / + Section / + Task) */}
      <div className="ml-auto flex items-center gap-1.5">
        <span className="flex items-center gap-1.5 text-[11px] text-muted-foreground">
          New in:
          <span className="flex h-6 items-center gap-1 rounded border border-border bg-card px-2 text-[11px] font-medium text-foreground">
            <span className="size-1.5 rounded-full" style={{ backgroundColor: treeProjectColor }} />
            {treeProjectName}
          </span>
        </span>
        <button
          type="button"
          onClick={onAddSection}
          disabled={addSectionDisabled}
          title={addSectionDisabled ? "Read-only: connect to add sections" : undefined}
          className="flex h-6 items-center gap-1.5 rounded-md border border-border bg-card px-2.5 text-[11px] font-medium text-foreground transition-colors hover:bg-accent disabled:cursor-not-allowed disabled:opacity-50"
        >
          <Plus className="size-3.5" />
          Section
        </button>
        <button
          type="button"
          onClick={onAddTask}
          disabled={addTaskDisabled}
          title={addTaskDisabled ? "Read-only: connect to add tasks" : undefined}
          className="flex h-6 items-center gap-1.5 rounded-md bg-primary px-2.5 text-[11px] font-semibold text-primary-foreground transition-colors hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-50"
        >
          <Plus className="size-3.5" />
          Task
        </button>
      </div>
    </>
  )
}

function StatusToolbar({
  tasks,
  filter,
  onFilterChange,
  hideDone,
  onHideDoneChange,
}: {
  tasks: Task[]
  filter: StatusFilterKey
  onFilterChange: (filter: StatusFilterKey) => void
  hideDone: boolean
  onHideDoneChange: (hideDone: boolean) => void
}) {
  return (
    <>
      <div className="flex min-w-0 flex-1 items-center gap-1 overflow-x-auto">
        {statusFilters.map((item) => {
          const active = filter === item.key
          const count = tasks.filter((task) =>
            matchesStatusFilter(task, item.key) &&
            (!hideDone || item.key === "DONE" || task.status !== "DONE"),
          ).length
          return (
            <button
              type="button"
              key={item.key}
              onClick={() => onFilterChange(item.key)}
              className={cn(
                "flex h-7 shrink-0 items-center gap-1.5 rounded-md border px-2.5 text-[11px] font-medium transition-colors",
                active
                  ? item.text
                    ? `border-current/40 bg-current/10 ${item.text}`
                    : "border-border bg-accent text-foreground"
                  : "border-transparent text-muted-foreground hover:border-border hover:bg-accent/50 hover:text-foreground",
              )}
            >
              {item.dot && (
                <span className={cn("size-1.5 rounded-full", active ? item.dot : "bg-muted-foreground/50")} aria-hidden />
              )}
              {item.label}
              <span className="rounded bg-muted px-1 font-mono text-[9px] tabular-nums text-muted-foreground">
                {count}
              </span>
            </button>
          )
        })}
      </div>
      <div className="ml-auto flex shrink-0 items-center gap-2 pl-2 text-[11px] text-muted-foreground">
        <span>Hide done</span>
        <button
          type="button"
          role="switch"
          aria-checked={hideDone}
          onClick={() => onHideDoneChange(!hideDone)}
          className={cn(
            "relative inline-flex h-4 w-7 rounded-full border-2 border-transparent transition-colors",
            hideDone ? "bg-primary" : "bg-muted",
          )}
        >
          <span
            className={cn(
              "pointer-events-none block h-3 w-3 rounded-full bg-white shadow-sm transition-transform",
              hideDone ? "translate-x-3" : "translate-x-0",
            )}
          />
        </button>
      </div>
    </>
  )
}

function TimelineToolbar() {
  return (
    <div className="flex items-center gap-3 text-[11px] text-muted-foreground">
      <span className="flex items-center gap-1 text-status-meet"><Video className="size-3" />MEET</span>
      <span className="flex items-center gap-1 text-status-remind"><Bell className="size-3" />REMIND</span>
      <span className="flex items-center gap-1 text-status-deadline"><Flag className="size-3" />DEADLINE</span>
      <span className="hidden text-muted-foreground lg:inline">Time view over current attention items</span>
    </div>
  )
}

function formatDayAnchor(selectedDate: string): string {
  const today = todayKey()
  if (selectedDate === today) return "Today"
  if (selectedDate === addDaysKey(today, 1)) return "Tomorrow"
  return parseDateKey(selectedDate).toLocaleDateString(undefined, {
    weekday: "short",
    day: "numeric",
    month: "short",
  })
}

function CalendarToolbar({
  selectedDate,
  viewMode,
  showDone,
  onToday,
  onTomorrow,
  onStep,
  onShowDoneChange,
  onViewModeChange,
  onAddMeeting,
  addMeetingDisabled,
  addMeetingLabel,
  addMeetingError,
}: {
  selectedDate: string
  viewMode: "day" | "week"
  showDone: boolean
  onToday: () => void
  onTomorrow: () => void
  onStep: (dir: number) => void
  onShowDoneChange: (showDone: boolean) => void
  onViewModeChange: (mode: "day" | "week") => void
  onAddMeeting: () => void
  addMeetingDisabled?: boolean
  addMeetingLabel: string
  addMeetingError: string | null
}) {
  const today = todayKey()
  const isToday = selectedDate === today && viewMode === "day"
  const isTomorrow = selectedDate === addDaysKey(today, 1) && viewMode === "day"
  const label = viewMode === "week"
    ? formatWeekLabel(mondayOfWeekKey(selectedDate))
    : formatDayAnchor(selectedDate)

  const ghost = (active: boolean) =>
    cn(
      "h-6 rounded-md border px-2.5 text-[11px] font-medium transition-colors",
      active
        ? "border-primary/40 bg-primary/10 text-primary"
        : "border-border bg-card text-muted-foreground hover:bg-accent/50 hover:text-foreground",
    )

  return (
    <>
      {/* View mode */}
      <div className={toolbar.segment} role="group" aria-label="Calendar view mode">
        <button type="button" onClick={() => onViewModeChange("day")} className={toolbar.segmentItem(viewMode === "day")}>
          Day
        </button>
        <button type="button" onClick={() => onViewModeChange("week")} className={toolbar.segmentItem(viewMode === "week")}>
          Week
        </button>
      </div>

      {/* Quick jumps */}
      <button type="button" onClick={onToday} className={ghost(isToday)}>Today</button>
      <button type="button" onClick={onTomorrow} className={ghost(isTomorrow)}>Tomorrow</button>

      {/* Prev / label / next — steps by day in Day view, by week in Week view */}
      <div className="flex items-center gap-0.5">
        <button
          type="button"
          onClick={() => onStep(-1)}
          aria-label={viewMode === "week" ? "Previous week" : "Previous day"}
          className="flex h-6 w-6 items-center justify-center rounded-md border border-border bg-card text-muted-foreground transition-colors hover:text-foreground"
        >
          <ChevronLeft className="size-3.5" />
        </button>
        <span className="flex h-6 min-w-32 items-center justify-center gap-1.5 rounded-md border border-border bg-card px-2 text-[11px] font-medium text-foreground tabular-nums">
          <CalendarDays className="size-3.5 text-muted-foreground" />
          {label}
        </span>
        <button
          type="button"
          onClick={() => onStep(1)}
          aria-label={viewMode === "week" ? "Next week" : "Next day"}
          className="flex h-6 w-6 items-center justify-center rounded-md border border-border bg-card text-muted-foreground transition-colors hover:text-foreground"
        >
          <ChevronRight className="size-3.5" />
        </button>
      </div>

      {/* Right: Show done toggle + MEET planning entry */}
      <div className="ml-auto flex items-center gap-3">
        <label className="flex cursor-pointer items-center gap-1.5 select-none">
          <span className="text-[11px] text-muted-foreground">Show done</span>
          <button
            type="button"
            role="switch"
            aria-checked={showDone}
            onClick={() => onShowDoneChange(!showDone)}
            className={cn(
              "relative inline-flex h-4 w-7 rounded-full border-2 border-transparent transition-colors",
              showDone ? "bg-primary" : "bg-muted",
            )}
          >
            <span
              className={cn(
                "pointer-events-none block h-3 w-3 rounded-full bg-white shadow-sm transition-transform",
                showDone ? "translate-x-3" : "translate-x-0",
              )}
            />
          </button>
        </label>
        {addMeetingError && (
          <span
            className="max-w-48 truncate text-[10px] text-destructive"
            title={addMeetingError}
          >
            {addMeetingError}
          </span>
        )}
        <button
          type="button"
          onClick={onAddMeeting}
          disabled={addMeetingDisabled}
          title={addMeetingDisabled ? "Read-only: connect to create MEETs" : "Create MEET on selected calendar date"}
          className="flex h-6 items-center gap-1.5 rounded-md border border-status-meet/35 bg-status-meet/10 px-2.5 text-[11px] font-semibold text-status-meet transition-colors hover:bg-status-meet/20 disabled:cursor-not-allowed disabled:opacity-50"
        >
          <Plus className="size-3.5" />
          {addMeetingLabel}
        </button>
      </div>
    </>
  )
}

// Workstream state filter chips — follows v0 TreeToolbar/WorkstreamsToolbar
// (v0 workspace-header.tsx wsFilterCards), limited to states derivable from
// real task data (v0's "blocked"/"stale" need data the model does not track).
const wsFilterCards: {
  filter: WorkstreamFilter
  label: string
  countKey: string
  activeColor: string
  activeText: string
}[] = [
  { filter: "all", label: "All", countKey: "all", activeColor: "bg-accent border-border", activeText: "text-foreground" },
  { filter: "active", label: "Active", countKey: "active", activeColor: "bg-status-focus/10 border-status-focus/40", activeText: "text-status-focus" },
  { filter: "waiting", label: "Waiting", countKey: "waiting", activeColor: "bg-status-wait/10 border-status-wait/40", activeText: "text-status-wait" },
  { filter: "done", label: "Done", countKey: "done", activeColor: "bg-muted/60 border-border", activeText: "text-muted-foreground" },
]

function WorkstreamsToolbar({
  wsFilter,
  onWsFilterChange,
  wsSummary,
  onAddWorkstream,
  addDisabled,
  addHint,
}: {
  wsFilter: WorkstreamFilter
  onWsFilterChange: (filter: WorkstreamFilter) => void
  wsSummary: Record<string, number>
  onAddWorkstream: () => void
  addDisabled?: boolean
  addHint?: string | null
}) {
  return (
    <>
      <div className="flex items-center gap-1">
        {wsFilterCards.map((card) => {
          const count = wsSummary[card.countKey] ?? 0
          const isActive = wsFilter === card.filter
          return (
            <button
              type="button"
              key={card.filter}
              onClick={() => onWsFilterChange(isActive && card.filter !== "all" ? "all" : card.filter)}
              className={cn(
                "flex h-6 items-center gap-1.5 rounded-md border px-2.5 text-[11px] font-medium transition-colors",
                isActive
                  ? cn(card.activeColor, card.activeText)
                  : "border-border bg-card/50 text-muted-foreground hover:bg-accent/30 hover:text-foreground",
              )}
            >
              <span className="font-mono font-bold tabular-nums">{count}</span>
              {card.label}
            </button>
          )
        })}
      </div>

      <div className="ml-auto">
        <button
          type="button"
          onClick={onAddWorkstream}
          disabled={addDisabled}
          title={addDisabled ? addHint ?? undefined : undefined}
          className="flex h-6 items-center gap-1.5 rounded-md bg-primary px-2.5 text-[11px] font-semibold text-primary-foreground transition-colors hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-50"
        >
          <Plus className="size-3.5" />
          Workstream
        </button>
      </div>
    </>
  )
}
