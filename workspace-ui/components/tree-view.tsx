"use client"

import { useEffect, useRef, useState } from "react"
import { ChevronDown, Folder, Pencil, Plus, Trash2 } from "lucide-react"
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
  selectedSectionId?: string | null
  onSelectTask: (id: string) => void
  onSelectSection?: (id: string) => void
  onToggleSection: (id: string) => void
  onToggleTask: (id: string) => void
  onTogglePin: (id: string) => void
  /** Gates the pin button (pin from Tree is not wired to the bridge yet). */
  readOnly?: boolean
  /** Enables right-click context menus and their actions (connected or mock). */
  canEdit?: boolean
  onCreateTaskHere?: (sectionId?: string) => void
  onCreateSection?: () => void
  onRenameSection?: (sectionId: string) => void
  onDeleteSection?: (sectionId: string) => void
  onAddSubtask?: (taskId: string) => void
  onDeleteTask?: (taskId: string) => void
}

// Badge count: attention items (FOCUS + WAIT), unchanged.
const isAttention = (t: Task) => t.status === "FOCUS" || t.status === "WAIT"
// Active-only filter: all non-DONE tasks (product decision, differs from v0
// which used FOCUS/WAIT only).
const isVisibleActive = (t: Task) => t.status !== "DONE"

type TreeMenu =
  | { kind: "empty"; x: number; y: number }
  | { kind: "section"; x: number; y: number; sectionId: string; isProjectRoot: boolean }
  | { kind: "task"; x: number; y: number; taskId: string }
  | null

export function TreeView({
  sections,
  tasks,
  filter,
  selectedTaskId,
  collapsedSections,
  collapsedTasks,
  selectedSectionId,
  onSelectTask,
  onSelectSection,
  onToggleSection,
  onToggleTask,
  onTogglePin,
  readOnly,
  canEdit,
  onCreateTaskHere,
  onCreateSection,
  onRenameSection,
  onDeleteSection,
  onAddSubtask,
  onDeleteTask,
}: Props) {
  const [menu, setMenu] = useState<TreeMenu>(null)

  // Close the menu on outside click / Escape.
  useEffect(() => {
    if (!menu) return
    const close = () => setMenu(null)
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") setMenu(null) }
    window.addEventListener("mousedown", close)
    window.addEventListener("keydown", onKey)
    return () => {
      window.removeEventListener("mousedown", close)
      window.removeEventListener("keydown", onKey)
    }
  }, [menu])

  const openEmptyMenu = (e: React.MouseEvent) => {
    if (!canEdit) return
    e.preventDefault()
    setMenu({ kind: "empty", x: e.clientX, y: e.clientY })
  }

  const openTaskMenu = (e: React.MouseEvent, taskId: string) => {
    if (!canEdit) return
    e.preventDefault()
    e.stopPropagation()
    setMenu({ kind: "task", x: e.clientX, y: e.clientY, taskId })
  }

  const openSectionMenu = (e: React.MouseEvent, section: Section) => {
    if (!canEdit) return
    e.preventDefault()
    e.stopPropagation()
    onSelectSection?.(section.id)
    setMenu({
      kind: "section",
      x: e.clientX,
      y: e.clientY,
      sectionId: section.id,
      isProjectRoot: !!section.isProjectRoot,
    })
  }

  // Determine which tasks pass the filter (keeping ancestors when needed)
  const visibleIds = new Set<string>()
  if (filter === "all") {
    tasks.forEach((t) => visibleIds.add(t.id))
  } else {
    const byId = new Map(tasks.map((t) => [t.id, t]))
    tasks.forEach((t) => {
      if (isVisibleActive(t)) {
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
            onContextMenu={canEdit ? openTaskMenu : undefined}
            readOnly={readOnly}
          />
          {!collapsed && kids.length > 0 && renderChildren(task.id, sectionId, depth + 1)}
        </div>
      )
    })
  }

  // In the full view sections always render (even empty), so "nothing here" only
  // applies to filtered views with no matching tasks, or a project with no sections.
  const allEmpty = sections.every(
    (s) => tasks.filter((t) => t.sectionId === s.id && visibleIds.has(t.id)).length === 0,
  )
  const nothingRendered = filter === "all" ? sections.length === 0 : allEmpty

  return (
    <div className="flex min-h-full flex-1 flex-col gap-4 px-4 py-3" onContextMenu={openEmptyMenu}>
      {sections.map((section) => {
        const sectionTasks = tasks.filter((t) => t.sectionId === section.id && visibleIds.has(t.id))
        // Empty sections are shown only in the full ("all") view so a freshly
        // created section is visible; filtered views hide sections with no match.
        if (sectionTasks.length === 0 && filter !== "all") return null
        const attentionInSection = sectionTasks.filter(isAttention).length
        const collapsed = collapsedSections.has(section.id)
        // `selectedSectionId` doubles as contextual-parent tracking (set to the
        // selected task's section) and as direct section selection (clicking the
        // section header itself). Only the latter should render as "selected" -
        // a section must stay neutral while it merely contains the selected task,
        // so it never competes with the task row's own selected treatment.
        const taskSelectedInSection = selectedTaskId != null && sectionTasks.some((t) => t.id === selectedTaskId)
        const directlySelected = !taskSelectedInSection && selectedSectionId === section.id

        return (
          <section key={section.id} onContextMenu={(e) => openSectionMenu(e, section)}>
            <button
              onClick={() => {
                onSelectSection?.(section.id)
                onToggleSection(section.id)
              }}
              className={cn(
                "flex w-full items-center gap-2 rounded-md px-1 py-1 text-left transition-colors hover:bg-accent/40",
                // Weak, structurally distinct neutral treatment - never the
                // warm/beige task-selected fill (--row-selected).
                directlySelected && "bg-surface-sunken",
              )}
            >
              <ChevronDown
                className={cn("size-4 text-muted-foreground transition-transform", collapsed && "-rotate-90")}
              />
              <Folder className="size-4 text-muted-foreground" />
              <span className="text-sm font-semibold text-foreground">{section.name}</span>
              <span className="ml-2 flex items-center gap-1.5">
                {attentionInSection > 0 && (
                  <span className="rounded-full bg-primary/15 px-2 py-0.5 text-[10px] font-semibold text-primary">
                    {attentionInSection} active
                  </span>
                )}
                <span className="rounded-full bg-accent px-2 py-0.5 text-[10px] font-semibold text-muted-foreground tabular-nums">
                  {sectionTasks.length}
                </span>
              </span>
            </button>

            {!collapsed && (
              <div className="mt-1 space-y-0.5">
                {sectionTasks.length === 0 ? (
                  <p className="px-2 py-1.5 text-[11px] text-muted-foreground/70">No tasks in this section yet.</p>
                ) : (
                  renderChildren(null, section.id, 0)
                )}
              </div>
            )}
          </section>
        )
      })}

      {nothingRendered && (
        <div className="py-16 text-center text-sm text-muted-foreground">
          Нет задач по выбранному фильтру.
          {canEdit && (
            <span className="mt-1 block text-xs text-muted-foreground/70">
              Правый клик по пустой области, чтобы создать задачу или раздел.
            </span>
          )}
        </div>
      )}

      {menu && (
        <TreeContextMenu
          menu={menu}
          onCreateTask={onCreateTaskHere}
          onCreateSection={onCreateSection}
          onRenameSection={onRenameSection}
          onDeleteSection={onDeleteSection}
          onAddSubtask={onAddSubtask}
          onDeleteTask={onDeleteTask}
          onClose={() => setMenu(null)}
        />
      )}
    </div>
  )
}

function TreeContextMenu({
  menu,
  onCreateTask,
  onCreateSection,
  onRenameSection,
  onDeleteSection,
  onAddSubtask,
  onDeleteTask,
  onClose,
}: {
  menu: NonNullable<TreeMenu>
  onCreateTask?: (sectionId?: string) => void
  onCreateSection?: () => void
  onRenameSection?: (sectionId: string) => void
  onDeleteSection?: (sectionId: string) => void
  onAddSubtask?: (taskId: string) => void
  onDeleteTask?: (taskId: string) => void
  onClose: () => void
}) {
  const ref = useRef<HTMLDivElement>(null)

  // Keep the menu within the viewport.
  const [pos, setPos] = useState({ x: menu.x, y: menu.y })
  useEffect(() => {
    const el = ref.current
    if (!el) return
    const rect = el.getBoundingClientRect()
    const x = Math.min(menu.x, window.innerWidth - rect.width - 8)
    const y = Math.min(menu.y, window.innerHeight - rect.height - 8)
    setPos({ x: Math.max(8, x), y: Math.max(8, y) })
  }, [menu])

  const run = (action?: () => void) => {
    action?.()
    onClose()
  }

  return (
    <div
      ref={ref}
      role="menu"
      // Stop the window mousedown-to-close from firing before the click lands.
      onMouseDown={(e) => e.stopPropagation()}
      style={{ left: pos.x, top: pos.y }}
      className="fixed z-50 min-w-44 overflow-hidden rounded-md border border-border bg-popover py-1 text-popover-foreground shadow-lg"
    >
      {menu.kind === "empty" ? (
        <>
          <MenuItem icon={Plus} label="Create task" onClick={() => run(() => onCreateTask?.())} />
          <MenuItem icon={Folder} label="Create section / folder" onClick={() => run(onCreateSection)} />
        </>
      ) : menu.kind === "section" ? (
        <>
          <MenuItem
            icon={Plus}
            label="Create task in this section"
            onClick={() => run(() => onCreateTask?.(menu.sectionId))}
          />
          {!menu.isProjectRoot && (
            <>
              <MenuItem
                icon={Pencil}
                label="Rename section"
                onClick={() => run(() => onRenameSection?.(menu.sectionId))}
              />
              <MenuItem
                icon={Trash2}
                label="Delete section"
                destructive
                onClick={() => run(() => onDeleteSection?.(menu.sectionId))}
              />
            </>
          )}
        </>
      ) : (
        <>
          <MenuItem icon={Plus} label="Add subtask" onClick={() => run(() => onAddSubtask?.(menu.taskId))} />
          <MenuItem
            icon={Trash2}
            label="Delete task"
            destructive
            onClick={() => run(() => onDeleteTask?.(menu.taskId))}
          />
        </>
      )}
    </div>
  )
}

function MenuItem({
  icon: Icon,
  label,
  onClick,
  destructive,
}: {
  icon: typeof Plus
  label: string
  onClick: () => void
  destructive?: boolean
}) {
  return (
    <button
      type="button"
      role="menuitem"
      onClick={onClick}
      className={cn(
        "flex w-full items-center gap-2 px-3 py-1.5 text-left text-[12px] transition-colors",
        destructive
          ? "text-destructive hover:bg-destructive/10"
          : "text-foreground hover:bg-accent",
      )}
    >
      <Icon className="size-3.5 shrink-0" />
      {label}
    </button>
  )
}
