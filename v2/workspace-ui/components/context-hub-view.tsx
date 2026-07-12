"use client"

import { useEffect, useMemo, useRef, useState } from "react"
import {
  AlertTriangle,
  Ban,
  ClipboardList,
  FileText,
  Flag,
  HelpCircle,
  Info,
  Layers,
  Link2,
  ListTodo,
  Lock,
  Save,
  StickyNote,
  Trash2,
  UndoDot,
  Video,
  X,
} from "lucide-react"
import type {
  ContextItemStatus,
  ContextItemType,
  ContextSourceApp,
  ContextSourceType,
  MeetItem,
  Project,
  Section,
  Task,
  WorkspaceContextHubCommand,
  WorkspaceContextItemSnapshot,
  WorkspaceContextSourceSnapshot,
} from "@/lib/types"
import { cn } from "@/lib/utils"
import { LinkedTasksField } from "./linked-task-picker"

// ─── Selection shared with task-manager ─────────────────────────────────────

export type ContextHubSelection =
  | { kind: "item"; id: string }
  | { kind: "source"; id: string }
  | null

export type ContextHubModal = "source" | "context" | null

// ─── Meta (v0 badge language mapped onto real status tokens) ────────────────

export const contextTypeMeta: Record<ContextItemType, {
  label: string
  icon: typeof Flag
  dot: string
  text: string
  bg: string
  border: string
}> = {
  decision:     { label: "Decision",     icon: Flag,          dot: "bg-status-focus",    text: "text-status-focus",    bg: "bg-status-focus/10",    border: "border-status-focus/40" },
  requirement:  { label: "Requirement",  icon: ClipboardList, dot: "bg-primary",         text: "text-primary",         bg: "bg-primary/10",         border: "border-primary/40" },
  constraint:   { label: "Constraint",   icon: Lock,          dot: "bg-status-panel",    text: "text-status-panel",    bg: "bg-status-panel/10",    border: "border-status-panel/40" },
  blocker:      { label: "Blocker",      icon: Ban,           dot: "bg-destructive",     text: "text-destructive",     bg: "bg-destructive/10",     border: "border-destructive/40" },
  openQuestion: { label: "Open question", icon: HelpCircle,   dot: "bg-status-wait",     text: "text-status-wait",     bg: "bg-status-wait/10",     border: "border-status-wait/40" },
  actionItem:   { label: "Action item",  icon: ListTodo,      dot: "bg-status-meet",     text: "text-status-meet",     bg: "bg-status-meet/10",     border: "border-status-meet/40" },
  projectFact:  { label: "Fact",         icon: Info,          dot: "bg-status-todo",     text: "text-status-todo",     bg: "bg-status-todo/10",     border: "border-status-todo/40" },
  risk:         { label: "Risk",         icon: AlertTriangle, dot: "bg-status-remind",   text: "text-status-remind",   bg: "bg-status-remind/10",   border: "border-status-remind/40" },
  note:         { label: "Note",         icon: StickyNote,    dot: "bg-muted-foreground", text: "text-muted-foreground", bg: "bg-muted/60",          border: "border-border" },
}

export const contextStatusMeta: Record<ContextItemStatus, { label: string; text: string; bg: string; border: string }> = {
  active:     { label: "active",     text: "text-status-focus",  bg: "bg-status-focus/10",  border: "border-status-focus/40" },
  resolved:   { label: "resolved",   text: "text-status-done",   bg: "bg-status-done/10",   border: "border-status-done/40" },
  deprecated: { label: "deprecated", text: "text-muted-foreground", bg: "bg-muted/50",      border: "border-border" },
  superseded: { label: "superseded", text: "text-status-remind", bg: "bg-status-remind/10", border: "border-status-remind/40" },
}

export const sourceTypeMeta: Record<ContextSourceType, { label: string; short: string }> = {
  meetingSummary:    { label: "Meeting summary",    short: "Meeting" },
  meetingTranscript: { label: "Meeting transcript", short: "Transcript" },
  chatSummary:       { label: "Chat summary",       short: "Chat" },
  manualNote:        { label: "Manual note",        short: "Note" },
  clientRequest:     { label: "Client request",     short: "Client" },
  documentSummary:   { label: "Document summary",   short: "Document" },
  statusUpdate:      { label: "Status update",      short: "Status" },
  telegramCapture:   { label: "Telegram capture",   short: "Telegram" },
  other:             { label: "Other",              short: "Other" },
}

export const sourceAppMeta: Record<ContextSourceApp, string> = {
  chatgpt: "ChatGPT",
  claude: "Claude",
  codex: "Codex",
  telegram: "Telegram",
  manual: "Manual",
  other: "Other",
}

const CONTEXT_TYPES = Object.keys(contextTypeMeta) as ContextItemType[]
const CONTEXT_STATUSES = Object.keys(contextStatusMeta) as ContextItemStatus[]
const SOURCE_TYPES = Object.keys(sourceTypeMeta) as ContextSourceType[]
const SOURCE_APPS = Object.keys(sourceAppMeta) as ContextSourceApp[]

type ContextFilter =
  | "all"
  | "decision"
  | "requirement"
  | "blocker"
  | "openQuestion"
  | "risk"
  | "note"
  | "sources"

const CONTEXT_FILTERS: { id: ContextFilter; label: string }[] = [
  { id: "all", label: "All" },
  { id: "decision", label: "Decisions" },
  { id: "requirement", label: "Requirements" },
  { id: "blocker", label: "Blockers" },
  { id: "openQuestion", label: "Open Questions" },
  { id: "risk", label: "Risks" },
  { id: "note", label: "Notes" },
  { id: "sources", label: "Sources" },
]

function formatDate(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ""
  return d.toLocaleDateString("en-GB", { day: "numeric", month: "short" })
}

// ─── Badges ──────────────────────────────────────────────────────────────────

function TypeBadge({ type }: { type: ContextItemType }) {
  const meta = contextTypeMeta[type]
  return (
    <span className={cn(
      "inline-flex shrink-0 items-center gap-1.5 rounded-md border px-1.5 py-0.5 text-[10px] font-semibold tracking-wide",
      meta.bg, meta.border, meta.text,
    )}>
      <span className={cn("size-1.5 rounded-full", meta.dot)} aria-hidden />
      {meta.label}
    </span>
  )
}

function StatusChip({ status }: { status: ContextItemStatus }) {
  const meta = contextStatusMeta[status]
  return (
    <span className={cn(
      "inline-flex shrink-0 items-center rounded border px-1.5 py-0.5 font-mono text-[10px] font-medium tracking-wide",
      meta.bg, meta.border, meta.text,
      status === "deprecated" && "line-through",
    )}>
      {meta.label}
    </span>
  )
}

function SourceChip({ type }: { type: ContextSourceType }) {
  return (
    <span className="inline-flex shrink-0 items-center gap-1 rounded border border-border bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
      <FileText className="size-2.5" aria-hidden />
      {sourceTypeMeta[type].short}
    </span>
  )
}

function LinkCount({ kind, count }: { kind: "task" | "meet"; count: number }) {
  if (!count) return null
  const Icon = kind === "task" ? Link2 : Video
  return (
    <span className="inline-flex items-center gap-1 text-[10px] tabular-nums text-muted-foreground">
      <Icon className="size-3" aria-hidden />
      {count} {kind === "task" ? "task" : "MEET"}{count > 1 ? "s" : ""}
    </span>
  )
}

function FieldLabel({ children }: { children: React.ReactNode }) {
  return (
    <span className="text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
      {children}
    </span>
  )
}

const inputClass = "rounded-md border border-input bg-background px-2.5 py-1.5 text-[13px] text-foreground outline-none placeholder:text-muted-foreground/60 focus:border-primary/60 focus:ring-1 focus:ring-primary/40"

// ─── Main tab view (left filters + center list; details render in the shared
//     right panel slot, see ContextHubDetailsPanel below) ────────────────────

interface ContextHubViewProps {
  projects: Project[]
  tasks: Task[]
  meetItems: MeetItem[]
  contextSources: WorkspaceContextSourceSnapshot[]
  contextItems: WorkspaceContextItemSnapshot[]
  selectedProjectIds: string[]
  search: string
  selection: ContextHubSelection
  onSelect: (selection: ContextHubSelection) => void
  modal: ContextHubModal
  onModalChange: (modal: ContextHubModal) => void
  onCommand: (command: WorkspaceContextHubCommand) => boolean
  readOnly: boolean
}

export function ContextHubView({
  projects,
  tasks,
  meetItems,
  contextSources,
  contextItems,
  selectedProjectIds,
  search,
  selection,
  onSelect,
  modal,
  onModalChange,
  onCommand,
  readOnly,
}: ContextHubViewProps) {
  const [filter, setFilter] = useState<ContextFilter>("all")
  const [sourceTypeFilters, setSourceTypeFilters] = useState<Set<ContextSourceType>>(new Set())

  const query = search.trim().toLowerCase()
  const matchesQuery = (title: string, body: string, summary = "") =>
    !query ||
    title.toLowerCase().includes(query) ||
    body.toLowerCase().includes(query) ||
    summary.toLowerCase().includes(query)

  const scopedItems = useMemo(
    () => contextItems.filter((item) =>
      selectedProjectIds.includes(item.projectId) &&
      matchesQuery(item.title, item.body)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [contextItems, selectedProjectIds, query],
  )
  const scopedSources = useMemo(
    () => contextSources.filter((source) =>
      selectedProjectIds.includes(source.projectId) &&
      (sourceTypeFilters.size === 0 || sourceTypeFilters.has(source.sourceType)) &&
      matchesQuery(source.title, source.body, source.summary)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [contextSources, selectedProjectIds, sourceTypeFilters, query],
  )

  const counts = useMemo(() => ({
    decisions: scopedItems.filter((i) => i.itemType === "decision").length,
    blockers: scopedItems.filter((i) => i.itemType === "blocker").length,
    questions: scopedItems.filter((i) => i.itemType === "openQuestion").length,
    sources: scopedSources.length,
  }), [scopedItems, scopedSources])

  const projectName = (id: string) => projects.find((p) => p.id === id)?.name ?? ""

  const toggleSourceType = (type: ContextSourceType) =>
    setSourceTypeFilters((prev) => {
      const next = new Set(prev)
      if (next.has(type)) next.delete(type)
      else next.add(type)
      return next
    })

  const itemRow = (item: WorkspaceContextItemSnapshot) => (
    <ContextRow
      key={item.id}
      item={item}
      sourceType={item.sourceDocumentIds.length > 0
        ? contextSources.find((s) => s.id === item.sourceDocumentIds[0])?.sourceType ?? null
        : null}
      projectName={projectName(item.projectId)}
      selected={selection?.kind === "item" && selection.id === item.id}
      onSelect={() => onSelect({ kind: "item", id: item.id })}
    />
  )
  const sourceRow = (source: WorkspaceContextSourceSnapshot) => (
    <SourceRow
      key={source.id}
      source={source}
      derivedCount={contextItems.filter((i) => i.sourceDocumentIds.includes(source.id)).length}
      projectName={projectName(source.projectId)}
      selected={selection?.kind === "source" && selection.id === source.id}
      onSelect={() => onSelect({ kind: "source", id: source.id })}
    />
  )

  const group = (label: string, rows: React.ReactNode[]) =>
    rows.length === 0 ? null : (
      <div key={label}>
        <div className="sticky top-0 z-10 flex items-center gap-2 border-b border-border bg-background/95 px-3 py-1.5 backdrop-blur">
          <span className="text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">{label}</span>
          <span className="rounded bg-muted px-1 font-mono text-[10px] tabular-nums text-muted-foreground">{rows.length}</span>
        </div>
        {rows}
      </div>
    )

  const byType = (type: ContextItemType) => scopedItems.filter((i) => i.itemType === type)
  const otherTypes: ContextItemType[] = ["requirement", "constraint", "actionItem", "projectFact"]
  const totalEmpty = scopedItems.length === 0 && scopedSources.length === 0

  let body: React.ReactNode
  if (totalEmpty) {
    body = (
      <div className="flex flex-col items-center gap-3 px-6 py-16 text-center">
        <Layers className="size-8 text-muted-foreground/40" />
        <p className="text-sm font-medium text-foreground">
          {query ? "No project memory matches this search." : "No project memory yet."}
        </p>
        {!query && (
          <p className="max-w-sm text-xs text-muted-foreground">
            Add a decision, blocker, or question — or paste a meeting/chat summary as a source.
          </p>
        )}
        {!query && !readOnly && (
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => onModalChange("context")}
              className="rounded-md bg-primary px-3 py-1.5 text-[12px] font-semibold text-primary-foreground transition-colors hover:bg-primary/90"
            >
              Add context
            </button>
            <button
              type="button"
              onClick={() => onModalChange("source")}
              className="rounded-md border border-border bg-card px-3 py-1.5 text-[12px] font-medium text-foreground transition-colors hover:bg-accent"
            >
              Add source
            </button>
          </div>
        )}
      </div>
    )
  } else if (filter === "sources") {
    body = group("Recent sources", scopedSources.map(sourceRow)) ?? (
      <EmptyFilterNote label="sources" />
    )
  } else if (filter === "all") {
    body = (
      <>
        {group("Active decisions", byType("decision").map(itemRow))}
        {group("Blockers", byType("blocker").map(itemRow))}
        {group("Open questions", byType("openQuestion").map(itemRow))}
        {group("Risks", byType("risk").map(itemRow))}
        {group("Notes", byType("note").map(itemRow))}
        {group("Other context", otherTypes.flatMap((t) => byType(t)).map(itemRow))}
        {group("Recent sources", scopedSources.map(sourceRow))}
      </>
    )
  } else {
    const rows = byType(filter).map(itemRow)
    body = rows.length > 0
      ? group(CONTEXT_FILTERS.find((f) => f.id === filter)?.label ?? "", rows)
      : <EmptyFilterNote label={CONTEXT_FILTERS.find((f) => f.id === filter)?.label.toLowerCase() ?? "items"} />
  }

  return (
    <div className="flex h-full min-h-0">
      {/* Left: filters */}
      <aside className="flex w-48 shrink-0 flex-col gap-4 overflow-y-auto border-r border-border bg-card/20 p-3">
        <div className="flex flex-col gap-0.5">
          <FieldLabel>Filters</FieldLabel>
          <div className="mt-1 flex flex-col gap-0.5">
            {CONTEXT_FILTERS.map((f) => (
              <button
                key={f.id}
                type="button"
                onClick={() => setFilter(f.id)}
                className={cn(
                  "rounded-md px-2 py-1.5 text-left text-[12px] transition-colors",
                  filter === f.id
                    ? "bg-accent text-foreground ring-1 ring-inset ring-border"
                    : "text-muted-foreground hover:bg-accent/50 hover:text-foreground",
                )}
              >
                {f.label}
              </button>
            ))}
          </div>
        </div>

        <div className="flex flex-col gap-0.5">
          <FieldLabel>Source types</FieldLabel>
          <div className="mt-1 flex flex-col gap-0.5">
            {SOURCE_TYPES.map((t) => {
              const on = sourceTypeFilters.has(t)
              return (
                <button
                  key={t}
                  type="button"
                  onClick={() => toggleSourceType(t)}
                  className={cn(
                    "flex items-center gap-2 rounded-md px-2 py-1 text-left text-[12px] transition-colors",
                    on
                      ? "bg-primary/10 text-primary ring-1 ring-inset ring-primary/30"
                      : "text-muted-foreground hover:bg-accent/50 hover:text-foreground",
                  )}
                >
                  <span className={cn(
                    "flex size-3.5 shrink-0 items-center justify-center rounded border",
                    on ? "border-primary bg-primary/20" : "border-border",
                  )}>
                    {on && <span className="size-1.5 rounded-[1px] bg-primary" />}
                  </span>
                  {sourceTypeMeta[t].label}
                </button>
              )
            })}
          </div>
        </div>
      </aside>

      {/* Center: overview + grouped rows */}
      <section className="flex min-w-0 flex-1 flex-col">
        <div className="grid shrink-0 grid-cols-4 gap-2 border-b border-border p-3">
          <OverviewCard icon={Flag} label="Decisions" value={counts.decisions} tint="bg-status-focus/15 text-status-focus" />
          <OverviewCard icon={Ban} label="Blockers" value={counts.blockers} tint="bg-destructive/15 text-destructive" />
          <OverviewCard icon={HelpCircle} label="Questions" value={counts.questions} tint="bg-status-wait/15 text-status-wait" />
          <OverviewCard icon={FileText} label="Sources" value={counts.sources} tint="bg-primary/15 text-primary" />
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto">
          {body}
        </div>
      </section>

      {modal && (
        <AddFlowModal
          mode={modal}
          projects={projects}
          tasks={tasks}
          meetItems={meetItems}
          contextSources={contextSources}
          selectedProjectIds={selectedProjectIds}
          onSubmit={(command) => {
            if (onCommand(command)) onModalChange(null)
          }}
          onClose={() => onModalChange(null)}
        />
      )}
    </div>
  )
}

function EmptyFilterNote({ label }: { label: string }) {
  return (
    <div className="px-3 py-10 text-center text-[13px] text-muted-foreground">
      No {label} yet in this scope.
    </div>
  )
}

function OverviewCard({
  icon: Icon,
  label,
  value,
  tint,
}: {
  icon: typeof Flag
  label: string
  value: number
  tint: string
}) {
  return (
    <div className="flex items-center gap-2.5 rounded-lg border border-border bg-card/60 px-3 py-2">
      <span className={cn("flex size-7 shrink-0 items-center justify-center rounded-md", tint)}>
        <Icon className="size-4" />
      </span>
      <div className="flex min-w-0 flex-col">
        <span className="font-mono text-base font-semibold leading-tight tabular-nums text-foreground">{value}</span>
        <span className="truncate text-[10px] uppercase tracking-wide text-muted-foreground">{label}</span>
      </div>
    </div>
  )
}

function ContextRow({
  item,
  sourceType,
  projectName,
  selected,
  onSelect,
}: {
  item: WorkspaceContextItemSnapshot
  sourceType: ContextSourceType | null
  projectName: string
  selected: boolean
  onSelect: () => void
}) {
  return (
    <div
      role="button"
      tabIndex={0}
      onClick={onSelect}
      onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onSelect() } }}
      className={cn(
        "flex cursor-pointer flex-col gap-1 border-b border-border/60 px-3 py-2 transition-colors",
        selected ? "bg-row-selected" : "hover:bg-accent/40",
      )}
    >
      <div className="flex items-center gap-2">
        <TypeBadge type={item.itemType} />
        <span className="min-w-0 flex-1 truncate text-[13px] font-medium text-foreground/90">{item.title}</span>
        <StatusChip status={item.status} />
      </div>
      {item.body && (
        <p className="line-clamp-1 pl-0.5 text-[11px] leading-snug text-muted-foreground">{item.body}</p>
      )}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        {sourceType && <SourceChip type={sourceType} />}
        <span className="text-[10px] text-muted-foreground/70">{projectName}</span>
        <LinkCount kind="task" count={item.linkedTaskIds.length} />
        <LinkCount kind="meet" count={item.linkedMeetingIds.length} />
      </div>
    </div>
  )
}

function SourceRow({
  source,
  derivedCount,
  projectName,
  selected,
  onSelect,
}: {
  source: WorkspaceContextSourceSnapshot
  derivedCount: number
  projectName: string
  selected: boolean
  onSelect: () => void
}) {
  return (
    <div
      role="button"
      tabIndex={0}
      onClick={onSelect}
      onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onSelect() } }}
      className={cn(
        "flex cursor-pointer flex-col gap-1 border-b border-border/60 px-3 py-2 transition-colors",
        selected ? "bg-row-selected" : "hover:bg-accent/40",
      )}
    >
      <div className="flex items-center gap-2">
        <SourceChip type={source.sourceType} />
        <span className="min-w-0 flex-1 truncate text-[13px] font-medium text-foreground/90">{source.title}</span>
        <span className="shrink-0 text-[10px] text-muted-foreground">{formatDate(source.sourceDateUtc)}</span>
      </div>
      {(source.summary || source.body) && (
        <p className="line-clamp-1 pl-0.5 text-[11px] leading-snug text-muted-foreground">
          {source.summary || source.body}
        </p>
      )}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <span className="text-[10px] text-muted-foreground/70">{projectName}</span>
        {derivedCount > 0 && (
          <span className="inline-flex items-center gap-1 text-[10px] text-muted-foreground">
            <Layers className="size-3" aria-hidden />
            {derivedCount} context
          </span>
        )}
        <LinkCount kind="task" count={source.linkedTaskIds.length} />
        <LinkCount kind="meet" count={source.linkedMeetingIds.length} />
      </div>
    </div>
  )
}

// ─── Right details panel (rendered in the shared Details slot) ──────────────

interface ContextHubDetailsPanelProps {
  selection: ContextHubSelection
  item: WorkspaceContextItemSnapshot | null
  source: WorkspaceContextSourceSnapshot | null
  projects: Project[]
  sections: Section[]
  tasks: Task[]
  meetItems: MeetItem[]
  contextSources: WorkspaceContextSourceSnapshot[]
  contextItems: WorkspaceContextItemSnapshot[]
  onCommand: (command: WorkspaceContextHubCommand) => boolean
  onOpenSelection: (selection: ContextHubSelection) => void
  readOnly: boolean
}

export function ContextHubDetailsPanel(props: ContextHubDetailsPanelProps) {
  const { selection, item, source } = props
  return (
    <aside className="flex h-full w-full min-w-0 flex-col overflow-x-hidden border-l border-border bg-sidebar">
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <h2 className="text-sm font-semibold text-foreground">Details</h2>
        <span className="rounded-md bg-accent px-2 py-0.5 font-mono text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
          {item ? "context" : source ? "source" : "memory"}
        </span>
      </div>
      {item ? (
        // key remounts the editor per record — the v0 stale-defaultValue bug
        // cannot happen with a keyed, controlled draft.
        <ContextItemEditor key={item.id} {...props} item={item} />
      ) : source ? (
        <SourceEditor key={source.id} {...props} source={source} />
      ) : (
        <div className="flex flex-1 flex-col items-center justify-center gap-2 px-6 text-center">
          <Layers className="size-6 text-muted-foreground/50" />
          <p className="text-sm font-medium text-foreground">Nothing selected</p>
          <p className="text-xs text-muted-foreground">
            {selection ? "This record is no longer available." : "Select a context item or source to view and edit it."}
          </p>
        </div>
      )}
    </aside>
  )
}

/** Immediate link mutations shared by both editors. Lists render from the
 * snapshot record (never from the draft), matching the checkpoints rule. */
function LinkEditor({
  label,
  icon: Icon,
  linkedIds,
  options,
  emptyText,
  disabled,
  onLink,
  onUnlink,
}: {
  label: string
  icon: typeof Link2
  linkedIds: string[]
  options: { id: string; label: string }[]
  emptyText: string
  disabled: boolean
  onLink: (id: string) => void
  onUnlink: (id: string) => void
}) {
  const available = options.filter((option) => !linkedIds.includes(option.id))
  const labelOf = (id: string) => options.find((option) => option.id === id)?.label ?? "(missing)"
  return (
    <div className="flex min-w-0 flex-col gap-1.5">
      <FieldLabel>{label}</FieldLabel>
      {linkedIds.length === 0 ? (
        <p className="text-[11px] text-muted-foreground">{emptyText}</p>
      ) : (
        <div className="flex min-w-0 flex-col gap-1">
          {linkedIds.map((id) => (
            <div key={id} className="flex min-w-0 items-center gap-2 rounded-md border border-border bg-card/60 px-2 py-1.5">
              <Icon className="size-3.5 shrink-0 text-muted-foreground" />
              <span className="min-w-0 flex-1 truncate text-[12px] text-foreground/90">{labelOf(id)}</span>
              {!disabled && (
                <button
                  type="button"
                  onClick={() => onUnlink(id)}
                  aria-label={`Unlink ${labelOf(id)}`}
                  className="shrink-0 rounded p-0.5 text-muted-foreground transition-colors hover:text-destructive"
                >
                  <X className="size-3" />
                </button>
              )}
            </div>
          ))}
        </div>
      )}
      {!disabled && available.length > 0 && (
        <select
          value=""
          onChange={(e) => { if (e.target.value) onLink(e.target.value) }}
          className={cn(inputClass, "py-1 text-[12px]")}
        >
          <option value="">+ Link…</option>
          {available.map((option) => (
            <option key={option.id} value={option.id}>{option.label}</option>
          ))}
        </select>
      )}
    </div>
  )
}

function ContextItemEditor({
  item,
  projects,
  sections,
  tasks,
  meetItems,
  contextSources,
  onCommand,
  onOpenSelection,
  readOnly,
}: ContextHubDetailsPanelProps & { item: WorkspaceContextItemSnapshot }) {
  const [title, setTitle] = useState(item.title)
  const [body, setBody] = useState(item.body)
  const [itemType, setItemType] = useState<ContextItemType>(item.itemType)
  const [status, setStatus] = useState<ContextItemStatus>(item.status)
  // Reconcile non-focused fields from fresh snapshots of the same record.
  const baseRef = useRef(item)
  useEffect(() => {
    if (baseRef.current.updatedAtUtc === item.updatedAtUtc) return
    baseRef.current = item
    setTitle(item.title)
    setBody(item.body)
    setItemType(item.itemType)
    setStatus(item.status)
  }, [item])

  const dirty = title !== item.title || body !== item.body ||
    itemType !== item.itemType || status !== item.status
  const canSave = dirty && title.trim().length > 0 && !readOnly

  const save = () => {
    if (!canSave) return
    onCommand({
      type: "updateContextItem",
      itemId: item.id,
      title: title.trim(),
      body,
      itemType,
      status,
    })
  }

  const revert = () => {
    setTitle(item.title)
    setBody(item.body)
    setItemType(item.itemType)
    setStatus(item.status)
  }

  const remove = () => {
    if (window.confirm(`Delete context item "${item.title}"? Linked tasks and meetings are not affected.`)) {
      onCommand({ type: "deleteContextItem", itemId: item.id })
    }
  }

  const projectLabel = projects.find((p) => p.id === item.projectId)?.name ?? ""
  const sourceOptions = contextSources
    .filter((s) => s.projectId === item.projectId || item.sourceDocumentIds.includes(s.id))
    .map((s) => ({ id: s.id, label: s.title }))

  return (
    <div className="flex min-h-0 min-w-0 flex-1 flex-col">
      <fieldset disabled={readOnly} className="flex min-h-0 min-w-0 flex-1 flex-col gap-4 overflow-x-hidden overflow-y-auto p-4 disabled:opacity-70">
        <div className="flex min-w-0 flex-col gap-1.5">
          <FieldLabel>Type</FieldLabel>
          <div className="flex min-w-0 max-w-full flex-wrap gap-1">
            {CONTEXT_TYPES.map((t) => {
              const on = t === itemType
              const meta = contextTypeMeta[t]
              return (
                <button
                  key={t}
                  type="button"
                  onClick={() => setItemType(t)}
                  className={cn(
                    "inline-flex max-w-full items-center gap-1 rounded-md border px-1.5 py-0.5 text-left text-[10px] font-semibold leading-tight transition-colors whitespace-normal",
                    on ? cn(meta.bg, meta.border, meta.text) : "border-border text-muted-foreground hover:text-foreground",
                  )}
                >
                  <span className={cn("size-1.5 rounded-full", on ? meta.dot : "bg-muted-foreground/40")} />
                  <span className="min-w-0 break-words">{meta.label}</span>
                </button>
              )
            })}
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <FieldLabel>Title</FieldLabel>
          <input value={title} onChange={(e) => setTitle(e.target.value)} className={inputClass} />
        </div>

        <div className="flex flex-col gap-1.5">
          <FieldLabel>Body</FieldLabel>
          <textarea
            value={body}
            onChange={(e) => setBody(e.target.value)}
            rows={4}
            className={cn(inputClass, "resize-none leading-relaxed")}
          />
        </div>

        <div className="flex min-w-0 flex-col gap-1.5">
          <FieldLabel>Status</FieldLabel>
          <div className="flex min-w-0 max-w-full flex-wrap gap-1.5">
            {CONTEXT_STATUSES.map((s) => {
              const on = s === status
              const meta = contextStatusMeta[s]
              return (
                <button
                  key={s}
                  type="button"
                  onClick={() => setStatus(s)}
                  className={cn(
                    "max-w-full rounded-md border px-2 py-1 text-left font-mono text-[11px] font-medium leading-tight transition-colors whitespace-normal",
                    on ? cn(meta.bg, meta.border, meta.text) : "border-border text-muted-foreground hover:text-foreground",
                  )}
                >
                  <span className="min-w-0 break-words">{meta.label}</span>
                </button>
              )
            })}
          </div>
        </div>

        <div className="flex items-center gap-2 text-[11px] text-muted-foreground">
          <span>{projectLabel}</span>
          {item.resolvedAtUtc && <span>· resolved {formatDate(item.resolvedAtUtc)}</span>}
        </div>

        <LinkEditor
          label="Derived from sources"
          icon={FileText}
          linkedIds={item.sourceDocumentIds}
          options={sourceOptions}
          emptyText="No linked sources."
          disabled={readOnly}
          onLink={(sourceId) => onCommand({
            type: "updateContextItem",
            itemId: item.id,
            sourceDocumentIds: [...item.sourceDocumentIds, sourceId],
          })}
          onUnlink={(sourceId) => onCommand({
            type: "updateContextItem",
            itemId: item.id,
            sourceDocumentIds: item.sourceDocumentIds.filter((id) => id !== sourceId),
          })}
        />
        {item.sourceDocumentIds.length > 0 && (
          <button
            type="button"
            onClick={() => onOpenSelection({ kind: "source", id: item.sourceDocumentIds[0] })}
            className="self-start text-[11px] text-primary underline-offset-2 hover:underline"
          >
            Open source
          </button>
        )}

        <LinkedTasksField
          projectId={item.projectId}
          tasks={tasks}
          projects={projects}
          sections={sections}
          linkedTaskIds={item.linkedTaskIds}
          disabled={readOnly}
          onLink={(taskId) => onCommand({ type: "linkContextItemToTask", itemId: item.id, taskId })}
          onUnlink={(taskId) => onCommand({ type: "unlinkContextItemFromTask", itemId: item.id, taskId })}
        />

        <LinkEditor
          label="Linked MEETs"
          icon={Video}
          linkedIds={item.linkedMeetingIds}
          options={meetItems.map((m) => ({ id: m.id, label: m.title }))}
          emptyText="No linked MEETs."
          disabled={readOnly}
          onLink={(meetingId) => onCommand({ type: "linkContextItemToMeeting", itemId: item.id, meetingId })}
          onUnlink={(meetingId) => onCommand({ type: "unlinkContextItemFromMeeting", itemId: item.id, meetingId })}
        />
      </fieldset>

      <div className="flex items-center gap-2 border-t border-border px-4 py-3">
        <button
          type="button"
          onClick={remove}
          disabled={readOnly}
          className="flex items-center gap-1.5 rounded-lg px-2.5 py-2 text-sm font-medium text-destructive transition-colors hover:bg-destructive/10 disabled:cursor-not-allowed disabled:opacity-40"
        >
          <Trash2 className="size-3.5" />
          Delete
        </button>
        <div className="flex-1" />
        <button
          type="button"
          onClick={revert}
          disabled={!dirty}
          className="flex items-center gap-1.5 rounded-lg border border-border px-2.5 py-2 text-sm font-medium text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-30"
        >
          <UndoDot className="size-3.5" />
          Revert
        </button>
        <button
          type="button"
          onClick={save}
          disabled={!canSave}
          className="flex items-center gap-1.5 rounded-lg bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground transition-opacity disabled:cursor-not-allowed disabled:opacity-40"
        >
          <Save className="size-3.5" />
          Save
        </button>
      </div>
    </div>
  )
}

function SourceEditor({
  source,
  projects,
  sections,
  tasks,
  meetItems,
  contextItems,
  onCommand,
  onOpenSelection,
  readOnly,
}: ContextHubDetailsPanelProps & { source: WorkspaceContextSourceSnapshot }) {
  const [title, setTitle] = useState(source.title)
  const [body, setBody] = useState(source.body)
  const [summary, setSummary] = useState(source.summary)
  const [sourceType, setSourceType] = useState<ContextSourceType>(source.sourceType)
  const [sourceApp, setSourceApp] = useState<ContextSourceApp | "">(source.sourceApp ?? "")
  const baseRef = useRef(source)
  useEffect(() => {
    if (baseRef.current.updatedAtUtc === source.updatedAtUtc) return
    baseRef.current = source
    setTitle(source.title)
    setBody(source.body)
    setSummary(source.summary)
    setSourceType(source.sourceType)
    setSourceApp(source.sourceApp ?? "")
  }, [source])

  const dirty = title !== source.title || body !== source.body || summary !== source.summary ||
    sourceType !== source.sourceType || (sourceApp || null) !== source.sourceApp
  const canSave = dirty && title.trim().length > 0 && !readOnly

  const save = () => {
    if (!canSave) return
    onCommand({
      type: "updateContextSource",
      sourceId: source.id,
      title: title.trim(),
      body,
      summary,
      sourceType,
      sourceApp: sourceApp === "" ? null : sourceApp,
    })
  }

  const revert = () => {
    setTitle(source.title)
    setBody(source.body)
    setSummary(source.summary)
    setSourceType(source.sourceType)
    setSourceApp(source.sourceApp ?? "")
  }

  const remove = () => {
    if (window.confirm(
      `Delete source "${source.title}"? Context items derived from it are kept — only the source link is removed.`,
    )) {
      onCommand({ type: "deleteContextSource", sourceId: source.id })
    }
  }

  const derived = contextItems.filter((item) => item.sourceDocumentIds.includes(source.id))
  const projectLabel = projects.find((p) => p.id === source.projectId)?.name ?? ""

  return (
    <div className="flex min-h-0 min-w-0 flex-1 flex-col">
      <fieldset disabled={readOnly} className="flex min-h-0 min-w-0 flex-1 flex-col gap-4 overflow-x-hidden overflow-y-auto p-4 disabled:opacity-70">
        <div className="grid grid-cols-2 gap-3">
          <div className="flex flex-col gap-1.5">
            <FieldLabel>Source type</FieldLabel>
            <select
              value={sourceType}
              onChange={(e) => setSourceType(e.target.value as ContextSourceType)}
              className={cn(inputClass, "px-2")}
            >
              {SOURCE_TYPES.map((t) => (
                <option key={t} value={t}>{sourceTypeMeta[t].label}</option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-1.5">
            <FieldLabel>Source app</FieldLabel>
            <select
              value={sourceApp}
              onChange={(e) => setSourceApp(e.target.value as ContextSourceApp | "")}
              className={cn(inputClass, "px-2")}
            >
              <option value="">None</option>
              {SOURCE_APPS.map((a) => (
                <option key={a} value={a}>{sourceAppMeta[a]}</option>
              ))}
            </select>
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <FieldLabel>Title</FieldLabel>
          <input value={title} onChange={(e) => setTitle(e.target.value)} className={inputClass} />
        </div>

        <div className="flex items-center gap-2 text-[11px] text-muted-foreground">
          <span>{projectLabel}</span>
          <span>· {formatDate(source.sourceDateUtc)}</span>
        </div>

        <div className="flex flex-col gap-1.5">
          <FieldLabel>Body — pasted text</FieldLabel>
          <textarea
            value={body}
            onChange={(e) => setBody(e.target.value)}
            rows={6}
            className={cn(inputClass, "resize-none leading-relaxed")}
          />
        </div>

        <div className="flex flex-col gap-1.5">
          <FieldLabel>Summary</FieldLabel>
          <textarea
            value={summary}
            onChange={(e) => setSummary(e.target.value)}
            rows={2}
            placeholder="One-line recap for scanning…"
            className={cn(inputClass, "resize-none")}
          />
        </div>

        <div className="flex flex-col gap-1.5">
          <FieldLabel>Context derived from this source</FieldLabel>
          {derived.length === 0 ? (
            <p className="text-[11px] text-muted-foreground">No context items yet.</p>
          ) : (
            <div className="flex min-w-0 flex-col gap-1">
              {derived.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => onOpenSelection({ kind: "item", id: item.id })}
                  className="flex min-w-0 items-center gap-2 rounded-md border border-border bg-card/60 px-2 py-1.5 text-left transition-colors hover:border-primary/40 hover:bg-accent/40"
                >
                  <TypeBadge type={item.itemType} />
                  <span className="min-w-0 flex-1 truncate text-[12px] text-foreground/90">{item.title}</span>
                  <StatusChip status={item.status} />
                </button>
              ))}
            </div>
          )}
        </div>

        <LinkedTasksField
          projectId={source.projectId}
          tasks={tasks}
          projects={projects}
          sections={sections}
          linkedTaskIds={source.linkedTaskIds}
          disabled={readOnly}
          onLink={(taskId) => onCommand({ type: "linkSourceToTask", sourceId: source.id, taskId })}
          onUnlink={(taskId) => onCommand({ type: "unlinkSourceFromTask", sourceId: source.id, taskId })}
        />

        <LinkEditor
          label="Linked MEETs"
          icon={Video}
          linkedIds={source.linkedMeetingIds}
          options={meetItems.map((m) => ({ id: m.id, label: m.title }))}
          emptyText="No linked MEETs."
          disabled={readOnly}
          onLink={(meetingId) => onCommand({ type: "linkSourceToMeeting", sourceId: source.id, meetingId })}
          onUnlink={(meetingId) => onCommand({ type: "unlinkSourceFromMeeting", sourceId: source.id, meetingId })}
        />
      </fieldset>

      <div className="flex items-center gap-2 border-t border-border px-4 py-3">
        <button
          type="button"
          onClick={remove}
          disabled={readOnly}
          className="flex items-center gap-1.5 rounded-lg px-2.5 py-2 text-sm font-medium text-destructive transition-colors hover:bg-destructive/10 disabled:cursor-not-allowed disabled:opacity-40"
        >
          <Trash2 className="size-3.5" />
          Delete
        </button>
        <div className="flex-1" />
        <button
          type="button"
          onClick={revert}
          disabled={!dirty}
          className="flex items-center gap-1.5 rounded-lg border border-border px-2.5 py-2 text-sm font-medium text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-30"
        >
          <UndoDot className="size-3.5" />
          Revert
        </button>
        <button
          type="button"
          onClick={save}
          disabled={!canSave}
          className="flex items-center gap-1.5 rounded-lg bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground transition-opacity disabled:cursor-not-allowed disabled:opacity-40"
        >
          <Save className="size-3.5" />
          Save
        </button>
      </div>
    </div>
  )
}

// ─── Add Source / Add Context modals (v0 layout, connected submit) ──────────

function AddFlowModal({
  mode,
  projects,
  tasks,
  meetItems,
  contextSources,
  selectedProjectIds,
  onSubmit,
  onClose,
}: {
  mode: "source" | "context"
  projects: Project[]
  tasks: Task[]
  meetItems: MeetItem[]
  contextSources: WorkspaceContextSourceSnapshot[]
  selectedProjectIds: string[]
  onSubmit: (command: WorkspaceContextHubCommand) => void
  onClose: () => void
}) {
  const defaultProjectId = selectedProjectIds[0] ?? projects[0]?.id ?? ""
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose() }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [onClose])

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-background/70 p-6 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      onClick={onClose}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="flex max-h-full w-full max-w-md flex-col overflow-hidden rounded-xl border border-border bg-popover shadow-2xl shadow-black/50"
      >
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <div className="flex flex-col">
            <span className="text-sm font-semibold text-foreground">
              {mode === "source" ? "Add source" : "Add context item"}
            </span>
            <span className="text-[11px] text-muted-foreground">
              {mode === "source"
                ? "Store a meeting, chat, or request into project memory"
                : "Capture a decision, blocker, question, or fact"}
            </span>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="flex size-7 items-center justify-center rounded text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
          >
            <X className="size-4" />
          </button>
        </div>
        {mode === "source" ? (
          <AddSourceForm
            projects={projects}
            tasks={tasks}
            meetItems={meetItems}
            defaultProjectId={defaultProjectId}
            onSubmit={onSubmit}
            onClose={onClose}
          />
        ) : (
          <AddContextForm
            projects={projects}
            contextSources={contextSources}
            defaultProjectId={defaultProjectId}
            onSubmit={onSubmit}
            onClose={onClose}
          />
        )}
      </div>
    </div>
  )
}

function ModalFooter({ canSave, onSave, onClose }: { canSave: boolean; onSave: () => void; onClose: () => void }) {
  return (
    <div className="flex items-center gap-2 border-t border-border px-4 py-3">
      <button
        type="button"
        onClick={onSave}
        disabled={!canSave}
        className="flex flex-1 items-center justify-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-[13px] font-semibold text-primary-foreground transition-colors hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-40"
      >
        <Save className="size-4" />
        Save
      </button>
      <button
        type="button"
        onClick={onClose}
        className="flex items-center justify-center gap-1.5 rounded-md border border-border bg-card px-3 py-1.5 text-[13px] text-foreground transition-colors hover:bg-accent"
      >
        <X className="size-4" />
        Cancel
      </button>
    </div>
  )
}

function AddSourceForm({
  projects,
  tasks,
  meetItems,
  defaultProjectId,
  onSubmit,
  onClose,
}: {
  projects: Project[]
  tasks: Task[]
  meetItems: MeetItem[]
  defaultProjectId: string
  onSubmit: (command: WorkspaceContextHubCommand) => void
  onClose: () => void
}) {
  const [sourceType, setSourceType] = useState<ContextSourceType>("meetingSummary")
  const [sourceApp, setSourceApp] = useState<ContextSourceApp | "">("")
  const [projectId, setProjectId] = useState(defaultProjectId)
  const [title, setTitle] = useState("")
  const [body, setBody] = useState("")
  const [summary, setSummary] = useState("")
  const [linkedMeetingId, setLinkedMeetingId] = useState("")
  const [linkedTaskId, setLinkedTaskId] = useState("")

  const canSave = title.trim().length > 0 && !!projectId
  const submit = () => {
    if (!canSave) return
    onSubmit({
      type: "createContextSource",
      projectId,
      sourceType,
      sourceApp: sourceApp === "" ? null : sourceApp,
      title: title.trim(),
      body,
      summary,
      linkedTaskIds: linkedTaskId ? [linkedTaskId] : [],
      linkedMeetingIds: linkedMeetingId ? [linkedMeetingId] : [],
    })
  }

  const projectTasks = tasks.filter((t) => t.projectId === projectId && t.title)
  const projectMeets = meetItems.filter((m) => m.projectId === projectId)

  return (
    <>
      <div className="flex flex-col gap-4 overflow-y-auto p-4">
        <div className="grid grid-cols-2 gap-3">
          <div className="flex flex-col gap-1.5">
            <FieldLabel>Source type</FieldLabel>
            <select value={sourceType} onChange={(e) => setSourceType(e.target.value as ContextSourceType)} className={cn(inputClass, "px-2")}>
              {SOURCE_TYPES.map((t) => (
                <option key={t} value={t}>{sourceTypeMeta[t].label}</option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-1.5">
            <FieldLabel>Project</FieldLabel>
            <select value={projectId} onChange={(e) => setProjectId(e.target.value)} className={cn(inputClass, "px-2")}>
              {projects.map((p) => (
                <option key={p.id} value={p.id}>{p.name}</option>
              ))}
            </select>
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <FieldLabel>Source app (optional)</FieldLabel>
          <select value={sourceApp} onChange={(e) => setSourceApp(e.target.value as ContextSourceApp | "")} className={cn(inputClass, "px-2")}>
            <option value="">None</option>
            {SOURCE_APPS.map((a) => (
              <option key={a} value={a}>{sourceAppMeta[a]}</option>
            ))}
          </select>
        </div>

        <div className="flex flex-col gap-1.5">
          <FieldLabel>Title</FieldLabel>
          <input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Short, recognizable name…" className={inputClass} autoFocus />
        </div>

        <div className="flex flex-col gap-1.5">
          <FieldLabel>Body — pasted text</FieldLabel>
          <textarea
            value={body}
            onChange={(e) => setBody(e.target.value)}
            rows={6}
            placeholder="Paste meeting notes, chat summary, client request…"
            className={cn(inputClass, "resize-none leading-relaxed")}
          />
        </div>

        <div className="flex flex-col gap-1.5">
          <FieldLabel>Summary (optional)</FieldLabel>
          <textarea
            value={summary}
            onChange={(e) => setSummary(e.target.value)}
            rows={2}
            placeholder="One-line recap for scanning…"
            className={cn(inputClass, "resize-none")}
          />
        </div>

        <div className="grid grid-cols-2 gap-3">
          <div className="flex flex-col gap-1.5">
            <FieldLabel>Link MEET (optional)</FieldLabel>
            <select value={linkedMeetingId} onChange={(e) => setLinkedMeetingId(e.target.value)} className={cn(inputClass, "px-2")}>
              <option value="">None</option>
              {projectMeets.map((m) => (
                <option key={m.id} value={m.id}>{m.title}</option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-1.5">
            <FieldLabel>Link task (optional)</FieldLabel>
            <select value={linkedTaskId} onChange={(e) => setLinkedTaskId(e.target.value)} className={cn(inputClass, "px-2")}>
              <option value="">None</option>
              {projectTasks.map((t) => (
                <option key={t.id} value={t.id}>{t.title}</option>
              ))}
            </select>
          </div>
        </div>
      </div>
      <ModalFooter canSave={canSave} onSave={submit} onClose={onClose} />
    </>
  )
}

function AddContextForm({
  projects,
  contextSources,
  defaultProjectId,
  onSubmit,
  onClose,
}: {
  projects: Project[]
  contextSources: WorkspaceContextSourceSnapshot[]
  defaultProjectId: string
  onSubmit: (command: WorkspaceContextHubCommand) => void
  onClose: () => void
}) {
  const [itemType, setItemType] = useState<ContextItemType>("decision")
  const [status, setStatus] = useState<ContextItemStatus>("active")
  const [projectId, setProjectId] = useState(defaultProjectId)
  const [title, setTitle] = useState("")
  const [body, setBody] = useState("")
  const [sourceDocumentId, setSourceDocumentId] = useState("")

  const canSave = title.trim().length > 0 && !!projectId
  const submit = () => {
    if (!canSave) return
    onSubmit({
      type: "createContextItem",
      projectId,
      itemType,
      status,
      title: title.trim(),
      body,
      sourceDocumentIds: sourceDocumentId ? [sourceDocumentId] : [],
    })
  }

  const projectSources = contextSources.filter((s) => s.projectId === projectId)

  return (
    <>
      <div className="flex flex-col gap-4 overflow-y-auto p-4">
        <div className="flex flex-col gap-1.5">
          <FieldLabel>Type</FieldLabel>
          <div className="flex flex-wrap gap-1">
            {CONTEXT_TYPES.map((t) => {
              const on = t === itemType
              const meta = contextTypeMeta[t]
              return (
                <button
                  key={t}
                  type="button"
                  onClick={() => setItemType(t)}
                  className={cn(
                    "inline-flex items-center gap-1 rounded-md border px-1.5 py-1 text-[11px] font-semibold transition-colors",
                    on ? cn(meta.bg, meta.border, meta.text) : "border-border text-muted-foreground hover:text-foreground",
                  )}
                >
                  <span className={cn("size-1.5 rounded-full", on ? meta.dot : "bg-muted-foreground/40")} />
                  {meta.label}
                </button>
              )
            })}
          </div>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <div className="flex flex-col gap-1.5">
            <FieldLabel>Project</FieldLabel>
            <select value={projectId} onChange={(e) => setProjectId(e.target.value)} className={cn(inputClass, "px-2")}>
              {projects.map((p) => (
                <option key={p.id} value={p.id}>{p.name}</option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-1.5">
            <FieldLabel>Status</FieldLabel>
            <select value={status} onChange={(e) => setStatus(e.target.value as ContextItemStatus)} className={cn(inputClass, "px-2")}>
              {CONTEXT_STATUSES.map((s) => (
                <option key={s} value={s}>{contextStatusMeta[s].label}</option>
              ))}
            </select>
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <FieldLabel>Title</FieldLabel>
          <input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="What is the decision / fact / question…" className={inputClass} autoFocus />
        </div>

        <div className="flex flex-col gap-1.5">
          <FieldLabel>Body</FieldLabel>
          <textarea
            value={body}
            onChange={(e) => setBody(e.target.value)}
            rows={4}
            placeholder="Details and reasoning…"
            className={cn(inputClass, "resize-none leading-relaxed")}
          />
        </div>

        <div className="flex flex-col gap-1.5">
          <FieldLabel>Derived from source (optional)</FieldLabel>
          <select value={sourceDocumentId} onChange={(e) => setSourceDocumentId(e.target.value)} className={cn(inputClass, "px-2")}>
            <option value="">None</option>
            {projectSources.map((s) => (
              <option key={s.id} value={s.id}>{s.title}</option>
            ))}
          </select>
        </div>

        <p className="text-[11px] text-muted-foreground/70">
          Tasks and MEETs can be linked after creation from the details panel.
        </p>
      </div>
      <ModalFooter canSave={canSave} onSave={submit} onClose={onClose} />
    </>
  )
}
