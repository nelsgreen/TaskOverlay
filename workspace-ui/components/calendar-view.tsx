"use client"

import { useEffect, useMemo, useRef, useState } from "react"
import { Bell, Check, Flag, GripVertical, Link2, MapPin, X } from "lucide-react"
import type { MeetItem, Project, Section, Status, Task, TaskWorkSession } from "@/lib/types"
import { cn } from "@/lib/utils"
import { CalendarActivationGuard } from "@/lib/calendar-interaction"
import {
  DAY_END_MIN,
  DAY_START_MIN,
  DEFAULT_MEET_DURATION_MIN,
  DEFAULT_TASK_DURATION_MIN,
  GRID_HEIGHT,
  MIN_DURATION_MIN,
  PX_PER_MIN,
  SNAP_MIN,
  UNTIMED_DEADLINE_MIN,
  WORKDAY_END_MIN,
  WORKDAY_START_MIN,
  clamp,
  clipRangeToDay,
  durationFits,
  initialScrollMinute,
  initialScrollTop,
  minuteFromPointer,
  rangeForMove,
  resizeRange,
  snapMinute,
} from "@/lib/calendar-layout"
import {
  formatDayNumber,
  formatWeekdayShort,
  isoFromLocalDateTime,
  localSlotFromIso,
  mondayOfWeekKey,
  todayKey,
  weekDayKeys,
} from "@/lib/calendar-date"

const MARKER_RAIL = 76

const QUICK_DURATIONS = [
  { label: "30m", min: 30 },
  { label: "45m", min: 45 },
  { label: "1h", min: 60 },
  { label: "1.5h", min: 90 },
  { label: "2h", min: 120 },
]

const DURATION_MIN_MAP: Record<string, number> = {
  "15m": 15,
  "30m": 30,
  "45m": 45,
  "1h": 60,
  "90m": 90,
  "2h": 120,
  custom: 60,
}

function toMin(hhmm: string): number {
  const [h, m] = hhmm.split(":").map(Number)
  return (h || 0) * 60 + (m || 0)
}

function fmtMin(min: number): string {
  if (min >= DAY_END_MIN) return "24:00"
  const safe = clamp(min, DAY_START_MIN, DAY_END_MIN)
  return `${String(Math.floor(safe / 60)).padStart(2, "0")}:${String(safe % 60).padStart(2, "0")}`
}

function fmtDur(min: number): string {
  const h = Math.floor(min / 60)
  const m = min % 60
  return h && m ? `${h}h ${m}m` : h ? `${h}h` : `${m}m`
}

function meetEndMin(meeting: MeetItem): number {
  const startMin = toMin(meeting.startTime)
  if (meeting.endTime) {
    const endMin = toMin(meeting.endTime)
    return endMin <= startMin ? DAY_END_MIN : endMin
  }
  return startMin + (DURATION_MIN_MAP[meeting.duration] ?? DEFAULT_TASK_DURATION_MIN)
}

function hourLabelClass(hour: number): string {
  if (hour === 0) return "translate-y-0"
  if (hour === 24) return "-translate-y-full"
  return "-translate-y-1/2"
}

interface Block {
  key: string
  kind: "WORK" | "MEET"
  title: string
  projectId: string
  sectionId?: string
  dateKey: string
  startMin: number
  endMin: number
  taskId?: string
  sessionId?: string
  meetId?: string
  location?: string
  hasLink?: boolean
  status?: Status
  selected: boolean
}

interface Marker {
  key: string
  kind: "REMIND" | "DEADLINE"
  title: string
  projectId: string
  min: number
  taskId: string
  selected: boolean
}

type DragState =
  | { kind: "TASK"; taskId: string; duration: number }
  | { kind: "TASK_SESSION"; taskId: string; sessionId: string; duration: number }
  | { kind: "MEET"; meetId: string; duration: number }

type CalendarMenuState =
  | { kind: "slot"; x: number; y: number; dateKey: string; startMin: number }
  | { kind: "block"; x: number; y: number; block: Block }

type PlanningPoolFilter = "ACTIVE" | "UNSCHEDULED" | "TODAY" | "ALL"

const PLANNING_POOL_FILTERS: ReadonlyArray<{ id: PlanningPoolFilter; label: string; title: string }> = [
  { id: "ACTIVE", label: "Active", title: "All TODO, FOCUS, and WAIT tasks" },
  { id: "UNSCHEDULED", label: "Unscheduled", title: "Active tasks with no work sessions" },
  { id: "TODAY", label: "Today", title: "Active tasks with at least one work session today" },
  { id: "ALL", label: "All", title: "All tasks in scope, including DONE" },
]

const PLANNING_POOL_EMPTY_COPY: Record<PlanningPoolFilter, string> = {
  ACTIVE: "No active tasks in scope. DONE tasks stay hidden.",
  UNSCHEDULED: "No unscheduled active tasks in scope.",
  TODAY: "No active tasks are scheduled today.",
  ALL: "No tasks in scope.",
}

const STATUS_STYLES: Record<Status, { bg: string; text: string; label: string }> = {
  FOCUS: { bg: "bg-status-focus/15", text: "text-status-focus", label: "Focus" },
  WAIT: { bg: "bg-status-wait/15", text: "text-status-wait", label: "Wait" },
  TODO: { bg: "bg-muted/40", text: "text-muted-foreground", label: "Todo" },
  DONE: { bg: "bg-status-done/15", text: "text-status-done", label: "Done" },
}

function layoutColumns<T extends { startMin: number; endMin: number }>(items: T[]): (T & { col: number; cols: number })[] {
  const sorted = [...items].sort((a, b) => a.startMin - b.startMin || a.endMin - b.endMin)
  const out: (T & { col: number; cols: number })[] = []
  let cluster: (T & { col: number })[] = []
  let clusterEnd = -1
  const flush = () => {
    const cols = cluster.reduce((mx, block) => Math.max(mx, block.col + 1), 0)
    cluster.forEach((block) => out.push({ ...block, cols }))
    cluster = []
    clusterEnd = -1
  }
  for (const block of sorted) {
    if (cluster.length && block.startMin >= clusterEnd) flush()
    const used = new Set(cluster.filter((candidate) => candidate.endMin > block.startMin).map((candidate) => candidate.col))
    let col = 0
    while (used.has(col)) col += 1
    cluster.push({ ...block, col })
    clusterEnd = Math.max(clusterEnd < 0 ? block.endMin : clusterEnd, block.endMin)
  }
  if (cluster.length) flush()
  return out
}

interface CalendarViewProps {
  viewMode: "day" | "week"
  selectedDate: string
  projects: Project[]
  sections: Section[]
  tasks: Task[]
  taskWorkSessions: TaskWorkSession[]
  meetItems: MeetItem[]
  selectedProjectIds: string[]
  selectedTaskId: string | null
  selectedMeetId: string | null
  showDone: boolean
  canSchedule: boolean
  createMeetDisabled?: boolean
  onSelectTask: (taskId: string) => void
  onSelectMeet: (meetId: string) => void
  onPickDay: (dateKey: string) => void
  onCreateTaskAtSlot: (dateKey: string, startMin: number) => void
  onCreateMeetAtSlot: (dateKey: string, startMin: number) => void
  onCreateTaskWorkSession: (taskId: string, startUtc: string, endUtc: string) => void
  onUpdateTaskWorkSession: (sessionId: string, startUtc: string, endUtc: string) => void
  onRequestDeleteTaskWorkSession: (sessionId: string) => void
  onSetTaskStatus: (taskId: string, status: Status) => void
  onMoveMeet: (meetId: string, startsAtUtc: string, durationMinutes: number) => void
  onRequestDeleteMeet: (meetId: string) => void
}

export function CalendarView({
  viewMode,
  selectedDate,
  projects,
  sections,
  tasks,
  taskWorkSessions,
  meetItems,
  selectedProjectIds,
  selectedTaskId,
  selectedMeetId,
  showDone,
  canSchedule,
  createMeetDisabled = false,
  onSelectTask,
  onSelectMeet,
  onPickDay,
  onCreateTaskAtSlot,
  onCreateMeetAtSlot,
  onCreateTaskWorkSession,
  onUpdateTaskWorkSession,
  onRequestDeleteTaskWorkSession,
  onSetTaskStatus,
  onMoveMeet,
  onRequestDeleteMeet,
}: CalendarViewProps) {
  const today = todayKey()
  const [nowMin, setNowMin] = useState(() => {
    const now = new Date()
    return now.getHours() * 60 + now.getMinutes()
  })

  useEffect(() => {
    const id = setInterval(() => {
      const now = new Date()
      setNowMin(now.getHours() * 60 + now.getMinutes())
    }, 60_000)
    return () => clearInterval(id)
  }, [])

  const rootRef = useRef<HTMLDivElement>(null)
  const scrollViewportRef = useRef<HTMLDivElement>(null)
  const dayGridRef = useRef<HTMLDivElement>(null)
  const dragRef = useRef<DragState | null>(null)
  const activationGuardRef = useRef(new CalendarActivationGuard())

  const [poolWidth, setPoolWidth] = useState(224)
  const [poolFilter, setPoolFilter] = useState<PlanningPoolFilter>("ACTIVE")
  const [poolDropActive, setPoolDropActive] = useState(false)
  const [ghost, setGhost] = useState<{ startMin: number; endMin: number } | null>(null)
  const [weekGhost, setWeekGhost] = useState<{ dayIso: string; startMin: number; endMin: number } | null>(null)
  const [newBlock, setNewBlock] = useState<{ taskId: string; dateKey: string; startMin: number } | null>(null)
  const [menu, setMenu] = useState<CalendarMenuState | null>(null)

  const projectMap = useMemo(() => new Map(projects.map((project) => [project.id, project])), [projects])
  const sectionMap = useMemo(() => new Map(sections.map((section) => [section.id, section])), [sections])
  const taskMap = useMemo(() => new Map(tasks.map((task) => [task.id, task])), [tasks])
  const allSelected = selectedProjectIds.length === projects.length
  const inScope = (projectId: string) => allSelected || selectedProjectIds.includes(projectId)

  const buildDay = (dateKey: string): { blocks: Block[]; markers: Marker[] } => {
    const blocks: Block[] = []
    const markers: Marker[] = []

    taskWorkSessions.forEach((session) => {
      const task = taskMap.get(session.taskId)
      if (!task || !inScope(task.projectId)) return
      if (task.status === "DONE" && !showDone) return
      const startSlot = localSlotFromIso(session.startUtc)
      if (!startSlot || startSlot.dateKey !== dateKey) return
      const endSlot = localSlotFromIso(session.endUtc)
      const clipped = clipRangeToDay(
        startSlot.minutes,
        endSlot?.dateKey === dateKey ? endSlot.minutes : DAY_END_MIN,
      )
      blocks.push({
        key: `work-${session.id}`,
        kind: "WORK",
        title: task.title,
        projectId: task.projectId,
        sectionId: task.sectionId,
        dateKey,
        startMin: clipped.startMin,
        endMin: clipped.endMin,
        taskId: task.id,
        sessionId: session.id,
        status: task.status,
        selected: task.id === selectedTaskId,
      })
    })

    meetItems.forEach((meeting) => {
      if (meeting.date !== dateKey || !inScope(meeting.projectId)) return
      const clipped = clipRangeToDay(toMin(meeting.startTime), meetEndMin(meeting))
      blocks.push({
        key: `meet-${meeting.id}`,
        kind: "MEET",
        title: meeting.title,
        projectId: meeting.projectId,
        dateKey,
        startMin: clipped.startMin,
        endMin: clipped.endMin,
        meetId: meeting.id,
        location: meeting.location,
        hasLink: !!meeting.link,
        selected: meeting.id === selectedMeetId,
      })
    })

    tasks.forEach((task) => {
      if (!inScope(task.projectId)) return
      if (task.status === "DONE" && !showDone) return
      if (task.reminderDate === dateKey && task.reminderTime) {
        markers.push({
          key: `rem-${task.id}`,
          kind: "REMIND",
          title: task.title,
          projectId: task.projectId,
          min: clamp(toMin(task.reminderTime), DAY_START_MIN, DAY_END_MIN),
          taskId: task.id,
          selected: task.id === selectedTaskId,
        })
      }
      if (task.deadlineDate === dateKey) {
        markers.push({
          key: `dl-${task.id}`,
          kind: "DEADLINE",
          title: task.title,
          projectId: task.projectId,
          min: task.deadlineTime
            ? clamp(toMin(task.deadlineTime), DAY_START_MIN, DAY_END_MIN)
            : UNTIMED_DEADLINE_MIN,
          taskId: task.id,
          selected: task.id === selectedTaskId,
        })
      }
    })

    return { blocks, markers }
  }

  const todaySessionTotals = useMemo(() => {
    const totals = new Map<string, { blocks: number; minutes: number }>()
    taskWorkSessions.forEach((session) => {
      const start = localSlotFromIso(session.startUtc)
      const end = localSlotFromIso(session.endUtc)
      if (!start || !end || start.dateKey !== today || end.dateKey !== today) return
      const current = totals.get(session.taskId) ?? { blocks: 0, minutes: 0 }
      current.blocks += 1
      current.minutes += Math.max(0, end.minutes - start.minutes)
      totals.set(session.taskId, current)
    })
    return totals
  }, [taskWorkSessions, today])

  const scheduledTaskIds = useMemo(
    () => new Set(taskWorkSessions.map((session) => session.taskId)),
    [taskWorkSessions],
  )

  const planningTasks = useMemo(() => {
    const pool = tasks.filter((task) => inScope(task.projectId)).filter((task) => {
      if (poolFilter === "ALL") return true
      if (task.status === "DONE") return false
      if (poolFilter === "UNSCHEDULED") return !scheduledTaskIds.has(task.id)
      if (poolFilter === "TODAY") return todaySessionTotals.has(task.id)
      return true
    })
    return [
      ...pool.filter((task) => task.pinned || task.status === "FOCUS"),
      ...pool.filter((task) => !task.pinned && task.status !== "FOCUS"),
    ]
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tasks, allSelected, selectedProjectIds, poolFilter, scheduledTaskIds, todaySessionTotals])

  const hours = Array.from({ length: 25 }, (_, index) => index)
  const displayTitle = (block: Block) => block.title.trim() || (block.kind === "MEET" ? "Untitled MEET" : "Untitled task")

  useEffect(() => {
    const viewport = scrollViewportRef.current
    if (!viewport) return
    const now = new Date()
    const currentMinute = now.getHours() * 60 + now.getMinutes()
    const weekContainsToday = weekDayKeys(mondayOfWeekKey(selectedDate)).includes(today)
    const targetMinute = initialScrollMinute(viewMode, selectedDate, today, weekContainsToday, currentMinute)
    const frame = requestAnimationFrame(() => {
      viewport.scrollTop = initialScrollTop(targetMinute, viewport.clientHeight)
    })
    return () => cancelAnimationFrame(frame)
  }, [viewMode, selectedDate, today])

  const startPoolDrag = (event: React.DragEvent, taskId: string) => {
    activationGuardRef.current.completeManipulation()
    dragRef.current = { kind: "TASK", taskId, duration: DEFAULT_TASK_DURATION_MIN }
    event.dataTransfer.effectAllowed = "copyMove"
    event.dataTransfer.setData("text/plain", taskId)
  }

  const startBlockDrag = (event: React.DragEvent, block: Block) => {
    activationGuardRef.current.completeManipulation()
    const duration = block.endMin - block.startMin
    if (block.kind === "MEET") {
      if (!block.meetId) return
      dragRef.current = { kind: "MEET", meetId: block.meetId, duration }
      event.dataTransfer.effectAllowed = "move"
      event.dataTransfer.setData("text/plain", block.meetId)
      return
    }
    if (!block.taskId || !block.sessionId) return
    dragRef.current = { kind: "TASK_SESSION", taskId: block.taskId, sessionId: block.sessionId, duration }
    event.dataTransfer.effectAllowed = "move"
    event.dataTransfer.setData("text/plain", block.taskId)
  }

  const openSlotMenu = (event: React.MouseEvent<HTMLElement>, dateKey: string) => {
    if (!canSchedule || (event.target as HTMLElement).closest("[data-calendar-block='true']")) return
    event.preventDefault()
    event.stopPropagation()
    const startMin = minuteFromPointer(event.currentTarget.getBoundingClientRect().top, event.clientY)
    setMenu({ kind: "slot", x: event.clientX, y: event.clientY, dateKey, startMin })
  }

  const openBlockMenu = (event: React.MouseEvent, block: Block) => {
    event.preventDefault()
    event.stopPropagation()
    setMenu({ kind: "block", x: event.clientX, y: event.clientY, block })
  }

  const resizeBlock = (block: Block, dateKey: string, startMin: number, endMin: number) => {
    activationGuardRef.current.completeManipulation()
    const startsAtUtc = isoFromLocalDateTime(dateKey, Math.floor(startMin / 60), startMin % 60)
    const duration = Math.max(MIN_DURATION_MIN, endMin - startMin)
    if (block.kind === "MEET" && block.meetId) {
      onMoveMeet(block.meetId, startsAtUtc, duration)
      return
    }
    if (block.taskId && block.sessionId) {
      const endsAtUtc = isoFromLocalDateTime(dateKey, Math.floor(endMin / 60), endMin % 60)
      onUpdateTaskWorkSession(block.sessionId, startsAtUtc, endsAtUtc)
    }
  }

  const onDayDragOver = (event: React.DragEvent) => {
    const drag = dragRef.current
    const grid = dayGridRef.current
    if (!drag || !grid) return
    event.preventDefault()
    event.dataTransfer.dropEffect = drag.kind === "TASK" ? "copy" : "move"
    const start = minuteFromPointer(grid.getBoundingClientRect().top, event.clientY, drag.duration)
    setGhost(rangeForMove(start, drag.duration))
  }

  const onDayDrop = (event: React.DragEvent) => {
    event.preventDefault()
    const drag = dragRef.current
    dragRef.current = null
    setGhost(null)
    const grid = dayGridRef.current
    if (!drag || !canSchedule || !grid) return
    const rawStart = minuteFromPointer(grid.getBoundingClientRect().top, event.clientY, drag.duration)
    const range = rangeForMove(rawStart, drag.duration)
    const startsAtUtc = isoFromLocalDateTime(selectedDate, Math.floor(range.startMin / 60), range.startMin % 60)
    if (drag.kind === "MEET") {
      onMoveMeet(drag.meetId, startsAtUtc, drag.duration)
      return
    }
    const endsAtUtc = isoFromLocalDateTime(selectedDate, Math.floor(range.endMin / 60), range.endMin % 60)
    if (drag.kind === "TASK_SESSION") {
      onUpdateTaskWorkSession(drag.sessionId, startsAtUtc, endsAtUtc)
    } else {
      onCreateTaskWorkSession(drag.taskId, startsAtUtc, endsAtUtc)
      setNewBlock({ taskId: drag.taskId, dateKey: selectedDate, startMin: range.startMin })
    }
  }

  const onColumnDragOver = (event: React.DragEvent, dayIso: string) => {
    const drag = dragRef.current
    if (!drag || !canSchedule) return
    event.preventDefault()
    event.dataTransfer.dropEffect = drag.kind === "TASK" ? "copy" : "move"
    const start = minuteFromPointer((event.currentTarget as HTMLElement).getBoundingClientRect().top, event.clientY, drag.duration)
    const range = rangeForMove(start, drag.duration)
    setWeekGhost({ dayIso, ...range })
  }

  const onColumnDrop = (event: React.DragEvent, dayIso: string) => {
    event.preventDefault()
    const drag = dragRef.current
    dragRef.current = null
    setWeekGhost(null)
    if (!drag || !canSchedule) return
    const rawStart = minuteFromPointer((event.currentTarget as HTMLElement).getBoundingClientRect().top, event.clientY, drag.duration)
    const range = rangeForMove(rawStart, drag.duration)
    const startsAtUtc = isoFromLocalDateTime(dayIso, Math.floor(range.startMin / 60), range.startMin % 60)
    if (drag.kind === "MEET") {
      onMoveMeet(drag.meetId, startsAtUtc, drag.duration)
      return
    }
    const endsAtUtc = isoFromLocalDateTime(dayIso, Math.floor(range.endMin / 60), range.endMin % 60)
    if (drag.kind === "TASK_SESSION") {
      onUpdateTaskWorkSession(drag.sessionId, startsAtUtc, endsAtUtc)
    } else {
      onCreateTaskWorkSession(drag.taskId, startsAtUtc, endsAtUtc)
    }
  }

  const onDragCleanup = () => {
    dragRef.current = null
    setGhost(null)
    setWeekGhost(null)
    setPoolDropActive(false)
  }

  const beginBlockPointer = () => activationGuardRef.current.beginPointerInteraction()
  const activateBlock = (event: React.MouseEvent, block: Block) => {
    if (!activationGuardRef.current.shouldActivate(event.detail)) return
    if (block.kind === "MEET" && block.meetId) onSelectMeet(block.meetId)
    else if (block.taskId) onSelectTask(block.taskId)
  }

  const onPoolDrop = (event: React.DragEvent) => {
    event.preventDefault()
    setPoolDropActive(false)
    const drag = dragRef.current
    dragRef.current = null
    if (drag?.kind === "TASK_SESSION" && canSchedule) onRequestDeleteTaskWorkSession(drag.sessionId)
  }

  useEffect(() => {
    if (!newBlock) return
    const handler = () => setNewBlock(null)
    window.addEventListener("click", handler, { capture: true, once: true })
    return () => window.removeEventListener("click", handler, { capture: true })
  }, [newBlock])

  useEffect(() => {
    if (!menu) return
    const close = () => setMenu(null)
    window.addEventListener("click", close)
    window.addEventListener("blur", close)
    return () => {
      window.removeEventListener("click", close)
      window.removeEventListener("blur", close)
    }
  }, [menu])

  const applyDuration = (sessionId: string, dateKey: string, startMin: number, durationMin: number) => {
    if (!durationFits(startMin, durationMin)) return
    const endMin = startMin + durationMin
    onUpdateTaskWorkSession(
      sessionId,
      isoFromLocalDateTime(dateKey, Math.floor(startMin / 60), startMin % 60),
      isoFromLocalDateTime(dateKey, Math.floor(endMin / 60), endMin % 60),
    )
    setNewBlock(null)
  }

  const startPoolResize = (event: React.MouseEvent) => {
    event.preventDefault()
    const onMove = (moveEvent: MouseEvent) => {
      const left = rootRef.current?.getBoundingClientRect().left ?? 0
      setPoolWidth(clamp(moveEvent.clientX - left, 168, 380))
    }
    const onUp = () => {
      window.removeEventListener("mousemove", onMove)
      window.removeEventListener("mouseup", onUp)
      document.body.style.cursor = ""
      document.body.style.userSelect = ""
    }
    document.body.style.cursor = "col-resize"
    document.body.style.userSelect = "none"
    window.addEventListener("mousemove", onMove)
    window.addEventListener("mouseup", onUp)
  }

  const canCreateTaskAtMenu = menu?.kind === "slot" && durationFits(menu.startMin, DEFAULT_TASK_DURATION_MIN)
  const canCreateMeetAtMenu = menu?.kind === "slot" && durationFits(menu.startMin, DEFAULT_MEET_DURATION_MIN)
  const canAddSessionAtMenu = menu?.kind === "block" && menu.block.kind === "WORK" && durationFits(menu.block.endMin, DEFAULT_TASK_DURATION_MIN)

  return (
    <div ref={rootRef} data-cal-root className="flex h-full overflow-hidden bg-background">
      <div
        className={cn("relative flex shrink-0 flex-col border-r border-border bg-sidebar/40", poolDropActive && "bg-primary/5")}
        style={{ width: poolWidth }}
        onDragOver={(event) => {
          if (dragRef.current?.kind === "TASK_SESSION") {
            event.preventDefault()
            setPoolDropActive(true)
          }
        }}
        onDragLeave={() => setPoolDropActive(false)}
        onDrop={onPoolDrop}
      >
        <div className="flex shrink-0 items-center gap-2 border-b border-border px-3 py-2.5">
          <span className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">Planning pool</span>
          <span className="rounded-full bg-muted px-1.5 py-0.5 text-[10px] font-medium tabular-nums text-muted-foreground">{planningTasks.length}</span>
        </div>
        <div className="grid shrink-0 grid-cols-2 gap-1 border-b border-border px-2 py-2" aria-label="Planning pool filters">
          {PLANNING_POOL_FILTERS.map((filter) => {
            const selected = poolFilter === filter.id
            return (
              <button
                key={filter.id}
                type="button"
                aria-pressed={selected}
                title={filter.title}
                onClick={() => setPoolFilter(filter.id)}
                className={cn(
                  "min-w-0 rounded-md border px-1.5 py-1 text-[10px] font-medium transition-colors",
                  selected
                    ? "border-primary/40 bg-primary/10 text-foreground"
                    : "border-border/60 bg-card/50 text-muted-foreground hover:border-border hover:bg-accent/40 hover:text-foreground",
                )}
              >
                {filter.label}
              </button>
            )
          })}
        </div>
        <div className="min-h-0 flex-1 space-y-1.5 overflow-y-auto p-2">
          {planningTasks.length === 0 ? (
            <p className="px-1 py-3 text-[11px] text-muted-foreground">{PLANNING_POOL_EMPTY_COPY[poolFilter]}</p>
          ) : (
            planningTasks.map((task) => {
              const project = projectMap.get(task.projectId)
              const section = sectionMap.get(task.sectionId ?? "")
              const status = STATUS_STYLES[task.status] ?? STATUS_STYLES.TODO
              const todayTotal = todaySessionTotals.get(task.id)
              const hasAnySession = scheduledTaskIds.has(task.id)
              const scheduleLabel = todayTotal
                ? `Scheduled today · ${todayTotal.blocks} ${todayTotal.blocks === 1 ? "block" : "blocks"} · ${fmtDur(todayTotal.minutes)} total`
                : hasAnySession ? "No session today" : "Unscheduled"
              const taskCanSchedule = canSchedule && task.status !== "DONE"
              return (
                <div
                  key={task.id}
                  draggable={taskCanSchedule}
                  onDragStart={(event) => startPoolDrag(event, task.id)}
                  onDragEnd={onDragCleanup}
                  onPointerDown={beginBlockPointer}
                  onClick={(event) => {
                    if (activationGuardRef.current.shouldActivate(event.detail)) onSelectTask(task.id)
                  }}
                  className={cn(
                    "group flex flex-col gap-1.5 rounded-lg border bg-card p-2 text-left transition-colors",
                    taskCanSchedule ? "cursor-grab active:cursor-grabbing" : "cursor-pointer",
                    task.id === selectedTaskId ? "border-primary/50 bg-primary/8" : "border-border hover:border-border/80 hover:bg-accent/40",
                    task.status === "DONE" && "opacity-65",
                  )}
                >
                  <div className="flex items-center gap-1.5">
                    {taskCanSchedule && <GripVertical className="size-3 shrink-0 text-muted-foreground/50 group-hover:text-muted-foreground" />}
                    <span className={cn("rounded px-1 py-0.5 text-[9px] font-bold uppercase tracking-wide", status.bg, status.text)}>{status.label}</span>
                    {task.pinned && <span className="ml-auto text-[9px] font-semibold uppercase tracking-wide text-muted-foreground">pinned</span>}
                  </div>
                  <p className="line-clamp-2 text-[12px] font-medium leading-snug text-foreground">{task.title}</p>
                  <div className="flex items-center gap-1 text-[10px] text-muted-foreground">
                    <span className="size-1.5 shrink-0 rounded-full" style={{ backgroundColor: project?.color }} />
                    <span className="truncate">{project?.name}{section ? ` / ${section.name}` : ""}</span>
                  </div>
                  <span className={cn("text-[10px]", todayTotal ? "text-primary/80" : "text-muted-foreground/70")}>{scheduleLabel}</span>
                </div>
              )
            })
          )}
        </div>
        {canSchedule && (
          <div className="shrink-0 border-t border-border px-2 py-1.5 text-[10px] text-muted-foreground">
            {poolDropActive ? "Drop to remove this calendar block" : "Active tasks stay until DONE · drag to add a session"}
          </div>
        )}
        <div onMouseDown={startPoolResize} title="Drag to resize" className="absolute right-0 top-0 z-20 h-full w-1.5 translate-x-1/2 cursor-col-resize bg-transparent transition-colors hover:bg-primary/40" />
      </div>

      <div ref={scrollViewportRef} className="min-w-0 flex-1 overflow-y-auto overscroll-contain">
        {viewMode === "day" ? (
          <DayGrid
            hours={hours}
            {...buildDay(selectedDate)}
            isToday={selectedDate === today}
            nowMin={nowMin}
            projectMap={projectMap}
            sectionMap={sectionMap}
            canSchedule={canSchedule}
            ghost={ghost}
            newBlock={newBlock}
            gridRef={dayGridRef}
            onDayDragOver={onDayDragOver}
            onDayDragLeave={() => setGhost(null)}
            onDayDrop={onDayDrop}
            onStartBlockDrag={startBlockDrag}
            onDragCleanup={onDragCleanup}
            onEmptyContextMenu={(event) => openSlotMenu(event, selectedDate)}
            onBlockContextMenu={openBlockMenu}
            onBlockPointerDown={beginBlockPointer}
            onBlockActivate={activateBlock}
            onSelectTask={onSelectTask}
            onRequestDeleteTaskWorkSession={onRequestDeleteTaskWorkSession}
            onResizeBlock={(block, startMin, endMin) => resizeBlock(block, selectedDate, startMin, endMin)}
            onApplyDuration={applyDuration}
            displayTitle={displayTitle}
          />
        ) : (
          <WeekGrid
            hours={hours}
            selectedDate={selectedDate}
            today={today}
            nowMin={nowMin}
            buildDay={buildDay}
            projectMap={projectMap}
            canSchedule={canSchedule}
            weekGhost={weekGhost}
            onColumnDragOver={onColumnDragOver}
            onColumnDrop={onColumnDrop}
            onStartBlockDrag={startBlockDrag}
            onDragCleanup={onDragCleanup}
            onEmptyContextMenu={openSlotMenu}
            onBlockContextMenu={openBlockMenu}
            onResizeBlock={resizeBlock}
            onBlockPointerDown={beginBlockPointer}
            onBlockActivate={activateBlock}
            onSelectTask={onSelectTask}
            onPickDay={onPickDay}
            displayTitle={displayTitle}
          />
        )}
      </div>

      {menu && (
        <CalendarContextMenu
          menu={menu}
          onClose={() => setMenu(null)}
          onCreateTask={() => {
            if (menu.kind !== "slot" || !canCreateTaskAtMenu) return
            onCreateTaskAtSlot(menu.dateKey, menu.startMin)
            setMenu(null)
          }}
          onCreateMeet={() => {
            if (menu.kind !== "slot" || !canCreateMeetAtMenu) return
            onCreateMeetAtSlot(menu.dateKey, menu.startMin)
            setMenu(null)
          }}
          onOpenBlock={() => {
            if (menu.kind !== "block") return
            if (menu.block.kind === "MEET" && menu.block.meetId) onSelectMeet(menu.block.meetId)
            else if (menu.block.taskId) onSelectTask(menu.block.taskId)
            setMenu(null)
          }}
          onAddTaskSession={() => {
            if (menu.kind !== "block" || menu.block.kind !== "WORK" || !menu.block.taskId || !canAddSessionAtMenu) return
            const startMin = menu.block.endMin
            const endMin = startMin + DEFAULT_TASK_DURATION_MIN
            onCreateTaskWorkSession(
              menu.block.taskId,
              isoFromLocalDateTime(menu.block.dateKey, Math.floor(startMin / 60), startMin % 60),
              isoFromLocalDateTime(menu.block.dateKey, Math.floor(endMin / 60), endMin % 60),
            )
            onSelectTask(menu.block.taskId)
            setMenu(null)
          }}
          onRemoveBlock={() => {
            if (menu.kind !== "block") return
            if (menu.block.kind === "WORK" && menu.block.sessionId) onRequestDeleteTaskWorkSession(menu.block.sessionId)
            if (menu.block.kind === "MEET" && menu.block.meetId) onRequestDeleteMeet(menu.block.meetId)
            setMenu(null)
          }}
          onSetTaskStatus={(status) => {
            if (menu.kind === "block" && menu.block.kind === "WORK" && menu.block.taskId) onSetTaskStatus(menu.block.taskId, status)
            setMenu(null)
          }}
          canSchedule={canSchedule}
          canCreateTask={!!canCreateTaskAtMenu}
          canCreateMeet={canSchedule && !createMeetDisabled && !!canCreateMeetAtMenu}
          canAddTaskSession={!!canAddSessionAtMenu}
        />
      )}
    </div>
  )
}

function CalendarContextMenu({
  menu,
  onClose,
  onCreateTask,
  onCreateMeet,
  onOpenBlock,
  onAddTaskSession,
  onRemoveBlock,
  onSetTaskStatus,
  canSchedule,
  canCreateTask,
  canCreateMeet,
  canAddTaskSession,
}: {
  menu: CalendarMenuState
  onClose: () => void
  onCreateTask: () => void
  onCreateMeet: () => void
  onOpenBlock: () => void
  onAddTaskSession: () => void
  onRemoveBlock: () => void
  onSetTaskStatus: (status: Status) => void
  canSchedule: boolean
  canCreateTask: boolean
  canCreateMeet: boolean
  canAddTaskSession: boolean
}) {
  return (
    <div
      role="menu"
      className="fixed z-[100] min-w-40 rounded-lg border border-border bg-popover p-1.5 text-[12px] shadow-xl"
      style={{ left: menu.x, top: menu.y }}
      onClick={(event) => event.stopPropagation()}
      onContextMenu={(event) => event.preventDefault()}
    >
      {menu.kind === "slot" ? (
        <>
          <button
            type="button"
            role="menuitem"
            onClick={onCreateTask}
            disabled={!canCreateTask}
            title={canCreateTask ? undefined : "A 60-minute task session does not fit before midnight."}
            className="flex h-7 w-full items-center rounded-md px-2 text-left text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50"
          >
            Create task
          </button>
          <button
            type="button"
            role="menuitem"
            onClick={onCreateMeet}
            disabled={!canCreateMeet}
            title={canCreateMeet ? undefined : "A 30-minute MEET does not fit before midnight, or creation is unavailable."}
            className="flex h-7 w-full items-center rounded-md px-2 text-left text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50"
          >
            Create MEET
          </button>
        </>
      ) : (
        <>
          <button type="button" role="menuitem" onClick={onOpenBlock} className="flex h-7 w-full items-center rounded-md px-2 text-left text-muted-foreground transition-colors hover:bg-accent hover:text-foreground">
            Open {menu.block.kind === "MEET" ? "MEET" : "task"} details
          </button>
          {menu.block.kind === "WORK" && (
            <>
              <button
                type="button"
                role="menuitem"
                onClick={onAddTaskSession}
                disabled={!canSchedule || !canAddTaskSession}
                title={canAddTaskSession ? undefined : "There is not enough room for another 60-minute session before midnight."}
                className="flex h-7 w-full items-center rounded-md px-2 text-left text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50"
              >
                Add another session today
              </button>
              <div className="my-1 border-t border-border" />
              <button type="button" role="menuitem" onClick={() => onSetTaskStatus("DONE")} disabled={!canSchedule} className="flex h-7 w-full items-center rounded-md px-2 text-left text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50">Complete task</button>
              <button type="button" role="menuitem" onClick={() => onSetTaskStatus("FOCUS")} disabled={!canSchedule} className="flex h-7 w-full items-center rounded-md px-2 text-left text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50">Set FOCUS</button>
              <button type="button" role="menuitem" onClick={() => onSetTaskStatus("WAIT")} disabled={!canSchedule} className="flex h-7 w-full items-center rounded-md px-2 text-left text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50">Set WAIT</button>
            </>
          )}
          <div className="my-1 border-t border-border" />
          <button type="button" role="menuitem" onClick={onRemoveBlock} disabled={!canSchedule} className="flex h-7 w-full items-center rounded-md px-2 text-left text-destructive transition-colors hover:bg-destructive/10 disabled:cursor-not-allowed disabled:opacity-50">
            {menu.block.kind === "MEET" ? "Delete MEET" : "Remove this calendar block"}
          </button>
        </>
      )}
      <button type="button" role="menuitem" onClick={onClose} className="mt-1 flex h-7 w-full items-center rounded-md px-2 text-left text-muted-foreground transition-colors hover:bg-accent hover:text-foreground">Cancel</button>
    </div>
  )
}

function WorkdayBand() {
  return (
    <div
      className="pointer-events-none absolute inset-x-0 border-y border-border/20 bg-muted/10"
      style={{
        top: WORKDAY_START_MIN * PX_PER_MIN,
        height: (WORKDAY_END_MIN - WORKDAY_START_MIN) * PX_PER_MIN,
      }}
    />
  )
}

function DayGrid({
  hours,
  blocks,
  markers,
  isToday,
  nowMin,
  projectMap,
  sectionMap,
  canSchedule,
  ghost,
  newBlock,
  gridRef,
  onDayDragOver,
  onDayDragLeave,
  onDayDrop,
  onStartBlockDrag,
  onDragCleanup,
  onEmptyContextMenu,
  onBlockContextMenu,
  onBlockPointerDown,
  onBlockActivate,
  onSelectTask,
  onRequestDeleteTaskWorkSession,
  onResizeBlock,
  onApplyDuration,
  displayTitle,
}: {
  hours: number[]
  blocks: Block[]
  markers: Marker[]
  isToday: boolean
  nowMin: number
  projectMap: Map<string, Project>
  sectionMap: Map<string, Section>
  canSchedule: boolean
  ghost: { startMin: number; endMin: number } | null
  newBlock: { taskId: string; dateKey: string; startMin: number } | null
  gridRef: React.RefObject<HTMLDivElement | null>
  onDayDragOver: (event: React.DragEvent) => void
  onDayDragLeave: () => void
  onDayDrop: (event: React.DragEvent) => void
  onStartBlockDrag: (event: React.DragEvent, block: Block) => void
  onDragCleanup: () => void
  onEmptyContextMenu: (event: React.MouseEvent<HTMLElement>) => void
  onBlockContextMenu: (event: React.MouseEvent, block: Block) => void
  onBlockPointerDown: () => void
  onBlockActivate: (event: React.MouseEvent, block: Block) => void
  onSelectTask: (id: string) => void
  onRequestDeleteTaskWorkSession: (sessionId: string) => void
  onResizeBlock: (block: Block, startMin: number, endMin: number) => void
  onApplyDuration: (sessionId: string, dateKey: string, startMin: number, durationMin: number) => void
  displayTitle: (block: Block) => string
}) {
  const laid = layoutColumns(blocks)
  const isEmpty = blocks.length === 0 && markers.length === 0
  const gaps = useMemo(() => {
    const busy = blocks
      .map((block) => [clamp(block.startMin, DAY_START_MIN, DAY_END_MIN), clamp(block.endMin, DAY_START_MIN, DAY_END_MIN)] as [number, number])
      .filter(([start, end]) => end > start)
      .sort((a, b) => a[0] - b[0])
    const merged: [number, number][] = []
    busy.forEach(([start, end]) => {
      const last = merged[merged.length - 1]
      if (last && start <= last[1]) last[1] = Math.max(last[1], end)
      else merged.push([start, end])
    })
    const out: { start: number; end: number }[] = []
    let cursor = DAY_START_MIN
    merged.forEach(([start, end]) => {
      if (start - cursor >= 30) out.push({ start: cursor, end: start })
      cursor = Math.max(cursor, end)
    })
    if (DAY_END_MIN - cursor >= 30) out.push({ start: cursor, end: DAY_END_MIN })
    return out
  }, [blocks])

  const [resizing, setResizing] = useState<{
    block: Block
    edge: "top" | "bottom"
    startMouseY: number
    origStart: number
    origEnd: number
    curStart: number
    curEnd: number
  } | null>(null)

  const startResize = (event: React.MouseEvent, block: Block, edge: "top" | "bottom") => {
    event.stopPropagation()
    event.preventDefault()
    setResizing({ block, edge, startMouseY: event.clientY, origStart: block.startMin, origEnd: block.endMin, curStart: block.startMin, curEnd: block.endMin })
    document.body.style.cursor = "ns-resize"
    document.body.style.userSelect = "none"
  }

  useEffect(() => {
    if (!resizing) return
    const onMove = (event: MouseEvent) => {
      const deltaMin = snapMinute((event.clientY - resizing.startMouseY) / PX_PER_MIN, SNAP_MIN)
      setResizing((current) => {
        if (!current) return current
        const range = resizeRange(current.origStart, current.origEnd, current.edge, deltaMin)
        return { ...current, curStart: range.startMin, curEnd: range.endMin }
      })
    }
    const onUp = () => {
      setResizing((current) => {
        if (current) onResizeBlock(current.block, current.curStart, current.curEnd)
        return null
      })
      document.body.style.cursor = ""
      document.body.style.userSelect = ""
    }
    window.addEventListener("mousemove", onMove)
    window.addEventListener("mouseup", onUp)
    return () => {
      window.removeEventListener("mousemove", onMove)
      window.removeEventListener("mouseup", onUp)
    }
  }, [resizing, onResizeBlock])

  const laidResized = laid.map((block) => resizing && block.key === resizing.block.key
    ? { ...block, startMin: resizing.curStart, endMin: resizing.curEnd }
    : block)

  return (
    <div className="flex px-4 py-4">
      <div className="relative w-12 shrink-0 select-none" style={{ height: GRID_HEIGHT }}>
        {hours.map((hour) => (
          <div
            key={hour}
            className={cn("absolute pr-2 text-right text-[10px] font-medium tabular-nums text-muted-foreground", hourLabelClass(hour))}
            style={{ top: hour * 60 * PX_PER_MIN, right: 0 }}
          >
            {String(hour).padStart(2, "0")}:00
          </div>
        ))}
      </div>

      <div
        ref={gridRef}
        className="relative flex-1 rounded-lg border border-border/40 bg-card/10"
        style={{ height: GRID_HEIGHT }}
        onContextMenu={onEmptyContextMenu}
        onDragOver={onDayDragOver}
        onDragLeave={onDayDragLeave}
        onDrop={onDayDrop}
      >
        <WorkdayBand />
        {hours.map((hour) => <div key={hour} className="pointer-events-none absolute inset-x-0 border-t border-border/40" style={{ top: hour * 60 * PX_PER_MIN }} />)}
        {hours.slice(0, -1).map((hour) => <div key={`${hour}h`} className="pointer-events-none absolute inset-x-0 border-t border-dashed border-border/20" style={{ top: (hour * 60 + 30) * PX_PER_MIN }} />)}

        {gaps.map((gap, index) => (
          <div key={index} className="pointer-events-none absolute inset-x-0 flex items-center justify-center" style={{ top: gap.start * PX_PER_MIN, height: (gap.end - gap.start) * PX_PER_MIN }}>
            <span className="rounded-full bg-muted/30 px-2 py-0.5 text-[10px] text-muted-foreground/60">Free · {fmtDur(gap.end - gap.start)}</span>
          </div>
        ))}

        {isToday && (
          <div className="pointer-events-none absolute inset-x-0 z-20 flex items-center" style={{ top: nowMin * PX_PER_MIN }}>
            <span className="-ml-1 size-2 rounded-full bg-now-marker" />
            <span className="h-px flex-1 bg-now-marker" />
            <span className="rounded-l bg-now-marker px-1 py-0.5 text-[9px] font-bold text-background">{fmtMin(nowMin)}</span>
          </div>
        )}

        {isEmpty && !ghost && (
          <div className="pointer-events-none absolute inset-x-0 flex flex-col items-center justify-center gap-1.5 text-center" style={{ top: WORKDAY_START_MIN * PX_PER_MIN, height: (WORKDAY_END_MIN - WORKDAY_START_MIN) * PX_PER_MIN }}>
            <p className="text-sm font-medium text-muted-foreground">Nothing planned on this day</p>
            <p className="max-w-xs text-[11px] text-muted-foreground/70">{canSchedule ? "Drag a task from the Planning pool onto any time of day." : "Planned work, reminders and deadlines land here."}</p>
          </div>
        )}

        {ghost && (
          <div className="pointer-events-none absolute inset-x-2 z-10 rounded-lg border-2 border-dashed border-primary/50 bg-primary/8" style={{ top: ghost.startMin * PX_PER_MIN, height: (ghost.endMin - ghost.startMin) * PX_PER_MIN, right: MARKER_RAIL }}>
            <span className="block px-2 pt-1 text-[11px] font-medium text-primary/70">{fmtMin(ghost.startMin)} - {fmtMin(ghost.endMin)}</span>
          </div>
        )}

        <div className="absolute inset-y-0 left-0" style={{ right: MARKER_RAIL }}>
          {laidResized.map((block) => {
            const project = projectMap.get(block.projectId)
            const section = block.sectionId ? sectionMap.get(block.sectionId) : undefined
            const top = block.startMin * PX_PER_MIN
            const height = Math.max(28, (block.endMin - block.startMin) * PX_PER_MIN)
            const widthPct = 100 / block.cols
            const isMeet = block.kind === "MEET"
            const status = block.status ? STATUS_STYLES[block.status] : null
            const isNew = block.kind === "WORK" && !!block.sessionId && newBlock !== null && newBlock.taskId === block.taskId && newBlock.dateKey === block.dateKey && newBlock.startMin === block.startMin
            const draggable = canSchedule && !resizing
            return (
              <div key={block.key} className="group absolute" style={{ top, height, left: `calc(${block.col * widthPct}% + ${block.col * 4}px)`, width: `calc(${widthPct}% - 4px)` }}>
                <button
                  type="button"
                  data-calendar-block="true"
                  draggable={draggable}
                  onDragStart={(event) => onStartBlockDrag(event, block)}
                  onDragEnd={onDragCleanup}
                  onContextMenu={(event) => onBlockContextMenu(event, block)}
                  onPointerDown={onBlockPointerDown}
                  onClick={(event) => onBlockActivate(event, block)}
                  className={cn(
                    "absolute inset-0 overflow-hidden rounded-lg border text-left transition-colors",
                    isMeet
                      ? block.selected ? "border-status-meet/70 bg-status-meet/20 ring-2 ring-status-meet/40" : "border-status-meet/40 bg-status-meet/12 hover:bg-status-meet/22"
                      : block.selected ? "border-border bg-card ring-2 ring-primary/50" : "border-border bg-card hover:bg-accent/50",
                    draggable && "cursor-grab active:cursor-grabbing",
                    block.status === "DONE" && "opacity-60",
                  )}
                  style={{ borderLeftWidth: isMeet ? undefined : 3, borderLeftColor: isMeet ? undefined : project?.color }}
                >
                  <div className="flex h-full flex-col gap-0.5 px-2 py-1.5">
                    {height >= 42 && <div className="flex items-center gap-1.5"><span className="text-[9px] font-semibold tabular-nums text-muted-foreground">{fmtMin(block.startMin)}-{fmtMin(block.endMin)}</span><span className="text-[9px] text-muted-foreground/70">· {fmtDur(block.endMin - block.startMin)}</span></div>}
                    <span className={cn("font-semibold leading-snug text-foreground", height < 52 ? "line-clamp-1 text-[11px]" : "line-clamp-2 text-[12px]")}>{displayTitle(block)}</span>
                    {height > 50 && (
                      <div className="mt-auto flex items-center gap-1.5">
                        {!isMeet && <><span className="size-1.5 shrink-0 rounded-full" style={{ backgroundColor: project?.color }} /><span className="truncate text-[10px] text-muted-foreground">{project?.name}{section ? ` / ${section.name}` : ""}</span></>}
                        {isMeet && block.location && <span className="flex items-center gap-0.5 text-[10px] text-muted-foreground"><MapPin className="size-2.5" />{block.location}</span>}
                        {isMeet && block.hasLink && <Link2 className="size-2.5 text-muted-foreground" />}
                        {status && !isMeet && <span className={cn("ml-auto rounded px-1 py-0.5 text-[8px] font-bold uppercase tracking-wide", status.bg, status.text)}>{status.label}</span>}
                      </div>
                    )}
                  </div>
                </button>

                {canSchedule && (
                  <>
                    <div onMouseDown={(event) => startResize(event, block, "top")} className="absolute inset-x-2 top-0 z-30 h-2 cursor-ns-resize rounded-t opacity-0 transition-opacity hover:bg-primary/20 group-hover:opacity-100" />
                    <div onMouseDown={(event) => startResize(event, block, "bottom")} className="absolute inset-x-2 bottom-0 z-30 h-2 cursor-ns-resize rounded-b opacity-0 transition-opacity hover:bg-primary/20 group-hover:opacity-100" />
                    {block.kind === "WORK" && block.sessionId && <button type="button" onClick={(event) => { event.stopPropagation(); onRequestDeleteTaskWorkSession(block.sessionId!) }} title="Remove this calendar block" className="absolute right-1 top-1 z-30 flex size-4 items-center justify-center rounded bg-background/80 text-muted-foreground opacity-0 transition-opacity hover:text-destructive group-hover:opacity-100"><X className="size-3" /></button>}
                  </>
                )}

                {isNew && (
                  <div className="absolute left-0 top-full z-50 mt-1 flex gap-1 rounded-lg border border-border bg-popover p-1.5 shadow-lg" onClick={(event) => event.stopPropagation()}>
                    {QUICK_DURATIONS.map((duration) => {
                      const fits = durationFits(newBlock!.startMin, duration.min)
                      return (
                        <button
                          key={duration.label}
                          type="button"
                          disabled={!fits}
                          title={fits ? undefined : "This duration does not fit before midnight."}
                          onClick={() => onApplyDuration(block.sessionId!, block.dateKey, newBlock!.startMin, duration.min)}
                          className="flex items-center gap-1 rounded-md border border-border px-2 py-1 text-[11px] font-medium text-muted-foreground transition-colors hover:border-primary/50 hover:bg-primary/10 hover:text-foreground disabled:cursor-not-allowed disabled:opacity-40"
                        >
                          <Check className="size-2.5 opacity-0" />{duration.label}
                        </button>
                      )
                    })}
                  </div>
                )}
              </div>
            )
          })}
        </div>

        <div className="absolute inset-y-0 right-0" style={{ width: MARKER_RAIL }}>
          {markers.map((marker) => {
            const isDeadline = marker.kind === "DEADLINE"
            const top = clamp(marker.min, DAY_START_MIN, DAY_END_MIN) * PX_PER_MIN
            return (
              <button
                key={marker.key}
                type="button"
                onClick={() => onSelectTask(marker.taskId)}
                title={`${isDeadline ? "Deadline" : "Reminder"} · ${marker.title} · ${fmtMin(marker.min)}`}
                className={cn(
                  "absolute right-0 flex -translate-y-1/2 items-center gap-1 rounded-full border px-1.5 py-0.5 transition-colors",
                  isDeadline
                    ? marker.selected ? "border-status-deadline bg-status-deadline/20 ring-2 ring-status-deadline/40" : "border-status-deadline/40 bg-status-deadline/10 hover:bg-status-deadline/20"
                    : marker.selected ? "border-status-remind bg-status-remind/20 ring-2 ring-status-remind/40" : "border-status-remind/40 bg-status-remind/10 hover:bg-status-remind/20",
                )}
                style={{ top }}
              >
                {isDeadline ? <Flag className="size-2.5 shrink-0 fill-current text-status-deadline" /> : <Bell className="size-2.5 shrink-0 text-status-remind" />}
                <span className={cn("text-[9px] font-bold tabular-nums", isDeadline ? "text-status-deadline" : "text-status-remind")}>{fmtMin(marker.min)}</span>
              </button>
            )
          })}
        </div>
      </div>
    </div>
  )
}

function WeekGrid({
  hours,
  selectedDate,
  today,
  nowMin,
  buildDay,
  projectMap,
  canSchedule,
  weekGhost,
  onColumnDragOver,
  onColumnDrop,
  onStartBlockDrag,
  onDragCleanup,
  onEmptyContextMenu,
  onBlockContextMenu,
  onResizeBlock,
  onBlockPointerDown,
  onBlockActivate,
  onSelectTask,
  onPickDay,
  displayTitle,
}: {
  hours: number[]
  selectedDate: string
  today: string
  nowMin: number
  buildDay: (dateKey: string) => { blocks: Block[]; markers: Marker[] }
  projectMap: Map<string, Project>
  canSchedule: boolean
  weekGhost: { dayIso: string; startMin: number; endMin: number } | null
  onColumnDragOver: (event: React.DragEvent, dayIso: string) => void
  onColumnDrop: (event: React.DragEvent, dayIso: string) => void
  onStartBlockDrag: (event: React.DragEvent, block: Block) => void
  onDragCleanup: () => void
  onEmptyContextMenu: (event: React.MouseEvent<HTMLElement>, dateKey: string) => void
  onBlockContextMenu: (event: React.MouseEvent, block: Block) => void
  onResizeBlock: (block: Block, dateKey: string, startMin: number, endMin: number) => void
  onBlockPointerDown: () => void
  onBlockActivate: (event: React.MouseEvent, block: Block) => void
  onSelectTask: (id: string) => void
  onPickDay: (dateKey: string) => void
  displayTitle: (block: Block) => string
}) {
  const days = weekDayKeys(mondayOfWeekKey(selectedDate))
  const weekHasItems = days.some((day) => {
    const { blocks, markers } = buildDay(day)
    return blocks.length > 0 || markers.length > 0
  })

  const [resizing, setResizing] = useState<{
    block: Block
    dateKey: string
    edge: "top" | "bottom"
    startMouseY: number
    origStart: number
    origEnd: number
    curStart: number
    curEnd: number
  } | null>(null)

  const startResize = (event: React.MouseEvent, block: Block, dateKey: string, edge: "top" | "bottom") => {
    event.stopPropagation()
    event.preventDefault()
    setResizing({ block, dateKey, edge, startMouseY: event.clientY, origStart: block.startMin, origEnd: block.endMin, curStart: block.startMin, curEnd: block.endMin })
    document.body.style.cursor = "ns-resize"
    document.body.style.userSelect = "none"
  }

  useEffect(() => {
    if (!resizing) return
    const onMove = (event: MouseEvent) => {
      const deltaMin = snapMinute((event.clientY - resizing.startMouseY) / PX_PER_MIN, SNAP_MIN)
      setResizing((current) => {
        if (!current) return current
        const range = resizeRange(current.origStart, current.origEnd, current.edge, deltaMin)
        return { ...current, curStart: range.startMin, curEnd: range.endMin }
      })
    }
    const onUp = () => {
      setResizing((current) => {
        if (current) onResizeBlock(current.block, current.dateKey, current.curStart, current.curEnd)
        return null
      })
      document.body.style.cursor = ""
      document.body.style.userSelect = ""
    }
    window.addEventListener("mousemove", onMove)
    window.addEventListener("mouseup", onUp)
    return () => {
      window.removeEventListener("mousemove", onMove)
      window.removeEventListener("mouseup", onUp)
    }
  }, [resizing, onResizeBlock])

  return (
    <div className="relative flex px-4 py-4">
      <div className="relative w-12 shrink-0 select-none" style={{ height: GRID_HEIGHT, marginTop: 36 }}>
        {hours.map((hour) => (
          <div key={hour} className={cn("absolute pr-2 text-right text-[10px] tabular-nums text-muted-foreground", hourLabelClass(hour))} style={{ top: hour * 60 * PX_PER_MIN, right: 0 }}>
            {String(hour).padStart(2, "0")}
          </div>
        ))}
      </div>

      {!weekHasItems && (
        <div className="pointer-events-none absolute inset-x-0 flex flex-col items-center gap-1.5 text-center" style={{ left: 64, top: 36 + WORKDAY_START_MIN * PX_PER_MIN + 120 }}>
          <p className="text-sm font-medium text-muted-foreground">Nothing planned this week</p>
          <p className="text-[11px] text-muted-foreground/70">Drag a task onto any day and time, or pick a day to plan.</p>
        </div>
      )}

      <div className="grid flex-1 grid-cols-7 gap-1.5">
        {days.map((iso) => {
          const { blocks, markers } = buildDay(iso)
          const laid = layoutColumns(blocks).map((block) => resizing && resizing.dateKey === iso && resizing.block.key === block.key
            ? { ...block, startMin: resizing.curStart, endMin: resizing.curEnd }
            : block)
          const isToday = iso === today
          const isSelected = iso === selectedDate
          return (
            <div key={iso} className="flex flex-col">
              <button
                type="button"
                onClick={() => onPickDay(iso)}
                className={cn(
                  "sticky top-0 z-30 mb-1 flex h-8 items-center justify-center gap-1.5 rounded-md border bg-background text-[11px] font-semibold transition-colors",
                  isToday ? "border-primary/50 bg-primary/10 text-foreground" : isSelected ? "border-primary/30 bg-primary/5 text-foreground" : "border-border text-muted-foreground hover:text-foreground",
                )}
              >
                {formatWeekdayShort(iso)}<span className="tabular-nums">{formatDayNumber(iso)}</span>
              </button>
              <div
                className={cn("relative rounded-md border bg-card/20", isSelected ? "border-primary/30" : "border-border/50")}
                style={{ height: GRID_HEIGHT }}
                onContextMenu={(event) => onEmptyContextMenu(event, iso)}
                onDragOver={(event) => onColumnDragOver(event, iso)}
                onDrop={(event) => onColumnDrop(event, iso)}
              >
                <WorkdayBand />
                {hours.map((hour) => <div key={hour} className="pointer-events-none absolute inset-x-0 border-t border-border/30" style={{ top: hour * 60 * PX_PER_MIN }} />)}
                {isToday && <div className="pointer-events-none absolute inset-x-0 z-20 h-px bg-now-marker" style={{ top: nowMin * PX_PER_MIN }} />}
                {weekGhost?.dayIso === iso && (
                  <div className="pointer-events-none absolute inset-x-0.5 z-10 rounded border-2 border-dashed border-primary/50 bg-primary/10" style={{ top: weekGhost.startMin * PX_PER_MIN, height: (weekGhost.endMin - weekGhost.startMin) * PX_PER_MIN }}>
                    <span className="block truncate px-1 pt-0.5 text-[8px] font-semibold text-primary/80">{fmtMin(weekGhost.startMin)}</span>
                  </div>
                )}
                {laid.map((block) => {
                  const project = projectMap.get(block.projectId)
                  const isMeet = block.kind === "MEET"
                  const top = block.startMin * PX_PER_MIN
                  const height = Math.max(16, (block.endMin - block.startMin) * PX_PER_MIN)
                  const widthPct = 100 / block.cols
                  const draggable = canSchedule && !resizing
                  return (
                    <div key={block.key} className="group absolute" style={{ top, height, left: `calc(${block.col * widthPct}% + ${block.col * 2}px)`, width: `calc(${widthPct}% - 2px)` }}>
                      <button
                        type="button"
                        data-calendar-block="true"
                        draggable={draggable}
                        onDragStart={(event) => onStartBlockDrag(event, block)}
                        onDragEnd={onDragCleanup}
                        onContextMenu={(event) => onBlockContextMenu(event, block)}
                        onPointerDown={onBlockPointerDown}
                        onClick={(event) => onBlockActivate(event, block)}
                        title={`${displayTitle(block)} · ${fmtMin(block.startMin)}-${fmtMin(block.endMin)}`}
                        className={cn(
                          "absolute inset-0 overflow-hidden rounded border px-1 py-0.5 text-left transition-colors",
                          isMeet
                            ? block.selected ? "border-status-meet/70 bg-status-meet/25 ring-1 ring-status-meet/50" : "border-status-meet/40 bg-status-meet/15 hover:bg-status-meet/25"
                            : block.selected ? "bg-card ring-1 ring-primary/50" : "bg-card hover:bg-accent/60",
                          draggable && "cursor-grab active:cursor-grabbing",
                          block.status === "DONE" && "opacity-60",
                        )}
                        style={{ borderLeftWidth: isMeet ? undefined : 2, borderLeftColor: isMeet ? undefined : project?.color }}
                      >
                        <span className="block truncate text-[9px] font-semibold leading-tight text-foreground">{displayTitle(block)}</span>
                        {height > 26 && <span className="block truncate text-[8px] tabular-nums text-muted-foreground">{fmtMin(block.startMin)}</span>}
                      </button>
                      {canSchedule && (
                        <>
                          <div onMouseDown={(event) => startResize(event, block, iso, "top")} className="absolute inset-x-1 top-0 z-30 h-2 cursor-ns-resize rounded-t opacity-0 transition-opacity hover:bg-primary/20 group-hover:opacity-100" />
                          <div onMouseDown={(event) => startResize(event, block, iso, "bottom")} className="absolute inset-x-1 bottom-0 z-30 h-2 cursor-ns-resize rounded-b opacity-0 transition-opacity hover:bg-primary/20 group-hover:opacity-100" />
                        </>
                      )}
                    </div>
                  )
                })}
                {markers.map((marker) => {
                  const isDeadline = marker.kind === "DEADLINE"
                  const top = clamp(marker.min, DAY_START_MIN, DAY_END_MIN) * PX_PER_MIN
                  return (
                    <button key={marker.key} type="button" onClick={() => onSelectTask(marker.taskId)} title={`${isDeadline ? "Deadline" : "Reminder"} · ${marker.title} · ${fmtMin(marker.min)}`} className="absolute right-0.5 z-10 -translate-y-1/2" style={{ top }}>
                      {isDeadline ? <Flag className={cn("size-3 fill-current text-status-deadline", marker.selected && "drop-shadow")} /> : <Bell className={cn("size-3 text-status-remind", marker.selected && "drop-shadow")} />}
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
