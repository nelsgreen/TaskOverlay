"use client"

import { useEffect, useMemo, useRef, useState } from "react"
import { Folder, FolderTree } from "lucide-react"
import type { MeetItem, Section, Status, StatusFilterKey, TabKey, Task, TimelineItem, TreeFilter, WorkspaceTaskCommand, WorkstreamFilter } from "@/lib/types"
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
import { WorkstreamsView, WorkstreamDetailPanel, deriveWorkstreamState, isRootSection } from "./workstreams-view"

type WorkspaceSelection =
  | { kind: "task"; id: string }
  | { kind: "meet"; id: string }
  | null

export function TaskManager() {
  const bridge = useWorkspaceBridge()
  const [mockTasks, setMockTasks] = useState<Task[]>(initialTasks)
  const [mockMeetItems, setMockMeetItems] = useState<MeetItem[]>(initialMeetItems)
  // Sections need local state in mock mode so "Add workstream" stays usable for
  // orientation; connected mode always uses the authoritative snapshot sections.
  const [mockSectionList, setMockSectionList] = useState<Section[]>(mockSections)
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
  // Optimistic planned-work overrides: reflect duration/time immediately in the
  // grid, then reconciled by the authoritative C# snapshot (does not persist).
  const [plannedOverrides, setPlannedOverrides] = useState<Record<string, { plannedStartAtUtc: string | null; plannedDurationMinutes: number | null }>>({})
  const [filter, setFilter] = useState<TreeFilter>("all")
  const [statusFilter, setStatusFilter] = useState<StatusFilterKey>("all")
  const [hideDone, setHideDone] = useState(false)
  const [wsFilter, setWsFilter] = useState<WorkstreamFilter>("all")
  const [addingWorkstream, setAddingWorkstream] = useState(false)
  const [addingTreeSection, setAddingTreeSection] = useState(false)
  const [newSectionName, setNewSectionName] = useState("")
  const [pendingDelete, setPendingDelete] = useState<string | null>(null)
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
  const sections = bridge.data?.sections ?? mockSectionList
  const rawTasks = bridge.data?.tasks ?? mockTasks
  const tasks = useMemo(() => {
    if (Object.keys(plannedOverrides).length === 0) return rawTasks
    return rawTasks.map((t) => {
      const o = plannedOverrides[t.id]
      return o ? { ...t, plannedStartAtUtc: o.plannedStartAtUtc ?? undefined, plannedDurationMinutes: o.plannedDurationMinutes ?? undefined } : t
    })
  }, [rawTasks, plannedOverrides])
  const meetItems = bridge.data?.meetItems ?? mockMeetItems
  const timelineItems = bridge.data?.timelineItems ?? mockTimelineItems
  const activeNowTaskIds = bridge.data?.activeNowTaskIds

  // Drop an optimistic override once the authoritative snapshot reflects the same value.
  useEffect(() => {
    const data = bridge.data
    if (!data) return
    setPlannedOverrides((prev) => {
      if (Object.keys(prev).length === 0) return prev
      const sameStart = (a: string | null, b?: string) => {
        if (a === null) return !b
        return !!b && new Date(a).getTime() === new Date(b).getTime()
      }
      let changed = false
      const next = { ...prev }
      for (const t of data.tasks) {
        const o = next[t.id]
        if (o && sameStart(o.plannedStartAtUtc, t.plannedStartAtUtc) &&
            (o.plannedDurationMinutes ?? null) === (t.plannedDurationMinutes ?? null)) {
          delete next[t.id]
          changed = true
        }
      }
      return changed ? next : prev
    })
  }, [bridge.data])

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
      // A persisted workstream id may reference a since-deleted section.
      const restoredWorkstream = context.selectedWorkstreamId &&
        bridgedData.sections.some((section) => section.id === context.selectedWorkstreamId)
        ? context.selectedWorkstreamId
        : null

      setSelectedProjectIds(restoredProjectIds)
      setSelection(restoredTask ? { kind: "task", id: restoredTask } : null)
      setSelectedTimelineItemId(restoredTimelineItem)
      setSelectedWorkstreamId(restoredWorkstream)
      setTab(context.activeTab)
      setFilter(context.filter)
      lastPersistedContext.current = JSON.stringify({
        activeTab: context.activeTab,
        selectedProjectIds: restoredProjectIds,
        selectedTaskId: restoredTask,
        selectedTimelineItemId: restoredTimelineItem,
        selectedWorkstreamId: restoredWorkstream,
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
      if (tab === "workstreams") {
        // Keep the clicked task selected across snapshot refreshes so Details
        // editing keeps working inside Workstreams.
        if (selected?.kind === "task" && bridgedData.tasks.some((task) => task.id === selected.id)) return selected
        return null
      }
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
    setSelectedWorkstreamId((selected) =>
      selected && bridgedData.sections.some((section) => section.id === selected)
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

  // Matches title, notes/context, waitingFor, and the task's project/section names.
  const taskMatchesQuery = (t: Task, q: string) => {
    if (t.title.toLowerCase().includes(q)) return true
    if (t.notes?.toLowerCase().includes(q)) return true
    if (t.waitingFor?.toLowerCase().includes(q)) return true
    const project = projects.find((p) => p.id === t.projectId)
    if (project?.name.toLowerCase().includes(q)) return true
    const section = sections.find((s) => s.id === t.sectionId)
    if (section?.name.toLowerCase().includes(q)) return true
    return false
  }

  const applySearch = (list: Task[], keepAncestors: boolean, query = search) => {
    if (!query.trim()) return list
    const q = query.trim().toLowerCase()
    if (!keepAncestors) return list.filter((t) => taskMatchesQuery(t, q))
    const matchIds = new Set(list.filter((t) => taskMatchesQuery(t, q)).map((t) => t.id))
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
    if (tab === "workstreams") {
      clearDetailsSelection()
      // Drop the selected workstream when its project leaves the scope.
      setSelectedWorkstreamId((selected) => {
        if (!selected) return null
        const section = sections.find((candidate) => candidate.id === selected)
        return section && scopeIds.includes(section.projectId) ? selected : null
      })
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
      // Optimistic: reflect immediately, then the snapshot reconciles this override.
      setPlannedOverrides((prev) => ({ ...prev, [taskId]: { plannedStartAtUtc, plannedDurationMinutes } }))
      bridge.sendCommand({ type: "updateTaskPlannedWork", taskId, plannedStartAtUtc, plannedDurationMinutes })
      return
    }
    if (!bridged) {
      setMockTasks((prev) => prev.map((t) => (t.id === taskId
        ? { ...t, plannedStartAtUtc: plannedStartAtUtc ?? undefined, plannedDurationMinutes: plannedDurationMinutes ?? undefined }
        : t)))
    }
  }

  // Add task from Workspace: defaults to the current Tree project, and to the
  // selected task's section when that task is in the same project.
  // Matches v0's "+ Task" toolbar button (workspace-header.tsx TreeToolbar):
  // creates immediately with a placeholder title, then the task is auto-selected
  // so the title can be renamed straight away in Details.
  const handleCreateTask = (title = "New task") => {
    const trimmed = title.trim()
    if (!trimmed) return
    const currentTask = selection?.kind === "task" ? tasks.find((t) => t.id === selection.id) : null
    const targetSectionId = currentTask && currentTask.projectId === treeProjectId
      ? currentTask.sectionId
      : null

    if (connected) {
      bridge.sendCreateTask({ title: trimmed, projectId: treeProjectId, sectionId: targetSectionId })
      return
    }
    if (!bridged) {
      const id = `local-${Date.now()}-${Math.random().toString(16).slice(2)}`
      const fallbackSectionId = sections.find((s) => s.projectId === treeProjectId)?.id ?? ""
      const created: Task = {
        id,
        projectId: treeProjectId,
        sectionId: targetSectionId ?? fallbackSectionId,
        parentId: null,
        title: trimmed,
        status: "TODO",
        pinned: false,
        reminder: "none",
      }
      setMockTasks((prev) => [...prev, created])
      setSelection({ kind: "task", id })
    }
  }

  // Once the bridge confirms a created task, select it so Details opens on it immediately.
  useEffect(() => {
    if (!bridge.lastCreatedTaskId) return
    setSelection({ kind: "task", id: bridge.lastCreatedTaskId })
    bridge.clearLastCreatedTaskId()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bridge.lastCreatedTaskId])

  // ── Workstreams: a workstream is a top-level section under a project ──
  const singleProjectScope = selectedProjectIds.length === 1
  const canCreateWorkstream = singleProjectScope && !readOnly
  const createWorkstreamHint = !singleProjectScope
    ? "Select one project in Project Scope to add a workstream."
    : readOnly
      ? "Read-only: connect to add workstreams."
      : null

  const handleSelectWorkstream = (id: string) => {
    setSelectedWorkstreamId(id)
    // Clearing the task selection switches the right panel to the workstream view.
    setSelection(null)
    setSelectedTimelineItemId(null)
  }

  const handleCreateWorkstream = (title: string) => {
    const trimmed = title.trim()
    if (!trimmed || !singleProjectScope) return
    const projectId = selectedProjectIds[0]
    if (connected) {
      bridge.sendCreateSection({ title: trimmed, projectId })
      setAddingWorkstream(false)
      return
    }
    if (!bridged) {
      const id = `local-s-${Date.now()}-${Math.random().toString(16).slice(2)}`
      setMockSectionList((prev) => [...prev, { id, projectId, name: trimmed }])
      setSelectedWorkstreamId(id)
      setSelection(null)
      setAddingWorkstream(false)
    }
  }

  // Add task inside the currently selected workstream (uses the createTask
  // bridge command with the workstream's sectionId).
  const handleAddTaskInWorkstream = () => {
    const section = selectedWorkstreamId
      ? sections.find((candidate) => candidate.id === selectedWorkstreamId)
      : null
    if (!section) return
    if (connected) {
      bridge.sendCreateTask({ title: "New task", projectId: section.projectId, sectionId: section.id })
      return
    }
    if (!bridged) {
      const id = `local-${Date.now()}-${Math.random().toString(16).slice(2)}`
      const created: Task = {
        id,
        projectId: section.projectId,
        sectionId: section.id,
        parentId: null,
        title: "New task",
        status: "TODO",
        pinned: false,
        reminder: "none",
      }
      setMockTasks((prev) => [...prev, created])
      setSelection({ kind: "task", id })
    }
  }

  // Once the bridge confirms a created workstream, select it so its (empty)
  // task list opens immediately in the right panel.
  useEffect(() => {
    if (!bridge.lastCreatedSectionId) return
    setSelectedWorkstreamId(bridge.lastCreatedSectionId)
    setSelection(null)
    bridge.clearLastCreatedSectionId()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bridge.lastCreatedSectionId])

  // Toolbar summary counts for the Workstreams filter chips (real rollups).
  const wsSummary = useMemo(() => {
    const scoped = sections.filter((section) =>
      selectedProjectIds.includes(section.projectId) && !isRootSection(section))
    const counts: Record<string, number> = { all: scoped.length, active: 0, waiting: 0, done: 0 }
    for (const section of scoped) {
      const state = deriveWorkstreamState(tasks.filter((task) => task.sectionId === section.id))
      if (state && state in counts) counts[state]++
    }
    return counts
  }, [sections, tasks, selectedProjectIds])

  const selectedTask = tasks.find((t) => t.id === selectedTaskId) ?? null
  const selectedWorkstreamSection = selectedWorkstreamId
    ? sections.find((s) => s.id === selectedWorkstreamId && !isRootSection(s)) ?? null
    : null
  const selectedWorkstreamProject = selectedWorkstreamSection
    ? projects.find((p) => p.id === selectedWorkstreamSection.projectId) ?? null
    : null

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

  // Choose a safe selection after the current task disappears (deleted/moved out
  // of scope): the next sibling in the same section, else clear.
  const clearSelectionAfterRemoval = (removedId: string) => {
    if (selection?.kind !== "task" || selection.id !== removedId) return
    const removed = tasks.find((t) => t.id === removedId)
    const nearby = removed
      ? tasks.find((t) => t.id !== removedId && t.sectionId === removed.sectionId && t.projectId === removed.projectId)
      : null
    setSelection(nearby ? { kind: "task", id: nearby.id } : null)
    setSelectedTimelineItemId(null)
  }

  const handleDelete = (id: string) => {
    if (connected) {
      bridge.sendCommand({ type: "deleteTask", taskId: id })
      clearSelectionAfterRemoval(id)
      return
    }
    if (!bridged) {
      // Match connected DeleteNode: reparent direct children up to the deleted
      // task's parent instead of cascade-deleting them.
      const removed = tasks.find((t) => t.id === id)
      const newParent = removed?.parentId ?? null
      setMockTasks((prev) => prev
        .filter((t) => t.id !== id)
        .map((t) => (t.parentId === id ? { ...t, parentId: newParent } : t)))
      clearSelectionAfterRemoval(id)
    }
  }

  // Delete always routes through a confirmation dialog (Tree menu + Details button).
  const requestDeleteTask = (id: string) => setPendingDelete(id)
  const confirmDeleteTask = () => {
    if (pendingDelete) handleDelete(pendingDelete)
    setPendingDelete(null)
  }
  const pendingDeleteTask = pendingDelete ? tasks.find((t) => t.id === pendingDelete) ?? null : null
  const pendingDeleteHasSubtasks = pendingDeleteTask
    ? tasks.some((t) => t.parentId === pendingDeleteTask.id)
    : false

  // Add a subtask under a specific task (Tree context menu). Reuses createTask
  // with parentTaskId so the new task inherits the parent's project/section.
  const handleAddSubtask = (parentTaskId: string) => {
    if (connected) {
      bridge.sendCreateTask({ title: "New task", parentTaskId })
      return
    }
    if (!bridged) {
      const parent = tasks.find((t) => t.id === parentTaskId)
      if (!parent) return
      const id = `local-${Date.now()}-${Math.random().toString(16).slice(2)}`
      const created: Task = {
        id,
        projectId: parent.projectId,
        sectionId: parent.sectionId,
        parentId: parent.id,
        title: "New task",
        status: "TODO",
        pinned: false,
        reminder: "none",
      }
      setMockTasks((prev) => [...prev, created])
      setSelection({ kind: "task", id })
    }
  }

  // Move a task to another section/project root through the bridge. sectionId is
  // the snapshot section id (group:{id} or project:{id}:root).
  const handleMoveTask = (taskId: string, sectionId: string) => {
    if (connected) {
      bridge.sendCommand({ type: "moveTask", taskId, sectionId })
      return
    }
    if (!bridged) {
      const section = sections.find((s) => s.id === sectionId)
      if (!section) return
      setMockTasks((prev) => prev.map((t) => (t.id === taskId
        ? { ...t, sectionId: section.id, projectId: section.projectId, parentId: null }
        : t)))
    }
  }

  // Create a task at the current Tree project's root (empty-area context menu).
  const handleCreateTaskAtRoot = () => {
    const rootSectionId = `project:${treeProjectId}:root`
    if (connected) {
      bridge.sendCreateTask({ title: "New task", projectId: treeProjectId, sectionId: rootSectionId })
      return
    }
    if (!bridged) {
      const fallbackSectionId = sections.find((s) => s.projectId === treeProjectId)?.id ?? ""
      const id = `local-${Date.now()}-${Math.random().toString(16).slice(2)}`
      const created: Task = {
        id,
        projectId: treeProjectId,
        sectionId: fallbackSectionId,
        parentId: null,
        title: "New task",
        status: "TODO",
        pinned: false,
        reminder: "none",
      }
      setMockTasks((prev) => [...prev, created])
      setSelection({ kind: "task", id })
    }
  }

  // Create a section under the current Tree project. Sections have no rename
  // command yet, so the name is taken up front via an inline input.
  const handleCreateTreeSection = (title: string) => {
    const trimmed = title.trim()
    if (!trimmed) return
    if (connected) {
      bridge.sendCreateSection({ title: trimmed, projectId: treeProjectId })
      setAddingTreeSection(false)
      return
    }
    if (!bridged) {
      const id = `local-s-${Date.now()}-${Math.random().toString(16).slice(2)}`
      setMockSectionList((prev) => [...prev, { id, projectId: treeProjectId, name: trimmed }])
      setAddingTreeSection(false)
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
    field: "title" | "status" | "pinToPanel" | "notes" | "waitingFor" | "reminder" | "deadline",
    value: string | boolean | null | { remindAtUtc: string | null; remindEveryMinutes: number | null },
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
      case "waitingFor":
        command = { type: "updateTaskWaitingFor", taskId, waitingFor: String(value ?? "") }
        break
      case "reminder": {
        const reminder = value as { remindAtUtc: string | null; remindEveryMinutes: number | null }
        command = {
          type: "updateTaskReminder",
          taskId,
          remindAtUtc: reminder.remindAtUtc,
          remindEveryMinutes: reminder.remindEveryMinutes,
        }
        break
      }
      case "deadline":
        command = { type: "updateTaskDeadline", taskId, deadlineAtUtc: value as string | null }
        break
    }
    return bridge.sendCommand(command)
  }
  const pendingFieldOf: Record<WorkspaceTaskCommand["type"], string> = {
    updateTaskStatus: "status",
    updateTaskPinToPanel: "pinToPanel",
    updateTaskNotes: "notes",
    updateTaskTitle: "title",
    updateTaskPlannedWork: "plannedWork",
    updateTaskWaitingFor: "waitingFor",
    updateTaskReminder: "reminder",
    updateTaskDeadline: "deadline",
    moveTask: "location",
    deleteTask: "delete",
  }
  const pendingFields = new Set(
    bridge.pendingCommands
      .filter((command) => command.taskId === selectedTaskId)
      .map((command) => pendingFieldOf[command.type]),
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
            treeProjectName={treeProject.name}
            treeProjectColor={treeProject.color}
            onAddTask={() => handleCreateTask()}
            addTaskDisabled={readOnly}
            onAddSection={() => setAddingTreeSection(true)}
            addSectionDisabled={readOnly}
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
            wsFilter={wsFilter}
            onWsFilterChange={setWsFilter}
            wsSummary={wsSummary}
            onAddWorkstream={() => setAddingWorkstream(true)}
            addWorkstreamDisabled={!canCreateWorkstream}
            addWorkstreamHint={createWorkstreamHint}
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
                {addingTreeSection && !readOnly && (
                  <div className="flex items-center gap-2 border-b border-border bg-card/40 px-5 py-2">
                    <Folder className="size-3.5 shrink-0 text-muted-foreground" />
                    <input
                      autoFocus
                      value={newSectionName}
                      onChange={(e) => setNewSectionName(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") {
                          handleCreateTreeSection(newSectionName)
                          setNewSectionName("")
                        } else if (e.key === "Escape") {
                          setAddingTreeSection(false)
                          setNewSectionName("")
                        }
                      }}
                      placeholder={`New section in ${treeProject.name}…`}
                      className="h-7 flex-1 rounded-md border border-input bg-background px-2.5 text-xs text-foreground outline-none transition-colors focus:border-primary/60 focus:ring-1 focus:ring-primary/20"
                    />
                    <button
                      type="button"
                      onClick={() => { handleCreateTreeSection(newSectionName); setNewSectionName("") }}
                      disabled={!newSectionName.trim()}
                      className="h-7 rounded-md bg-primary px-2.5 text-[11px] font-semibold text-primary-foreground transition-colors hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-50"
                    >
                      Create
                    </button>
                    <button
                      type="button"
                      onClick={() => { setAddingTreeSection(false); setNewSectionName("") }}
                      className="h-7 rounded-md border border-border px-2.5 text-[11px] font-medium text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
                    >
                      Cancel
                    </button>
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
                  canEdit={!readOnly}
                  onCreateTaskHere={handleCreateTaskAtRoot}
                  onCreateSection={() => setAddingTreeSection(true)}
                  onAddSubtask={handleAddSubtask}
                  onDeleteTask={requestDeleteTask}
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
            {tab === "workstreams" && (
              <WorkstreamsView
                projects={projects}
                sections={sections}
                tasks={tasks}
                selectedProjectIds={selectedProjectIds}
                wsFilter={wsFilter}
                search={search}
                selectedWorkstreamId={selectedWorkstreamId}
                onSelectWorkstream={handleSelectWorkstream}
                adding={addingWorkstream}
                onAddingChange={setAddingWorkstream}
                onCreateWorkstream={handleCreateWorkstream}
                canCreate={canCreateWorkstream}
                createHint={createWorkstreamHint}
                addProjectName={treeProject.name}
              />
            )}
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
          ) : tab === "workstreams" && !selectedTask && selectedWorkstreamSection && selectedWorkstreamProject ? (
            <WorkstreamDetailPanel
              section={selectedWorkstreamSection}
              project={selectedWorkstreamProject}
              tasks={tasks.filter((t) => t.sectionId === selectedWorkstreamSection.id)}
              search={search}
              readOnly={readOnly}
              onSelectTask={selectTask}
              onAddTask={handleAddTaskInWorkstream}
            />
          ) : (
            <DetailsPanel
              task={selectedTask}
              projects={projects}
              sections={sections}
              onApply={handleApply}
              onDelete={requestDeleteTask}
              onMoveTask={handleMoveTask}
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

      {pendingDeleteTask && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 px-4"
          role="dialog"
          aria-modal="true"
          onClick={() => setPendingDelete(null)}
        >
          <div
            onClick={(e) => e.stopPropagation()}
            className="w-full max-w-sm rounded-lg border border-border bg-card p-5 shadow-xl"
          >
            <h2 className="text-sm font-semibold text-foreground">Delete task?</h2>
            <p className="mt-2 text-[13px] leading-relaxed text-muted-foreground">
              "{pendingDeleteTask.title}" will be deleted.
              {pendingDeleteHasSubtasks
                ? " Its subtasks will be moved up to the parent, not deleted."
                : ""}
            </p>
            <div className="mt-4 flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setPendingDelete(null)}
                className="rounded-md border border-border px-3 py-1.5 text-[12px] font-medium text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={confirmDeleteTask}
                className="rounded-md bg-destructive px-3 py-1.5 text-[12px] font-semibold text-white transition-colors hover:bg-destructive/90"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
