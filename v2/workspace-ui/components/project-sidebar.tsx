"use client"

import { Check, Folder, Plus } from "lucide-react"
import type { Project, Task } from "@/lib/types"
import { cn } from "@/lib/utils"

interface Props {
  projects: Project[]
  tasks: Task[]
  selectedProjectIds: string[]
  /** plain click — focus a single project */
  onSelectOnly: (id: string) => void
  /** cmd/ctrl click or checkbox — add/remove from multi-selection */
  onToggleProject: (id: string) => void
  readOnly?: boolean
}

export function ProjectSidebar({ projects, tasks, selectedProjectIds, onSelectOnly, onToggleProject, readOnly }: Props) {
  const activeCount = (projectId: string) =>
    tasks.filter((t) => t.projectId === projectId && (t.status === "FOCUS" || t.status === "WAIT")).length

  const totalProjects = projects.length
  const totalActive = tasks.filter((t) => t.status === "FOCUS").length
  const multi = selectedProjectIds.length > 1

  return (
    <aside className="flex w-60 shrink-0 flex-col border-r border-border bg-sidebar">
      <div className="flex items-center justify-between px-4 pb-2 pt-4">
        <span className="text-[11px] font-semibold uppercase tracking-widest text-muted-foreground">Projects</span>
        <button
          disabled={readOnly}
          title={readOnly ? "Workspace is read-only" : "New project"}
          className="flex size-5 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-40"
          aria-label="New project"
        >
          <Plus className="size-4" />
        </button>
      </div>

      <nav className="flex-1 overflow-y-auto px-2 py-1">
        {projects.map((p) => {
          const selected = selectedProjectIds.includes(p.id)
          const count = activeCount(p.id)
          return (
            <div
              key={p.id}
              className={cn(
                "group mb-0.5 flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-sm transition-colors",
                selected
                  ? "bg-sidebar-accent text-foreground"
                  : "text-muted-foreground hover:bg-sidebar-accent/60 hover:text-foreground",
              )}
            >
              {/* Multi-select checkbox / active dot */}
              <button
                onClick={() => onToggleProject(p.id)}
                aria-label={`Toggle ${p.name} in selection`}
                aria-pressed={selected}
                className={cn(
                  "flex size-4 shrink-0 items-center justify-center rounded border transition-colors",
                  selected ? "border-primary bg-primary/20 text-primary" : "border-transparent",
                )}
              >
                {selected ? (
                  <Check className="size-3" />
                ) : (
                  <span className="size-2 rounded-full" style={{ backgroundColor: p.color }} aria-hidden />
                )}
              </button>

              {/* Focus this project (plain click) or extend with modifier key */}
              <button
                onClick={(e) => (e.metaKey || e.ctrlKey ? onToggleProject(p.id) : onSelectOnly(p.id))}
                className="flex min-w-0 flex-1 items-center gap-2 text-left"
              >
                <Folder className={cn("size-4 shrink-0", selected ? "text-foreground" : "text-muted-foreground")} />
                <span className="flex-1 truncate font-medium">{p.name}</span>
              </button>

              {count > 0 && (
                <span
                  className={cn(
                    "flex min-w-5 items-center justify-center rounded-full px-1.5 text-[11px] font-semibold tabular-nums",
                    selected ? "bg-primary/20 text-primary" : "bg-accent text-muted-foreground",
                  )}
                >
                  {count}
                </span>
              )}
            </div>
          )
        })}
      </nav>

      <div className="border-t border-border px-4 py-3 text-[11px] text-muted-foreground">
        {multi ? (
          <span>{selectedProjectIds.length} of {totalProjects} selected</span>
        ) : (
          <span>{totalProjects} projects · {totalActive} active</span>
        )}
      </div>
    </aside>
  )
}
