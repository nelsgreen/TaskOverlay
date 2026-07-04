"use client"

import { useMemo, useState } from "react"
import { FolderTree } from "lucide-react"
import type { MeetItem, TabKey, Task, TreeFilter } from "@/lib/types"
import { initialMeetItems, initialTasks, projects, sections } from "@/lib/mock-data"
import { cn } from "@/lib/utils"
import { ProjectSidebar } from "./project-sidebar"
import { WorkspaceHeader } from "./workspace-header"
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
  const [tasks, setTasks] = useState<Task[]>(initialTasks)
  const [meetItems, setMeetItems] = useState<MeetItem[]>(initialMeetItems)
  const [selectedProjectIds, setSelectedProjectIds] = useState<string[]>(["kazchess"])
  const [selection, setSelection] = useState<WorkspaceSelection>({ kind: "task", id: "t-pr-1" })
  const [tab, setTab] = useState<TabKey>("tree")
  const [filter, setFilter] = useState<TreeFilter>("all")
  const [search, setSearch] = useState("")
  const [collapsedSections, setCollapsedSections] = useState<Set<string>>(new Set())
  const [collapsedTasks, setCollapsedTasks] = useState<Set<string>>(new Set())

  const allSelected = selectedProjectIds.length === projects.length
  const multi = selectedProjectIds.length > 1
  const selectedTaskId = selection?.kind === "task" ? selection.id : null
  const selectedMeetId = selection?.kind === "meet" ? selection.id : null
  const selectTask = (id: string) => setSelection({ kind: "task", id })
  const selectMeet = (id: string) => setSelection({ kind: "meet", id })

  // The single project used for the Tree tab (Tree is single-project by design)
  const treeProjectId = selectedProjectIds[0] ?? projects[0].id
  const treeProject = projects.find((p) => p.id === treeProjectId) ?? projects[0]

  const projectSections = useMemo(
    () => sections.filter((s) => s.projectId === treeProjectId),
    [treeProjectId],
  )

  // --- Project selection handlers ---
  const selectOnlyProject = (id: string) => setSelectedProjectIds([id])
  const toggleProject = (id: string) =>
    setSelectedProjectIds((prev) =>
      prev.includes(id) ? (prev.length > 1 ? prev.filter((x) => x !== id) : prev) : [...prev, id],
    )
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
    setTasks((prev) => prev.map((t) => (t.id === id ? { ...t, pinned: !t.pinned } : t)))

  const handleApply = (updated: Task) => {
    setTasks((prev) => prev.map((t) => (t.id === updated.id ? updated : t)))
    if (!selectedProjectIds.includes(updated.projectId)) setSelectedProjectIds([updated.projectId])
  }

  const handleDelete = (id: string) => {
    setTasks((prev) => prev.filter((t) => t.id !== id && t.parentId !== id))
    if (selection?.kind === "task" && selection.id === id) setSelection(null)
  }

  const handleApplyMeet = (updated: MeetItem) => {
    setMeetItems((prev) => prev.map((m) => (m.id === updated.id ? updated : m)))
  }

  const handleDeleteMeet = (id: string) => {
    setMeetItems((prev) => prev.filter((m) => m.id !== id))
    if (selection?.kind === "meet" && selection.id === id) setSelection(null)
  }

  const handleNewMeet = () => {
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
    setMeetItems((prev) => [...prev, newMeet])
    selectMeet(newMeet.id)
  }

  // The currently selected MeetItem (may come from timeline items or newly created)
  const selectedMeet = meetItems.find((m) => m.id === selectedMeetId) ?? null

  return (
    <div className="flex h-screen flex-col overflow-hidden bg-background text-foreground">
      <div className="flex min-h-0 flex-1">
        <ProjectSidebar
          projects={projects}
          tasks={tasks}
          selectedProjectIds={selectedProjectIds}
          onSelectOnly={selectOnlyProject}
          onToggleProject={toggleProject}
        />

        <main className="flex min-w-0 flex-1 flex-col">
          <WorkspaceHeader
            projects={projects}
            selectedProjectIds={selectedProjectIds}
            treeProject={treeProject}
            allSelected={allSelected}
            multi={multi}
            onSelectOnly={selectOnlyProject}
            onToggleProject={toggleProject}
            onSelectAll={selectAllProjects}
            tab={tab}
            onTabChange={setTab}
            filter={filter}
            onFilterChange={setFilter}
            search={search}
            onSearchChange={setSearch}
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
              />
            )}
            {tab === "timeline" && (
              <TimelineView
                projectIds={selectedProjectIds}
                selectedMeetId={selectedMeetId}
                selectedTaskId={selectedTaskId}
                onSelectMeet={selectMeet}
                onSelectTask={selectTask}
                onNewMeet={handleNewMeet}
              />
            )}
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
          />
        )}
      </div>

      <ActiveNowStrip
        tasks={tasks}
        projects={projects}
        sections={sections}
        selectedTaskId={selectedTaskId}
        onSelectTask={selectTask}
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
