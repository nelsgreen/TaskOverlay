"use client"

import { Bell, Pin, Zap } from "lucide-react"
import type { Project, Section, Task } from "@/lib/types"
import { cn } from "@/lib/utils"
import { statusConfig } from "./status-badge"

interface Props {
  tasks: Task[]
  projects: Project[]
  sections: Section[]
  selectedTaskId: string | null
  onSelectTask: (id: string) => void
}

export function ActiveNowStrip({ tasks, projects, sections, selectedTaskId, onSelectTask }: Props) {
  const active = tasks.filter((t) => t.status === "FOCUS" || (t.reminder !== "none" && t.status !== "DONE"))

  const pathFor = (t: Task) => {
    const p = projects.find((x) => x.id === t.projectId)
    const s = sections.find((x) => x.id === t.sectionId)
    return `${p?.name ?? ""}${s ? ` / ${s.name}` : ""}`
  }

  return (
    <footer className="shrink-0 border-t border-border bg-sidebar">
      <div className="flex items-center gap-4 px-4 py-3">
        <div className="flex w-32 shrink-0 flex-col">
          <div className="flex items-center gap-1.5">
            <Zap className="size-4 text-status-focus" />
            <span className="text-sm font-semibold text-foreground">Active now</span>
          </div>
          <span className="font-mono text-[11px] text-muted-foreground">
            FOCUS + active REMIND · feeds Working overlay · {active.length} items
          </span>
        </div>

        <div className="flex flex-1 gap-2 overflow-x-auto pb-1">
          {active.map((task, i) => {
            const c = statusConfig[task.status]
            const selected = task.id === selectedTaskId
            return (
              <button
                key={task.id}
                onClick={() => onSelectTask(task.id)}
                className={cn(
                  "group flex w-52 shrink-0 items-start gap-2 rounded-lg border px-2.5 py-2 text-left transition-colors",
                  selected
                    ? "border-row-selected-border bg-row-selected"
                    : "border-border bg-card/60 hover:border-border hover:bg-accent/40",
                )}
              >
                <span className="flex size-4 shrink-0 items-center justify-center rounded bg-accent/80 font-mono text-[10px] font-bold text-muted-foreground tabular-nums">
                  {i + 1}
                </span>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-1">
                    <p className="min-w-0 flex-1 truncate text-[13px] font-medium text-foreground">{task.title}</p>
                    {task.pinned && <Pin className="size-3 shrink-0 fill-current text-status-panel" />}
                    {task.reminder !== "none" && <Bell className="size-3 shrink-0 text-status-remind" />}
                  </div>
                  <div className="mt-0.5 flex items-center gap-1.5">
                    <span className={cn("size-1.5 rounded-full", c.dot)} />
                    <span className={cn("font-mono text-[10px] font-semibold uppercase tracking-wide", c.text)}>{c.label}</span>
                    <span className="truncate text-[11px] text-muted-foreground">{pathFor(task)}</span>
                  </div>
                </div>
              </button>
            )
          })}
          {active.length === 0 && (
            <div className="flex items-center px-3 text-xs text-muted-foreground">
              Нет активных задач. Отметьте задачу как FOCUS или добавьте напоминание.
            </div>
          )}
        </div>
      </div>
    </footer>
  )
}
