"use client"

import { Bell, Clock, Pin } from "lucide-react"
import type { Project, Section, StatusFilterKey, Task } from "@/lib/types"
import { matchesStatusFilter } from "@/lib/status-filter"
import { cn } from "@/lib/utils"
import { statusConfig } from "./status-badge"

interface Props {
  tasks: Task[]
  projects: Project[]
  sections: Section[]
  selectedTaskId: string | null
  onSelectTask: (id: string) => void
  filter: StatusFilterKey
  hideDone: boolean
}

export function StatusBoard({ tasks, projects, sections, selectedTaskId, onSelectTask, filter, hideDone }: Props) {
  const visible = tasks.filter(
    (task) => matchesStatusFilter(task, filter) && (!hideDone || task.status !== "DONE"),
  )

  const pathFor = (t: Task) => {
    const p = projects.find((x) => x.id === t.projectId)
    const s = sections.find((x) => x.id === t.sectionId)
    return `${p?.name ?? ""}${s ? ` / ${s.name}` : ""}`
  }

  return (
    <div className="flex h-full flex-col overflow-hidden">
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
        {task.status === "WAIT" && task.waitingFor && (
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
