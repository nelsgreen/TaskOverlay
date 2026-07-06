"use client"

import { useEffect, useState } from "react"
import { Bell, Flag, Video } from "lucide-react"
import type { Project, TimelineItem, TimelineKind } from "@/lib/types"
import { cn } from "@/lib/utils"

// ─── Constants ─────────────────────────────────────────────────────────────

const DAY_START_H = 9   // 09:00
const DAY_END_H   = 18  // 18:00
const DAY_SPAN    = (DAY_END_H - DAY_START_H) * 60 // minutes

const hours = ["09", "10", "11", "12", "13", "14", "15", "16", "17", "18"]

// ─── Kind config ───────────────────────────────────────────────────────────

type KindCfg = {
  label: string
  Icon: typeof Bell
  text: string
  dot: string
  bg: string
  ring: string
}

const kindConfig: Record<TimelineKind, KindCfg> = {
  MEET: {
    label: "MEET",
    Icon: Video,
    text: "text-status-meet",
    dot: "bg-status-meet",
    bg: "bg-status-meet/10",
    ring: "ring-status-meet/25",
  },
  REMIND: {
    label: "REMIND",
    Icon: Bell,
    text: "text-status-remind",
    dot: "bg-status-remind",
    bg: "bg-status-remind/10",
    ring: "ring-status-remind/25",
  },
  DEADLINE: {
    label: "DEADLINE",
    Icon: Flag,
    text: "text-status-deadline",
    dot: "bg-status-deadline",
    bg: "bg-status-deadline/10",
    ring: "ring-status-deadline/25",
  },
}

// ─── Scrubber dots (today's items) ────────────────────────────────────────

/** Parse "HH:MM" → minutes from midnight */
function parseTime(t: string): number | null {
  const m = t.match(/^(\d{1,2}):(\d{2})$/)
  if (!m) return null
  return parseInt(m[1], 10) * 60 + parseInt(m[2], 10)
}

/** Minutes from midnight → 0–100 percent on the DAY rail, clamped */
function toPct(minutesSinceMidnight: number): number {
  const dayStartMins = DAY_START_H * 60
  return Math.min(100, Math.max(0, ((minutesSinceMidnight - dayStartMins) / DAY_SPAN) * 100))
}

// ─── Now helpers ──────────────────────────────────────────────────────────

function getNowMinutes(): number {
  const now = new Date()
  return now.getHours() * 60 + now.getMinutes()
}

function formatNow(mins: number): string {
  const h = Math.floor(mins / 60).toString().padStart(2, "0")
  const m = (mins % 60).toString().padStart(2, "0")
  return `${h}:${m}`
}

/** Returns "before" | "in-range" | "after" for the day window */
function nowPosition(mins: number): "before" | "in-range" | "after" {
  if (mins < DAY_START_H * 60) return "before"
  if (mins > DAY_END_H * 60) return "after"
  return "in-range"
}

// ─── Bucket config ─────────────────────────────────────────────────────────

const buckets: { key: TimelineItem["bucket"]; label: string }[] = [
  { key: "today", label: "TODAY" },
  { key: "tomorrow", label: "TOMORROW" },
  { key: "week", label: "THIS WEEK" },
  { key: "later", label: "LATER" },
]

// ─── Component ─────────────────────────────────────────────────────────────

interface TimelineViewProps {
  projectIds?: string[]
  projects: Project[]
  items: TimelineItem[]
  selectedTimelineItemId?: string | null
  /** called with the timeline item and linked MeetItem ids when user clicks a MEET row */
  onSelectMeet?: (timelineItemId: string, meetId: string) => void
  /** called with the timeline item and linked task ids when user clicks a REMIND or DEADLINE row */
  onSelectTask?: (timelineItemId: string, taskId: string) => void
  search?: string
}

export function TimelineView({
  projectIds,
  projects,
  items,
  selectedTimelineItemId,
  onSelectMeet,
  onSelectTask,
  search = "",
}: TimelineViewProps) {
  const [nowMins, setNowMins] = useState<number>(getNowMinutes)

  // Update every minute
  useEffect(() => {
    const tick = () => setNowMins(getNowMinutes())
    const id = setInterval(tick, 60_000)
    return () => clearInterval(id)
  }, [])

  const nowPos = nowPosition(nowMins)
  const nowPct = nowPos === "before" ? 0 : nowPos === "after" ? 100 : toPct(nowMins)

  // Combined time view across the selected projects
  const projectItems =
    projectIds && projectIds.length > 0
      ? items.filter((i) => !i.projectId || projectIds.includes(i.projectId))
      : items
  const query = search.trim().toLowerCase()
  const scopedItems = query
    ? projectItems.filter((item) =>
        item.title.toLowerCase().includes(query) ||
        item.projectPath.toLowerCase().includes(query) ||
        (item.meta?.toLowerCase().includes(query) ?? false))
    : projectItems

  // Collect today scrubber dots from items that have parseable times
  const todayItems = scopedItems.filter((i) => i.bucket === "today")
  const scrubberDots = todayItems
    .map((item) => ({ item, mins: parseTime(item.time) }))
    .filter((d): d is { item: TimelineItem; mins: number } => d.mins !== null)
    .map(({ item, mins }) => ({ item, mins, pct: toPct(mins) }))

  return (
    <div className="flex h-full flex-col overflow-y-auto">
      {/* Day scrubber */}
      <div className="shrink-0 border-b border-border bg-card/30 px-5 py-3">
        <div className="mb-2 flex items-center justify-between">
          <span className="text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
            Today · Day Timeline
          </span>
          <span className="font-mono text-[11px] text-muted-foreground">
            {DAY_START_H.toString().padStart(2, "0")}:00 – {DAY_END_H.toString().padStart(2, "0")}:00
          </span>
        </div>

        {/* Track */}
        <div className="relative mb-2 h-6">
          {/* Past portion — slightly dimmed */}
          <div
            className="absolute top-1/2 h-px -translate-y-1/2 bg-border/60"
            style={{ left: 0, width: `${nowPct}%` }}
          />
          {/* Future portion — normal */}
          <div
            className="absolute top-1/2 h-px -translate-y-1/2 bg-border"
            style={{ left: `${nowPct}%`, right: 0 }}
          />

          {/* Event dots */}
          {scrubberDots.map(({ item, mins: dotMins, pct }) => {
            const c = kindConfig[item.kind]
            const isPast = nowPos === "in-range" && dotMins < nowMins
            return (
              <div
                key={item.id}
                className="absolute top-1/2 -translate-x-1/2 -translate-y-1/2"
                style={{ left: `${pct}%` }}
                title={`${item.kind} · ${item.time} · ${item.title}`}
              >
                <div
                  className={cn(
                    "size-2.5 rounded-full ring-2 ring-background transition-opacity",
                    c.dot,
                    isPast ? "opacity-35" : "opacity-100",
                  )}
                />
              </div>
            )
          })}

          {/* Now marker */}
          <div
            className="absolute top-0 -translate-x-1/2"
            style={{ left: `${nowPct}%` }}
            aria-label={`Now: ${formatNow(nowMins)}`}
          >
            {/* Vertical line */}
            <div className="mx-auto h-6 w-px bg-now-marker/70" />
            {/* Dot at center */}
            <div className="absolute top-1/2 left-1/2 size-2 -translate-x-1/2 -translate-y-1/2 rounded-full bg-now-marker ring-2 ring-background" />
            {/* Label below */}
            <div
              className={cn(
                "absolute top-full mt-0.5 -translate-x-1/2 whitespace-nowrap rounded px-1 py-0.5 font-mono text-[9px] font-semibold text-now-marker",
                nowPos === "before" && "translate-x-0 left-0",
                nowPos === "after" && "translate-x-[-100%] left-full",
              )}
            >
              {nowPos === "before" ? "Before workday" : nowPos === "after" ? "After workday" : formatNow(nowMins)}
            </div>
          </div>
        </div>

        {/* Hour labels */}
        <div className="mt-4 flex justify-between">
          {hours.map((h) => (
            <span key={h} className="font-mono text-[10px] text-muted-foreground/60">
              {h}
            </span>
          ))}
        </div>
      </div>

      {/* Grouped list */}
      {scopedItems.length === 0 ? (
        <div className="flex flex-1 items-center justify-center px-5 py-8 text-center">
          <div>
            <p className="text-sm font-medium text-foreground">
              {query ? "No timeline items match this search" : "No timeline items in this project scope"}
            </p>
            <p className="mt-1 text-[11px] text-muted-foreground">
              {query
                ? "Clear or change the search to see other attention items."
                : "REMIND, DEADLINE, and available MEET items will appear here."}
            </p>
          </div>
        </div>
      ) : (
        <div className="flex-1 space-y-5 overflow-y-auto px-5 py-4">
          {buckets.map((bucket) => {
            const items = scopedItems.filter((i) => i.bucket === bucket.key)
            return (
              <section key={bucket.key}>
              <div className="mb-2 flex items-center gap-2">
                <span className="font-mono text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
                  {bucket.label}
                </span>
                <span className="h-px flex-1 bg-border" />
                <span className="font-mono text-[10px] tabular-nums text-muted-foreground/70">{items.length}</span>
              </div>

              {items.length === 0 ? (
                <div className="rounded-lg border border-dashed border-border px-3 py-3 text-center text-[11px] text-muted-foreground">
                  Nothing scheduled
                </div>
              ) : (
                <div className="space-y-1">
                  {items.map((item) => {
                    // Determine past / near / upcoming for today items
                    const mins = bucket.key === "today" ? parseTime(item.time) : null
                    const isPast = nowPos === "in-range" && mins !== null && mins < nowMins
                    const isNear =
                      nowPos === "in-range" && mins !== null && mins >= nowMins && mins - nowMins <= 30
                    const selected = selectedTimelineItemId === item.id
                    return (
                      <TimelineRow
                        key={item.id}
                        item={item}
                        projects={projects}
                        isPast={isPast}
                        isNear={isNear}
                        selected={selected}
                        onSelect={() => {
                          if (item.kind === "MEET" && item.linkedMeetId) onSelectMeet?.(item.id, item.linkedMeetId)
                          else if (item.linkedTaskId) onSelectTask?.(item.id, item.linkedTaskId)
                        }}
                      />
                    )
                  })}
                </div>
              )}
              </section>
            )
          })}
        </div>
      )}
    </div>
  )
}

// ─── Sub-components ────────────────────────────────────────────────────────

function TimelineRow({
  item,
  projects,
  isPast,
  isNear,
  selected,
  onSelect,
}: {
  item: TimelineItem
  projects: Project[]
  isPast: boolean
  isNear: boolean
  selected?: boolean
  onSelect?: () => void
}) {
  const c = kindConfig[item.kind]
  const Icon = c.Icon
  const project = item.projectId ? projects.find((p) => p.id === item.projectId) : null
  const isClickable = item.kind === "MEET" ? !!item.linkedMeetId : !!item.linkedTaskId

  return (
    <div
      role={isClickable ? "button" : undefined}
      tabIndex={isClickable ? 0 : undefined}
      onClick={onSelect}
      onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") onSelect?.() }}
      className={cn(
        "flex items-center gap-3 rounded-lg border px-3 py-2.5 transition-colors",
        isClickable ? "cursor-pointer" : "",
        selected
          ? "border-row-selected-border bg-row-selected"
          : isPast
            ? "border-border/40 bg-card/20 opacity-50 hover:opacity-70"
            : isNear
              ? "border-border bg-card/70 ring-1 ring-inset ring-now-marker/20 hover:bg-accent/30"
              : "border-border bg-card/50 hover:bg-accent/30",
      )}
    >
      {/* Kind icon */}
      <span
        className={cn(
          "flex size-8 shrink-0 items-center justify-center rounded-lg ring-1 ring-inset",
          c.bg,
          c.ring,
          isPast && "opacity-60",
        )}
      >
        <Icon className={cn("size-4", c.text)} />
      </span>

      {/* Content */}
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span
            className={cn(
              "inline-flex items-center gap-1 rounded px-1 py-0.5 font-mono text-[9px] font-bold uppercase tracking-wider ring-1 ring-inset",
              c.bg,
              c.text,
              c.ring,
            )}
          >
            {c.label}
          </span>
          <span
            className={cn(
              "min-w-0 flex-1 truncate text-sm font-medium",
              isPast ? "text-muted-foreground" : "text-foreground",
            )}
          >
            {item.title}
          </span>
          {isNear && (
            <span className="shrink-0 rounded bg-now-marker/15 px-1.5 py-0.5 font-mono text-[9px] font-semibold text-now-marker">
              soon
            </span>
          )}
        </div>
        <div className="mt-0.5 flex items-center gap-2">
          {project && (
            <span
              className="size-1.5 shrink-0 rounded-full"
              style={{ backgroundColor: project.color }}
              aria-hidden
            />
          )}
          <span className="truncate text-[11px] text-muted-foreground">{item.projectPath}</span>
          {item.meta && (
            <>
              <span className="text-muted-foreground/40" aria-hidden>·</span>
              <span className="truncate text-[11px] text-muted-foreground/70">{item.meta}</span>
            </>
          )}
        </div>
      </div>

      {/* Time */}
      <span
        className={cn(
          "shrink-0 font-mono text-xs font-medium tabular-nums",
          isPast ? "text-muted-foreground/50" : isNear ? "text-now-marker" : "text-muted-foreground",
        )}
      >
        {item.time}
      </span>
    </div>
  )
}
