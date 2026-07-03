"use client"

import { useState } from "react"
import { Bell, Clock, Pin } from "lucide-react"
import type { Project, Section, Status, Task } from "@/lib/types"
import { cn } from "@/lib/utils"
import { statusConfig } from "./status-badge"

interface Props {
  tasks: Task[]
  projects: Project[]
  sections: Section[]
  selectedTaskId: string | null
  onSelectTask: (id: string) => void
}

type FilterKey = "all" | "panel" | Status | "remind"

interface FilterCfg {
  key: FilterKey
  label: string
  dot?: string
  text?: string
}

const filters: FilterCfg[] = [
  { key: "all", label: "All" },
  { key: "panel", label: "Panel", dot: "bg-status-panel", text: "text-status-panel" },
  { key: "FOCUS", label: "Focus", dot: "bg-status-focus", text: "text-status-focus" },
  { key: "WAIT", label: "Wait", dot: "bg-status-wait", text: "text-status-wait" },
  { key: "remind", label: "Remind", dot: "bg-status-remind", text: "text-status-remind" },
  { key: "TODO", label: "Todo", dot: "bg-status-todo", text: "text-status-todo" },
  { key: "DONE", label: "Done", dot: "bg-status-done", text: "text-status-done" },
]

function matchesFilter(task: Task, filter: FilterKey): boolean {
  if (filter === "all") return true
  if (filter === "panel") return task.pinned
  if (filter === "remind") return task.reminder !== "none"
  return task.status === filter
}

export function StatusBoard({ tasks, projects, sections, selectedTaskId, onSelectTask }: Props) {
  const [filter, setFilter] = useState<FilterKey>("all")

  const visible = tasks.filter((t) => matchesFilter(t, filter))

  const countFor = (f: FilterKey) => tasks.filter((t) => matchesFilter(t, f)).length

  const pathFor = (t: Task) => {
    const p = projects.find((x) => x.id === t.projectId)
    const s = sections.find((x) => x.id === t.sectionId)
    return `${p?.name ?? ""}${s ? ` / ${s.name}` : ""}`
  }

  return (
    <div className="flex h-full flex-col overflow-hidden">
      {/* Filter chips */}
      <div className="flex shrink-0 items-center gap-1 border-b border-border px-4 py-2.5">
        {filters.map((f) => {
          const active = filter === f.key
          const count = countFor(f.key)
          return (
            <button
              key={f.key}
              onClick={() => setFilter(f.key)}
              className={cn(
                "flex items-center gap-1.5 rounded-md border px-2.5 py-1 text-xs font-medium transition-colors",
                active
                  ? f.dot
                    ? `border-current/40 bg-current/10 ${f.text}`
                    : "border-border bg-accent text-foreground"
                  : "border-transparent text-muted-foreground hover:border-border hover:text-foreground",
              )}
              style={active && f.dot ? {} : {}}
            >
              {f.dot && (
                <span className={cn("size-1.5 rounded-full", active ? f.dot : "bg-muted-foreground/50")} aria-hidden />
              )}
              {f.label}
              <span
                className={cn(
                  "rounded px-1 font-mono text-[10px] tabular-nums",
                  active ? "bg-foreground/10" : "bg-muted text-muted-foreground",
                )}
              >
                {count}
              </span>
            </button>
          )
        })}
      </div>

      {/* Task list */}
      <div className="flex-1 overflow-y-auto">
        {visible.length === 0 ? (
          <div className="flex h-32 items-center justify-center text-sm text-muted-foreground">
            No tasks match this filter
          </div>
        ) : (
          <div className="divide-y divide-border/50">
            {visible.map((task) => (
              <StatusRow
                key={task.id}
                task={task}
                path={pathFor(task)}
                selected={task.id === selectedTaskId}
                onSelect={() => onSelectTask(task.id)}
              />
            ))}
          </div>
        )}
      </div>

      <div className="shrink-0 border-t border-border px-4 py-2 text-[11px] text-muted-foreground">
        {visible.length} of {tasks.length} tasks
      </div>
    </div>
  )
}

function StatusRow({
  task,
  path,
  selected,
  onSelect,
}: {
  task: Task
  path: string
  selected: boolean
  onSelect: () => void
}) {
  const c = statusConfig[task.status]
  const isDone = task.status === "DONE"

  return (
    <button
      onClick={onSelect}
      className={cn(
        "group flex w-full items-center gap-3 px-4 py-2.5 text-left transition-colors",
        selected ? "bg-row-selected" : "hover:bg-accent/30",
      )}
    >
      {/* Panel indicator */}
      <span className="flex w-4 shrink-0 items-center justify-center">
        {task.pinned && <Pin className="size-3 fill-current text-status-panel" aria-label="Pinned to panel" />}
      </span>

      {/* Status chip */}
      <span
        className={cn(
          "inline-flex w-14 shrink-0 items-center justify-center gap-1 rounded px-1 py-0.5 font-mono text-[9px] font-bold uppercase tracking-wider ring-1 ring-inset",
          c.soft,
          c.text,
          c.ring,
        )}
      >
        <span className={cn("size-1 rounded-full", c.dot)} aria-hidden />
        {c.label}
      </span>

      {/* Title + path */}
      <div className="min-w-0 flex-1">
        <p
          className={cn(
            "truncate text-[13px] font-medium leading-tight",
            isDone ? "text-muted-foreground line-through" : "text-foreground",
          )}
        >
          {task.title}
        </p>
        <p className="mt-0.5 truncate text-[11px] text-muted-foreground">{path}</p>
      </div>

      {/* Right-side metadata */}
      <div className="flex shrink-0 items-center gap-2">
        {/* WAIT — waiting for */}
        {task.waitingFor && (
          <span className="hidden max-w-32 truncate text-[11px] text-status-wait md:block">
            {task.waitingFor}
          </span>
        )}
        {/* REMIND — reminder badge */}
        {task.reminder !== "none" && (
          <span className="flex items-center gap-1 text-[11px] text-status-remind">
            <Bell className="size-3" aria-hidden />
            {task.reminder}
          </span>
        )}
        {/* DEADLINE — red */}
        {task.deadline && (
          <span className="flex items-center gap-1 text-[11px] text-status-deadline">
            <Clock className="size-3" aria-hidden />
            {task.deadline}
          </span>
        )}
      </div>
    </button>
  )
}
