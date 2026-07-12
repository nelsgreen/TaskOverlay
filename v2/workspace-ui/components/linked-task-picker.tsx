"use client"

import { useEffect, useMemo, useState } from "react"
import { Bell, Link2, Plus, X } from "lucide-react"
import type { Project, Section, Status, Task } from "@/lib/types"
import { cn } from "@/lib/utils"
import { StatusBadge } from "./status-badge"

/**
 * ContextHUB Details -> LINKED TASKS. Replaces the old plain <select> (which
 * listed every task in the workspace, including other projects and already-
 * linked ones — selecting one of those silently failed against the Core
 * cross-project guard with no feedback). This shows only same-project,
 * not-yet-linked tasks with status/path context and a title+path search.
 */

const inputClass =
  "rounded-md border border-input bg-background px-2.5 py-1.5 text-[13px] text-foreground outline-none placeholder:text-muted-foreground/60 focus:border-primary/60 focus:ring-1 focus:ring-primary/40"

/** Same-project tasks not already linked. The picker's only eligibility rule. */
export function getEligibleTasks(tasks: Task[], projectId: string, linkedTaskIds: string[]): Task[] {
  const linked = new Set(linkedTaskIds)
  return tasks.filter((task) => task.projectId === projectId && !linked.has(task.id))
}

/** "Project / Section / Parent task" breadcrumb, skipping any missing segment. */
export function taskPath(task: Task, projects: Project[], sections: Section[], tasks: Task[]): string {
  const projectName = projects.find((p) => p.id === task.projectId)?.name
  const sectionName = sections.find((s) => s.id === task.sectionId)?.name
  const parentTitle = task.parentId ? tasks.find((t) => t.id === task.parentId)?.title : undefined
  return [projectName, sectionName, parentTitle].filter(Boolean).join(" / ") || "—"
}

/** Title-or-path search, case-insensitive. Empty query matches everything. */
export function matchesTaskQuery(task: Task, path: string, query: string): boolean {
  const q = query.trim().toLowerCase()
  if (!q) return true
  return task.title.toLowerCase().includes(q) || path.toLowerCase().includes(q)
}

/**
 * REMIND is reminder/attention metadata, not a task Status (TODO/FOCUS/WAIT/
 * DONE are the only statuses) — shown here as a small indicator instead of a
 * fake fifth status chip.
 */
function hasActiveReminder(task: Task): boolean {
  return task.reminder !== "none" || !!task.reminderDate
}

type StatusFilter = "all" | Status

const STATUS_FILTERS: { id: StatusFilter; label: string }[] = [
  { id: "all", label: "All" },
  { id: "TODO", label: "TODO" },
  { id: "FOCUS", label: "FOCUS" },
  { id: "WAIT", label: "WAIT" },
  { id: "DONE", label: "DONE" },
]

interface LinkedTasksFieldProps {
  projectId: string
  tasks: Task[]
  projects: Project[]
  sections: Section[]
  linkedTaskIds: string[]
  disabled: boolean
  onLink: (taskId: string) => void
  onUnlink: (taskId: string) => void
}

export function LinkedTasksField({
  projectId,
  tasks,
  projects,
  sections,
  linkedTaskIds,
  disabled,
  onLink,
  onUnlink,
}: LinkedTasksFieldProps) {
  const [pickerOpen, setPickerOpen] = useState(false)

  // Ids referencing a deleted task are already filtered out of the snapshot;
  // this filter is just defensive against a stale prop mid-transition.
  const linkedTasks = linkedTaskIds
    .map((id) => tasks.find((task) => task.id === id))
    .filter((task): task is Task => !!task)

  return (
    <div className="flex min-w-0 flex-col gap-1.5">
      <span className="text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">Linked tasks</span>
      {linkedTasks.length === 0 ? (
        <p className="text-[11px] text-muted-foreground">No linked tasks.</p>
      ) : (
        <div className="flex min-w-0 flex-col gap-1">
          {linkedTasks.map((task) => (
            <div
              key={task.id}
              className="flex min-w-0 items-start gap-2 rounded-md border border-border bg-card/60 px-2 py-1.5"
            >
              <Link2 className="mt-0.5 size-3.5 shrink-0 text-muted-foreground" />
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-1.5">
                  <span className="min-w-0 truncate text-[12px] text-foreground/90">{task.title || "(untitled)"}</span>
                  <StatusBadge status={task.status} />
                </div>
                <p className="truncate text-[10px] text-muted-foreground">{taskPath(task, projects, sections, tasks)}</p>
              </div>
              {!disabled && (
                <button
                  type="button"
                  onClick={() => onUnlink(task.id)}
                  aria-label={`Unlink task: ${task.title}`}
                  className="shrink-0 rounded p-0.5 text-muted-foreground transition-colors hover:text-destructive"
                >
                  <X className="size-3" />
                </button>
              )}
            </div>
          ))}
        </div>
      )}
      {!disabled && (
        <button
          type="button"
          onClick={() => setPickerOpen(true)}
          className="flex items-center gap-1 self-start rounded border border-border px-2 py-1 text-[11px] font-medium text-foreground transition-colors hover:bg-accent"
        >
          <Plus className="size-3" />
          Link task
        </button>
      )}
      {pickerOpen && (
        <LinkedTaskPickerModal
          projectId={projectId}
          tasks={tasks}
          projects={projects}
          sections={sections}
          linkedTaskIds={linkedTaskIds}
          onLink={onLink}
          onClose={() => setPickerOpen(false)}
        />
      )}
    </div>
  )
}

function LinkedTaskPickerModal({
  projectId,
  tasks,
  projects,
  sections,
  linkedTaskIds,
  onLink,
  onClose,
}: {
  projectId: string
  tasks: Task[]
  projects: Project[]
  sections: Section[]
  linkedTaskIds: string[]
  onLink: (taskId: string) => void
  onClose: () => void
}) {
  const [search, setSearch] = useState("")
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all")

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose() }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [onClose])

  const eligible = useMemo(
    () => getEligibleTasks(tasks, projectId, linkedTaskIds),
    [tasks, projectId, linkedTaskIds],
  )

  const rows = useMemo(() => {
    return eligible
      .map((task) => ({ task, path: taskPath(task, projects, sections, tasks) }))
      .filter(({ task, path }) =>
        (statusFilter === "all" || task.status === statusFilter) &&
        matchesTaskQuery(task, path, search))
  }, [eligible, projects, sections, tasks, statusFilter, search])

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-background/70 p-6 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      onClick={onClose}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="flex max-h-[70vh] w-full max-w-md flex-col overflow-hidden rounded-xl border border-border bg-popover shadow-2xl shadow-black/50"
      >
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <div className="flex flex-col">
            <span className="text-sm font-semibold text-foreground">Link task</span>
            <span className="text-[11px] text-muted-foreground">Same-project tasks only</span>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="flex size-7 items-center justify-center rounded text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
          >
            <X className="size-4" />
          </button>
        </div>

        <div className="space-y-2 border-b border-border px-4 py-2.5">
          <input
            autoFocus
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by title or path…"
            className={cn(inputClass, "w-full")}
          />
          <div className="flex flex-wrap gap-1">
            {STATUS_FILTERS.map((filter) => (
              <button
                key={filter.id}
                type="button"
                onClick={() => setStatusFilter(filter.id)}
                className={cn(
                  "rounded-full border px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide transition-colors",
                  statusFilter === filter.id
                    ? "border-primary/60 bg-primary/10 text-primary"
                    : "border-border text-muted-foreground hover:bg-accent",
                )}
              >
                {filter.label}
              </button>
            ))}
          </div>
        </div>

        <div className="flex-1 overflow-y-auto">
          {rows.length === 0 ? (
            <p className="px-4 py-6 text-center text-[12px] text-muted-foreground">
              {eligible.length === 0 ? "No eligible tasks in this project." : "Try another search."}
            </p>
          ) : (
            rows.map(({ task, path }) => (
              <button
                key={task.id}
                type="button"
                onClick={() => onLink(task.id)}
                className="flex w-full items-start gap-2 border-b border-border/60 px-4 py-2.5 text-left transition-colors hover:bg-accent/40"
              >
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <span className="min-w-0 truncate text-[13px] text-foreground/90">{task.title || "(untitled)"}</span>
                    <StatusBadge status={task.status} />
                    {hasActiveReminder(task) && (
                      <Bell className="size-3 shrink-0 text-status-remind" aria-label="Has a reminder" />
                    )}
                  </div>
                  <p className="truncate text-[11px] text-muted-foreground">{path}</p>
                </div>
                <Plus className="size-3.5 shrink-0 text-muted-foreground" aria-hidden />
              </button>
            ))
          )}
        </div>
      </div>
    </div>
  )
}
