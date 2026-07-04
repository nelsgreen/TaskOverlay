"use client"

import { useState } from "react"
import {
  FolderTree,
  ListChecks,
  CalendarClock,
  CalendarDays,
  Layers,
  Plus,
  Search,
  ChevronsUpDown,
  Check,
} from "lucide-react"
import type { Project, TabKey, TreeFilter } from "@/lib/types"
import { cn } from "@/lib/utils"

interface Props {
  projects: Project[]
  selectedProjectIds: string[]
  treeProject: Project
  allSelected: boolean
  multi: boolean
  onSelectOnly: (id: string) => void
  onToggleProject: (id: string) => void
  onSelectAll: () => void
  tab: TabKey
  onTabChange: (t: TabKey) => void
  filter: TreeFilter
  onFilterChange: (f: TreeFilter) => void
  search: string
  onSearchChange: (v: string) => void
  readOnly?: boolean
}

const tabs: { key: TabKey; label: string; icon: typeof FolderTree; later?: boolean }[] = [
  { key: "tree", label: "Tree", icon: FolderTree },
  { key: "status", label: "Status", icon: ListChecks },
  { key: "timeline", label: "Timeline", icon: CalendarClock },
  { key: "calendar", label: "Calendar", icon: CalendarDays, later: true },
  { key: "workstreams", label: "Workstreams", icon: Layers, later: true },
]

const filters: { key: TreeFilter; label: string }[] = [
  { key: "all", label: "All" },
  { key: "active", label: "Active only" },
  { key: "active-path", label: "Active + path" },
]

export function WorkspaceHeader({
  projects,
  selectedProjectIds,
  treeProject,
  allSelected,
  multi,
  onSelectOnly,
  onToggleProject,
  onSelectAll,
  tab,
  onTabChange,
  filter,
  onFilterChange,
  search,
  onSearchChange,
  readOnly,
}: Props) {
  const [open, setOpen] = useState(false)

  const scopeLabel = allSelected
    ? "All projects"
    : multi
      ? `${selectedProjectIds.length} projects selected`
      : treeProject.name

  return (
    <div className="border-b border-border">
      {/* Title row */}
      <div className="flex items-center gap-4 px-5 pb-3 pt-4">
        <div className="flex min-w-0 items-center gap-2.5">
          <img
            src="./taskoverlay-mark-32.png"
            alt=""
            aria-hidden="true"
            className="size-7 shrink-0 object-contain"
          />
          <div className="min-w-0">
            <h1 className="text-lg font-semibold leading-tight text-foreground">Workspace</h1>
            <p className="text-xs text-muted-foreground">Main task organization surface — overlay is only an attention layer</p>
          </div>
        </div>

        {/* Project scope selector */}
        <div className="relative">
          <button
            onClick={() => setOpen((v) => !v)}
            className="flex items-center gap-2 rounded-lg border border-border bg-card px-3 py-1.5 transition-colors hover:bg-accent"
          >
            {!multi && !allSelected && (
              <span className="size-2 rounded-full" style={{ backgroundColor: treeProject.color }} />
            )}
            {(multi || allSelected) && <Layers className="size-3.5 text-muted-foreground" />}
            <span className="text-sm font-medium text-foreground">{scopeLabel}</span>
            <ChevronsUpDown className="size-3.5 text-muted-foreground" />
          </button>

          {open && (
            <>
              <div className="fixed inset-0 z-10" onClick={() => setOpen(false)} aria-hidden />
              <div className="absolute left-0 top-full z-20 mt-1 w-64 rounded-lg border border-border bg-popover p-1 shadow-lg">
                <button
                  onClick={() => {
                    onSelectAll()
                    setOpen(false)
                  }}
                  className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm transition-colors hover:bg-accent"
                >
                  <Layers className="size-4 text-muted-foreground" />
                  <span className="flex-1 font-medium text-foreground">All projects</span>
                  {allSelected && <Check className="size-4 text-primary" />}
                </button>
                <div className="my-1 h-px bg-border" />
                <p className="px-2 py-1 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
                  Click to focus · checkbox to combine
                </p>
                {projects.map((p) => {
                  const checked = selectedProjectIds.includes(p.id)
                  return (
                    <div
                      key={p.id}
                      className="flex items-center gap-2 rounded-md px-2 py-1.5 transition-colors hover:bg-accent"
                    >
                      <button
                        onClick={() => onToggleProject(p.id)}
                        aria-label={`Toggle ${p.name}`}
                        className={cn(
                          "flex size-4 shrink-0 items-center justify-center rounded border transition-colors",
                          checked ? "border-primary bg-primary text-primary-foreground" : "border-input",
                        )}
                      >
                        {checked && <Check className="size-3" />}
                      </button>
                      <button
                        onClick={() => {
                          onSelectOnly(p.id)
                          setOpen(false)
                        }}
                        className="flex flex-1 items-center gap-2 text-left"
                      >
                        <span className="size-2 rounded-full" style={{ backgroundColor: p.color }} />
                        <span className="text-sm font-medium text-foreground">{p.name}</span>
                      </button>
                    </div>
                  )
                })}
              </div>
            </>
          )}
        </div>

        <div className="relative ml-auto w-72">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
          <input
            value={search}
            onChange={(e) => onSearchChange(e.target.value)}
            placeholder="Search or jump to…"
            className="w-full rounded-lg border border-input bg-card py-2 pl-9 pr-3 text-sm text-foreground outline-none transition-colors placeholder:text-muted-foreground focus:border-primary/60 focus:ring-2 focus:ring-primary/20"
          />
        </div>
      </div>

      {/* Tabs */}
      <div className="flex items-center gap-1 px-4">
        {tabs.map((t) => {
          const Icon = t.icon
          const active = tab === t.key
          return (
            <button
              key={t.key}
              onClick={() => onTabChange(t.key)}
              className={cn(
                "flex items-center gap-1.5 border-b-2 px-3 py-2.5 text-sm font-medium transition-colors",
                active
                  ? "border-primary text-foreground"
                  : "border-transparent text-muted-foreground hover:text-foreground",
                t.later && !active && "opacity-60",
              )}
            >
              <Icon className="size-4" />
              {t.label}
              {t.later && (
                <span className="rounded bg-accent px-1 py-0.5 text-[9px] font-semibold uppercase tracking-wide text-muted-foreground">
                  Later
                </span>
              )}
            </button>
          )
        })}
      </div>

      {/* Toolbar (Tree tab only) */}
      {tab === "tree" && (
        <div className="flex items-center gap-3 px-5 py-3">
          <div className="flex items-center rounded-lg border border-border bg-card p-0.5">
            {filters.map((f) => (
              <button
                key={f.key}
                onClick={() => onFilterChange(f.key)}
                className={cn(
                  "rounded-md px-2.5 py-1 text-xs font-medium transition-colors",
                  filter === f.key ? "bg-accent text-foreground" : "text-muted-foreground hover:text-foreground",
                )}
              >
                {f.label}
              </button>
            ))}
          </div>

          <div className="ml-auto flex items-center gap-2">
            {/* Target project for new items (Tree is single-project) */}
            <span className="flex items-center gap-1.5 rounded-lg border border-dashed border-border px-2.5 py-1.5 text-xs text-muted-foreground">
              <span className="size-1.5 rounded-full" style={{ backgroundColor: treeProject.color }} />
              into {treeProject.name}
            </span>
            <button
              disabled={readOnly}
              title={readOnly ? "Workspace is read-only" : "New section"}
              className="flex items-center gap-1.5 rounded-lg border border-border bg-card px-3 py-1.5 text-sm font-medium text-foreground transition-colors hover:bg-accent disabled:cursor-not-allowed disabled:opacity-40"
            >
              <Plus className="size-4" />
              New section
            </button>
            <button
              disabled={readOnly}
              title={readOnly ? "Workspace is read-only" : "New task"}
              className="flex items-center gap-1.5 rounded-lg bg-primary px-3 py-1.5 text-sm font-semibold text-primary-foreground transition-colors hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-40"
            >
              <Plus className="size-4" />
              New task
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
