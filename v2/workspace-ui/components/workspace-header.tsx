"use client"

import {
  Bell,
  CalendarClock,
  CalendarDays,
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  Flag,
  FolderTree,
  Layers,
  ListChecks,
  Search,
  Video,
} from "lucide-react"
import type { StatusFilterKey, TabKey, Task, TreeFilter } from "@/lib/types"
import { matchesStatusFilter } from "@/lib/status-filter"
import { cn } from "@/lib/utils"
import { formatDayLabel, formatWeekLabel, mondayOfWeekKey, todayKey } from "@/lib/calendar-date"

interface Props {
  tab: TabKey
  onTabChange: (tab: TabKey) => void
  filter: TreeFilter
  onFilterChange: (filter: TreeFilter) => void
  statusFilter: StatusFilterKey
  onStatusFilterChange: (filter: StatusFilterKey) => void
  hideDone: boolean
  onHideDoneChange: (hideDone: boolean) => void
  statusTasks: Task[]
  search: string
  onSearchChange: (value: string) => void
  calendarSelectedDate: string
  calendarViewMode: "day" | "week"
  onCalendarToday: () => void
  onCalendarTomorrow: () => void
  onCalendarShiftDay: (delta: number) => void
  onCalendarShiftWeek: (delta: number) => void
  onCalendarViewModeChange: (mode: "day" | "week") => void
}

const tabs: { key: TabKey; label: string; icon: typeof FolderTree; later?: boolean }[] = [
  { key: "tree", label: "Tree", icon: FolderTree },
  { key: "status", label: "Status", icon: ListChecks },
  { key: "timeline", label: "Timeline", icon: CalendarClock },
  { key: "calendar", label: "Calendar", icon: CalendarDays },
  { key: "workstreams", label: "Workstreams", icon: Layers, later: true },
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
  statusFilter,
  onStatusFilterChange,
  hideDone,
  onHideDoneChange,
  statusTasks,
  search,
  onSearchChange,
  calendarSelectedDate,
  calendarViewMode,
  onCalendarToday,
  onCalendarTomorrow,
  onCalendarShiftDay,
  onCalendarShiftWeek,
  onCalendarViewModeChange,
}: Props) {
  const searchDisabled = tab === "calendar" || tab === "workstreams"

  return (
    <header className="shrink-0 border-b border-border">
      <div className="flex h-14 items-center gap-4 px-5">
        <div className="flex min-w-0 items-center gap-2.5">
          <img
            src="./taskoverlay-mark-32.png"
            alt=""
            aria-hidden="true"
            className="size-7 shrink-0 object-contain"
          />
          <div className="min-w-0">
            <h1 className="text-[14px] font-semibold leading-tight text-foreground">Workspace</h1>
            <p className="truncate text-[11px] text-muted-foreground">
              Main task organization surface
            </p>
          </div>
        </div>
        <div className="relative ml-auto w-64 max-w-[40vw]">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
          <input
            value={search}
            onChange={(event) => onSearchChange(event.target.value)}
            disabled={searchDisabled}
            placeholder={searchDisabled ? "Search unavailable in this view" : "Search or jump to..."}
            className="h-8 w-full rounded-md border border-input bg-card pl-8 pr-3 text-[12px] text-foreground outline-none transition-colors placeholder:text-muted-foreground focus:border-primary/60 focus:ring-1 focus:ring-primary/20 disabled:cursor-not-allowed disabled:opacity-50"
          />
        </div>
      </div>

      <nav className="flex h-10 items-end gap-0 border-t border-border/40 px-4" aria-label="Workspace views">
        {tabs.map((item) => {
          const Icon = item.icon
          const active = tab === item.key
          return (
            <button
              type="button"
              key={item.key}
              onClick={() => onTabChange(item.key)}
              className={cn(
                "flex h-full items-center gap-1.5 whitespace-nowrap border-b-2 px-3 text-[12px] font-medium transition-colors",
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

      <div className="flex h-10 items-center gap-2 border-t border-border/40 px-4">
        {tab === "tree" && (
          <TreeToolbar
            filter={filter}
            onFilterChange={onFilterChange}
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
            onToday={onCalendarToday}
            onTomorrow={onCalendarTomorrow}
            onShiftDay={onCalendarShiftDay}
            onShiftWeek={onCalendarShiftWeek}
            onViewModeChange={onCalendarViewModeChange}
          />
        )}
        {tab === "workstreams" && <LaterToolbar label="Workstreams are planned for a later release" />}
      </div>
    </header>
  )
}

function TreeToolbar({
  filter,
  onFilterChange,
}: {
  filter: TreeFilter
  onFilterChange: (filter: TreeFilter) => void
}) {
  return (
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

function CalendarToolbar({
  selectedDate,
  viewMode,
  onToday,
  onTomorrow,
  onShiftDay,
  onShiftWeek,
  onViewModeChange,
}: {
  selectedDate: string
  viewMode: "day" | "week"
  onToday: () => void
  onTomorrow: () => void
  onShiftDay: (delta: number) => void
  onShiftWeek: (delta: number) => void
  onViewModeChange: (mode: "day" | "week") => void
}) {
  const isToday = selectedDate === todayKey()
  const label = viewMode === "week"
    ? formatWeekLabel(mondayOfWeekKey(selectedDate))
    : formatDayLabel(selectedDate)

  return (
    <>
      <div className={toolbar.segment} role="group" aria-label="Jump to date">
        <button type="button" onClick={onToday} className={toolbar.segmentItem(isToday)}>
          Today
        </button>
        <button type="button" onClick={onTomorrow} className={toolbar.segmentItem(false)}>
          Tomorrow
        </button>
      </div>

      <span className="h-4 w-px bg-border" aria-hidden />

      <div className={toolbar.segment} role="group" aria-label="Shift by week">
        <button type="button" onClick={() => onShiftWeek(-1)} className="flex h-6 w-6 items-center justify-center rounded text-muted-foreground hover:text-foreground" aria-label="Previous week">
          <ChevronsLeft className="size-3.5" />
        </button>
        <button type="button" onClick={() => onShiftDay(-1)} className="flex h-6 w-6 items-center justify-center rounded text-muted-foreground hover:text-foreground" aria-label="Previous day">
          <ChevronLeft className="size-3.5" />
        </button>
        <button type="button" onClick={() => onShiftDay(1)} className="flex h-6 w-6 items-center justify-center rounded text-muted-foreground hover:text-foreground" aria-label="Next day">
          <ChevronRight className="size-3.5" />
        </button>
        <button type="button" onClick={() => onShiftWeek(1)} className="flex h-6 w-6 items-center justify-center rounded text-muted-foreground hover:text-foreground" aria-label="Next week">
          <ChevronsRight className="size-3.5" />
        </button>
      </div>

      <span className="h-4 w-px bg-border" aria-hidden />

      <span className="flex items-center gap-1.5 text-[11px] font-medium text-foreground">
        <CalendarDays className="size-3.5 text-muted-foreground" />
        {label}
      </span>

      <div className={cn(toolbar.segment, "ml-auto")} role="group" aria-label="Calendar view mode">
        <button type="button" onClick={() => onViewModeChange("day")} className={toolbar.segmentItem(viewMode === "day")}>
          Day
        </button>
        <button type="button" onClick={() => onViewModeChange("week")} className={toolbar.segmentItem(viewMode === "week")}>
          Week
        </button>
      </div>
    </>
  )
}

function LaterToolbar({ label }: { label: string }) {
  return (
    <div className="flex items-center gap-2 text-[11px] text-muted-foreground">
      <span>{label}</span>
      <span className="rounded bg-accent px-1.5 py-0.5 text-[9px] font-semibold uppercase tracking-wide">
        Later
      </span>
    </div>
  )
}
