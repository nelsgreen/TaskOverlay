"use client"

import { useEffect, useMemo, useRef, useState } from "react"
import { FolderTree } from "lucide-react"
import type { MeetItem, Status, StatusFilterKey, TabKey, Task, TimelineItem, TreeFilter, WorkspaceTaskCommand } from "@/lib/types"
import {
  initialMeetItems,
  initialTasks,
  projects as mockProjects,
  sections as mockSections,
  timelineItems as mockTimelineItems,
} from "@/lib/mock-data"
import { cn } from "@/lib/utils"
import { useWorkspaceBridge } from "@/lib/workspace-bridge"
import { matchesStatusFilter } from "@/lib/status-filter"
import { addDaysKey, todayKey } from "@/lib/calendar-date"
import { WorkspaceHeader } from "./workspace-header"
import { ProjectScopeBar } from "./project-scope-bar"
import { TreeView } from "./tree-view"
import { StatusBoard } from "./status-board"
import { TimelineView } from "./timeline-view"
import { CalendarView } from "./calendar-view"
import { DetailsPanel } from "./details-panel"
import { MeetDetailsPanel } from "./meet-details-panel"
import { ActiveNowStrip } from "./active-now-strip"

type WorkspaceSelection =
  | { kind: "task"; id: string }
  | { kind: "meet"; id: string }
  | null

export function TaskManager() {
  const bridge = useWorkspaceBridge()
  const [mockTasks, setMockTasks] = useState<Task[]>(initialTasks)
  const [mockMeetItems, setMockMeetItems] = useState<MeetItem[]>(initialMeetItems)
  const [selectedProjectIds, setSelectedProjectIds] = useState<string[]>(["kazchess"])
  const [selection, setSelection] = useState<WorkspaceSelection>({ kind: "task", id: "t-pr-1" })
  const [selectedTimelineItemId, setSelectedTimelineItemId] = useState<string | null>(null)
  const [selectedWorkstreamId, setSelectedWorkstreamId] = useState<string | null>(null)
  const [tab, setTab] = useState<TabKey>("tree")
  // Initialized once on mount; only the Today button resets it back.
  const [calendarSelectedDate, setCalendarSelectedDate] = useState<string>(() => todayKey())
  const [calendarViewMode, setCalendarViewMode] = useState<"day" | "week">("week")
  const [calendarShowDone, setCalendarShowDone] = useState(false)
  // Session-only Details panel width (no persistence yet — follow-up).
  const [detailsWidth, setDetailsWidth] = useState(288)
  const [filter, setFilter] = useState<TreeFilter>("all")
  const [statusFilter, setStatusFilter] = useState<StatusFilterKey>("all")
  const [hideDone, setHideDone] = useState(false)
  const [search, setSearch] = useState("")
  const [collapsedSections, setCollapsedSections] = useState<Set<string>>(new Set())
  const [collapsedTasks, setCollapsedTasks] = useState<Set<string>>(new Set())
  const [contextReady, setContextReady] = useState(false)
  const contextHydrated = useRef(false)
  const lastPersistedContext = useRef<string | null>(null)

  const bridged = bridge.status === "bridged"
  const connected = bridged && bridge.canEdit
  const readOnly = bridged && !connected
  const projects = bridge.data?.projects ?? mockProjects
  const sections = bridge.data?.sections ?? mockSections
  const tasks = bridge.data?.tasks ?? mockTasks
  const meetItems = bridge.data?.meetItems ?? mockMeetItems
  const timelineItems = bridge.data?.timelineItems ?? mockTimelineItems
  const activeNowTaskIds = bridge.data?.activeNowTaskIds

  useEffect(() => {
    const bridgedData = bridge.data
    if (bridge.status === "mock") {
      setContextReady(true)
      return
    }
    if (bridge.status !== "bridged" || !bridgedData) return

    if (!contextHydrated.current) {
      const context = bridgedData.context
      const selectedProjects = context.selectedProjectIds.filter((id) =>
        bridgedData.projects.some((project) => project.id === id))
      const fallbackProjectId = bridgedData.projects[0]?.id
      const restoredProjectIds = selectedProjects.length > 0
        ? selectedProjects
        : fallbackProjectId ? [fallbackProjectId] : []
      const contextTask = context.selectedTaskId
        ? bridgedData.tasks.find((task) => task.id === context.selectedTaskId)
        : null
      const restoredTimeline = context.selectedTimelineItemId
        ? bridgedData.timelineItems.find((item) =>
            item.id === context.selectedTimelineItemId &&
            (!item.projectId || restoredProjectIds.includes(item.projectId)))
        : null
      const restoredTask = context.activeTab === "calendar" || context.activeTab === "workstreams"
        ? null
        : context.activeTab === "timeline"
          ? restoredTimeline?.linkedTaskId &&
            bridgedData.tasks.some((task) => task.id === restoredTimeline.linkedTaskId)
              ? restoredTimeline.linkedTaskId
              : null
          : context.activeTab === "tree"
            ? contextTask?.projectId === restoredProjectIds[0]
              ? contextTask.id
              : bridgedData.tasks.find((task) => task.projectId === restoredProjectIds[0])?.id ?? null
            : contextTask && restoredProjectIds.includes(contextTask.projectId)
              ? contextTask.id
              : bridgedData.tasks.find((task) => restoredProjectIds.includes(task.projectId))?.id ?? null
      const restoredTimelineItem = context.activeTab === "timeline" && restoredTask
        ? restoredTimeline?.id ?? null
        : null

      setSelectedProjectIds(restoredProjectIds)
      setSelection(restoredTask ? { kind: "task", id: restoredTask } : null)
      setSelectedTimelineItemId(restoredTimelineItem)
      setSelectedWorkstreamId(context.selectedWorkstreamId)
      setTab(context.activeTab)
      setFilter(context.filter)
      lastPersistedContext.current = JSON.stringify({
        activeTab: context.activeTab,
        selectedProjectIds: restoredProjectIds,
        selectedTaskId: restoredTask,
        selectedTimelineItemId: restoredTimelineItem,
        selectedWorkstreamId: context.selectedWorkstreamId,
        filter: context.filter,
      })
      contextHydrated.current = true
      setContextReady(true)
      return
    }

    const firstProjectId = bridgedData.projects[0]?.id
    const validProjectIds = selectedProjectIds.filter((id) =>
      bridgedData.projects.some((project) => project.id === id))
    const repairedProjectIds = validProjectIds.length > 0
      ? validProjectIds
      : firstProjectId ? [firstProjectId] : []
    setSelectedProjectIds(repairedProjectIds)
    setSelection((selected) => {
      if (tab === "workstreams") return null
      if (tab === "calendar") {
        // Keep the clicked task/meet selected across snapshot refreshes.
        if (selected?.kind === "task" && bridgedData.tasks.some((task) => task.id === selected.id)) return selected
        if (selected?.kind === "meet" && bridgedData.meetItems.some((meet) => meet.id === selected.id)) return selected
        return null
      }
      if (tab === "timeline") {
        const timelineItem = selectedTimelineItemId
          ? bridgedData.timelineItems.find((item) =>
              item.id === selectedTimelineItemId &&
              (!item.projectId || repairedProjectIds.includes(item.projectId)))
          : null
        if (selected?.kind === "task" && timelineItem?.linkedTaskId === selected.id &&
            bridgedData.tasks.some((task) => task.id === selected.id)) return selected
        if (selected?.kind === "meet" && timelineItem?.linkedMeetId === selected.id &&
            bridgedData.meetItems.some((meet) => meet.id === selected.id)) return selected
        return null
      }
      const targetProjectIds = tab === "tree" ? repairedProjectIds.slice(0, 1) : repairedProjectIds
      const query = search.trim().toLowerCase()
      const candidates = bridgedData.tasks.filter((task) =>
        targetProjectIds.includes(task.projectId) &&
        (tab !== "status" || (
          matchesStatusFilter(task, statusFilter) &&
          (!hideDone || task.status !== "DONE") &&
          (!query || task.title.toLowerCase().includes(query))
        )))
      if (selected?.kind === "task" && candidates.some((task) => task.id === selected.id)) return selected
      const fallbackTaskId = candidates[0]?.id
      return fallbackTaskId ? { kind: "task", id: fallbackTaskId } : null
    })
    setSelectedTimelineItemId((selected) =>
      tab === "timeline" && selected && bridgedData.timelineItems.some((item) =>
        item.id === selected && (!item.projectId || repairedProjectIds.includes(item.projectId)))
        ? selected
        : null)
  }, [bridge.status, bridge.data])

  useEffect(() => {
    if (!connected || !contextReady) return

    const context = {
      activeTab: tab,
      selectedProjectIds,
      selectedTaskId: selection?.kind === "task" ? selection.id : null,
      selectedTimelineItemId,
      selectedWorkstreamId,
      filter,
    }
    const serialized = JSON.stringify(context)
    if (serialized === lastPersistedContext.current) return
    if (bridge.sendWorkspaceContext(context)) {
      lastPersistedContext.current = serialized
    }
  }, [
    connected,
    contextReady,
    tab,
    selectedProjectIds,
    selection,
    selectedTimelineItemId,
    selectedWorkstreamId,
    filter,
    bridge.sendWorkspaceContext,
  ])

  const multi = selectedProjectIds.length > 1
  const selectedTaskId = selection?.kind === "task" ? selection.id : null
  const selectedMeetId = selection?.kind === "meet" ? selection.id : null
  const selectTask = (id: string) => {
    const task = tasks.find((candidate) => candidate.id === id)
    if (task && (
      !selectedProjectIds.includes(task.projectId) ||
      (tab === "tree" && selectedProjectIds[0] !== task.projectId)
    )) {
      setSelectedProjectIds([task.projectId])
    }
    setSelection({ kind: "task", id })
    setSelectedTimelineItemId(null)
  }
  const selectTimelineTask = (timelineItemId: string, taskId: string) => {
    const task = tasks.find((candidate) => candidate.id === taskId)
    if (task && !selectedProjectIds.includes(task.projectId)) {
      setSelectedProjectIds([task.projectId])
    }
    setSelectedTimelineItemId(timelineItemId)
    setSelection({ kind: "task", id: taskId })
  }
  const selectTimelineMeet = (timelineItemId: string, meetId: string) => {
    const meet = meetItems.find((candidate) => candidate.id === meetId)
    if (meet && !selectedProjectIds.includes(meet.projectId)) {
      setSelectedProjectIds([meet.projectId])
    }
    setSelectedTimelineItemId(timelineItemId)
    setSelection({ kind: "meet", id: meetId })
  }
  // The single project used for the Tree tab (Tree is single-project by design)
  const treeProjectId = selectedProjectIds[0] ?? projects[0].id
  const treeProject = projects.find((p) => p.id === treeProjectId) ?? projects[0]

  const projectSections = useMemo(
    () => sections.filter((s) => s.projectId === treeProjectId),
    [sections, treeProjectId],
  )

  const applySearch = (list: Task[], keepAncestors: boolean, query = search) => {
    if (!query.trim()) return list
    const q = query.trim().toLowerCase()
    if (!keepAncestors) return list.filter((t) => t.title.toLowerCase().includes(q))
    const matchIds = new Set(list.filter((t) => t.title.toLowerCase().includes(q)).map((t) => t.id))
    const byId = new Map(list.map((t) => [t.id, t]))
    matchIds.forEach((id) => {
      let p = byId.get(id)?.parentId ? byId.get(byId.get(id)!.parentId!) : undefined
      while (p) {
        matchIds.add(p.id)
        p = p.parentId ? byId.get(p.parentId) : undefined
      }
    })
    return list.filter((t) => matchIds.has(t.id))
  }

  // Tree tab — one project, keep ancestor path when searching
  const treeTasks = useMemo(
    () => applySearch(tasks.filter((t) => t.projectId === treeProjectId), true),
    [tasks, treeProjectId, search],
  )

  // Status tab — flat list across all selected projects
  const scopedTasks = useMemo(
    () => applySearch(tasks.filter((t) => selectedProjectIds.includes(t.projectId)), false),
    [tasks, selectedProjectIds, search],
  )

  const treeCandidatesFor = (
    scopeIds: string[],
    nextFilter = filter,
    query = search,
  ): Task[] => {
    const candidates = applySearch(
      tasks.filter((task) => task.projectId === scopeIds[0]),
      true,
      query,
    )
    if (nextFilter === "all") return candidates

    const visibleIds = new Set<string>()
    const byId = new Map(candidates.map((task) => [task.id, task]))
    candidates.forEach((task) => {
      if (task.status !== "FOCUS" && task.status !== "WAIT") return
      visibleIds.add(task.id)
      if (nextFilter !== "active-path") return
      let parent = task.parentId ? byId.get(task.parentId) : undefined
      while (parent) {
        visibleIds.add(parent.id)
        parent = parent.parentId ? byId.get(parent.parentId) : undefined
      }
    })
    return candidates.filter((task) => visibleIds.has(task.id))
  }

  const statusCandidatesFor = (
    scopeIds: string[],
    nextFilter = statusFilter,
    nextHideDone = hideDone,
    query = search,
  ): Task[] =>
    applySearch(
      tasks.filter((task) => scopeIds.includes(task.projectId)),
      false,
      query,
    ).filter(
      (task) => matchesStatusFilter(task, nextFilter) && (!nextHideDone || task.status !== "DONE"),
    )

  const reconcileTaskSelection = (candidates: Task[]) => {
    const currentId = selection?.kind === "task" ? selection.id : null
    const nextId = currentId && candidates.some((task) => task.id === currentId)
      ? currentId
      : candidates[0]?.id ?? null
    setSelection(nextId ? { kind: "task", id: nextId } : null)
    setSelectedTimelineItemId(null)
  }

  const clearDetailsSelection = () => {
    setSelection(null)
    setSelectedTimelineItemId(null)
  }

  const timelineItemMatchesQuery = (item: TimelineItem, query: string) => {
    const normalized = query.trim().toLowerCase()
    return !normalized ||
      item.title.toLowerCase().includes(normalized) ||
      item.projectPath.toLowerCase().includes(normalized) ||
      (item.meta?.toLowerCase().includes(normalized) ?? false)
  }

  const reconcileScope = (scopeIds: string[]) => {
    setSelectedProjectIds(scopeIds)
    if (tab === "tree") {
      reconcileTaskSelection(treeCandidatesFor(scopeIds))
      return
    }
    if (tab === "status") {
      reconcileTaskSelection(statusCandidatesFor(scopeIds))
      return
    }
    if (tab === "timeline") {
      const item = selectedTimelineItemId
        ? timelineItems.find((timelineItem) => timelineItem.id === selectedTimelineItemId)
        : null
      const itemInScope = item &&
        (!item.projectId || scopeIds.includes(item.projectId)) &&
        timelineItemMatchesQuery(item, search)
      const selectionMatches = item && (
        selection?.kind === "task"
          ? item.linkedTaskId === selection.id
          : selection?.kind === "meet" && item.linkedMeetId === selection.id
      )
      if (!itemInScope || !selectionMatches) clearDetailsSelection()
      return
    }
    clearDetailsSelection()
  }

  const selectOnlyProject = (id: string) => reconcileScope([id])
  const toggleProject = (id: string) => {
    const next = selectedProjectIds.includes(id)
      ? selectedProjectIds.length > 1
        ? selectedProjectIds.filter((projectId) => projectId !== id)
        : selectedProjectIds
      : [...selectedProjectIds, id]
    reconcileScope(next)
  }
  const selectAllProjects = () => reconcileScope(projects.map((project) => project.id))

  const changeTab = (nextTab: TabKey) => {
    if (nextTab === tab) return
    if (nextTab === "tree") {
      reconcileTaskSelection(treeCandidatesFor(selectedProjectIds))
    } else if (nextTab === "status") {
      setStatusFilter("all")
      reconcileTaskSelection(statusCandidatesFor(selectedProjectIds, "all"))
    } else {
      clearDetailsSelection()
    }
    setTab(nextTab)
  }

  const changeTreeFilter = (nextFilter: TreeFilter) => {
    setFilter(nextFilter)
    if (tab === "tree") reconcileTaskSelection(treeCandidatesFor(selectedProjectIds, nextFilter))
  }

  const changeStatusFilter = (nextFilter: StatusFilterKey) => {
    const nextHideDone = nextFilter === "DONE" ? false : hideDone
    setStatusFilter(nextFilter)
    if (nextFilter === "DONE") setHideDone(false)
    if (tab === "status") {
      reconcileTaskSelection(statusCandidatesFor(selectedProjectIds, nextFilter, nextHideDone))
    }
  }

  const changeHideDone = (nextHideDone: boolean) => {
    const nextFilter = nextHideDone && statusFilter === "DONE" ? "all" : statusFilter
    setHideDone(nextHideDone)
    if (nextFilter !== statusFilter) setStatusFilter(nextFilter)
    if (tab === "status") {
      reconcileTaskSelection(statusCandidatesFor(selectedProjectIds, nextFilter, nextHideDone))
    }
  }

  const changeSearch = (query: string) => {
    setSearch(query)
    if (tab === "tree") {
      reconcileTaskSelection(treeCandidatesFor(selectedProjectIds, filter, query))
    } else if (tab === "status") {
      reconcileTaskSelection(statusCandidatesFor(selectedProjectIds, statusFilter, hideDone, query))
    } else if (tab === "timeline" && selectedTimelineItemId) {
      const item = timelineItems.find((timelineItem) => timelineItem.id === selectedTimelineItemId)
      if (!item || !timelineItemMatchesQuery(item, query)) clearDetailsSelection()
    }
  }

  const handleCalendarToday = () => setCalendarSelectedDate(todayKey())
  const handleCalendarTomorrow = () => setCalendarSelectedDate(addDaysKey(todayKey(), 1))
  // Single prev/next: steps by one day in Day view, by seven days in Week view.
  const handleCalendarStep = (dir: number) =>
    setCalendarSelectedDate((current) => addDaysKey(current, calendarViewMode === "week" ? dir * 7 : dir))
  const handleCalendarPickDay = (dateKey: string) => {
    setCalendarSelectedDate(dateKey)
    setCalendarViewMode("day")
  }
  const startDetailsResize = (event: React.MouseEvent) => {
    event.preventDefault()
    const onMove = (e: MouseEvent) => setDetailsWidth(Math.max(260, Math.min(560, window.innerWidth - e.clientX)))
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
  // Planned work: persists through the C# bridge when connected; local-only in dev mock mode.
  const handlePlannedWork = (
    taskId: string,
    plannedStartAtUtc: string | null,
    plannedDurationMinutes: number | null,
  ) => {
    if (connected) {
      bridge.sendCommand({ type: "updateTaskPlannedWork", taskId, plannedStartAtUtc, plannedDurationMinutes })
      return
    }
    if (!bridged) {
      setMockTasks((prev) => prev.map((t) => (t.id === taskId
        ? { ...t, plannedStartAtUtc: plannedStartAtUtc ?? undefined, plannedDurationMinutes: plannedDurationMinutes ?? undefined }
        : t)))
    }
  }

  const selectedTask = tasks.find((t) => t.id === selectedTaskId) ?? null

  const toggle = (setter: React.Dispatch<React.SetStateAction<Set<string>>>) => (id: string) =>
    setter((prev) => {
      const next = new Set(prev)
      next.has(id) ? next.delete(id) : next.add(id)
      return next
    })

  const handleTogglePin = (id: string) =>
    !bridged && setMockTasks((prev) => prev.map((t) => (t.id === id ? { ...t, pinned: !t.pinned } : t)))

  const handleApply = (updated: Task) => {
    if (bridged) return
    setMockTasks((prev) => prev.map((t) => (t.id === updated.id ? updated : t)))
    if (!selectedProjectIds.includes(updated.projectId)) setSelectedProjectIds([updated.projectId])
  }

  const handleDelete = (id: string) => {
    if (bridged) return
    setMockTasks((prev) => prev.filter((t) => t.id !== id && t.parentId !== id))
    if (selection?.kind === "task" && selection.id === id) {
      setSelection(null)
      setSelectedTimelineItemId(null)
    }
  }

  const handleApplyMeet = (updated: MeetItem) => {
    if (bridged) return
    setMockMeetItems((prev) => prev.map((m) => (m.id === updated.id ? updated : m)))
  }

  const handleDeleteMeet = (id: string) => {
    if (bridged) return
    setMockMeetItems((prev) => prev.filter((m) => m.id !== id))
    if (selection?.kind === "meet" && selection.id === id) {
      setSelection(null)
      setSelectedTimelineItemId(null)
    }
  }

  // The currently selected MeetItem comes from the timeline data source.
  const selectedMeet = meetItems.find((m) => m.id === selectedMeetId) ?? null
  const sendTaskEdit = (
    taskId: string,
    field: "title" | "status" | "pinToPanel" | "notes",
    value: string | boolean,
  ) => {
    if (!connected) return false
    let command: WorkspaceTaskCommand
    switch (field) {
      case "title":
        command = { type: "updateTaskTitle", taskId, title: String(value) }
        break
      case "status":
        command = { type: "updateTaskStatus", taskId, status: value as Status }
        break
      case "pinToPanel":
        command = { type: "updateTaskPinToPanel", taskId, pinToPanel: Boolean(value) }
        break
      case "notes":
        command = { type: "updateTaskNotes", taskId, notes: String(value) }
        break
    }
    return bridge.sendCommand(command)
  }
  const pendingFields = new Set(
    bridge.pendingCommands
      .filter((command) => command.taskId === selectedTaskId)
      .map((command) => command.type === "updateTaskStatus"
        ? "status"
        : command.type === "updateTaskPinToPanel"
          ? "pinToPanel"
          : command.type === "updateTaskNotes"
            ? "notes"
            : "title"),
  )

  if (bridge.status === "loading" || (bridged && !contextReady)) {
    return (
      <div className="flex h-screen items-center justify-center bg-background text-sm text-muted-foreground">
        Loading current app state…
      </div>
    )
  }

  return (
    <div className="flex h-screen flex-col overflow-hidden bg-background text-foreground">
      <div className="flex min-h-0 flex-1">
        <main className="flex min-w-0 flex-1 flex-col">
          <WorkspaceHeader
            tab={tab}
            onTabChange={changeTab}
            filter={filter}
            onFilterChange={changeTreeFilter}
            statusFilter={statusFilter}
            onStatusFilterChange={changeStatusFilter}
            hideDone={hideDone}
            onHideDoneChange={changeHideDone}
            statusTasks={scopedTasks}
            search={search}
            onSearchChange={changeSearch}
            calendarSelectedDate={calendarSelectedDate}
            calendarViewMode={calendarViewMode}
            calendarShowDone={calendarShowDone}
            onCalendarToday={handleCalendarToday}
            onCalendarTomorrow={handleCalendarTomorrow}
            onCalendarStep={handleCalendarStep}
            onCalendarShowDoneChange={setCalendarShowDone}
            onCalendarViewModeChange={setCalendarViewMode}
          />
          <ProjectScopeBar
            projects={projects}
            tasks={tasks}
            selectedProjectIds={selectedProjectIds}
            onSelectOnly={selectOnlyProject}
            onToggleProject={toggleProject}
            onSelectAll={selectAllProjects}
          />
          <div className="min-h-0 flex-1 overflow-y-auto">
            {tab === "tree" && (
              <div className="flex flex-col">
                {multi && (
                  <div className="flex flex-wrap items-center gap-2 border-b border-border bg-card/40 px-5 py-2.5">
                    <FolderTree className="size-4 shrink-0 text-muted-foreground" />
                    <span className="text-xs text-muted-foreground">
                      Select one project to edit its tree — showing
                    </span>
                    {selectedProjectIds.map((id) => {
                      const p = projects.find((x) => x.id === id)
                      if (!p) return null
                      const active = id === treeProjectId
                      return (
                        <button
                          key={id}
                          onClick={() => selectOnlyProject(id)}
                          className={cn(
                            "flex items-center gap-1.5 rounded-md border px-2 py-0.5 text-xs font-medium transition-colors",
                            active
                              ? "border-primary/50 bg-primary/10 text-foreground"
                              : "border-border text-muted-foreground hover:text-foreground",
                          )}
                        >
                          <span className="size-1.5 rounded-full" style={{ backgroundColor: p.color }} />
                          {p.name}
                        </button>
                      )
                    })}
                  </div>
                )}
                <TreeView
                  sections={projectSections}
                  tasks={treeTasks}
                  filter={filter}
                  selectedTaskId={selectedTaskId}
                  collapsedSections={collapsedSections}
                  collapsedTasks={collapsedTasks}
                  onSelectTask={selectTask}
                  onToggleSection={toggle(setCollapsedSections)}
                  onToggleTask={toggle(setCollapsedTasks)}
                  onTogglePin={handleTogglePin}
                  readOnly={bridged}
                />
              </div>
            )}
            {tab === "status" && (
              <StatusBoard
                tasks={scopedTasks}
                projects={projects}
                sections={sections}
                selectedTaskId={selectedTaskId}
                onSelectTask={selectTask}
                filter={statusFilter}
                hideDone={hideDone}
              />
            )}
            {tab === "timeline" && (
              <TimelineView
                projectIds={selectedProjectIds}
                projects={projects}
                items={timelineItems}
                selectedTimelineItemId={selectedTimelineItemId}
                onSelectMeet={selectTimelineMeet}
                onSelectTask={selectTimelineTask}
                search={search}
              />
            )}
            {tab === "calendar" && (
              <CalendarView
                viewMode={calendarViewMode}
                selectedDate={calendarSelectedDate}
                projects={projects}
                sections={sections}
                tasks={tasks}
                meetItems={meetItems}
                selectedProjectIds={selectedProjectIds}
                selectedTaskId={selectedTaskId}
                selectedMeetId={selectedMeetId}
                showDone={calendarShowDone}
                canSchedule={connected || !bridged}
                onSelectTask={selectTask}
                onSelectMeet={(meetId) => setSelection({ kind: "meet", id: meetId })}
                onPickDay={handleCalendarPickDay}
                onPlanTask={(taskId, iso, duration) => handlePlannedWork(taskId, iso, duration)}
                onClearPlanned={(taskId) => handlePlannedWork(taskId, null, null)}
              />
            )}
            {tab === "workstreams" && <WorkstreamsPlaceholder />}
          </div>
        </main>

        <div className="relative hidden shrink-0 xl:flex" style={{ width: detailsWidth }}>
          <div
            onMouseDown={startDetailsResize}
            role="separator"
            aria-orientation="vertical"
            title="Drag to resize"
            className="absolute left-0 top-0 z-20 h-full w-1.5 -translate-x-1/2 cursor-col-resize bg-transparent transition-colors hover:bg-primary/40"
          />
          {selection?.kind === "meet" && selectedMeet ? (
            <MeetDetailsPanel
              meet={selectedMeet}
              projects={projects}
              tasks={tasks}
              onApply={handleApplyMeet}
              onDelete={handleDeleteMeet}
            />
          ) : (
            <DetailsPanel
              task={selectedTask}
              projects={projects}
              sections={sections}
              onApply={handleApply}
              onDelete={handleDelete}
              editMode={connected ? "connected" : readOnly ? "readonly" : "full"}
              pendingFields={pendingFields}
              bridgeError={bridge.error}
              onBridgeEdit={sendTaskEdit}
              onClearBridgeError={bridge.clearError}
            />
          )}
        </div>
      </div>

      <ActiveNowStrip
        tasks={tasks}
        projects={projects}
        sections={sections}
        selectedTaskId={selectedTaskId}
        onSelectTask={selectTask}
        taskIds={activeNowTaskIds}
      />
    </div>
  )
}

function WorkstreamsPlaceholder() {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-3 px-6 text-center">
      <div className="flex size-12 items-center justify-center rounded-full bg-accent">
        <FolderTree className="size-5 text-muted-foreground" />
      </div>
      <div>
        <p className="text-sm font-medium text-foreground">Workstreams</p>
        <p className="mt-1 max-w-sm text-xs text-muted-foreground text-pretty">
          Workstreams will group long-running work tracks across projects. Not the same as a Project, Status, or
          Timeline — coming later.
        </p>
      </div>
      <span className="rounded bg-accent px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
        Later
      </span>
    </div>
  )
}
