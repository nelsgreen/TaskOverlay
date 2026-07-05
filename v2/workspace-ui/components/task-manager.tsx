"use client"

import { useEffect, useMemo, useRef, useState } from "react"
import { CalendarDays, FolderTree } from "lucide-react"
import type { MeetItem, Status, StatusFilterKey, TabKey, Task, TreeFilter, WorkspaceTaskCommand } from "@/lib/types"
import {
  initialMeetItems,
  initialTasks,
  projects as mockProjects,
  sections as mockSections,
  timelineItems as mockTimelineItems,
} from "@/lib/mock-data"
import { cn } from "@/lib/utils"
import { useWorkspaceBridge } from "@/lib/workspace-bridge"
import { WorkspaceHeader } from "./workspace-header"
import { ProjectScopeBar } from "./project-scope-bar"
import { TreeView } from "./tree-view"
import { StatusBoard } from "./status-board"
import { TimelineView } from "./timeline-view"
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
  const [filter, setFilter] = useState<TreeFilter>("all")
  const [statusFilter, setStatusFilter] = useState<StatusFilterKey>("all")
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
      const restoredTask = context.selectedTaskId &&
        bridgedData.tasks.some((task) => task.id === context.selectedTaskId)
        ? context.selectedTaskId
        : bridgedData.tasks.find((task) => restoredProjectIds.includes(task.projectId))?.id ?? null
      const restoredTimelineItem = context.selectedTimelineItemId &&
        bridgedData.timelineItems.some((item) => item.id === context.selectedTimelineItemId)
        ? context.selectedTimelineItemId
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
    setSelectedProjectIds((selected) => {
      const valid = selected.filter((id) =>
        bridgedData.projects.some((project) => project.id === id))
      return valid.length > 0 ? valid : firstProjectId ? [firstProjectId] : []
    })
    setSelection((selected) => {
      if (selected?.kind === "task" &&
          bridgedData.tasks.some((task) => task.id === selected.id)) return selected
      const fallbackTaskId = bridgedData.tasks.find((task) =>
        selectedProjectIds.includes(task.projectId))?.id
      return fallbackTaskId ? { kind: "task", id: fallbackTaskId } : null
    })
    setSelectedTimelineItemId((selected) =>
      selected && bridgedData.timelineItems.some((item) => item.id === selected)
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
    setSelection({ kind: "task", id })
    setSelectedTimelineItemId(null)
  }
  const selectMeet = (id: string) => {
    setSelection({ kind: "meet", id })
    setSelectedTimelineItemId(null)
  }
  const selectTimelineTask = (timelineItemId: string, taskId: string) => {
    setSelectedTimelineItemId(timelineItemId)
    setSelection({ kind: "task", id: taskId })
  }
  const selectTimelineMeet = (timelineItemId: string, meetId: string) => {
    setSelectedTimelineItemId(timelineItemId)
    setSelection({ kind: "meet", id: meetId })
  }
  const changeTab = (nextTab: TabKey) => {
    if (nextTab === "status" && tab !== "status") setStatusFilter("all")
    setTab(nextTab)
  }

  // The single project used for the Tree tab (Tree is single-project by design)
  const treeProjectId = selectedProjectIds[0] ?? projects[0].id
  const treeProject = projects.find((p) => p.id === treeProjectId) ?? projects[0]

  const projectSections = useMemo(
    () => sections.filter((s) => s.projectId === treeProjectId),
    [sections, treeProjectId],
  )

  // --- Project selection handlers ---
  const selectOnlyProject = (id: string) => {
    setSelectedProjectIds([id])
    if (selection?.kind !== "task" ||
        tasks.find((task) => task.id === selection.id)?.projectId !== id) {
      const taskId = tasks.find((task) => task.projectId === id)?.id
      setSelection(taskId ? { kind: "task", id: taskId } : null)
      setSelectedTimelineItemId(null)
    }
  }
  const toggleProject = (id: string) => {
    const next = selectedProjectIds.includes(id)
      ? selectedProjectIds.length > 1
        ? selectedProjectIds.filter((projectId) => projectId !== id)
        : selectedProjectIds
      : [...selectedProjectIds, id]
    setSelectedProjectIds(next)
    if (selection?.kind === "task" &&
        !next.includes(tasks.find((task) => task.id === selection.id)?.projectId ?? "")) {
      const taskId = tasks.find((task) => next.includes(task.projectId))?.id
      setSelection(taskId ? { kind: "task", id: taskId } : null)
      setSelectedTimelineItemId(null)
    }
  }
  const selectAllProjects = () => setSelectedProjectIds(projects.map((p) => p.id))

  const applySearch = (list: Task[], keepAncestors: boolean) => {
    if (!search.trim()) return list
    const q = search.trim().toLowerCase()
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

  const handleNewMeet = () => {
    if (bridged) return
    const defaultProjectId = selectedProjectIds.length === 1 ? selectedProjectIds[0] : projects[0].id
    const today = new Date().toISOString().slice(0, 10)
    const newMeet: MeetItem = {
      id: `m-${Date.now()}`,
      projectId: defaultProjectId,
      title: "New meeting",
      date: today,
      startTime: "09:00",
      duration: "30m",
    }
    setMockMeetItems((prev) => [...prev, newMeet])
    selectMeet(newMeet.id)
  }

  // The currently selected MeetItem (may come from timeline items or newly created)
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
            treeProject={treeProject}
            tab={tab}
            onTabChange={changeTab}
            filter={filter}
            onFilterChange={setFilter}
            statusFilter={statusFilter}
            onStatusFilterChange={setStatusFilter}
            statusTasks={scopedTasks}
            search={search}
            onSearchChange={setSearch}
            onNewMeet={handleNewMeet}
            readOnly={bridged}
          />
          <ProjectScopeBar
            projects={projects}
            tasks={tasks}
            selectedProjectIds={selectedProjectIds}
            onSelectOnly={selectOnlyProject}
            onToggleProject={toggleProject}
            onSelectAll={selectAllProjects}
          />
          {bridged && (
            <div className="border-b border-border bg-primary/5 px-5 py-1.5 text-[11px] text-muted-foreground">
              {connected
                ? "Connected to current TaskOverlay app state · supported Details fields save through C#"
                : "Read-only · current TaskOverlay app state"}
            </div>
          )}
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
              />
            )}
            {tab === "calendar" && <CalendarPlaceholder />}
            {tab === "workstreams" && <WorkstreamsPlaceholder />}
          </div>
        </main>

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

function CalendarPlaceholder() {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-3 px-6 text-center">
      <div className="flex size-12 items-center justify-center rounded-full bg-accent">
        <CalendarDays className="size-5 text-muted-foreground" />
      </div>
      <div>
        <p className="text-sm font-medium text-foreground">Calendar</p>
        <p className="mt-1 max-w-sm text-pretty text-xs text-muted-foreground">
          Calendar planning is not enabled in the current Workspace. Timeline remains the time-based attention view.
        </p>
      </div>
      <span className="rounded bg-accent px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
        Later
      </span>
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
