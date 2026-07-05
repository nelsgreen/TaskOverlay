"use client"

import { useMemo } from "react"
import { Bell, Flag } from "lucide-react"
import type { Project, TimelineItem, TimelineKind } from "@/lib/types"
import { cn } from "@/lib/utils"
import { formatDayNumber, formatWeekdayShort, mondayOfWeekKey, todayKey, weekDayKeys } from "@/lib/calendar-date"

type CalendarKind = Exclude<TimelineKind, "MEET">

const kindConfig: Record<CalendarKind, { label: string; Icon: typeof Bell; text: string; dot: string; bg: string; ring: string }> = {
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

interface CalendarViewProps {
  viewMode: "day" | "week"
  selectedDate: string
  projectIds?: string[]
  projects: Project[]
  items: TimelineItem[]
  selectedTimelineItemId?: string | null
  onSelectTask?: (timelineItemId: string, taskId: string) => void
}

export function CalendarView({
  viewMode,
  selectedDate,
  projectIds,
  projects,
  items,
  selectedTimelineItemId,
  onSelectTask,
}: CalendarViewProps) {
  const scopedItems = useMemo(() => {
    const inScope = projectIds && projectIds.length > 0
      ? items.filter((item) => !item.projectId || projectIds.includes(item.projectId))
      : items
    return inScope.filter(
      (item): item is TimelineItem & { kind: CalendarKind; dateKey: string } =>
        item.kind !== "MEET" && !!item.dateKey,
    )
  }, [items, projectIds])

  const itemsByDate = useMemo(() => {
    const map = new Map<string, TimelineItem[]>()
    for (const item of scopedItems) {
      const list = map.get(item.dateKey!)
      if (list) list.push(item)
      else map.set(item.dateKey!, [item])
    }
    return map
  }, [scopedItems])

  return (
    <div className="flex h-full flex-col overflow-y-auto">
      {viewMode === "week" ? (
        <WeekGrid
          selectedDate={selectedDate}
          itemsByDate={itemsByDate}
          projects={projects}
          selectedTimelineItemId={selectedTimelineItemId}
          onSelectTask={onSelectTask}
        />
      ) : (
        <DayAgenda
          selectedDate={selectedDate}
          items={itemsByDate.get(selectedDate) ?? []}
          projects={projects}
          selectedTimelineItemId={selectedTimelineItemId}
          onSelectTask={onSelectTask}
        />
      )}

      <div className="shrink-0 border-t border-border px-5 py-2.5 text-center text-[11px] text-muted-foreground">
        REMIND &amp; Deadline placed by date. MEET is not available in this Workspace build yet.
      </div>
    </div>
  )
}

function WeekGrid({
  selectedDate,
  itemsByDate,
  projects,
  selectedTimelineItemId,
  onSelectTask,
}: {
  selectedDate: string
  itemsByDate: Map<string, TimelineItem[]>
  projects: Project[]
  selectedTimelineItemId?: string | null
  onSelectTask?: (timelineItemId: string, taskId: string) => void
}) {
  const monday = mondayOfWeekKey(selectedDate)
  const days = weekDayKeys(monday)
  const today = todayKey()

  return (
    <div className="grid flex-1 grid-cols-7 divide-x divide-border">
      {days.map((day) => {
        const dayItems = itemsByDate.get(day) ?? []
        const isToday = day === today
        const isSelected = day === selectedDate
        return (
          <div key={day} className="flex min-h-0 flex-col">
            <div
              className={cn(
                "flex shrink-0 items-center justify-between border-b border-border px-2.5 py-2",
                isSelected && "bg-primary/5",
              )}
            >
              <span className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
                {formatWeekdayShort(day)}
              </span>
              <span
                className={cn(
                  "flex size-5 items-center justify-center rounded-full text-[11px] font-medium tabular-nums",
                  isToday ? "bg-primary text-primary-foreground" : "text-foreground",
                )}
              >
                {formatDayNumber(day)}
              </span>
            </div>
            <div className="flex-1 space-y-1 overflow-y-auto px-1.5 py-1.5">
              {dayItems.length === 0 ? (
                <div className="px-1 py-2 text-center text-[10px] text-muted-foreground/60">Nothing</div>
              ) : (
                dayItems.map((item) => (
                  <CalendarChip
                    key={item.id}
                    item={item}
                    projects={projects}
                    selected={selectedTimelineItemId === item.id}
                    onSelect={() => item.linkedTaskId && onSelectTask?.(item.id, item.linkedTaskId)}
                  />
                ))
              )}
            </div>
          </div>
        )
      })}
    </div>
  )
}

function DayAgenda({
  selectedDate,
  items,
  projects,
  selectedTimelineItemId,
  onSelectTask,
}: {
  selectedDate: string
  items: TimelineItem[]
  projects: Project[]
  selectedTimelineItemId?: string | null
  onSelectTask?: (timelineItemId: string, taskId: string) => void
}) {
  const isToday = selectedDate === todayKey()

  if (items.length === 0) {
    return (
      <div className="flex flex-1 items-center justify-center px-5 py-8 text-center">
        <div>
          <p className="text-sm font-medium text-foreground">
            {isToday ? "Nothing scheduled today" : "Nothing scheduled on this day"}
          </p>
          <p className="mt-1 text-[11px] text-muted-foreground">
            REMIND and Deadline items with a date on this day will appear here.
          </p>
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 space-y-1.5 overflow-y-auto px-5 py-4">
      {items.map((item) => (
        <DayRow
          key={item.id}
          item={item}
          projects={projects}
          selected={selectedTimelineItemId === item.id}
          onSelect={() => item.linkedTaskId && onSelectTask?.(item.id, item.linkedTaskId)}
        />
      ))}
    </div>
  )
}

function CalendarChip({
  item,
  projects,
  selected,
  onSelect,
}: {
  item: TimelineItem
  projects: Project[]
  selected?: boolean
  onSelect?: () => void
}) {
  const config = kindConfig[item.kind as CalendarKind]
  const project = item.projectId ? projects.find((p) => p.id === item.projectId) : null
  const clickable = !!item.linkedTaskId

  return (
    <div
      role={clickable ? "button" : undefined}
      tabIndex={clickable ? 0 : undefined}
      onClick={onSelect}
      onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") onSelect?.() }}
      title={`${config.label} · ${item.time} · ${item.title}`}
      className={cn(
        "flex items-center gap-1 rounded px-1.5 py-1 text-left ring-1 ring-inset transition-colors",
        clickable ? "cursor-pointer" : "",
        selected ? "border-row-selected-border bg-row-selected ring-0" : cn(config.bg, config.ring, "hover:brightness-95"),
      )}
    >
      {project && <span className="size-1.5 shrink-0 rounded-full" style={{ backgroundColor: project.color }} aria-hidden />}
      <span className={cn("min-w-0 flex-1 truncate text-[10px] font-medium", config.text)}>{item.title}</span>
    </div>
  )
}

function DayRow({
  item,
  projects,
  selected,
  onSelect,
}: {
  item: TimelineItem
  projects: Project[]
  selected?: boolean
  onSelect?: () => void
}) {
  const config = kindConfig[item.kind as CalendarKind]
  const Icon = config.Icon
  const project = item.projectId ? projects.find((p) => p.id === item.projectId) : null
  const clickable = !!item.linkedTaskId

  return (
    <div
      role={clickable ? "button" : undefined}
      tabIndex={clickable ? 0 : undefined}
      onClick={onSelect}
      onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") onSelect?.() }}
      className={cn(
        "flex items-center gap-3 rounded-lg border px-3 py-2.5 transition-colors",
        clickable ? "cursor-pointer" : "",
        selected
          ? "border-row-selected-border bg-row-selected"
          : "border-border bg-card/50 hover:bg-accent/30",
      )}
    >
      <span className={cn("flex size-8 shrink-0 items-center justify-center rounded-lg ring-1 ring-inset", config.bg, config.ring)}>
        <Icon className={cn("size-4", config.text)} />
      </span>
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className={cn("inline-flex items-center gap-1 rounded px-1 py-0.5 font-mono text-[9px] font-bold uppercase tracking-wider ring-1 ring-inset", config.bg, config.text, config.ring)}>
            {config.label}
          </span>
          <span className="min-w-0 flex-1 truncate text-sm font-medium text-foreground">{item.title}</span>
        </div>
        <div className="mt-0.5 flex items-center gap-2">
          {project && <span className="size-1.5 shrink-0 rounded-full" style={{ backgroundColor: project.color }} aria-hidden />}
          <span className="truncate text-[11px] text-muted-foreground">{item.projectPath}</span>
        </div>
      </div>
      <span className="shrink-0 font-mono text-xs font-medium tabular-nums text-muted-foreground">{item.time}</span>
    </div>
  )
}
