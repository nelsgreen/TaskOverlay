"use client"

import { useEffect, useMemo, useRef, useState } from "react"
import { Bell, Check, Flag, GripVertical, Link2, MapPin, X } from "lucide-react"
import type { MeetItem, Project, Section, Status, Task, TaskWorkSession } from "@/lib/types"
import { cn } from "@/lib/utils"
import { CalendarActivationGuard } from "@/lib/calendar-interaction"
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
const TOTAL_MIN = END_MIN - START_MIN
const PX_PER_MIN = 1.15
const GRID_HEIGHT = Math.round(TOTAL_MIN * PX_PER_MIN)
const SNAP_MIN = 15
const MIN_DURATION = 15
const DEFAULT_DURATION = 60
const MARKER_RAIL = 76

const QUICK_DURATIONS = [
  { label: "30m", min: 30 }, { label: "45m", min: 45 }, { label: "1h", min: 60 },
  { label: "1.5h", min: 90 }, { label: "2h", min: 120 },
]
const DURATION_MIN_MAP: Record<string, number> = { "15m": 15, "30m": 30, "45m": 45, "1h": 60, "90m": 90, "2h": 120, custom: 60 }

// ─── Time helpers ────────────────────────────────────────────────────────────
function toMin(hhmm: string): number { const [h, m] = hhmm.split(":").map(Number); return (h || 0) * 60 + (m || 0) }
function fmtMin(min: number): string { return `${String(Math.floor(min / 60)).padStart(2, "0")}:${String(min % 60).padStart(2, "0")}` }
function fmtDur(min: number): string { const h = Math.floor(min / 60), m = min % 60; return h && m ? `${h}h ${m}m` : h ? `${h}h` : `${m}m` }
function snapTo(min: number, snap: number): number { return Math.round(min / snap) * snap }
const clamp = (v: number, lo: number, hi: number) => Math.max(lo, Math.min(hi, v))
function meetEndMin(m: MeetItem): number { return m.endTime ? toMin(m.endTime) : toMin(m.startTime) + (DURATION_MIN_MAP[m.duration] ?? 60) }

// ─── Models ────────────────────────────────────────────────────────────────────
interface Block {
  key: string; kind: "WORK" | "MEET"; title: string; projectId: string; sectionId?: string
  dateKey: string; startMin: number; endMin: number; taskId?: string; sessionId?: string; meetId?: string
  location?: string; hasLink?: boolean; status?: Status; selected: boolean
}
interface Marker { key: string; kind: "REMIND" | "DEADLINE"; title: string; projectId: string; min: number; taskId: string; selected: boolean }
type DragState =
  | { kind: "TASK"; taskId: string; duration: number }
  | { kind: "TASK_SESSION"; taskId: string; sessionId: string; duration: number }
  | { kind: "MEET"; meetId: string; duration: number }
type CalendarMenuState =
  | { kind: "slot"; x: number; y: number; dateKey: string; startMin: number }
  | { kind: "block"; x: number; y: number; block: Block }

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
  const flush = () => { const cols = cluster.reduce((mx, b) => Math.max(mx, b.col + 1), 0); cluster.forEach((b) => out.push({ ...b, cols })); cluster = []; clusterEnd = -1 }
  for (const b of sorted) {
    if (cluster.length && b.startMin >= clusterEnd) flush()
    const used = new Set(cluster.filter((c) => c.endMin > b.startMin).map((c) => c.col))
    let col = 0; while (used.has(col)) col++
    cluster.push({ ...b, col })
    clusterEnd = Math.max(clusterEnd < 0 ? b.endMin : clusterEnd, b.endMin)
  }
  if (cluster.length) flush()
  return out
}

// ─── Props ──────────────────────────────────────────────────────────────────
interface CalendarViewProps {
  viewMode: "day" | "week"; selectedDate: string
  projects: Project[]; sections: Section[]; tasks: Task[]; taskWorkSessions: TaskWorkSession[]; meetItems: MeetItem[]
  selectedProjectIds: string[]; selectedTaskId: string | null; selectedMeetId: string | null
  showDone: boolean; canSchedule: boolean; createMeetDisabled?: boolean
  onSelectTask: (taskId: string) => void; onSelectMeet: (meetId: string) => void
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
  viewMode, selectedDate, projects, sections, tasks, taskWorkSessions, meetItems,
  selectedProjectIds, selectedTaskId, selectedMeetId, showDone, canSchedule,
  createMeetDisabled = false,
  onSelectTask, onSelectMeet, onPickDay, onCreateTaskAtSlot, onCreateMeetAtSlot,
  onCreateTaskWorkSession, onUpdateTaskWorkSession, onRequestDeleteTaskWorkSession,
  onSetTaskStatus, onMoveMeet, onRequestDeleteMeet,
}: CalendarViewProps) {
  const today = todayKey()
  const [nowMin, setNowMin] = useState(() => { const n = new Date(); return n.getHours() * 60 + n.getMinutes() })
  useEffect(() => {
    const id = setInterval(() => { const n = new Date(); setNowMin(n.getHours() * 60 + n.getMinutes()) }, 60_000)
    return () => clearInterval(id)
  }, [])

  const [poolWidth, setPoolWidth] = useState(224) // session-only
  const [poolDropActive, setPoolDropActive] = useState(false)
  const [ghost, setGhost] = useState<{ startMin: number; endMin: number } | null>(null)
  const [weekGhost, setWeekGhost] = useState<{ dayIso: string; startMin: number; endMin: number } | null>(null)
  const [newBlock, setNewBlock] = useState<{ taskId: string; dateKey: string; startMin: number } | null>(null)
  const [menu, setMenu] = useState<CalendarMenuState | null>(null)
  const dragRef = useRef<DragState | null>(null)
  const activationGuardRef = useRef(new CalendarActivationGuard())
  const gridRef = useRef<HTMLDivElement>(null)

  const projectMap = useMemo(() => new Map(projects.map((p) => [p.id, p])), [projects])
  const sectionMap = useMemo(() => new Map(sections.map((s) => [s.id, s])), [sections])
  const taskMap = useMemo(() => new Map(tasks.map((task) => [task.id, task])), [tasks])
  const allSelected = selectedProjectIds.length === projects.length
  const inScope = (projectId: string) => allSelected || selectedProjectIds.includes(projectId)

  const buildDay = (dateKey: string): { blocks: Block[]; markers: Marker[] } => {
    const blocks: Block[] = []; const markers: Marker[] = []
    taskWorkSessions.forEach((session) => {
      const t = taskMap.get(session.taskId)
      if (!t || !inScope(t.projectId)) return
      if (t.status === "DONE" && !showDone) return
      const slot = localSlotFromIso(session.startUtc)
      if (!slot || slot.dateKey !== dateKey) return
      const endSlot = localSlotFromIso(session.endUtc)
      const startMin = clamp(slot.minutes, START_MIN, END_MIN)
      const endMin = endSlot?.dateKey === dateKey ? endSlot.minutes : END_MIN
      blocks.push({ key: `work-${session.id}`, kind: "WORK", title: t.title, projectId: t.projectId, sectionId: t.sectionId, dateKey, startMin, endMin: clamp(endMin, startMin + MIN_DURATION, END_MIN), taskId: t.id, sessionId: session.id, status: t.status, selected: t.id === selectedTaskId })
    })
    meetItems.forEach((m) => {
      if (m.date !== dateKey || !inScope(m.projectId)) return
      blocks.push({ key: `meet-${m.id}`, kind: "MEET", title: m.title, projectId: m.projectId, dateKey, startMin: toMin(m.startTime), endMin: meetEndMin(m), meetId: m.id, location: m.location, hasLink: !!m.link, selected: m.id === selectedMeetId })
    })
    tasks.forEach((t) => {
      if (!inScope(t.projectId)) return
      if (t.status === "DONE" && !showDone) return
      if (t.reminderDate === dateKey && t.reminderTime) markers.push({ key: `rem-${t.id}`, kind: "REMIND", title: t.title, projectId: t.projectId, min: toMin(t.reminderTime), taskId: t.id, selected: t.id === selectedTaskId })
      if (t.deadlineDate === dateKey) markers.push({ key: `dl-${t.id}`, kind: "DEADLINE", title: t.title, projectId: t.projectId, min: t.deadlineTime ? toMin(t.deadlineTime) : END_MIN, taskId: t.id, selected: t.id === selectedTaskId })
    })
    return { blocks, markers }
  }

  const planningTasks = useMemo(() => {
    const pool = tasks.filter((t) => (t.status === "TODO" || t.status === "FOCUS" || t.status === "WAIT") && inScope(t.projectId))
    return [...pool.filter((t) => t.pinned || t.status === "FOCUS"), ...pool.filter((t) => !t.pinned && t.status !== "FOCUS")]
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tasks, allSelected, selectedProjectIds])

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

  const hours = Array.from({ length: END_HOUR - START_HOUR + 1 }, (_, i) => START_HOUR + i)
  const displayTitle = (block: Block) => block.title.trim() || (block.kind === "MEET" ? "Untitled MEET" : "Untitled task")

  // ─── Drag model ───────────────────────────────────────────────────────────
  const startPoolDrag = (e: React.DragEvent, taskId: string) => {
    activationGuardRef.current.completeManipulation()
    dragRef.current = { kind: "TASK", taskId, duration: DEFAULT_DURATION }
    e.dataTransfer.effectAllowed = "copyMove"; e.dataTransfer.setData("text/plain", taskId)
  }
  const startBlockDrag = (e: React.DragEvent, block: Block) => {
    activationGuardRef.current.completeManipulation()
    const duration = block.endMin - block.startMin
    if (block.kind === "MEET") {
      if (!block.meetId) return
      dragRef.current = { kind: "MEET", meetId: block.meetId, duration }
      e.dataTransfer.effectAllowed = "move"; e.dataTransfer.setData("text/plain", block.meetId)
      return
    }
    if (!block.taskId || !block.sessionId) return
    dragRef.current = { kind: "TASK_SESSION", taskId: block.taskId, sessionId: block.sessionId, duration }
    e.dataTransfer.effectAllowed = "move"; e.dataTransfer.setData("text/plain", block.taskId)
  }
  const minFromRect = (rect: DOMRect, clientY: number) => clamp(snapTo(START_MIN + (clientY - rect.top) / PX_PER_MIN, SNAP_MIN), START_MIN, END_MIN - MIN_DURATION)
  const openSlotMenu = (event: React.MouseEvent<HTMLElement>, dateKey: string) => {
    if (!canSchedule || (event.target as HTMLElement).closest("[data-calendar-block='true']")) return
    event.preventDefault()
    event.stopPropagation()
    const startMin = minFromRect(event.currentTarget.getBoundingClientRect(), event.clientY)
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
    const duration = Math.max(MIN_DURATION, endMin - startMin)
    if (block.kind === "MEET" && block.meetId) {
      onMoveMeet(block.meetId, startsAtUtc, duration)
      return
    }
    if (block.taskId && block.sessionId) {
      const endsAtUtc = isoFromLocalDateTime(dateKey, Math.floor(endMin / 60), endMin % 60)
      onUpdateTaskWorkSession(block.sessionId, startsAtUtc, endsAtUtc)
    }
  }

  const onDayDragOver = (e: React.DragEvent) => {
    if (!dragRef.current || !gridRef.current) return
    e.preventDefault(); e.dataTransfer.dropEffect = dragRef.current.kind === "TASK" ? "copy" : "move"
    const startMin = minFromRect(gridRef.current.getBoundingClientRect(), e.clientY)
    setGhost({ startMin, endMin: clamp(startMin + dragRef.current.duration, START_MIN, END_MIN) })
  }
  const onDayDrop = (e: React.DragEvent) => {
    e.preventDefault()
    const d = dragRef.current; dragRef.current = null; setGhost(null)
    if (!d || !canSchedule || !gridRef.current) return
    const startMin = minFromRect(gridRef.current.getBoundingClientRect(), e.clientY)
    const startsAtUtc = isoFromLocalDateTime(selectedDate, Math.floor(startMin / 60), startMin % 60)
    if (d.kind === "MEET") {
      onMoveMeet(d.meetId, startsAtUtc, d.duration)
      return
    }
    const taskEndMin = clamp(startMin + d.duration, startMin + MIN_DURATION, END_MIN)
    const endsAtUtc = isoFromLocalDateTime(selectedDate, Math.floor(taskEndMin / 60), taskEndMin % 60)
    if (d.kind === "TASK_SESSION") {
      onUpdateTaskWorkSession(d.sessionId, startsAtUtc, endsAtUtc)
    } else {
      onCreateTaskWorkSession(d.taskId, startsAtUtc, endsAtUtc)
    }
    if (d.kind === "TASK") setNewBlock({ taskId: d.taskId, dateKey: selectedDate, startMin })
  }
  const onColumnDragOver = (e: React.DragEvent, dayIso: string) => {
    if (!dragRef.current || !canSchedule) return
    e.preventDefault()
    e.dataTransfer.dropEffect = dragRef.current.kind === "TASK" ? "copy" : "move"
    const startMin = minFromRect((e.currentTarget as HTMLElement).getBoundingClientRect(), e.clientY)
    setWeekGhost({ dayIso, startMin, endMin: clamp(startMin + dragRef.current.duration, START_MIN, END_MIN) })
  }
  const onColumnDrop = (e: React.DragEvent, dayIso: string) => {
    e.preventDefault()
    const d = dragRef.current; dragRef.current = null; setWeekGhost(null)
    if (!d || !canSchedule) return
    const startMin = minFromRect((e.currentTarget as HTMLElement).getBoundingClientRect(), e.clientY)
    const startsAtUtc = isoFromLocalDateTime(dayIso, Math.floor(startMin / 60), startMin % 60)
    if (d.kind === "MEET") {
      onMoveMeet(d.meetId, startsAtUtc, d.duration)
      return
    }
    const taskEndMin = clamp(startMin + d.duration, startMin + MIN_DURATION, END_MIN)
    const endsAtUtc = isoFromLocalDateTime(dayIso, Math.floor(taskEndMin / 60), taskEndMin % 60)
    if (d.kind === "TASK_SESSION") {
      onUpdateTaskWorkSession(d.sessionId, startsAtUtc, endsAtUtc)
    } else {
      onCreateTaskWorkSession(d.taskId, startsAtUtc, endsAtUtc)
    }
  }
  // Cleanup when a drag ends anywhere (including cancel/drop outside a target).
  const onDragCleanup = () => { dragRef.current = null; setGhost(null); setWeekGhost(null); setPoolDropActive(false) }
  const beginBlockPointer = () => activationGuardRef.current.beginPointerInteraction()
  const activateBlock = (event: React.MouseEvent, block: Block) => {
    if (!activationGuardRef.current.shouldActivate(event.detail)) return
    if (block.kind === "MEET" && block.meetId) onSelectMeet(block.meetId)
    else if (block.taskId) onSelectTask(block.taskId)
  }
  const onPoolDrop = (e: React.DragEvent) => {
    e.preventDefault(); setPoolDropActive(false)
    const d = dragRef.current; dragRef.current = null
    if (d?.kind === "TASK_SESSION" && canSchedule) onRequestDeleteTaskWorkSession(d.sessionId)
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

  const applyDuration = (sessionId: string, dateKey: string, startMin: number, durMin: number) => {
    const endMin = clamp(startMin + durMin, startMin + MIN_DURATION, END_MIN)
    onUpdateTaskWorkSession(
      sessionId,
      isoFromLocalDateTime(dateKey, Math.floor(startMin / 60), startMin % 60),
      isoFromLocalDateTime(dateKey, Math.floor(endMin / 60), endMin % 60),
    )
    setNewBlock(null)
  }
  const startPoolResize = (event: React.MouseEvent) => {
    event.preventDefault()
    const onMove = (e: MouseEvent) => setPoolWidth(clamp(e.clientX - (gridRef.current?.closest("[data-cal-root]")?.getBoundingClientRect().left ?? 0), 168, 380))
    const onUp = () => { window.removeEventListener("mousemove", onMove); window.removeEventListener("mouseup", onUp); document.body.style.cursor = ""; document.body.style.userSelect = "" }
    document.body.style.cursor = "col-resize"; document.body.style.userSelect = "none"
    window.addEventListener("mousemove", onMove); window.addEventListener("mouseup", onUp)
  }

  return (
    <div data-cal-root className="flex h-full overflow-hidden bg-background">
      {/* Planning pool holds logical tasks; dropping a block removes only that session. */}
      <div
        className={cn("relative flex shrink-0 flex-col border-r border-border bg-sidebar/40", poolDropActive && "bg-primary/5")}
        style={{ width: poolWidth }}
        onDragOver={(e) => { if (dragRef.current?.kind === "TASK_SESSION") { e.preventDefault(); setPoolDropActive(true) } }}
        onDragLeave={() => setPoolDropActive(false)}
        onDrop={onPoolDrop}
      >
        <div className="flex shrink-0 items-center gap-2 border-b border-border px-3 py-2.5">
          <span className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">Planning pool</span>
          <span className="rounded-full bg-muted px-1.5 py-0.5 text-[10px] font-medium tabular-nums text-muted-foreground">{planningTasks.length}</span>
        </div>
        <div className="min-h-0 flex-1 space-y-1.5 overflow-y-auto p-2">
          {planningTasks.length === 0 ? (
            <p className="px-1 py-3 text-[11px] text-muted-foreground">No active tasks in scope. DONE tasks stay hidden.</p>
          ) : (
            planningTasks.map((t) => {
              const p = projectMap.get(t.projectId); const s = sectionMap.get(t.sectionId ?? ""); const st = STATUS_STYLES[t.status] ?? STATUS_STYLES.TODO
              const todayTotal = todaySessionTotals.get(t.id)
              const scheduleLabel = !todayTotal
                ? "Unscheduled"
                : `Scheduled today · ${todayTotal.blocks} ${todayTotal.blocks === 1 ? "block" : "blocks"} · ${fmtDur(todayTotal.minutes)} total`
              return (
                <div
                  key={t.id}
                  draggable={canSchedule}
                  onDragStart={(e) => startPoolDrag(e, t.id)}
                  onDragEnd={onDragCleanup}
                  onPointerDown={beginBlockPointer}
                  onClick={(event) => {
                    if (activationGuardRef.current.shouldActivate(event.detail)) onSelectTask(t.id)
                  }}
                  className={cn(
                    "group flex flex-col gap-1.5 rounded-lg border bg-card p-2 text-left transition-colors",
                    canSchedule ? "cursor-grab active:cursor-grabbing" : "cursor-pointer",
                    t.id === selectedTaskId ? "border-primary/50 bg-primary/8" : "border-border hover:border-border/80 hover:bg-accent/40",
                  )}
                >
                  <div className="flex items-center gap-1.5">
                    {canSchedule && <GripVertical className="size-3 shrink-0 text-muted-foreground/50 group-hover:text-muted-foreground" />}
                    <span className={cn("rounded px-1 py-0.5 text-[9px] font-bold uppercase tracking-wide", st.bg, st.text)}>{st.label}</span>
                    {t.pinned && <span className="ml-auto text-[9px] font-semibold uppercase tracking-wide text-muted-foreground">pinned</span>}
                  </div>
                  <p className="line-clamp-2 text-[12px] font-medium leading-snug text-foreground">{t.title}</p>
                  <div className="flex items-center gap-1 text-[10px] text-muted-foreground">
                    <span className="size-1.5 shrink-0 rounded-full" style={{ backgroundColor: p?.color }} />
                    <span className="truncate">{p?.name}{s ? ` / ${s.name}` : ""}</span>
                  </div>
                  <span className={cn("text-[10px]", todayTotal ? "text-primary/80" : "text-muted-foreground/70")}>{scheduleLabel}</span>
                </div>
              )
            })
          )}
        </div>
        {canSchedule && (
          <div className="shrink-0 border-t border-border px-2 py-1.5 text-[10px] text-muted-foreground">
            {poolDropActive ? "Drop to remove this calendar block" : "Tasks stay until DONE · drag to add a session"}
          </div>
        )}
        <div onMouseDown={startPoolResize} title="Drag to resize" className="absolute right-0 top-0 z-20 h-full w-1.5 translate-x-1/2 cursor-col-resize bg-transparent transition-colors hover:bg-primary/40" />
      </div>

      {/* ── Grid area ────────────────────────────────────────────────────── */}
      <div className="min-w-0 flex-1 overflow-y-auto">
        {viewMode === "day" ? (
          <DayGrid
            hours={hours} {...buildDay(selectedDate)} isToday={selectedDate === today} nowMin={nowMin}
            projectMap={projectMap} sectionMap={sectionMap} canSchedule={canSchedule}
            ghost={ghost} newBlock={newBlock} gridRef={gridRef}
            onDayDragOver={onDayDragOver} onDayDragLeave={() => setGhost(null)} onDayDrop={onDayDrop}
            onStartBlockDrag={startBlockDrag} onDragCleanup={onDragCleanup}
            onEmptyContextMenu={(e) => openSlotMenu(e, selectedDate)}
            onBlockContextMenu={openBlockMenu}
            onBlockPointerDown={beginBlockPointer} onBlockActivate={activateBlock}
            onSelectTask={onSelectTask}
            onRequestDeleteTaskWorkSession={onRequestDeleteTaskWorkSession}
            onResizeBlock={(block, startMin, endMin) => resizeBlock(block, selectedDate, startMin, endMin)}
            onApplyDuration={applyDuration}
            displayTitle={displayTitle}
          />
        ) : (
          <WeekGrid
            hours={hours} selectedDate={selectedDate} today={today} nowMin={nowMin}
            buildDay={buildDay} projectMap={projectMap} canSchedule={canSchedule}
            weekGhost={weekGhost}
            onColumnDragOver={onColumnDragOver} onColumnDrop={onColumnDrop}
            onStartBlockDrag={startBlockDrag} onDragCleanup={onDragCleanup}
            onEmptyContextMenu={openSlotMenu}
            onBlockContextMenu={openBlockMenu}
            onResizeBlock={resizeBlock}
            onBlockPointerDown={beginBlockPointer} onBlockActivate={activateBlock}
            onSelectTask={onSelectTask} onPickDay={onPickDay}
            displayTitle={displayTitle}
          />
        )}
      </div>
      {menu && (
        <CalendarContextMenu
          menu={menu}
          onClose={() => setMenu(null)}
          onCreateTask={() => {
            if (menu.kind !== "slot") return
            onCreateTaskAtSlot(menu.dateKey, menu.startMin)
            setMenu(null)
          }}
          onCreateMeet={() => {
            if (menu.kind !== "slot") return
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
            if (menu.kind !== "block" || menu.block.kind !== "WORK" || !menu.block.taskId) return
            const startMin = clamp(menu.block.endMin, START_MIN, END_MIN - DEFAULT_DURATION)
            const endMin = startMin + DEFAULT_DURATION
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
            if (menu.kind === "block" && menu.block.kind === "WORK" && menu.block.taskId) {
              onSetTaskStatus(menu.block.taskId, status)
            }
            setMenu(null)
          }}
          canSchedule={canSchedule}
          canCreateMeet={canSchedule && !createMeetDisabled}
        />
      )}
    </div>
  )
}

// ─── Day grid ─────────────────────────────────────────────────────────────────
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
  canCreateMeet,
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
  canCreateMeet: boolean
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
            disabled={!canSchedule}
            className="flex h-7 w-full items-center rounded-md px-2 text-left text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50"
          >
            Create task
          </button>
          <button
            type="button"
            role="menuitem"
            onClick={onCreateMeet}
            disabled={!canCreateMeet}
            className="flex h-7 w-full items-center rounded-md px-2 text-left text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50"
          >
            Create MEET
          </button>
        </>
      ) : (
        <>
          <button
            type="button"
            role="menuitem"
            onClick={onOpenBlock}
            className="flex h-7 w-full items-center rounded-md px-2 text-left text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
          >
            Open {menu.block.kind === "MEET" ? "MEET" : "task"} details
          </button>
          {menu.block.kind === "WORK" && (
            <>
              <button
                type="button"
                role="menuitem"
                onClick={onAddTaskSession}
                disabled={!canSchedule}
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
          <button
            type="button"
            role="menuitem"
            onClick={onRemoveBlock}
            disabled={!canSchedule}
            className="flex h-7 w-full items-center rounded-md px-2 text-left text-destructive transition-colors hover:bg-destructive/10 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {menu.block.kind === "MEET" ? "Delete MEET" : "Remove this calendar block"}
          </button>
        </>
      )}
      <button
        type="button"
        role="menuitem"
        onClick={onClose}
        className="mt-1 flex h-7 w-full items-center rounded-md px-2 text-left text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
      >
        Cancel
      </button>
    </div>
  )
}

function DayGrid({
  hours, blocks, markers, isToday, nowMin, projectMap, sectionMap, canSchedule,
  ghost, newBlock, gridRef, onDayDragOver, onDayDragLeave, onDayDrop, onStartBlockDrag, onDragCleanup,
  onEmptyContextMenu, onBlockContextMenu, onBlockPointerDown, onBlockActivate, onSelectTask, onRequestDeleteTaskWorkSession, onResizeBlock, onApplyDuration,
  displayTitle,
}: {
  hours: number[]; blocks: Block[]; markers: Marker[]; isToday: boolean; nowMin: number
  projectMap: Map<string, Project>; sectionMap: Map<string, Section>; canSchedule: boolean
  ghost: { startMin: number; endMin: number } | null; newBlock: { taskId: string; dateKey: string; startMin: number } | null
  gridRef: React.RefObject<HTMLDivElement | null>
  onDayDragOver: (e: React.DragEvent) => void; onDayDragLeave: () => void; onDayDrop: (e: React.DragEvent) => void
  onStartBlockDrag: (e: React.DragEvent, block: Block) => void; onDragCleanup: () => void
  onEmptyContextMenu: (event: React.MouseEvent<HTMLElement>) => void
  onBlockContextMenu: (event: React.MouseEvent, block: Block) => void
  onBlockPointerDown: () => void
  onBlockActivate: (event: React.MouseEvent, block: Block) => void
  onSelectTask: (id: string) => void
  onRequestDeleteTaskWorkSession: (sessionId: string) => void
  onResizeBlock: (block: Block, startMin: number, endMin: number) => void
  onApplyDuration: (sessionId: string, dateKey: string, startMin: number, durMin: number) => void
  displayTitle: (block: Block) => string
}) {
  const laid = layoutColumns(blocks)
  const showNow = isToday && nowMin >= START_MIN && nowMin <= END_MIN
  const isEmpty = blocks.length === 0 && markers.length === 0

  const gaps = useMemo(() => {
    const busy = blocks.map((b) => [clamp(b.startMin, START_MIN, END_MIN), clamp(b.endMin, START_MIN, END_MIN)] as [number, number]).filter(([s, e]) => e > s).sort((a, b) => a[0] - b[0])
    const merged: [number, number][] = []
    busy.forEach(([s, e]) => { const last = merged[merged.length - 1]; if (last && s <= last[1]) last[1] = Math.max(last[1], e); else merged.push([s, e]) })
    const out: { start: number; end: number }[] = []; let cursor = START_MIN
    merged.forEach(([s, e]) => { if (s - cursor >= 30) out.push({ start: cursor, end: s }); cursor = Math.max(cursor, e) })
    if (END_MIN - cursor >= 30) out.push({ start: cursor, end: END_MIN })
    return out
  }, [blocks])

  const [resizing, setResizing] = useState<{ block: Block; edge: "top" | "bottom"; startMouseY: number; origStart: number; origEnd: number; curStart: number; curEnd: number } | null>(null)
  const startResize = (e: React.MouseEvent, block: Block, edge: "top" | "bottom", origStart: number, origEnd: number) => {
    e.stopPropagation(); e.preventDefault()
    setResizing({ block, edge, startMouseY: e.clientY, origStart, origEnd, curStart: origStart, curEnd: origEnd })
    document.body.style.cursor = "ns-resize"; document.body.style.userSelect = "none"
  }
  useEffect(() => {
    if (!resizing) return
    const onMove = (e: MouseEvent) => {
      const deltaMin = snapTo((e.clientY - resizing.startMouseY) / PX_PER_MIN, SNAP_MIN)
      setResizing((r) => !r ? r : r.edge === "bottom" ? { ...r, curEnd: clamp(r.origEnd + deltaMin, r.curStart + MIN_DURATION, END_MIN) } : { ...r, curStart: clamp(r.origStart + deltaMin, START_MIN, r.curEnd - MIN_DURATION) })
    }
    const onUp = () => { setResizing((r) => { if (r) onResizeBlock(r.block, r.curStart, r.curEnd); return null }); document.body.style.cursor = ""; document.body.style.userSelect = "" }
    window.addEventListener("mousemove", onMove); window.addEventListener("mouseup", onUp)
    return () => { window.removeEventListener("mousemove", onMove); window.removeEventListener("mouseup", onUp) }
  }, [resizing, onResizeBlock])
  const laidResized = laid.map((b) => resizing && b.key === resizing.block.key ? { ...b, startMin: resizing.curStart, endMin: resizing.curEnd } : b)

  return (
    <div className="flex px-4 py-4">
      <div className="relative w-12 shrink-0 select-none" style={{ height: GRID_HEIGHT }}>
        {hours.map((h) => (<div key={h} className="absolute -translate-y-1/2 pr-2 text-right text-[10px] font-medium tabular-nums text-muted-foreground" style={{ top: (h * 60 - START_MIN) * PX_PER_MIN, right: 0 }}>{String(h).padStart(2, "0")}:00</div>))}
      </div>

      <div ref={gridRef} className="relative flex-1 rounded-lg border border-border/40 bg-card/10" style={{ height: GRID_HEIGHT }} onContextMenu={onEmptyContextMenu} onDragOver={onDayDragOver} onDragLeave={onDayDragLeave} onDrop={onDayDrop}>
        {hours.map((h) => (<div key={h} className="absolute inset-x-0 border-t border-border/40" style={{ top: (h * 60 - START_MIN) * PX_PER_MIN }} />))}
        {hours.slice(0, -1).map((h) => (<div key={`${h}h`} className="absolute inset-x-0 border-t border-dashed border-border/20" style={{ top: (h * 60 + 30 - START_MIN) * PX_PER_MIN }} />))}

        {gaps.map((g, i) => (
          <div key={i} className="pointer-events-none absolute inset-x-0 flex items-center justify-center" style={{ top: (g.start - START_MIN) * PX_PER_MIN, height: (g.end - g.start) * PX_PER_MIN }}>
            <span className="rounded-full bg-muted/30 px-2 py-0.5 text-[10px] text-muted-foreground/60">Free · {fmtDur(g.end - g.start)}</span>
          </div>
        ))}

        {showNow && (
          <div className="pointer-events-none absolute inset-x-0 z-20 flex items-center" style={{ top: (nowMin - START_MIN) * PX_PER_MIN }}>
            <span className="-ml-1 size-2 rounded-full bg-now-marker" /><span className="h-px flex-1 bg-now-marker" /><span className="rounded-l bg-now-marker px-1 py-0.5 text-[9px] font-bold text-background">{fmtMin(nowMin)}</span>
          </div>
        )}

        {isEmpty && !ghost && (
          <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center gap-1.5 text-center">
            <p className="text-sm font-medium text-muted-foreground">Nothing planned on this day</p>
            <p className="max-w-xs text-[11px] text-muted-foreground/70">{canSchedule ? "Drag a task from the Planning pool onto the grid." : "Planned work, reminders and deadlines land here."}</p>
          </div>
        )}

        {ghost && (
          <div className="pointer-events-none absolute inset-x-2 z-10 rounded-lg border-2 border-dashed border-primary/50 bg-primary/8" style={{ top: (ghost.startMin - START_MIN) * PX_PER_MIN, height: (ghost.endMin - ghost.startMin) * PX_PER_MIN, right: MARKER_RAIL }}>
            <span className="block px-2 pt-1 text-[11px] font-medium text-primary/70">{fmtMin(ghost.startMin)} – {fmtMin(ghost.endMin)}</span>
          </div>
        )}

        <div className="absolute inset-y-0 left-0" style={{ right: MARKER_RAIL }}>
          {laidResized.map((b) => {
            const p = projectMap.get(b.projectId); const sec = b.sectionId ? sectionMap.get(b.sectionId) : undefined
            const top = (clamp(b.startMin, START_MIN, END_MIN) - START_MIN) * PX_PER_MIN
            const height = Math.max(28, (clamp(b.endMin, START_MIN, END_MIN) - clamp(b.startMin, START_MIN, END_MIN)) * PX_PER_MIN)
            const widthPct = 100 / b.cols; const isMeet = b.kind === "MEET"; const st = b.status ? STATUS_STYLES[b.status] : null
            const isNew = b.kind === "WORK" && !!b.sessionId && newBlock !== null && newBlock.taskId === b.taskId && newBlock.dateKey === b.dateKey && newBlock.startMin === b.startMin
            const draggable = canSchedule && !resizing && (b.kind === "WORK" || b.kind === "MEET")
            return (
              <div key={b.key} className="group absolute" style={{ top, height, left: `calc(${b.col * widthPct}% + ${b.col * 4}px)`, width: `calc(${widthPct}% - 4px)` }}>
                <button
                  type="button"
                  data-calendar-block="true"
                  draggable={draggable}
                  onDragStart={(e) => onStartBlockDrag(e, b)}
                  onDragEnd={onDragCleanup}
                  onContextMenu={(e) => onBlockContextMenu(e, b)}
                  onPointerDown={onBlockPointerDown}
                  onClick={(event) => onBlockActivate(event, b)}
                  className={cn("absolute inset-0 overflow-hidden rounded-lg border text-left transition-colors",
                    isMeet ? (b.selected ? "border-status-meet/70 bg-status-meet/20 ring-2 ring-status-meet/40" : "border-status-meet/40 bg-status-meet/12 hover:bg-status-meet/22")
                      : (b.selected ? "border-border bg-card ring-2 ring-primary/50" : "border-border bg-card hover:bg-accent/50"),
                    draggable && "cursor-grab active:cursor-grabbing", b.status === "DONE" && "opacity-60")}
                  style={{ borderLeftWidth: isMeet ? undefined : 3, borderLeftColor: isMeet ? undefined : p?.color }}
                >
                  <div className="flex h-full flex-col gap-0.5 px-2 py-1.5">
                    {height >= 42 && <div className="flex items-center gap-1.5"><span className="text-[9px] font-semibold tabular-nums text-muted-foreground">{fmtMin(b.startMin)}–{fmtMin(b.endMin)}</span><span className="text-[9px] text-muted-foreground/70">· {fmtDur(b.endMin - b.startMin)}</span></div>}
                    <span className={cn("font-semibold leading-snug text-foreground", height < 52 ? "line-clamp-1 text-[11px]" : "line-clamp-2 text-[12px]")}>{displayTitle(b)}</span>
                    {height > 50 && (
                      <div className="mt-auto flex items-center gap-1.5">
                        {!isMeet && (<><span className="size-1.5 shrink-0 rounded-full" style={{ backgroundColor: p?.color }} /><span className="truncate text-[10px] text-muted-foreground">{p?.name}{sec ? ` / ${sec.name}` : ""}</span></>)}
                        {isMeet && b.location && (<span className="flex items-center gap-0.5 text-[10px] text-muted-foreground"><MapPin className="size-2.5" />{b.location}</span>)}
                        {isMeet && b.hasLink && <Link2 className="size-2.5 text-muted-foreground" />}
                        {st && !isMeet && (<span className={cn("ml-auto rounded px-1 py-0.5 text-[8px] font-bold uppercase tracking-wide", st.bg, st.text)}>{st.label}</span>)}
                      </div>
                    )}
                  </div>
                </button>

                {canSchedule && (b.kind === "WORK" || b.kind === "MEET") && (
                  <>
                    <div onMouseDown={(e) => startResize(e, b, "top", b.startMin, b.endMin)} className="absolute inset-x-2 top-0 z-30 h-2 cursor-ns-resize rounded-t opacity-0 transition-opacity hover:bg-primary/20 group-hover:opacity-100" />
                    <div onMouseDown={(e) => startResize(e, b, "bottom", b.startMin, b.endMin)} className="absolute inset-x-2 bottom-0 z-30 h-2 cursor-ns-resize rounded-b opacity-0 transition-opacity hover:bg-primary/20 group-hover:opacity-100" />
                    {b.kind === "WORK" && b.sessionId && <button type="button" onClick={(e) => { e.stopPropagation(); onRequestDeleteTaskWorkSession(b.sessionId!) }} title="Remove this calendar block" className="absolute right-1 top-1 z-30 flex size-4 items-center justify-center rounded bg-background/80 text-muted-foreground opacity-0 transition-opacity hover:text-destructive group-hover:opacity-100"><X className="size-3" /></button>}
                  </>
                )}

                {isNew && (
                  <div className="absolute left-0 top-full z-50 mt-1 flex gap-1 rounded-lg border border-border bg-popover p-1.5 shadow-lg" onClick={(e) => e.stopPropagation()}>
                    {QUICK_DURATIONS.map((d) => (<button key={d.label} type="button" onClick={() => onApplyDuration(b.sessionId!, b.dateKey, newBlock!.startMin, d.min)} className="flex items-center gap-1 rounded-md border border-border px-2 py-1 text-[11px] font-medium text-muted-foreground transition-colors hover:border-primary/50 hover:bg-primary/10 hover:text-foreground"><Check className="size-2.5 opacity-0" />{d.label}</button>))}
                  </div>
                )}
              </div>
            )
          })}
        </div>

        <div className="absolute inset-y-0 right-0" style={{ width: MARKER_RAIL }}>
          {markers.map((mk) => {
            const isDeadline = mk.kind === "DEADLINE"; const top = (clamp(mk.min, START_MIN, END_MIN) - START_MIN) * PX_PER_MIN
            return (
              <button key={mk.key} type="button" onClick={() => onSelectTask(mk.taskId)} title={`${isDeadline ? "Deadline" : "Reminder"} · ${mk.title} · ${fmtMin(mk.min)}`}
                className={cn("absolute right-0 flex -translate-y-1/2 items-center gap-1 rounded-full border px-1.5 py-0.5 transition-colors",
                  isDeadline ? (mk.selected ? "border-status-deadline bg-status-deadline/20 ring-2 ring-status-deadline/40" : "border-status-deadline/40 bg-status-deadline/10 hover:bg-status-deadline/20")
                    : (mk.selected ? "border-status-remind bg-status-remind/20 ring-2 ring-status-remind/40" : "border-status-remind/40 bg-status-remind/10 hover:bg-status-remind/20"))} style={{ top }}>
                {isDeadline ? <Flag className="size-2.5 shrink-0 fill-current text-status-deadline" /> : <Bell className="size-2.5 shrink-0 text-status-remind" />}
                <span className={cn("text-[9px] font-bold tabular-nums", isDeadline ? "text-status-deadline" : "text-status-remind")}>{fmtMin(mk.min)}</span>
              </button>
            )
          })}
        </div>
      </div>
    </div>
  )
}

// ─── Week grid ────────────────────────────────────────────────────────────────
function WeekGrid({
  hours, selectedDate, today, nowMin, buildDay, projectMap, canSchedule, weekGhost,
  onColumnDragOver, onColumnDrop, onStartBlockDrag, onDragCleanup,
  onEmptyContextMenu, onBlockContextMenu, onResizeBlock, onBlockPointerDown, onBlockActivate, onSelectTask, onPickDay,
  displayTitle,
}: {
  hours: number[]; selectedDate: string; today: string; nowMin: number
  buildDay: (dateKey: string) => { blocks: Block[]; markers: Marker[] }
  projectMap: Map<string, Project>; canSchedule: boolean
  weekGhost: { dayIso: string; startMin: number; endMin: number } | null
  onColumnDragOver: (e: React.DragEvent, dayIso: string) => void
  onColumnDrop: (e: React.DragEvent, dayIso: string) => void
  onStartBlockDrag: (e: React.DragEvent, block: Block) => void; onDragCleanup: () => void
  onEmptyContextMenu: (event: React.MouseEvent<HTMLElement>, dateKey: string) => void
  onBlockContextMenu: (event: React.MouseEvent, block: Block) => void
  onResizeBlock: (block: Block, dateKey: string, startMin: number, endMin: number) => void
  onBlockPointerDown: () => void
  onBlockActivate: (event: React.MouseEvent, block: Block) => void
  onSelectTask: (id: string) => void; onPickDay: (dateKey: string) => void
  displayTitle: (block: Block) => string
}) {
  const days = weekDayKeys(mondayOfWeekKey(selectedDate))
  const weekHasItems = days.some((d) => { const { blocks, markers } = buildDay(d); return blocks.length > 0 || markers.length > 0 })
  const [resizing, setResizing] = useState<{ block: Block; dateKey: string; edge: "top" | "bottom"; startMouseY: number; origStart: number; origEnd: number; curStart: number; curEnd: number } | null>(null)
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
      const deltaMin = snapTo((event.clientY - resizing.startMouseY) / PX_PER_MIN, SNAP_MIN)
      setResizing((current) => !current ? current : current.edge === "bottom"
        ? { ...current, curEnd: clamp(current.origEnd + deltaMin, current.curStart + MIN_DURATION, END_MIN) }
        : { ...current, curStart: clamp(current.origStart + deltaMin, START_MIN, current.curEnd - MIN_DURATION) })
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
        {hours.map((h) => (<div key={h} className="absolute -translate-y-1/2 pr-2 text-right text-[10px] tabular-nums text-muted-foreground" style={{ top: (h * 60 - START_MIN) * PX_PER_MIN, right: 0 }}>{String(h).padStart(2, "0")}</div>))}
      </div>

      {!weekHasItems && (
        <div className="pointer-events-none absolute inset-x-0 top-1/2 flex -translate-y-1/2 flex-col items-center gap-1.5 text-center" style={{ left: 64 }}>
          <p className="text-sm font-medium text-muted-foreground">Nothing planned this week</p>
          <p className="text-[11px] text-muted-foreground/70">Drag a task onto a day, or pick a day to plan.</p>
        </div>
      )}

      <div className="grid flex-1 grid-cols-7 gap-1.5">
        {days.map((iso) => {
          const { blocks, markers } = buildDay(iso); const laid = layoutColumns(blocks).map((b) => resizing && resizing.dateKey === iso && resizing.block.key === b.key ? { ...b, startMin: resizing.curStart, endMin: resizing.curEnd } : b)
          const isToday = iso === today; const isSelected = iso === selectedDate
          const showNow = isToday && nowMin >= START_MIN && nowMin <= END_MIN
          return (
            <div key={iso} className="flex flex-col">
              <button type="button" onClick={() => onPickDay(iso)} className={cn("mb-1 flex h-8 items-center justify-center gap-1.5 rounded-md border text-[11px] font-semibold transition-colors",
                isToday ? "border-primary/50 bg-primary/10 text-foreground" : isSelected ? "border-primary/30 bg-primary/5 text-foreground" : "border-border bg-card text-muted-foreground hover:text-foreground")}>
                {formatWeekdayShort(iso)}<span className="tabular-nums">{formatDayNumber(iso)}</span>
              </button>
              <div className={cn("relative rounded-md border bg-card/20", isSelected ? "border-primary/30" : "border-border/50")} style={{ height: GRID_HEIGHT }}
                onContextMenu={(e) => onEmptyContextMenu(e, iso)}
                onDragOver={(e) => onColumnDragOver(e, iso)} onDrop={(e) => onColumnDrop(e, iso)}>
                {hours.map((h) => (<div key={h} className="absolute inset-x-0 border-t border-border/30" style={{ top: (h * 60 - START_MIN) * PX_PER_MIN }} />))}
                {showNow && (<div className="pointer-events-none absolute inset-x-0 z-20 h-px bg-now-marker" style={{ top: (nowMin - START_MIN) * PX_PER_MIN }} />)}
                {weekGhost?.dayIso === iso && (
                  <div className="pointer-events-none absolute inset-x-0.5 z-10 rounded border-2 border-dashed border-primary/50 bg-primary/10" style={{ top: (weekGhost.startMin - START_MIN) * PX_PER_MIN, height: (weekGhost.endMin - weekGhost.startMin) * PX_PER_MIN }}>
                    <span className="block truncate px-1 pt-0.5 text-[8px] font-semibold text-primary/80">{fmtMin(weekGhost.startMin)}</span>
                  </div>
                )}
                {laid.map((b) => {
                  const p = projectMap.get(b.projectId); const isMeet = b.kind === "MEET"
                  const top = (clamp(b.startMin, START_MIN, END_MIN) - START_MIN) * PX_PER_MIN
                  const height = Math.max(16, (clamp(b.endMin, START_MIN, END_MIN) - clamp(b.startMin, START_MIN, END_MIN)) * PX_PER_MIN)
                  const widthPct = 100 / b.cols; const draggable = canSchedule && !resizing && (b.kind === "WORK" || b.kind === "MEET")
                  return (
                    <div key={b.key} className="group absolute" style={{ top, height, left: `calc(${b.col * widthPct}% + ${b.col * 2}px)`, width: `calc(${widthPct}% - 2px)` }}>
                    <button type="button" data-calendar-block="true" draggable={draggable} onDragStart={(e) => onStartBlockDrag(e, b)} onDragEnd={onDragCleanup} onContextMenu={(e) => onBlockContextMenu(e, b)} onPointerDown={onBlockPointerDown} onClick={(event) => onBlockActivate(event, b)} title={`${displayTitle(b)} · ${fmtMin(b.startMin)}–${fmtMin(b.endMin)}`}
                      className={cn("absolute inset-0 overflow-hidden rounded border px-1 py-0.5 text-left transition-colors",
                        isMeet ? (b.selected ? "border-status-meet/70 bg-status-meet/25 ring-1 ring-status-meet/50" : "border-status-meet/40 bg-status-meet/15 hover:bg-status-meet/25") : (b.selected ? "bg-card ring-1 ring-primary/50" : "bg-card hover:bg-accent/60"),
                        draggable && "cursor-grab active:cursor-grabbing", b.status === "DONE" && "opacity-60")}
                      style={{ borderLeftWidth: isMeet ? undefined : 2, borderLeftColor: isMeet ? undefined : p?.color }}>
                      <span className="block truncate text-[9px] font-semibold leading-tight text-foreground">{displayTitle(b)}</span>
                      {height > 26 && <span className="block truncate text-[8px] tabular-nums text-muted-foreground">{fmtMin(b.startMin)}</span>}
                    </button>
                    {canSchedule && (
                      <>
                        <div onMouseDown={(e) => startResize(e, b, iso, "top")} className="absolute inset-x-1 top-0 z-30 h-2 cursor-ns-resize rounded-t opacity-0 transition-opacity hover:bg-primary/20 group-hover:opacity-100" />
                        <div onMouseDown={(e) => startResize(e, b, iso, "bottom")} className="absolute inset-x-1 bottom-0 z-30 h-2 cursor-ns-resize rounded-b opacity-0 transition-opacity hover:bg-primary/20 group-hover:opacity-100" />
                      </>
                    )}
                    </div>
                  )
                })}
                {markers.map((mk) => {
                  const isDeadline = mk.kind === "DEADLINE"; const top = (clamp(mk.min, START_MIN, END_MIN) - START_MIN) * PX_PER_MIN
                  return (<button key={mk.key} type="button" onClick={() => onSelectTask(mk.taskId)} title={`${isDeadline ? "Deadline" : "Reminder"} · ${mk.title} · ${fmtMin(mk.min)}`} className="absolute right-0.5 z-10 -translate-y-1/2" style={{ top }}>{isDeadline ? <Flag className={cn("size-3 fill-current text-status-deadline", mk.selected && "drop-shadow")} /> : <Bell className={cn("size-3 text-status-remind", mk.selected && "drop-shadow")} />}</button>)
                })}
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}
