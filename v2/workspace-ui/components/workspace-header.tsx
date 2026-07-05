"use client"

import {
  Bell,
  CalendarClock,
  CalendarDays,
  Flag,
  FolderTree,
  Layers,
  ListChecks,
  Plus,
  Search,
  Video,
} from "lucide-react"
import type { Project, StatusFilterKey, TabKey, Task, TreeFilter } from "@/lib/types"
import { matchesStatusFilter } from "@/lib/status-filter"
import { cn } from "@/lib/utils"

interface Props {
  tab: TabKey
  onTabChange: (tab: TabKey) => void
  filter: TreeFilter
  onFilterChange: (filter: TreeFilter) => void
  statusFilter: StatusFilterKey
  onStatusFilterChange: (filter: StatusFilterKey) => void
  statusTasks: Task[]
  treeProject: Project
  search: string
  onSearchChange: (value: string) => void
  onNewMeet: () => void
  readOnly?: boolean
}

const tabs: { key: TabKey; label: string; icon: typeof FolderTree; later?: boolean }[] = [
  { key: "tree", label: "Tree", icon: FolderTree },
  { key: "status", label: "Status", icon: ListChecks },
  { key: "timeline", label: "Timeline", icon: CalendarClock },
  { key: "calendar", label: "Calendar", icon: CalendarDays, later: true },
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
  secondary:
    "flex h-7 items-center gap-1.5 rounded-md border border-border bg-card px-2.5 text-[11px] font-medium text-foreground transition-colors hover:bg-accent disabled:cursor-not-allowed disabled:opacity-40",
  primary:
    "flex h-7 items-center gap-1.5 rounded-md bg-primary px-2.5 text-[11px] font-semibold text-primary-foreground transition-colors hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-40",
}

export function WorkspaceHeader({
  tab,
  onTabChange,
  filter,
  onFilterChange,
  statusFilter,
  onStatusFilterChange,
  statusTasks,
  treeProject,
  search,
  onSearchChange,
  onNewMeet,
  readOnly,
}: Props) {
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
            placeholder="Search or jump to..."
            className="h-8 w-full rounded-md border border-input bg-card pl-8 pr-3 text-[12px] text-foreground outline-none transition-colors placeholder:text-muted-foreground focus:border-primary/60 focus:ring-1 focus:ring-primary/20"
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
            treeProject={treeProject}
            readOnly={readOnly}
          />
        )}
        {tab === "status" && (
          <StatusToolbar
            tasks={statusTasks}
            filter={statusFilter}
            onFilterChange={onStatusFilterChange}
          />
        )}
        {tab === "timeline" && <TimelineToolbar onNewMeet={onNewMeet} readOnly={readOnly} />}
        {tab === "calendar" && <LaterToolbar label="Calendar planning is not enabled in this release" />}
        {tab === "workstreams" && <LaterToolbar label="Workstreams are planned for a later release" />}
      </div>
    </header>
  )
}

function TreeToolbar({
  filter,
  onFilterChange,
  treeProject,
  readOnly,
}: {
  filter: TreeFilter
  onFilterChange: (filter: TreeFilter) => void
  treeProject: Project
  readOnly?: boolean
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
      <div className="ml-auto flex items-center gap-1.5">
        <span className="flex h-7 items-center gap-1.5 rounded-md border border-dashed border-border px-2.5 text-[11px] text-muted-foreground">
          <span className="size-1.5 rounded-full" style={{ backgroundColor: treeProject.color }} aria-hidden />
          New in {treeProject.name}
        </span>
        <button type="button" disabled={readOnly} className={toolbar.secondary}>
          <Plus className="size-3.5" />
          Section
        </button>
        <button type="button" disabled={readOnly} className={toolbar.primary}>
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
}: {
  tasks: Task[]
  filter: StatusFilterKey
  onFilterChange: (filter: StatusFilterKey) => void
}) {
  return (
    <div className="flex min-w-0 items-center gap-1 overflow-x-auto">
      {statusFilters.map((item) => {
        const active = filter === item.key
        const count = tasks.filter((task) => matchesStatusFilter(task, item.key)).length
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
  )
}

function TimelineToolbar({ onNewMeet, readOnly }: { onNewMeet: () => void; readOnly?: boolean }) {
  return (
    <>
      <div className="flex items-center gap-3 text-[11px] text-muted-foreground">
        <span className="flex items-center gap-1 text-status-meet"><Video className="size-3" />MEET</span>
        <span className="flex items-center gap-1 text-status-remind"><Bell className="size-3" />REMIND</span>
        <span className="flex items-center gap-1 text-status-deadline"><Flag className="size-3" />DEADLINE</span>
        <span className="hidden text-muted-foreground lg:inline">Time view over current attention items</span>
      </div>
      <button
        type="button"
        onClick={onNewMeet}
        disabled={readOnly}
        title={readOnly ? "MEET is not persisted in the current app state" : "New MEET"}
        className={cn(toolbar.secondary, "ml-auto border-status-meet/40 text-status-meet")}
      >
        <Plus className="size-3.5" />
        New MEET
      </button>
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
