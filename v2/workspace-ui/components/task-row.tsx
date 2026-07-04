"use client"

import { Bell, ChevronRight, CornerDownRight, Pin, Clock } from "lucide-react"
import type { Task } from "@/lib/types"
import { cn } from "@/lib/utils"
import { StatusBadge } from "./status-badge"

interface Props {
  task: Task
  depth: number
  hasChildren: boolean
  collapsed: boolean
  selected: boolean
  onSelect: (id: string) => void
  onToggleCollapse: (id: string) => void
  onTogglePin: (id: string) => void
  readOnly?: boolean
}

export function TaskRow({
  task,
  depth,
  hasChildren,
  collapsed,
  selected,
  onSelect,
  onToggleCollapse,
  onTogglePin,
  readOnly,
}: Props) {
  const isDone = task.status === "DONE"

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={() => onSelect(task.id)}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault()
          onSelect(task.id)
        }
      }}
      className={cn(
        "group relative flex items-center gap-2 rounded-lg border py-1.5 pr-2 transition-colors",
        selected
          ? "border-row-selected-border bg-row-selected"
          : "border-transparent hover:border-border hover:bg-accent/40",
      )}
      style={{ paddingLeft: 8 + depth * 22 }}
    >
      {/* connector for nested rows */}
      {depth > 0 && (
        <CornerDownRight
          className="pointer-events-none absolute size-3.5 text-muted-foreground/50"
          style={{ left: depth * 22 - 8 }}
          aria-hidden
        />
      )}

      {/* chevron / spacer */}
      {hasChildren ? (
        <button
          onClick={(e) => {
            e.stopPropagation()
            onToggleCollapse(task.id)
          }}
          className="flex size-5 shrink-0 items-center justify-center rounded text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
          aria-label={collapsed ? "Expand" : "Collapse"}
        >
          <ChevronRight className={cn("size-4 transition-transform", !collapsed && "rotate-90")} />
        </button>
      ) : (
        <span className="size-5 shrink-0" aria-hidden />
      )}

      <StatusBadge status={task.status} />

      <span
        className={cn(
          "min-w-0 flex-1 truncate text-sm",
          isDone ? "text-muted-foreground line-through" : "text-foreground",
        )}
      >
        {task.title}
      </span>

      {/* inline metadata */}
      {task.status === "WAIT" && task.waitingFor && (
        <span className="hidden items-center gap-1 truncate text-xs text-status-wait/80 md:flex">
          <Clock className="size-3" />
          {task.waitingFor}
        </span>
      )}
      {task.deadline && !(task.status === "WAIT" && task.waitingFor) && (
        <span className="hidden items-center gap-1 text-xs text-status-deadline/80 md:flex">
          <Clock className="size-3" />
          {task.deadline}
        </span>
      )}
      {task.reminder !== "none" && (
        <Bell className="hidden size-3.5 shrink-0 text-status-remind md:block" aria-label="Has reminder" />
      )}

      {/* pin */}
      <button
        disabled={readOnly}
        title={readOnly ? "Workspace is read-only" : undefined}
        onClick={(e) => {
          e.stopPropagation()
          onTogglePin(task.id)
        }}
        className={cn(
          "flex size-6 shrink-0 items-center justify-center rounded-md transition-colors",
          task.pinned
            ? "text-primary hover:bg-primary/10"
            : "text-muted-foreground/40 opacity-0 hover:bg-accent hover:text-foreground group-hover:opacity-100",
          readOnly && "cursor-not-allowed opacity-40 group-hover:opacity-40",
        )}
        aria-label={task.pinned ? "Unpin from panel" : "Pin to panel"}
        aria-pressed={task.pinned}
      >
        <Pin className={cn("size-3.5", task.pinned && "fill-current")} />
      </button>
    </div>
  )
}
