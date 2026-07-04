"use client"

import { ChevronDown, Folder } from "lucide-react"
import type { Section, Task, TreeFilter } from "@/lib/types"
import { cn } from "@/lib/utils"
import { TaskRow } from "./task-row"

interface Props {
  sections: Section[]
  tasks: Task[]
  filter: TreeFilter
  selectedTaskId: string | null
  collapsedSections: Set<string>
  collapsedTasks: Set<string>
  onSelectTask: (id: string) => void
  onToggleSection: (id: string) => void
  onToggleTask: (id: string) => void
  onTogglePin: (id: string) => void
  readOnly?: boolean
}

const isActive = (t: Task) => t.status === "FOCUS" || t.status === "WAIT"

export function TreeView({
  sections,
  tasks,
  filter,
  selectedTaskId,
  collapsedSections,
  collapsedTasks,
  onSelectTask,
  onToggleSection,
  onToggleTask,
  onTogglePin,
  readOnly,
}: Props) {
  // Determine which tasks pass the filter (keeping ancestors when needed)
  const visibleIds = new Set<string>()
  if (filter === "all") {
    tasks.forEach((t) => visibleIds.add(t.id))
  } else {
    const byId = new Map(tasks.map((t) => [t.id, t]))
    tasks.forEach((t) => {
      if (isActive(t)) {
        visibleIds.add(t.id)
        if (filter === "active-path" && t.parentId) {
          let p = byId.get(t.parentId)
          while (p) {
            visibleIds.add(p.id)
            p = p.parentId ? byId.get(p.parentId) : undefined
          }
        }
      }
    })
  }

  const renderChildren = (parentId: string | null, sectionId: string, depth: number) => {
    const children = tasks.filter(
      (t) => t.sectionId === sectionId && t.parentId === parentId && visibleIds.has(t.id),
    )
    return children.map((task) => {
      const kids = tasks.filter((t) => t.parentId === task.id && visibleIds.has(t.id))
      const collapsed = collapsedTasks.has(task.id)
      return (
        <div key={task.id}>
          <TaskRow
            task={task}
            depth={depth}
            hasChildren={kids.length > 0}
            collapsed={collapsed}
            selected={task.id === selectedTaskId}
            onSelect={onSelectTask}
            onToggleCollapse={onToggleTask}
            onTogglePin={onTogglePin}
            readOnly={readOnly}
          />
          {!collapsed && kids.length > 0 && renderChildren(task.id, sectionId, depth + 1)}
        </div>
      )
    })
  }

  return (
    <div className="flex flex-col gap-4 px-4 py-3">
      {sections.map((section) => {
        const sectionTasks = tasks.filter((t) => t.sectionId === section.id && visibleIds.has(t.id))
        if (sectionTasks.length === 0) return null
        const activeInSection = sectionTasks.filter(isActive).length
        const collapsed = collapsedSections.has(section.id)

        return (
          <section key={section.id}>
            <button
              onClick={() => onToggleSection(section.id)}
              className="flex w-full items-center gap-2 rounded-md px-1 py-1 text-left transition-colors hover:bg-accent/40"
            >
              <ChevronDown
                className={cn("size-4 text-muted-foreground transition-transform", collapsed && "-rotate-90")}
              />
              <Folder className="size-4 text-muted-foreground" />
              <span className="text-sm font-semibold text-foreground">{section.name}</span>
              <span className="ml-2 flex items-center gap-1.5">
                {activeInSection > 0 && (
                  <span className="rounded-full bg-primary/15 px-2 py-0.5 text-[10px] font-semibold text-primary">
                    {activeInSection} active
                  </span>
                )}
                <span className="rounded-full bg-accent px-2 py-0.5 text-[10px] font-semibold text-muted-foreground tabular-nums">
                  {sectionTasks.length}
                </span>
              </span>
            </button>

            {!collapsed && <div className="mt-1 space-y-0.5">{renderChildren(null, section.id, 0)}</div>}
          </section>
        )
      })}

      {sections.every((s) => tasks.filter((t) => t.sectionId === s.id && visibleIds.has(t.id)).length === 0) && (
        <div className="py-16 text-center text-sm text-muted-foreground">Нет задач по выбранному фильтру.</div>
      )}
    </div>
  )
}
