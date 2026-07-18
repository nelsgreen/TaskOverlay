"use client"

import { useMemo, useState } from "react"
import { Bell, Flag, Hourglass, Layers, Plus, X } from "lucide-react"
import type { Project, Section, Task, WorkstreamFilter, WorkstreamState } from "@/lib/types"
import { cn } from "@/lib/utils"
import { statusConfig } from "./status-badge"

// A workstream is a top-level section (group) under a project. The bridge
// snapshot also contains synthetic "Project root" sections that hold tasks
// created directly under the project — those are not workstreams.
export function isRootSection(section: Section): boolean {
  return section.id.startsWith("project:") && section.id.endsWith(":root")
}

// ─── Rollups (computed from real tasks — no stored workstream metadata) ─────

export interface WorkstreamRollup {
  section: Section
  project: Project
  tasks: Task[]
  state: WorkstreamState | null
  counts: {
    total: number
    todo: number
    focus: number
    wait: number
    remind: number
    done: number
  }
  nextReminder: string | null
  nextDeadline: string | null
  waitingSummary: string | null
}

function taskHasReminder(t: Task): boolean {
  return !!(t.remindAtUtc || t.reminderDate || (t.reminder && t.reminder !== "none"))
}

function reminderInstant(t: Task): Date | null {
  if (t.remindAtUtc) return new Date(t.remindAtUtc)
  if (t.reminderDate) return new Date(`${t.reminderDate}T${t.reminderTime ?? "09:00"}`)
  return null
}

function deadlineInstant(t: Task): Date | null {
  if (t.deadlineAtUtc) return new Date(t.deadlineAtUtc)
  if (t.deadlineDate) return new Date(`${t.deadlineDate}T${t.deadlineTime ?? "23:59"}`)
  return null
}

function formatWhen(d: Date): string {
  const now = new Date()
  const time = d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })
  if (d.toDateString() === now.toDateString()) return `Today ${time}`
  const tomorrow = new Date(now)
  tomorrow.setDate(now.getDate() + 1)
  if (d.toDateString() === tomorrow.toDateString()) return `Tomorrow ${time}`
  return `${d.toLocaleDateString("en-GB", { day: "numeric", month: "short" })} ${time}`
}

function nearest(tasks: Task[], instant: (t: Task) => Date | null): string | null {
  let best: Date | null = null
  for (const t of tasks) {
    if (t.status === "DONE") continue
    const at = instant(t)
    if (at && !Number.isNaN(at.getTime()) && (!best || at < best)) best = at
  }
  return best ? formatWhen(best) : null
}

/** Derived activity state; null when the workstream has no tasks. */
export function deriveWorkstreamState(tasks: Task[]): WorkstreamState | null {
  if (tasks.length === 0) return null
  if (tasks.some((t) => t.status === "FOCUS")) return "active"
  if (tasks.some((t) => t.status === "WAIT")) return "waiting"
  if (tasks.every((t) => t.status === "DONE")) return "done"
  return "todo"
}

export function buildWorkstreamRollup(
  section: Section,
  project: Project,
  tasks: Task[],
): WorkstreamRollup {
  const counts = {
    total: tasks.length,
    todo: tasks.filter((t) => t.status === "TODO").length,
    focus: tasks.filter((t) => t.status === "FOCUS").length,
    wait: tasks.filter((t) => t.status === "WAIT").length,
    remind: tasks.filter((t) => t.status !== "DONE" && taskHasReminder(t)).length,
    done: tasks.filter((t) => t.status === "DONE").length,
  }
  const waitingNames = [...new Set(
    tasks
      .filter((t) => t.status === "WAIT" && t.waitingFor?.trim())
      .map((t) => t.waitingFor!.trim()),
  )]
  return {
    section,
    project,
    tasks,
    state: deriveWorkstreamState(tasks),
    counts,
    nextReminder: nearest(tasks, reminderInstant),
    nextDeadline: nearest(tasks, deadlineInstant),
    waitingSummary: waitingNames.length > 0 ? waitingNames.join(", ") : null,
  }
}

function taskMatches(t: Task, q: string): boolean {
  return t.title.toLowerCase().includes(q) ||
    (t.notes?.toLowerCase().includes(q) ?? false) ||
    (t.waitingFor?.toLowerCase().includes(q) ?? false)
}

function rollupMatches(rollup: WorkstreamRollup, q: string): boolean {
  return rollup.section.name.toLowerCase().includes(q) ||
    rollup.project.name.toLowerCase().includes(q) ||
    rollup.tasks.some((t) => taskMatches(t, q))
}

// ─── State badge config (v0 workstream-card stateConfig, limited to states the
//     real model can derive; "todo" reuses the app's own status token) ───────

const stateConfig: Record<WorkstreamState, { label: string; dot: string; text: string; bg: string }> = {
  active:  { label: "Active",  dot: "bg-status-focus", text: "text-status-focus", bg: "bg-status-focus/10" },
  waiting: { label: "Waiting", dot: "bg-status-wait",  text: "text-status-wait",  bg: "bg-status-wait/10" },
  todo:    { label: "Todo",    dot: "bg-status-todo",  text: "text-status-todo",  bg: "bg-status-todo/10" },
  done:    { label: "Done",    dot: "bg-status-done",  text: "text-status-done",  bg: "bg-muted" },
}

// ─── Board ───────────────────────────────────────────────────────────────────

interface WorkstreamsViewProps {
  projects: Project[]
  sections: Section[]
  tasks: Task[]
  selectedProjectIds: string[]
  wsFilter: WorkstreamFilter
  search: string
  selectedWorkstreamId: string | null
  onSelectWorkstream: (id: string) => void
  adding: boolean
  onAddingChange: (adding: boolean) => void
  onCreateWorkstream: (title: string) => void
  /** false when scope has multiple projects or the bridge is read-only */
  canCreate: boolean
  /** non-debug reason shown when creation is unavailable */
  createHint: string | null
  /** project that will receive a new workstream (single-project scope) */
  addProjectName: string
}

export function WorkstreamsView({
  projects,
  sections,
  tasks,
  selectedProjectIds,
  wsFilter,
  search,
  selectedWorkstreamId,
  onSelectWorkstream,
  adding,
  onAddingChange,
  onCreateWorkstream,
  canCreate,
  createHint,
  addProjectName,
}: WorkstreamsViewProps) {
  const q = search.trim().toLowerCase()

  const groups = useMemo(() => {
    const scopedProjects = projects.filter((p) => selectedProjectIds.includes(p.id))
    return scopedProjects.map((project) => {
      const rollups = sections
        .filter((s) => s.projectId === project.id && !isRootSection(s))
        .map((s) => buildWorkstreamRollup(s, project, tasks.filter((t) => t.sectionId === s.id)))
      const visible = rollups
        .filter((r) => !q || rollupMatches(r, q))
        .filter((r) => wsFilter === "all" || r.state === wsFilter)
      return { project, visible, total: rollups.length }
    })
  }, [projects, sections, tasks, selectedProjectIds, wsFilter, q])

  const multi = groups.length > 1
  const totalCount = groups.reduce((sum, g) => sum + g.total, 0)
  const visibleCount = groups.reduce((sum, g) => sum + g.visible.length, 0)

  return (
    <div className="flex h-full min-h-0 flex-col overflow-hidden">
      <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">
        {adding && (
          <AddWorkstreamRow
            projectName={addProjectName}
            onCreate={onCreateWorkstream}
            onCancel={() => onAddingChange(false)}
          />
        )}

        {visibleCount === 0 ? (
          <div className="flex flex-col items-center gap-3 py-16 text-center">
            <Layers className="size-8 text-muted-foreground/40" />
            <p className="text-sm font-medium text-muted-foreground">
              {totalCount === 0
                ? multi
                  ? "No workstreams in selected projects."
                  : "No workstreams yet."
                : q
                  ? "No workstreams match this search."
                  : "No workstreams match this filter."}
            </p>
            {totalCount === 0 && (
              canCreate ? (
                !adding && (
                  <button
                    type="button"
                    onClick={() => onAddingChange(true)}
                    className="flex h-7 items-center gap-1.5 rounded-md bg-primary px-3 text-[11px] font-semibold text-primary-foreground transition-colors hover:bg-primary/90"
                  >
                    <Plus className="size-3.5" />
                    Add workstream
                  </button>
                )
              ) : (
                createHint && <p className="text-xs text-muted-foreground">{createHint}</p>
              )
            )}
          </div>
        ) : (
          groups.map(({ project, visible }) => {
            if (visible.length === 0) return null
            return (
              <div key={project.id} className="mb-6">
                {multi && (
                  <div className="mb-2 flex items-center gap-2">
                    <span className="size-1.5 rounded-full" style={{ backgroundColor: project.color }} />
                    <span className="text-[11px] font-semibold uppercase tracking-wide text-foreground">
                      {project.name}
                    </span>
                    <span className="font-mono text-[11px] text-muted-foreground">{visible.length}</span>
                    <div className="h-px flex-1 bg-border" />
                  </div>
                )}
                <div className="grid grid-cols-1 gap-2 xl:grid-cols-2">
                  {visible.map((rollup) => (
                    <WorkstreamCard
                      key={rollup.section.id}
                      rollup={rollup}
                      selected={selectedWorkstreamId === rollup.section.id}
                      onSelect={() => onSelectWorkstream(rollup.section.id)}
                    />
                  ))}
                </div>
              </div>
            )
          })
        )}
      </div>
    </div>
  )
}

function AddWorkstreamRow({
  projectName,
  onCreate,
  onCancel,
}: {
  projectName: string
  onCreate: (title: string) => void
  onCancel: () => void
}) {
  const [title, setTitle] = useState("")
  const submit = () => {
    const trimmed = title.trim()
    if (trimmed) onCreate(trimmed)
  }
  return (
    <div className="mb-4 flex items-center gap-2 rounded-lg border border-border bg-card/40 px-3 py-2">
      <Layers className="size-3.5 shrink-0 text-muted-foreground" />
      <input
        autoFocus
        value={title}
        onChange={(e) => setTitle(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter") submit()
          else if (e.key === "Escape") onCancel()
        }}
        placeholder={`New workstream in ${projectName}…`}
        className="h-7 flex-1 rounded-md border border-input bg-background px-2.5 text-xs text-foreground outline-none transition-colors focus:border-primary/60 focus:ring-1 focus:ring-primary/20"
      />
      <button
        type="button"
        onClick={submit}
        disabled={!title.trim()}
        className="flex h-7 items-center gap-1 rounded-md bg-primary px-2.5 text-[11px] font-semibold text-primary-foreground transition-colors hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-50"
      >
        <Plus className="size-3" />
        Create
      </button>
      <button
        type="button"
        onClick={onCancel}
        aria-label="Cancel"
        className="flex size-7 items-center justify-center rounded-md border border-border text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
      >
        <X className="size-3.5" />
      </button>
    </div>
  )
}

// ─── Card (v0 workstream-card.tsx layout; data from real task rollups) ──────

function WorkstreamCard({
  rollup,
  selected,
  onSelect,
}: {
  rollup: WorkstreamRollup
  selected: boolean
  onSelect: () => void
}) {
  const { section, project, counts, state, waitingSummary, nextReminder, nextDeadline } = rollup
  const s = state ? stateConfig[state] : null

  return (
    <button
      type="button"
      onClick={onSelect}
      className={cn(
        "group relative w-full overflow-hidden rounded-xl border text-left transition-colors",
        selected
          ? "border-row-selected-border bg-row-selected"
          : "border-border bg-card hover:border-border hover:bg-accent/30",
      )}
    >
      {/* Project color stripe */}
      <div
        className="absolute inset-y-0 left-0 w-0.5 rounded-l-xl"
        style={{ backgroundColor: project.color }}
      />

      <div className="px-3 py-2.5 pl-4">
        {/* Top row: project · state badge */}
        <div className="flex items-center gap-1.5">
          <span className="size-1.5 shrink-0 rounded-full" style={{ backgroundColor: project.color }} />
          <span className="text-[11px] font-medium text-muted-foreground">{project.name}</span>
          {s ? (
            <span
              className={cn(
                "ml-auto flex items-center gap-1 rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide",
                s.bg, s.text,
              )}
            >
              <span className={cn("size-1.5 rounded-full", s.dot)} />
              {s.label}
            </span>
          ) : (
            <span className="ml-auto rounded bg-accent px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide text-muted-foreground">
              Empty
            </span>
          )}
        </div>

        {/* Title */}
        <p className="mt-1 text-[13px] font-semibold leading-snug text-foreground">{section.name}</p>

        {/* Waiting-for summary */}
        {waitingSummary && (
          <div className="mt-1.5 flex items-start gap-1.5">
            <Hourglass className="mt-0.5 size-3 shrink-0 text-status-wait" />
            <span className="line-clamp-1 text-[11px] leading-relaxed text-status-wait/90">
              <span className="font-semibold">Waiting for: </span>
              {waitingSummary}
            </span>
          </div>
        )}

        {/* Nearest reminder / deadline */}
        {nextReminder && (
          <div className="mt-1 flex items-center gap-1.5">
            <Bell className="size-3 shrink-0 text-status-remind" />
            <span className="text-[11px] text-status-remind/90">{nextReminder}</span>
          </div>
        )}
        {nextDeadline && (
          <div className="mt-1 flex items-center gap-1.5">
            <Flag className="size-3 shrink-0 text-status-deadline" />
            <span className="text-[11px] text-status-deadline/90">{nextDeadline}</span>
          </div>
        )}

        {/* Footer: task counts + progress */}
        <div className="mt-2 flex items-center gap-3 border-t border-border/60 pt-1.5">
          <TaskCount label="FOCUS" value={counts.focus} color="text-status-focus" />
          <TaskCount label="WAIT" value={counts.wait} color="text-status-wait" />
          <TaskCount label="TODO" value={counts.todo} color="text-status-todo" />
          <TaskCount label="REMIND" value={counts.remind} color="text-status-remind" />
          <TaskCount label="DONE" value={counts.done} color="text-status-done" />
          <span className="ml-auto font-mono text-[10px] text-muted-foreground">
            {counts.done}/{counts.total} done
          </span>
        </div>
      </div>
    </button>
  )
}

function TaskCount({
  label,
  value,
  color,
}: {
  label: string
  value: number
  color: string
}) {
  if (value === 0) return null
  return (
    <span className={cn("flex items-center gap-1 font-mono text-[10px] font-semibold", color)}>
      <span>{label}</span>
      <span className="text-foreground">{value}</span>
    </span>
  )
}

// ─── Detail panel (v0 workstream-detail-panel.tsx structure; mock-only fields
//     like goal/next action/activity log are omitted — no data model yet) ────

const statusOrder = ["FOCUS", "WAIT", "TODO", "DONE"] as const

interface WorkstreamDetailPanelProps {
  section: Section
  project: Project
  /** all tasks in this workstream/section */
  tasks: Task[]
  search: string
  readOnly: boolean
  onSelectTask: (taskId: string) => void
  onAddTask: () => void
}

export function WorkstreamDetailPanel({
  section,
  project,
  tasks,
  search,
  readOnly,
  onSelectTask,
  onAddTask,
}: WorkstreamDetailPanelProps) {
  const rollup = buildWorkstreamRollup(section, project, tasks)
  const s = rollup.state ? stateConfig[rollup.state] : null

  const q = search.trim().toLowerCase()
  const nameMatches = !q ||
    section.name.toLowerCase().includes(q) ||
    project.name.toLowerCase().includes(q)
  const visibleTasks = nameMatches ? tasks : tasks.filter((t) => taskMatches(t, q))

  const grouped = statusOrder.reduce<Record<string, Task[]>>((acc, st) => {
    const group = visibleTasks.filter((t) => t.status === st)
    if (group.length) acc[st] = group
    return acc
  }, {})

  return (
    <aside className="flex h-full w-full flex-col overflow-hidden border-l border-border bg-sidebar">
      {/* Header */}
      <div className="shrink-0 border-b border-border px-4 py-3">
        <div className="flex items-center gap-2">
          <span className="rounded bg-muted px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
            Workstream
          </span>
          {s && (
            <span
              className={cn(
                "ml-auto rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide",
                s.bg, s.text,
              )}
            >
              {s.label}
            </span>
          )}
        </div>
        <h2 className="mt-1.5 text-sm font-semibold leading-snug text-foreground">{section.name}</h2>
        <div className="mt-0.5 flex items-center gap-1.5">
          <span className="size-1.5 rounded-full" style={{ backgroundColor: project.color }} />
          <span className="text-[11px] text-muted-foreground">{project.name}</span>
          <span className="ml-auto font-mono text-[10px] text-muted-foreground">
            {rollup.counts.done}/{rollup.counts.total} done
          </span>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto">
        {/* Waiting for */}
        {rollup.waitingSummary && (
          <PanelSection label="Waiting for">
            <div className="flex items-start gap-2">
              <Hourglass className="mt-0.5 size-3.5 shrink-0 text-status-wait" />
              <p className="text-[13px] leading-relaxed text-status-wait/90">{rollup.waitingSummary}</p>
            </div>
          </PanelSection>
        )}

        {/* Next reminder / deadline */}
        {(rollup.nextReminder || rollup.nextDeadline) && (
          <PanelSection label="Coming up">
            <div className="space-y-1.5">
              {rollup.nextReminder && (
                <div className="flex items-center gap-2">
                  <Bell className="size-3.5 shrink-0 text-status-remind" />
                  <span className="text-[12px] text-status-remind/90">{rollup.nextReminder}</span>
                </div>
              )}
              {rollup.nextDeadline && (
                <div className="flex items-center gap-2">
                  <Flag className="size-3.5 shrink-0 text-status-deadline" />
                  <span className="text-[12px] text-status-deadline/90">{rollup.nextDeadline}</span>
                </div>
              )}
            </div>
          </PanelSection>
        )}

        {/* Tasks */}
        <PanelSection
          label="Tasks"
          action={
            <button
              type="button"
              onClick={onAddTask}
              disabled={readOnly}
              title={readOnly ? "Read-only: connect to add tasks" : undefined}
              className="flex h-6 items-center gap-1 rounded-md bg-primary px-2 text-[10px] font-semibold text-primary-foreground transition-colors hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-50"
            >
              <Plus className="size-3" />
              Task
            </button>
          }
        >
          {tasks.length === 0 ? (
            <p className="py-2 text-[12px] text-muted-foreground">No tasks in this workstream.</p>
          ) : visibleTasks.length === 0 ? (
            <p className="py-2 text-[12px] text-muted-foreground">No tasks match this search.</p>
          ) : (
            <div className="space-y-2.5">
              {statusOrder.map((st) => {
                const group = grouped[st]
                if (!group) return null
                const c = statusConfig[st]
                return (
                  <div key={st}>
                    <div className="mb-1 flex items-center gap-1.5">
                      <span className={cn("size-1.5 rounded-full", c.dot)} />
                      <span className={cn("font-mono text-[10px] font-semibold uppercase tracking-wide", c.text)}>
                        {c.label}
                      </span>
                      <span className="font-mono text-[10px] text-muted-foreground">{group.length}</span>
                    </div>
                    <div className="space-y-0.5 pl-3">
                      {group.map((t) => (
                        <TaskRow key={t.id} task={t} onSelect={() => onSelectTask(t.id)} />
                      ))}
                    </div>
                  </div>
                )
              })}
            </div>
          )}
        </PanelSection>
      </div>
    </aside>
  )
}

function TaskRow({ task, onSelect }: { task: Task; onSelect: () => void }) {
  const reminderAt = task.status !== "DONE" && taskHasReminder(task) ? reminderInstant(task) : null
  const deadlineAt = task.status !== "DONE" ? deadlineInstant(task) : null
  const waiting = task.status === "WAIT" ? task.waitingFor?.trim() : null
  const hasMeta = !!(waiting || reminderAt || deadlineAt)

  return (
    <button
      type="button"
      onClick={onSelect}
      className="block w-full rounded px-1 py-0.5 text-left transition-colors hover:bg-accent/50"
    >
      <span className="block truncate text-[12px] text-foreground">{task.title}</span>
      {hasMeta && (
        <span className="mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-0.5">
          {waiting && (
            <span className="flex items-center gap-1 text-[10px] text-status-wait/90">
              <Hourglass className="size-2.5 shrink-0" />
              <span className="truncate">{waiting}</span>
            </span>
          )}
          {reminderAt && (
            <span className="flex items-center gap-1 text-[10px] text-status-remind/90">
              <Bell className="size-2.5 shrink-0" />
              {formatWhen(reminderAt)}
            </span>
          )}
          {deadlineAt && (
            <span className="flex items-center gap-1 text-[10px] text-status-deadline/90">
              <Flag className="size-2.5 shrink-0" />
              {formatWhen(deadlineAt)}
            </span>
          )}
        </span>
      )}
    </button>
  )
}

function PanelSection({
  label,
  action,
  children,
}: {
  label: string
  action?: React.ReactNode
  children: React.ReactNode
}) {
  return (
    <div className="border-b border-border px-4 py-3">
      <div className="mb-1.5 flex items-center justify-between">
        <p className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">{label}</p>
        {action}
      </div>
      {children}
    </div>
  )
}
