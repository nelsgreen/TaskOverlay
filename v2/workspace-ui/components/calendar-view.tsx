"use client"

import { useMemo } from "react"
import { Bell, CalendarDays, Flag, Video } from "lucide-react"
import type { Project, TimelineItem, TimelineKind } from "@/lib/types"
import { cn } from "@/lib/utils"
import { formatDayLabel, formatDayNumber, formatWeekdayShort, mondayOfWeekKey, todayKey, weekDayKeys } from "@/lib/calendar-date"

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
    <div className="flex h-full flex-col overflow-y-auto bg-background">
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

      <CalendarLegend />
    </div>
  )
}

function CalendarLegend() {
  return (
    <div className="flex shrink-0 flex-wrap items-center gap-x-4 gap-y-1 border-t border-border px-5 py-2.5 text-[11px]">
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
        MEET — not available in this Workspace build yet
      </span>
    </div>
  )
}

function DayHeader({ selectedDate, count }: { selectedDate: string; count: number }) {
  const isToday = selectedDate === todayKey()
  return (
    <div className="flex shrink-0 items-center justify-between gap-3 border-b border-border bg-card/30 px-5 py-3">
      <div className="flex items-center gap-2">
        <span className="flex size-8 items-center justify-center rounded-lg bg-accent">
          <CalendarDays className="size-4 text-muted-foreground" />
        </span>
        <div>
          <div className="flex items-center gap-2">
            <span className="text-sm font-semibold text-foreground">{formatDayLabel(selectedDate)}</span>
            {isToday && (
              <span className="rounded bg-primary/15 px-1.5 py-0.5 text-[9px] font-semibold uppercase tracking-wide text-primary">
                Today
              </span>
            )}
          </div>
          <span className="text-[11px] text-muted-foreground">
            {count === 0 ? "No attention items" : count === 1 ? "1 item" : `${count} items`}
          </span>
        </div>
      </div>
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

  return (
    <div className="flex flex-1 flex-col">
      <DayHeader selectedDate={selectedDate} count={items.length} />
      <div className="flex-1 px-5 py-4">
        {items.length === 0 ? (
          <EmptyState
            title={isToday ? "Nothing scheduled today" : "Nothing scheduled on this day"}
            subtitle="REMIND and Deadline items with a date on this day will appear here."
          />
        ) : (
          <div className="space-y-1.5">
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
        )}
      </div>
    </div>
  )
}

function EmptyState({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <div className="flex h-full min-h-[220px] flex-col items-center justify-center gap-2 rounded-lg border border-dashed border-border/70 bg-card/20 px-6 text-center">
      <span className="flex size-10 items-center justify-center rounded-full bg-accent">
        <CalendarDays className="size-4 text-muted-foreground" />
      </span>
      <p className="text-sm font-medium text-foreground">{title}</p>
      <p className="max-w-xs text-pretty text-[11px] text-muted-foreground">{subtitle}</p>
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
    <div className="flex-1 overflow-y-auto p-3">
      <div className="grid h-full grid-cols-7 gap-2">
        {days.map((day) => {
          const dayItems = itemsByDate.get(day) ?? []
          const isToday = day === today
          const isSelected = day === selectedDate
          return (
            <div
              key={day}
              className={cn(
                "flex min-h-0 flex-col rounded-lg border bg-card/20",
                isSelected ? "border-primary/40 ring-1 ring-primary/20" : "border-border",
              )}
            >
              <div
                className={cn(
                  "flex shrink-0 items-center justify-between rounded-t-lg border-b px-2.5 py-2",
                  isToday ? "border-primary/25 bg-primary/10" : "border-border bg-card/40",
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
              <div className="flex-1 space-y-1 overflow-y-auto p-1.5">
                {dayItems.length === 0 ? (
                  <div className="flex h-full min-h-12 items-center justify-center text-[11px] text-muted-foreground/40">—</div>
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
  const Icon = config.Icon
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
      <Icon className={cn("size-2.5 shrink-0", config.text)} />
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
