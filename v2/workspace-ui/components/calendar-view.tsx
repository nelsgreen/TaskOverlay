"use client"

import { useEffect, useMemo, useState } from "react"
import { Bell, CalendarPlus, Flag, Plus, Video, X } from "lucide-react"
import type { Project, Section, Status, Task } from "@/lib/types"
import { cn } from "@/lib/utils"
import {
  formatDayNumber,
  formatWeekdayShort,
  isoFromLocalDateTime,
  localSlotFromIso,
  mondayOfWeekKey,
  todayKey,
  weekDayKeys,
} from "@/lib/calendar-date"

// ─── Work-hours grid constants (from v0 reference) ──────────────────────────
const START_HOUR = 9
const END_HOUR = 18
const START_MIN = START_HOUR * 60
const END_MIN = END_HOUR * 60
const PX_PER_MIN = 1.15
const GRID_HEIGHT = Math.round((END_MIN - START_MIN) * PX_PER_MIN)
const MIN_BLOCK_MINUTES = 20
const DEFAULT_DURATION_MIN = 60
const RAIL_WIDTH = 60
const POOL_MAX_VISIBLE = 6

type MarkerKind = "REMIND" | "DEADLINE"

// Scheduling presets applied to the currently selected date.
const SCHEDULE_PRESETS = [
  { hour: 9, minute: 0, duration: 30, label: "09:00", sub: "30m" },
  { hour: 10, minute: 0, duration: 60, label: "10:00", sub: "1h" },
  { hour: 14, minute: 0, duration: 60, label: "14:00", sub: "1h" },
  { hour: 16, minute: 0, duration: 30, label: "16:00", sub: "30m" },
]

// ─── Time helpers ────────────────────────────────────────────────────────────
function fmtMin(min: number): string {
  const h = Math.floor(min / 60)
  const m = min % 60
  return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}`
}
function fmtDur(min: number): string {
  const h = Math.floor(min / 60)
  const m = min % 60
  if (h && m) return `${h}h ${m}m`
  if (h) return `${h}h`
  return `${m}m`
}
const clamp = (v: number, lo: number, hi: number) => Math.max(lo, Math.min(hi, v))

// ─── Models ────────────────────────────────────────────────────────────────────
interface Marker {
  key: string
  kind: MarkerKind
  title: string
  projectId: string
  min: number
  taskId: string
  selected: boolean
}

interface PlannedBlock {
  key: string
  taskId: string
  title: string
  projectId: string
  sectionId?: string
  status: Status
  startMin: number
  endMin: number
  selected: boolean
}

const MARKER_STYLES: Record<MarkerKind, {
  Icon: typeof Bell
  text: string
  base: string
  selected: string
}> = {
  REMIND: {
    Icon: Bell,
    text: "text-status-remind",
    base: "border-status-remind/40 bg-status-remind/10 hover:bg-status-remind/20",
    selected: "border-status-remind bg-status-remind/20 ring-2 ring-status-remind/40",
  },
  DEADLINE: {
    Icon: Flag,
    text: "text-status-deadline",
    base: "border-status-deadline/40 bg-status-deadline/10 hover:bg-status-deadline/20",
    selected: "border-status-deadline bg-status-deadline/20 ring-2 ring-status-deadline/40",
  },
}

const STATUS_STYLES: Record<Status, { bg: string; text: string; label: string }> = {
  FOCUS: { bg: "bg-status-focus/15", text: "text-status-focus", label: "Focus" },
  WAIT: { bg: "bg-status-wait/15", text: "text-status-wait", label: "Wait" },
  TODO: { bg: "bg-muted/40", text: "text-muted-foreground", label: "Todo" },
  DONE: { bg: "bg-status-done/15", text: "text-status-done", label: "Done" },
}

// Assign side-by-side columns to time intervals that overlap (ported from v0 block layout).
function layoutColumns<T extends { startMin: number; endMin: number }>(
  items: T[],
): (T & { col: number; cols: number })[] {
  const sorted = [...items].sort((a, b) => a.startMin - b.startMin || a.endMin - b.endMin)
  const out: (T & { col: number; cols: number })[] = []
  let cluster: (T & { col: number })[] = []
  let clusterEnd = -1

  const flush = () => {
    const cols = cluster.reduce((mx, b) => Math.max(mx, b.col + 1), 0)
    cluster.forEach((b) => out.push({ ...b, cols }))
    cluster = []
    clusterEnd = -1
  }

  for (const b of sorted) {
    if (cluster.length && b.startMin >= clusterEnd) flush()
    const used = new Set(cluster.filter((c) => c.endMin > b.startMin).map((c) => c.col))
    let col = 0
    while (used.has(col)) col++
    cluster.push({ ...b, col })
    clusterEnd = Math.max(clusterEnd < 0 ? b.endMin : clusterEnd, b.endMin)
  }
  if (cluster.length) flush()
  return out
}

// ─── Props ──────────────────────────────────────────────────────────────────
interface CalendarViewProps {
  viewMode: "day" | "week"
  selectedDate: string
  projects: Project[]
  sections: Section[]
  tasks: Task[]
  selectedProjectIds: string[]
  selectedTaskId: string | null
  showDone: boolean
  canSchedule: boolean
  onSelectTask: (taskId: string) => void
  onPickDay: (dateKey: string) => void
  onSchedule: (taskId: string, plannedStartAtUtc: string, plannedDurationMinutes: number) => void
  onClearPlanned: (taskId: string) => void
}

export function CalendarView({
  viewMode,
  selectedDate,
  projects,
  sections,
  tasks,
  selectedProjectIds,
  selectedTaskId,
  showDone,
  canSchedule,
  onSelectTask,
  onPickDay,
  onSchedule,
  onClearPlanned,
}: CalendarViewProps) {
  const today = todayKey()

  const [nowMin, setNowMin] = useState(() => {
    const n = new Date()
    return n.getHours() * 60 + n.getMinutes()
  })
  useEffect(() => {
    const id = setInterval(() => {
      const n = new Date()
      setNowMin(n.getHours() * 60 + n.getMinutes())
    }, 60_000)
    return () => clearInterval(id)
  }, [])

  const [showAllPool, setShowAllPool] = useState(false)
  const [planMenuTaskId, setPlanMenuTaskId] = useState<string | null>(null)

  const projectMap = useMemo(() => new Map(projects.map((p) => [p.id, p])), [projects])
  const sectionMap = useMemo(() => new Map(sections.map((s) => [s.id, s])), [sections])
  const allSelected = selectedProjectIds.length === projects.length
  const inScope = (projectId: string) => allSelected || selectedProjectIds.includes(projectId)

  // REMIND / DEADLINE markers for a date, from real task data.
  const buildMarkers = (dateKey: string): Marker[] => {
    const markers: Marker[] = []
    tasks.forEach((t) => {
      if (!inScope(t.projectId)) return
      if (t.status === "DONE" && !showDone) return
      if (t.reminderDate === dateKey && t.reminderTime) {
        markers.push({
          key: `rem-${t.id}`, kind: "REMIND", title: t.title, projectId: t.projectId,
          min: toMinFromHm(t.reminderTime), taskId: t.id, selected: t.id === selectedTaskId,
        })
      }
      if (t.deadlineDate === dateKey) {
        markers.push({
          key: `dl-${t.id}`, kind: "DEADLINE", title: t.title, projectId: t.projectId,
          min: t.deadlineTime ? toMinFromHm(t.deadlineTime) : END_MIN, taskId: t.id,
          selected: t.id === selectedTaskId,
        })
      }
    })
    return markers
  }

  // Planned work blocks for a date, from plannedStartAtUtc / plannedDurationMinutes.
  const buildPlannedBlocks = (dateKey: string): PlannedBlock[] => {
    const blocks: PlannedBlock[] = []
    tasks.forEach((t) => {
      if (!t.plannedStartAtUtc || !inScope(t.projectId)) return
      if (t.status === "DONE" && !showDone) return
      const slot = localSlotFromIso(t.plannedStartAtUtc)
      if (!slot || slot.dateKey !== dateKey) return
      const startMin = clamp(slot.minutes, START_MIN, END_MIN)
      const duration = t.plannedDurationMinutes ?? DEFAULT_DURATION_MIN
      blocks.push({
        key: `plan-${t.id}`, taskId: t.id, title: t.title, projectId: t.projectId,
        sectionId: t.sectionId, status: t.status,
        startMin, endMin: clamp(startMin + duration, START_MIN, END_MIN),
        selected: t.id === selectedTaskId,
      })
    })
    return blocks
  }

  // Planning pool: unscheduled TODO / FOCUS / WAIT tasks (DONE excluded) with no planned block.
  const unscheduled = useMemo(() => {
    const pool = tasks.filter(
      (t) =>
        (t.status === "TODO" || t.status === "FOCUS" || t.status === "WAIT") &&
        inScope(t.projectId) &&
        !t.plannedStartAtUtc,
    )
    return [
      ...pool.filter((t) => t.pinned || t.status === "FOCUS"),
      ...pool.filter((t) => !t.pinned && t.status !== "FOCUS"),
    ]
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tasks, allSelected, selectedProjectIds])

  const hours = Array.from({ length: END_HOUR - START_HOUR + 1 }, (_, i) => START_HOUR + i)
  const visiblePool = showAllPool ? unscheduled : unscheduled.slice(0, POOL_MAX_VISIBLE)
  const hiddenCount = unscheduled.length - POOL_MAX_VISIBLE

  const schedule = (taskId: string, preset: (typeof SCHEDULE_PRESETS)[number]) => {
    onSchedule(taskId, isoFromLocalDateTime(selectedDate, preset.hour, preset.minute), preset.duration)
    setPlanMenuTaskId(null)
  }

  return (
    <div className="flex h-full flex-col overflow-hidden bg-background">
      <div className="min-h-0 flex-1 overflow-y-auto">
        {viewMode === "day" ? (
          <DayGrid
            hours={hours}
            markers={buildMarkers(selectedDate)}
            blocks={buildPlannedBlocks(selectedDate)}
            isToday={selectedDate === today}
            nowMin={nowMin}
            projectMap={projectMap}
            sectionMap={sectionMap}
            canSchedule={canSchedule}
            onSelectTask={onSelectTask}
            onClearPlanned={onClearPlanned}
          />
        ) : (
          <WeekGrid
            hours={hours}
            selectedDate={selectedDate}
            today={today}
            nowMin={nowMin}
            buildMarkers={buildMarkers}
            buildPlannedBlocks={buildPlannedBlocks}
            projectMap={projectMap}
            onSelectTask={onSelectTask}
            onPickDay={onPickDay}
          />
        )}

        {/* ── Unscheduled separator ─────────────────────────────────────── */}
        <div className="mx-5 flex items-center gap-3 py-3">
          <div className="h-px flex-1 bg-border" />
          <span className="shrink-0 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
            Unscheduled
          </span>
          <div className="h-px flex-1 bg-border" />
        </div>

        {/* ── Planning pool ─────────────────────────────────────────────── */}
        <div className="px-5 pb-4">
          <div className="mb-2 flex items-center gap-2">
            <p className="text-[11px] font-semibold text-foreground">Planning pool</p>
            <span className="rounded-full bg-muted px-1.5 py-0.5 text-[10px] font-medium tabular-nums text-muted-foreground">
              {unscheduled.length}
            </span>
            {unscheduled.length > 0 && (
              <p className="text-[11px] text-muted-foreground">
                {canSchedule ? "Plan a task onto the selected day" : "Todo / Focus / Wait tasks with no planned time"}
              </p>
            )}
          </div>

          {unscheduled.length === 0 ? (
            <p className="py-3 text-[11px] text-muted-foreground">
              No unscheduled Todo, Focus or Wait tasks in scope.
            </p>
          ) : (
            <div className="flex items-start gap-2 overflow-x-auto pb-1">
              {visiblePool.map((t) => {
                const p = projectMap.get(t.projectId)
                const s = sectionMap.get(t.sectionId ?? "")
                const st = STATUS_STYLES[t.status] ?? STATUS_STYLES.TODO
                return (
                  <div
                    key={t.id}
                    role="button"
                    tabIndex={0}
                    onClick={() => onSelectTask(t.id)}
                    onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") onSelectTask(t.id) }}
                    className={cn(
                      "relative flex w-[210px] shrink-0 cursor-pointer flex-col gap-1.5 rounded-lg border bg-card p-2.5 text-left transition-colors",
                      t.id === selectedTaskId
                        ? "border-primary/50 bg-primary/8"
                        : "border-border hover:border-border/80 hover:bg-accent/40",
                    )}
                  >
                    <div className="flex items-center gap-1.5">
                      <span className={cn("rounded px-1 py-0.5 text-[9px] font-bold uppercase tracking-wide", st.bg, st.text)}>
                        {st.label}
                      </span>
                      {t.pinned && (
                        <span className="text-[9px] font-semibold uppercase tracking-wide text-muted-foreground">pinned</span>
                      )}
                      {canSchedule && (
                        <button
                          type="button"
                          onClick={(e) => { e.stopPropagation(); setPlanMenuTaskId((cur) => (cur === t.id ? null : t.id)) }}
                          title="Plan onto the selected day"
                          className="ml-auto flex items-center gap-1 rounded border border-border px-1.5 py-0.5 text-[9px] font-medium text-muted-foreground transition-colors hover:border-primary/50 hover:text-foreground"
                        >
                          <CalendarPlus className="size-3" />
                          Plan
                        </button>
                      )}
                    </div>
                    <p className="line-clamp-2 text-[12px] font-medium leading-snug text-foreground">{t.title}</p>
                    <div className="flex items-center gap-1 text-[10px] text-muted-foreground">
                      <span className="size-1.5 shrink-0 rounded-full" style={{ backgroundColor: p?.color }} />
                      <span className="truncate">{p?.name}{s ? ` / ${s.name}` : ""}</span>
                    </div>

                    {planMenuTaskId === t.id && (
                      <div
                        className="absolute left-2 right-2 top-full z-30 mt-1 rounded-lg border border-border bg-popover p-1.5 shadow-lg"
                        onClick={(e) => e.stopPropagation()}
                      >
                        <p className="px-1 pb-1 text-[9px] font-semibold uppercase tracking-wide text-muted-foreground">
                          Plan on this day
                        </p>
                        <div className="grid grid-cols-2 gap-1">
                          {SCHEDULE_PRESETS.map((preset) => (
                            <button
                              key={preset.label}
                              type="button"
                              onClick={() => schedule(t.id, preset)}
                              className="flex items-center justify-between gap-1 rounded border border-border px-1.5 py-1 text-[10px] font-medium text-muted-foreground transition-colors hover:border-primary/50 hover:bg-primary/10 hover:text-foreground"
                            >
                              <span className="tabular-nums text-foreground">{preset.label}</span>
                              <span>{preset.sub}</span>
                            </button>
                          ))}
                        </div>
                      </div>
                    )}
                  </div>
                )
              })}

              {!showAllPool && hiddenCount > 0 && (
                <button
                  type="button"
                  onClick={() => setShowAllPool(true)}
                  className="flex w-[120px] shrink-0 items-center justify-center gap-1.5 rounded-lg border border-dashed border-border px-3 py-2.5 text-xs font-medium text-muted-foreground transition-colors hover:text-foreground"
                >
                  <Plus className="size-3.5" />
                  {hiddenCount} more
                </button>
              )}
              {showAllPool && unscheduled.length > POOL_MAX_VISIBLE && (
                <button
                  type="button"
                  onClick={() => setShowAllPool(false)}
                  className="flex w-[100px] shrink-0 items-center justify-center rounded-lg border border-border bg-card px-3 py-2.5 text-xs font-medium text-muted-foreground transition-colors hover:text-foreground"
                >
                  Show less
                </button>
              )}
            </div>
          )}
        </div>

        <CalendarLegend />
      </div>
    </div>
  )
}

// v0 mock reminder/deadline times are "HH:MM"; bridge supplies the same shape.
function toMinFromHm(hhmm: string): number {
  const [h, m] = hhmm.split(":").map(Number)
  return (h || 0) * 60 + (m || 0)
}

// ─── Legend ──────────────────────────────────────────────────────────────────
function CalendarLegend() {
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 border-t border-border px-5 py-2.5 text-[11px]">
      <span className="flex items-center gap-1.5 text-foreground">
        <span className="h-3 w-1 rounded-full bg-primary" />
        PLANNED
      </span>
      <span className="flex items-center gap-1.5 text-status-remind">
        <Bell className="size-3" />
        REMIND
      </span>
      <span className="flex items-center gap-1.5 text-status-deadline">
        <Flag className="size-3" />
        DEADLINE
      </span>
      <span className="flex items-center gap-1.5 text-muted-foreground/70">
        <Video className="size-3 text-status-meet/70" />
        MEET — later, not available in this build yet
      </span>
    </div>
  )
}

// ─── Day grid ─────────────────────────────────────────────────────────────────
function DayGrid({
  hours, markers, blocks, isToday, nowMin, projectMap, sectionMap, canSchedule, onSelectTask, onClearPlanned,
}: {
  hours: number[]
  markers: Marker[]
  blocks: PlannedBlock[]
  isToday: boolean
  nowMin: number
  projectMap: Map<string, Project>
  sectionMap: Map<string, Section>
  canSchedule: boolean
  onSelectTask: (taskId: string) => void
  onClearPlanned: (taskId: string) => void
}) {
  const laidBlocks = layoutColumns(blocks)
  const showNow = isToday && nowMin >= START_MIN && nowMin <= END_MIN
  const isEmpty = markers.length === 0 && blocks.length === 0

  return (
    <div className="flex px-4 py-4">
      {/* Hour gutter */}
      <div className="relative w-12 shrink-0 select-none" style={{ height: GRID_HEIGHT }}>
        {hours.map((h) => (
          <div
            key={h}
            className="absolute -translate-y-1/2 pr-2 text-right text-[10px] font-medium tabular-nums text-muted-foreground"
            style={{ top: (h * 60 - START_MIN) * PX_PER_MIN, right: 0 }}
          >
            {String(h).padStart(2, "0")}:00
          </div>
        ))}
      </div>

      {/* Grid track */}
      <div className="relative flex-1 rounded-lg border border-border/40 bg-card/10" style={{ height: GRID_HEIGHT }}>
        {hours.map((h) => (
          <div key={h} className="absolute inset-x-0 border-t border-border/40" style={{ top: (h * 60 - START_MIN) * PX_PER_MIN }} />
        ))}
        {hours.slice(0, -1).map((h) => (
          <div key={`${h}h`} className="absolute inset-x-0 border-t border-dashed border-border/20" style={{ top: (h * 60 + 30 - START_MIN) * PX_PER_MIN }} />
        ))}

        {showNow && (
          <div className="pointer-events-none absolute inset-x-0 z-20 flex items-center" style={{ top: (nowMin - START_MIN) * PX_PER_MIN }}>
            <span className="-ml-1 size-2 rounded-full bg-now-marker" />
            <span className="h-px flex-1 bg-now-marker" />
            <span className="rounded-l bg-now-marker px-1 py-0.5 text-[9px] font-bold text-background">{fmtMin(nowMin)}</span>
          </div>
        )}

        {isEmpty && (
          <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center gap-1.5 text-center">
            <p className="text-sm font-medium text-muted-foreground">Nothing planned on this day</p>
            <p className="max-w-xs text-[11px] text-muted-foreground/70">
              {canSchedule
                ? "Use Plan in the Planning pool below to place a task here."
                : "Planned work, reminders and deadlines with a time land here."}
            </p>
          </div>
        )}

        {/* Planned work blocks (body, left of the marker rail) */}
        <div className="absolute inset-y-0 left-0" style={{ right: RAIL_WIDTH }}>
          {laidBlocks.map((b) => {
            const p = projectMap.get(b.projectId)
            const sec = b.sectionId ? sectionMap.get(b.sectionId) : undefined
            const st = STATUS_STYLES[b.status] ?? STATUS_STYLES.TODO
            const top = (b.startMin - START_MIN) * PX_PER_MIN
            const height = Math.max(MIN_BLOCK_MINUTES, b.endMin - b.startMin) * PX_PER_MIN
            const widthPct = 100 / b.cols
            const muted = b.status === "DONE"
            return (
              <button
                key={b.key}
                type="button"
                onClick={() => onSelectTask(b.taskId)}
                title={`${fmtMin(b.startMin)}–${fmtMin(b.endMin)} · ${b.title}`}
                className={cn(
                  "group absolute overflow-hidden rounded-lg border text-left transition-colors",
                  b.selected ? "border-border bg-card ring-2 ring-primary/50" : "border-border bg-card hover:bg-accent/50",
                  muted && "opacity-60",
                )}
                style={{
                  top, height,
                  left: `calc(${b.col * widthPct}% + ${b.col * 4 + 4}px)`,
                  width: `calc(${widthPct}% - 8px)`,
                  borderLeftWidth: 3,
                  borderLeftColor: p?.color,
                }}
              >
                <div className="flex h-full flex-col gap-0.5 px-2 py-1">
                  <div className="flex items-center gap-1.5">
                    <span className="text-[9px] font-semibold tabular-nums text-muted-foreground">
                      {fmtMin(b.startMin)}–{fmtMin(b.endMin)}
                    </span>
                    <span className="text-[9px] text-muted-foreground/70">· {fmtDur(b.endMin - b.startMin)}</span>
                  </div>
                  <span className={cn("font-semibold leading-snug text-foreground", height < 52 ? "line-clamp-1 text-[11px]" : "line-clamp-2 text-[12px]")}>
                    {b.title}
                  </span>
                  {height > 50 && (
                    <div className="mt-auto flex items-center gap-1.5">
                      <span className="size-1.5 shrink-0 rounded-full" style={{ backgroundColor: p?.color }} />
                      <span className="truncate text-[10px] text-muted-foreground">{p?.name}{sec ? ` / ${sec.name}` : ""}</span>
                      <span className={cn("ml-auto rounded px-1 py-0.5 text-[8px] font-bold uppercase tracking-wide", st.bg, st.text)}>{st.label}</span>
                    </div>
                  )}
                </div>
                {canSchedule && (
                  <span
                    role="button"
                    tabIndex={0}
                    onClick={(e) => { e.stopPropagation(); onClearPlanned(b.taskId) }}
                    onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.stopPropagation(); onClearPlanned(b.taskId) } }}
                    title="Clear planned work"
                    className="absolute right-1 top-1 flex size-4 items-center justify-center rounded bg-background/70 text-muted-foreground opacity-0 transition-opacity hover:text-foreground group-hover:opacity-100"
                  >
                    <X className="size-3" />
                  </span>
                )}
              </button>
            )
          })}
        </div>

        {/* REMIND / DEADLINE marker rail (right) */}
        <div className="absolute inset-y-0 right-0" style={{ width: RAIL_WIDTH }}>
          {markers.map((mk) => {
            const style = MARKER_STYLES[mk.kind]
            const Icon = style.Icon
            const top = (clamp(mk.min, START_MIN, END_MIN) - START_MIN) * PX_PER_MIN
            return (
              <button
                key={mk.key}
                type="button"
                onClick={() => onSelectTask(mk.taskId)}
                title={`${mk.kind} · ${fmtMin(mk.min)} · ${mk.title}`}
                className={cn(
                  "absolute right-0 flex -translate-y-1/2 items-center gap-1 rounded-full border px-1.5 py-0.5 transition-colors",
                  mk.selected ? style.selected : style.base,
                )}
                style={{ top }}
              >
                <Icon className={cn("size-2.5 shrink-0", style.text)} />
                <span className={cn("text-[9px] font-bold tabular-nums", style.text)}>{fmtMin(mk.min)}</span>
              </button>
            )
          })}
        </div>
      </div>
    </div>
  )
}

// ─── Week grid (Monday–Sunday, per PR #39 week logic) ──────────────────────────
function WeekGrid({
  hours, selectedDate, today, nowMin, buildMarkers, buildPlannedBlocks, projectMap, onSelectTask, onPickDay,
}: {
  hours: number[]
  selectedDate: string
  today: string
  nowMin: number
  buildMarkers: (dateKey: string) => Marker[]
  buildPlannedBlocks: (dateKey: string) => PlannedBlock[]
  projectMap: Map<string, Project>
  onSelectTask: (taskId: string) => void
  onPickDay: (dateKey: string) => void
}) {
  const monday = mondayOfWeekKey(selectedDate)
  const days = weekDayKeys(monday)
  const weekHasItems = days.some((d) => buildMarkers(d).length > 0 || buildPlannedBlocks(d).length > 0)

  return (
    <div className="relative flex px-4 py-4">
      <div className="relative w-12 shrink-0 select-none" style={{ height: GRID_HEIGHT, marginTop: 36 }}>
        {hours.map((h) => (
          <div key={h} className="absolute -translate-y-1/2 pr-2 text-right text-[10px] tabular-nums text-muted-foreground" style={{ top: (h * 60 - START_MIN) * PX_PER_MIN, right: 0 }}>
            {String(h).padStart(2, "0")}
          </div>
        ))}
      </div>

      {!weekHasItems && (
        <div className="pointer-events-none absolute inset-x-0 top-1/2 flex -translate-y-1/2 flex-col items-center gap-1.5 text-center" style={{ left: 64 }}>
          <p className="text-sm font-medium text-muted-foreground">Nothing planned this week</p>
          <p className="text-[11px] text-muted-foreground/70">Pick a day to plan work, or change the project scope.</p>
        </div>
      )}

      <div className="grid flex-1 grid-cols-7 gap-1.5">
        {days.map((iso) => {
          const blocks = layoutColumns(buildPlannedBlocks(iso))
          const markers = buildMarkers(iso)
          const isToday = iso === today
          const isSelected = iso === selectedDate
          const showNow = isToday && nowMin >= START_MIN && nowMin <= END_MIN
          return (
            <div key={iso} className="flex flex-col">
              <button
                type="button"
                onClick={() => onPickDay(iso)}
                className={cn(
                  "mb-1 flex h-8 items-center justify-center gap-1.5 rounded-md border text-[11px] font-semibold transition-colors",
                  isToday ? "border-primary/50 bg-primary/10 text-foreground"
                    : isSelected ? "border-primary/30 bg-primary/5 text-foreground"
                      : "border-border bg-card text-muted-foreground hover:text-foreground",
                )}
              >
                {formatWeekdayShort(iso)}
                <span className="tabular-nums">{formatDayNumber(iso)}</span>
              </button>
              <div className={cn("relative rounded-md border bg-card/20", isSelected ? "border-primary/30" : "border-border/50")} style={{ height: GRID_HEIGHT }}>
                {hours.map((h) => (
                  <div key={h} className="absolute inset-x-0 border-t border-border/30" style={{ top: (h * 60 - START_MIN) * PX_PER_MIN }} />
                ))}
                {showNow && (
                  <div className="pointer-events-none absolute inset-x-0 z-20 h-px bg-now-marker" style={{ top: (nowMin - START_MIN) * PX_PER_MIN }} />
                )}
                {/* Planned blocks */}
                {blocks.map((b) => {
                  const p = projectMap.get(b.projectId)
                  const top = (b.startMin - START_MIN) * PX_PER_MIN
                  const height = Math.max(MIN_BLOCK_MINUTES, b.endMin - b.startMin) * PX_PER_MIN
                  const widthPct = 100 / b.cols
                  return (
                    <button
                      key={b.key}
                      type="button"
                      onClick={() => onSelectTask(b.taskId)}
                      title={`${fmtMin(b.startMin)}–${fmtMin(b.endMin)} · ${b.title}`}
                      className={cn(
                        "absolute overflow-hidden rounded border bg-card px-1 py-0.5 text-left transition-colors",
                        b.selected ? "ring-1 ring-primary/50" : "hover:bg-accent/60",
                        b.status === "DONE" && "opacity-60",
                      )}
                      style={{
                        top, height,
                        left: `calc(${b.col * widthPct}% + ${b.col * 2}px)`,
                        width: `calc(${widthPct}% - 2px)`,
                        borderLeftWidth: 2, borderLeftColor: p?.color,
                      }}
                    >
                      <span className="block truncate text-[9px] font-semibold leading-tight text-foreground">{b.title}</span>
                      {height > 24 && (
                        <span className="block truncate text-[8px] tabular-nums text-muted-foreground">{fmtMin(b.startMin)}</span>
                      )}
                    </button>
                  )
                })}
                {/* REMIND / DEADLINE icons at column right edge */}
                {markers.map((mk) => {
                  const style = MARKER_STYLES[mk.kind]
                  const Icon = style.Icon
                  const top = (clamp(mk.min, START_MIN, END_MIN) - START_MIN) * PX_PER_MIN
                  return (
                    <button
                      key={mk.key}
                      type="button"
                      onClick={() => onSelectTask(mk.taskId)}
                      title={`${mk.kind} · ${fmtMin(mk.min)} · ${mk.title}`}
                      className="absolute right-0.5 z-10 -translate-y-1/2"
                      style={{ top }}
                    >
                      <Icon className={cn("size-3", style.text, mk.selected && "drop-shadow")} />
                    </button>
                  )
                })}
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}
