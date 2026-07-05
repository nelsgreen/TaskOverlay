"use client"

import { useCallback, useEffect, useMemo, useState } from "react"
import { Bell, Flag, Plus, Video } from "lucide-react"
import type { Project, Section, Status, Task } from "@/lib/types"
import { cn } from "@/lib/utils"
import { formatDayNumber, formatWeekdayShort, mondayOfWeekKey, todayKey, weekDayKeys } from "@/lib/calendar-date"

// ─── Work-hours grid constants (from v0 reference) ──────────────────────────
const START_HOUR = 9
const END_HOUR = 18
const START_MIN = START_HOUR * 60         // 540
const END_MIN = END_HOUR * 60             // 1080
const PX_PER_MIN = 1.15                    // ~69px per hour
const GRID_HEIGHT = Math.round((END_MIN - START_MIN) * PX_PER_MIN)
// Vertical space (minutes) a point-in-time marker card claims, for overlap layout.
const MARKER_SPAN_MIN = 52
const POOL_MAX_VISIBLE = 6

type MarkerKind = "REMIND" | "DEADLINE"

// ─── Minute helpers ──────────────────────────────────────────────────────────
function toMin(hhmm: string): number {
  const [h, m] = hhmm.split(":").map(Number)
  return h * 60 + (m || 0)
}
function fmtMin(min: number): string {
  const h = Math.floor(min / 60)
  const m = min % 60
  return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}`
}
const clamp = (v: number, lo: number, hi: number) => Math.max(lo, Math.min(hi, v))

// ─── Marker model ──────────────────────────────────────────────────────────────
interface Marker {
  key: string
  kind: MarkerKind
  title: string
  projectId: string
  sectionId?: string
  min: number
  taskId: string
  selected: boolean
}

const MARKER_STYLES: Record<MarkerKind, {
  Icon: typeof Bell
  label: string
  text: string
  dot: string
  base: string
  selected: string
}> = {
  REMIND: {
    Icon: Bell,
    label: "REMIND",
    text: "text-status-remind",
    dot: "bg-status-remind",
    base: "border-status-remind/40 bg-status-remind/10 hover:bg-status-remind/20",
    selected: "border-status-remind bg-status-remind/20 ring-2 ring-status-remind/40",
  },
  DEADLINE: {
    Icon: Flag,
    label: "DEADLINE",
    text: "text-status-deadline",
    dot: "bg-status-deadline",
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

// Assign side-by-side columns to markers that overlap in time (ported from v0 block layout).
function layoutMarkers(markers: Marker[]): (Marker & { startMin: number; endMin: number; col: number; cols: number })[] {
  const items = markers.map((m) => {
    const startMin = clamp(m.min, START_MIN, END_MIN)
    return { ...m, startMin, endMin: Math.min(END_MIN, startMin + MARKER_SPAN_MIN) }
  })
  const sorted = [...items].sort((a, b) => a.startMin - b.startMin || a.endMin - b.endMin)
  const out: (Marker & { startMin: number; endMin: number; col: number; cols: number })[] = []
  let cluster: (Marker & { startMin: number; endMin: number; col: number })[] = []
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
  onSelectTask: (taskId: string) => void
  onPickDay: (dateKey: string) => void
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
  onSelectTask,
  onPickDay,
}: CalendarViewProps) {
  const today = todayKey()

  // Live "now" minute marker
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

  const projectMap = useMemo(() => new Map(projects.map((p) => [p.id, p])), [projects])
  const sectionMap = useMemo(() => new Map(sections.map((s) => [s.id, s])), [sections])
  const allSelected = selectedProjectIds.length === projects.length
  const inScope = useCallback(
    (projectId: string) => allSelected || selectedProjectIds.includes(projectId),
    [allSelected, selectedProjectIds],
  )

  // Build REMIND / DEADLINE markers for a given date from real task data.
  const buildDay = useCallback(
    (dateKey: string): Marker[] => {
      const markers: Marker[] = []
      tasks.forEach((t) => {
        if (!inScope(t.projectId)) return
        if (t.status === "DONE" && !showDone) return
        if (t.reminderDate === dateKey && t.reminderTime) {
          markers.push({
            key: `rem-${t.id}`,
            kind: "REMIND",
            title: t.title,
            projectId: t.projectId,
            sectionId: t.sectionId,
            min: toMin(t.reminderTime),
            taskId: t.id,
            selected: t.id === selectedTaskId,
          })
        }
        if (t.deadlineDate === dateKey) {
          markers.push({
            key: `dl-${t.id}`,
            kind: "DEADLINE",
            title: t.title,
            projectId: t.projectId,
            sectionId: t.sectionId,
            min: t.deadlineTime ? toMin(t.deadlineTime) : END_MIN,
            taskId: t.id,
            selected: t.id === selectedTaskId,
          })
        }
      })
      return markers
    },
    [tasks, inScope, showDone, selectedTaskId],
  )

  // Planning pool: FOCUS / TODO tasks in scope with no reminder date and no deadline date.
  const unscheduled = useMemo(() => {
    const pool = tasks.filter(
      (t) =>
        (t.status === "FOCUS" || t.status === "TODO") &&
        inScope(t.projectId) &&
        !t.reminderDate &&
        !t.deadlineDate,
    )
    return [
      ...pool.filter((t) => t.pinned || t.status === "FOCUS"),
      ...pool.filter((t) => !t.pinned && t.status !== "FOCUS"),
    ]
  }, [tasks, inScope])

  const hours = Array.from({ length: END_HOUR - START_HOUR + 1 }, (_, i) => START_HOUR + i)
  const visiblePool = showAllPool ? unscheduled : unscheduled.slice(0, POOL_MAX_VISIBLE)
  const hiddenCount = unscheduled.length - POOL_MAX_VISIBLE

  return (
    <div className="flex h-full flex-col overflow-hidden bg-background">
      <div className="min-h-0 flex-1 overflow-y-auto">
        {viewMode === "day" ? (
          <DayGrid
            hours={hours}
            markers={buildDay(selectedDate)}
            isToday={selectedDate === today}
            nowMin={nowMin}
            projectMap={projectMap}
            sectionMap={sectionMap}
            onSelectTask={onSelectTask}
          />
        ) : (
          <WeekGrid
            hours={hours}
            selectedDate={selectedDate}
            today={today}
            nowMin={nowMin}
            buildDay={buildDay}
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

        {/* ── Planning pool (read-only) ─────────────────────────────────── */}
        <div className="px-5 pb-4">
          <div className="mb-2 flex items-center gap-2">
            <p className="text-[11px] font-semibold text-foreground">Planning pool</p>
            <span className="rounded-full bg-muted px-1.5 py-0.5 text-[10px] font-medium tabular-nums text-muted-foreground">
              {unscheduled.length}
            </span>
            {unscheduled.length > 0 && (
              <p className="text-[11px] text-muted-foreground">
                Focus and todo tasks with no reminder or deadline date
              </p>
            )}
          </div>

          {unscheduled.length === 0 ? (
            <p className="py-3 text-[11px] text-muted-foreground">
              All focus and todo tasks in scope have a reminder or deadline date.
            </p>
          ) : (
            <div className="flex items-start gap-2 overflow-x-auto pb-1">
              {visiblePool.map((t) => {
                const p = projectMap.get(t.projectId)
                const s = sectionMap.get(t.sectionId ?? "")
                const st = STATUS_STYLES[t.status] ?? STATUS_STYLES.TODO
                return (
                  <button
                    key={t.id}
                    type="button"
                    onClick={() => onSelectTask(t.id)}
                    className={cn(
                      "flex w-[200px] shrink-0 cursor-pointer flex-col gap-1.5 rounded-lg border bg-card p-2.5 text-left transition-colors",
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
                        <span className="ml-auto text-[9px] font-semibold uppercase tracking-wide text-muted-foreground">
                          pinned
                        </span>
                      )}
                    </div>
                    <p className="line-clamp-2 text-[12px] font-medium leading-snug text-foreground">
                      {t.title}
                    </p>
                    <div className="flex items-center gap-1 text-[10px] text-muted-foreground">
                      <span className="size-1.5 shrink-0 rounded-full" style={{ backgroundColor: p?.color }} />
                      <span className="truncate">
                        {p?.name}{s ? ` / ${s.name}` : ""}
                      </span>
                    </div>
                  </button>
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

// ─── Legend ──────────────────────────────────────────────────────────────────
function CalendarLegend() {
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 border-t border-border px-5 py-2.5 text-[11px]">
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
  hours,
  markers,
  isToday,
  nowMin,
  projectMap,
  sectionMap,
  onSelectTask,
}: {
  hours: number[]
  markers: Marker[]
  isToday: boolean
  nowMin: number
  projectMap: Map<string, Project>
  sectionMap: Map<string, Section>
  onSelectTask: (taskId: string) => void
}) {
  const laid = layoutMarkers(markers)
  const showNow = isToday && nowMin >= START_MIN && nowMin <= END_MIN

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
          <div
            key={h}
            className="absolute inset-x-0 border-t border-border/40"
            style={{ top: (h * 60 - START_MIN) * PX_PER_MIN }}
          />
        ))}
        {hours.slice(0, -1).map((h) => (
          <div
            key={`${h}h`}
            className="absolute inset-x-0 border-t border-dashed border-border/20"
            style={{ top: (h * 60 + 30 - START_MIN) * PX_PER_MIN }}
          />
        ))}

        {/* Now marker */}
        {showNow && (
          <div
            className="pointer-events-none absolute inset-x-0 z-20 flex items-center"
            style={{ top: (nowMin - START_MIN) * PX_PER_MIN }}
          >
            <span className="-ml-1 size-2 rounded-full bg-now-marker" />
            <span className="h-px flex-1 bg-now-marker" />
            <span className="rounded-l bg-now-marker px-1 py-0.5 text-[9px] font-bold text-background">
              {fmtMin(nowMin)}
            </span>
          </div>
        )}

        {/* Empty state */}
        {markers.length === 0 && (
          <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center gap-1.5 text-center">
            <p className="text-sm font-medium text-muted-foreground">Nothing placed on this day</p>
            <p className="max-w-xs text-[11px] text-muted-foreground/70">
              Reminders and deadlines with a time land here on the work-hours grid.
            </p>
          </div>
        )}

        {/* Marker cards */}
        {laid.map((mk) => {
          const style = MARKER_STYLES[mk.kind]
          const Icon = style.Icon
          const p = projectMap.get(mk.projectId)
          const sec = mk.sectionId ? sectionMap.get(mk.sectionId) : undefined
          const top = (mk.startMin - START_MIN) * PX_PER_MIN
          const height = (mk.endMin - mk.startMin) * PX_PER_MIN
          const widthPct = 100 / mk.cols
          return (
            <button
              key={mk.key}
              type="button"
              onClick={() => onSelectTask(mk.taskId)}
              title={`${style.label} · ${fmtMin(mk.min)} · ${mk.title}`}
              className={cn(
                "absolute overflow-hidden rounded-lg border px-2 py-1 text-left transition-colors",
                mk.selected ? style.selected : style.base,
              )}
              style={{
                top,
                height,
                left: `calc(${mk.col * widthPct}% + ${mk.col * 4 + 4}px)`,
                width: `calc(${widthPct}% - 8px)`,
              }}
            >
              <div className="flex items-center gap-1.5">
                <Icon className={cn("size-3 shrink-0", style.text)} />
                <span className={cn("text-[10px] font-semibold tabular-nums", style.text)}>{fmtMin(mk.min)}</span>
                <span className={cn("text-[9px] font-bold uppercase tracking-wide", style.text)}>{style.label}</span>
              </div>
              <div className="mt-0.5 flex items-center gap-1.5">
                <span className="size-1.5 shrink-0 rounded-full" style={{ backgroundColor: p?.color }} />
                <span className="truncate text-[11px] font-medium text-foreground">{mk.title}</span>
              </div>
              {height > 50 && (
                <span className="mt-0.5 block truncate text-[10px] text-muted-foreground">
                  {p?.name}{sec ? ` / ${sec.name}` : ""}
                </span>
              )}
            </button>
          )
        })}
      </div>
    </div>
  )
}

// ─── Week grid (Monday–Sunday, per PR #39 week logic) ──────────────────────────
function WeekGrid({
  hours,
  selectedDate,
  today,
  nowMin,
  buildDay,
  onSelectTask,
  onPickDay,
}: {
  hours: number[]
  selectedDate: string
  today: string
  nowMin: number
  buildDay: (dateKey: string) => Marker[]
  onSelectTask: (taskId: string) => void
  onPickDay: (dateKey: string) => void
}) {
  const monday = mondayOfWeekKey(selectedDate)
  const days = weekDayKeys(monday)
  const weekHasItems = days.some((d) => buildDay(d).length > 0)

  return (
    <div className="relative flex px-4 py-4">
      <div className="relative w-12 shrink-0 select-none" style={{ height: GRID_HEIGHT, marginTop: 36 }}>
        {hours.map((h) => (
          <div
            key={h}
            className="absolute -translate-y-1/2 pr-2 text-right text-[10px] tabular-nums text-muted-foreground"
            style={{ top: (h * 60 - START_MIN) * PX_PER_MIN, right: 0 }}
          >
            {String(h).padStart(2, "0")}
          </div>
        ))}
      </div>

      {!weekHasItems && (
        <div
          className="pointer-events-none absolute inset-x-0 top-1/2 flex -translate-y-1/2 flex-col items-center gap-1.5 text-center"
          style={{ left: 64 }}
        >
          <p className="text-sm font-medium text-muted-foreground">No reminders or deadlines this week</p>
          <p className="text-[11px] text-muted-foreground/70">Pick a day to see the work-hours grid, or change the project scope.</p>
        </div>
      )}

      <div className="grid flex-1 grid-cols-7 gap-1.5">
        {days.map((iso) => {
          const markers = layoutMarkers(buildDay(iso))
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
                  isToday
                    ? "border-primary/50 bg-primary/10 text-foreground"
                    : isSelected
                      ? "border-primary/30 bg-primary/5 text-foreground"
                      : "border-border bg-card text-muted-foreground hover:text-foreground",
                )}
              >
                {formatWeekdayShort(iso)}
                <span className="tabular-nums">{formatDayNumber(iso)}</span>
              </button>
              <div
                className={cn(
                  "relative rounded-md border bg-card/20",
                  isSelected ? "border-primary/30" : "border-border/50",
                )}
                style={{ height: GRID_HEIGHT }}
              >
                {hours.map((h) => (
                  <div
                    key={h}
                    className="absolute inset-x-0 border-t border-border/30"
                    style={{ top: (h * 60 - START_MIN) * PX_PER_MIN }}
                  />
                ))}
                {showNow && (
                  <div
                    className="pointer-events-none absolute inset-x-0 z-20 h-px bg-now-marker"
                    style={{ top: (nowMin - START_MIN) * PX_PER_MIN }}
                  />
                )}
                {markers.map((mk) => {
                  const style = MARKER_STYLES[mk.kind]
                  const Icon = style.Icon
                  const top = (mk.startMin - START_MIN) * PX_PER_MIN
                  const widthPct = 100 / mk.cols
                  return (
                    <button
                      key={mk.key}
                      type="button"
                      onClick={() => onSelectTask(mk.taskId)}
                      title={`${style.label} · ${fmtMin(mk.min)} · ${mk.title}`}
                      className={cn(
                        "absolute flex items-center gap-0.5 overflow-hidden rounded border px-1 py-0.5 transition-colors",
                        mk.selected ? style.selected : style.base,
                      )}
                      style={{
                        top,
                        left: `calc(${mk.col * widthPct}% + ${mk.col * 2}px)`,
                        width: `calc(${widthPct}% - 2px)`,
                      }}
                    >
                      <Icon className={cn("size-2.5 shrink-0", style.text)} />
                      <span className={cn("truncate text-[8px] font-semibold tabular-nums", style.text)}>
                        {fmtMin(mk.min)}
                      </span>
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
