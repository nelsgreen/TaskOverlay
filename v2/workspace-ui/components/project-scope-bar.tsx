"use client"

import type { Project, Task } from "@/lib/types"
import { cn } from "@/lib/utils"

interface Props {
  projects: Project[]
  tasks: Task[]
  selectedProjectIds: string[]
  onSelectOnly: (id: string) => void
  onToggleProject: (id: string) => void
  onSelectAll: () => void
}

export function ProjectScopeBar({
  projects,
  tasks,
  selectedProjectIds,
  onSelectOnly,
  onToggleProject,
  onSelectAll,
}: Props) {
  const allSelected = projects.length > 0 && selectedProjectIds.length === projects.length
  const attentionCount = (projectId: string) =>
    tasks.filter(
      (task) => task.projectId === projectId && (task.status === "FOCUS" || task.status === "WAIT"),
    ).length

  return (
    <div className="flex h-10 shrink-0 items-center gap-1 overflow-x-auto border-b border-border bg-card/25 px-4">
      <span className="mr-1 shrink-0 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
        Project scope
      </span>
      <button
        type="button"
        onClick={onSelectAll}
        aria-pressed={allSelected}
        className={cn(
          "h-7 shrink-0 rounded-md border px-2.5 text-[11px] font-medium transition-colors",
          allSelected
            ? "border-primary/40 bg-primary/10 text-foreground"
            : "border-transparent text-muted-foreground hover:border-border hover:bg-accent/50 hover:text-foreground",
        )}
      >
        All
      </button>
      <span className="mx-1 h-4 w-px shrink-0 bg-border" aria-hidden />
      {projects.map((project) => {
        const selected = selectedProjectIds.includes(project.id)
        const count = attentionCount(project.id)
        return (
          <button
            type="button"
            key={project.id}
            onClick={(event) =>
              event.metaKey || event.ctrlKey
                ? onToggleProject(project.id)
                : onSelectOnly(project.id)
            }
            aria-pressed={selected}
            title="Click to focus; Ctrl+click to add or remove from scope"
            className={cn(
              "flex h-7 shrink-0 items-center gap-1.5 rounded-md border px-2.5 text-[11px] font-medium transition-colors",
              selected
                ? "border-border bg-accent text-foreground"
                : "border-transparent text-muted-foreground hover:border-border hover:bg-accent/50 hover:text-foreground",
            )}
          >
            <span
              className={cn("size-1.5 rounded-full", !selected && "opacity-60")}
              style={{ backgroundColor: project.color }}
              aria-hidden
            />
            <span>{project.name}</span>
            {count > 0 && (
              <span
                className={cn(
                  "min-w-4 rounded px-1 font-mono text-[9px] font-semibold tabular-nums",
                  selected ? "bg-primary/15 text-primary" : "bg-muted text-muted-foreground",
                )}
              >
                {count}
              </span>
            )}
          </button>
        )
      })}
    </div>
  )
}
