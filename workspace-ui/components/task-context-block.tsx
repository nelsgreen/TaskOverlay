"use client"

import { useEffect, useMemo, useState } from "react"
import { ClipboardList, Layers, Link2, Plus, X } from "lucide-react"
import { Button } from "@/components/ui/button"
import { IconButton } from "@/components/ui/icon-button"
import { Panel } from "@/components/ui/panel"
import { DetailsSection } from "@/components/ui/details-section"
import type {
  ContextItemStatus,
  ContextItemType,
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
import { contextStatusMeta, contextTypeMeta, sourceAppMeta, sourceTypeMeta } from "./context-hub-view"
import { buildMeetingContextPack, buildTaskContextPack } from "@/lib/context-pack-builder"
import { ContextPackModal } from "./context-pack-modal"

/**
 * Task/MEET Details -> Context block (MVP). Shows SourceDocuments/ContextItems
 * already linked to the selected task or MEET and lets the user link an
 * existing same-project record or unlink one — no create/edit here, that
 * stays in ContextHUB. Read-only preview: no local optimistic state, this
 * simply renders whatever contextSources/contextItems the caller passes;
 * after a link/unlink command round-trips through the bridge, a fresh
 * snapshot re-renders it.
 *
 * RecordContextBlock is the shared core (owner-agnostic: an id + project +
 * which linked-id array to read/mutate). TaskContextBlock and MeetContextBlock
 * are thin, unchanged-behavior wrappers so Task Details keeps its original
 * prop shape and command types while MEET Details reuses the same UI/logic.
 */
interface TaskContextBlockProps {
  task: Task
  projects: Project[]
  sections: Section[]
  tasks: Task[]
  meetItems: MeetItem[]
  contextSources: WorkspaceContextSourceSnapshot[]
  contextItems: WorkspaceContextItemSnapshot[]
  onCommand: (command: WorkspaceContextHubCommand) => boolean
  onOpenContextHub: () => void
  locked: boolean
}

export function TaskContextBlock({
  task,
  projects,
  sections,
  tasks,
  meetItems,
  contextSources,
  contextItems,
  onCommand,
  onOpenContextHub,
  locked,
}: TaskContextBlockProps) {
  return (
    <RecordContextBlock
      ownerId={task.id}
      projectId={task.projectId}
      contextSources={contextSources}
      contextItems={contextItems}
      getLinkedSourceIds={(source) => source.linkedTaskIds}
      getLinkedItemIds={(item) => item.linkedTaskIds}
      onLinkSource={(sourceId) => onCommand({ type: "linkSourceToTask", sourceId, taskId: task.id })}
      onUnlinkSource={(sourceId) => onCommand({ type: "unlinkSourceFromTask", sourceId, taskId: task.id })}
      onLinkItem={(itemId) => onCommand({ type: "linkContextItemToTask", itemId, taskId: task.id })}
      onUnlinkItem={(itemId) => onCommand({ type: "unlinkContextItemFromTask", itemId, taskId: task.id })}
      onOpenContextHub={onOpenContextHub}
      locked={locked}
      contextPack={{
        subtitle: `Task: ${task.title || "(untitled)"}`,
        buildMarkdown: () =>
          buildTaskContextPack({ task, projects, sections, tasks, meetItems, contextSources, contextItems }),
      }}
    />
  )
}

interface MeetContextBlockProps {
  meet: MeetItem
  projects: Project[]
  sections: Section[]
  tasks: Task[]
  meetItems: MeetItem[]
  contextSources: WorkspaceContextSourceSnapshot[]
  contextItems: WorkspaceContextItemSnapshot[]
  onCommand: (command: WorkspaceContextHubCommand) => boolean
  onOpenContextHub: () => void
  locked: boolean
  /** MEET Details keeps Context open by default; Task Details never sets this. */
  defaultOpenWhenEmpty?: boolean
}

export function MeetContextBlock({
  meet,
  projects,
  sections,
  tasks,
  meetItems,
  contextSources,
  contextItems,
  onCommand,
  onOpenContextHub,
  locked,
  defaultOpenWhenEmpty = false,
}: MeetContextBlockProps) {
  return (
    <RecordContextBlock
      ownerId={meet.id}
      projectId={meet.projectId}
      contextSources={contextSources}
      contextItems={contextItems}
      getLinkedSourceIds={(source) => source.linkedMeetingIds}
      getLinkedItemIds={(item) => item.linkedMeetingIds}
      onLinkSource={(sourceId) => onCommand({ type: "linkSourceToMeeting", sourceId, meetingId: meet.id })}
      onUnlinkSource={(sourceId) => onCommand({ type: "unlinkSourceFromMeeting", sourceId, meetingId: meet.id })}
      onLinkItem={(itemId) => onCommand({ type: "linkContextItemToMeeting", itemId, meetingId: meet.id })}
      onUnlinkItem={(itemId) => onCommand({ type: "unlinkContextItemFromMeeting", itemId, meetingId: meet.id })}
      onOpenContextHub={onOpenContextHub}
      locked={locked}
      defaultOpenWhenEmpty={defaultOpenWhenEmpty}
      contextPack={{
        subtitle: `MEET: ${meet.title || "(untitled)"}`,
        buildMarkdown: () =>
          buildMeetingContextPack({ meet, projects, sections, tasks, meetItems, contextSources, contextItems }),
      }}
    />
  )
}

type Candidate =
  | { kind: "source"; record: WorkspaceContextSourceSnapshot }
  | { kind: "item"; record: WorkspaceContextItemSnapshot }

/** Read-only export/copy for the owning task/MEET — never a mutation, so it stays available even when locked. */
interface ContextPackAction {
  subtitle: string
  buildMarkdown: () => string
}

interface RecordContextBlockProps {
  ownerId: string
  projectId: string
  contextSources: WorkspaceContextSourceSnapshot[]
  contextItems: WorkspaceContextItemSnapshot[]
  getLinkedSourceIds: (source: WorkspaceContextSourceSnapshot) => string[]
  getLinkedItemIds: (item: WorkspaceContextItemSnapshot) => string[]
  onLinkSource: (sourceId: string) => void
  onUnlinkSource: (sourceId: string) => void
  onLinkItem: (itemId: string) => void
  onUnlinkItem: (itemId: string) => void
  onOpenContextHub: () => void
  locked: boolean
  contextPack: ContextPackAction
  /**
   * When true, the card opens by default even with nothing linked (MEET Details
   * keeps Context visible). Task Details omits this prop, so its collapse-when-
   * empty default is unchanged.
   */
  defaultOpenWhenEmpty?: boolean
}

function RecordContextBlock({
  ownerId,
  projectId,
  contextSources,
  contextItems,
  getLinkedSourceIds,
  getLinkedItemIds,
  onLinkSource,
  onUnlinkSource,
  onLinkItem,
  onUnlinkItem,
  onOpenContextHub,
  locked,
  contextPack,
  defaultOpenWhenEmpty = false,
}: RecordContextBlockProps) {
  const [modalOpen, setModalOpen] = useState(false)
  const [search, setSearch] = useState("")
  const [packMarkdown, setPackMarkdown] = useState<string | null>(null)
  // null = no manual override yet, so the card's open/closed state follows
  // totalLinked (collapsed when empty, expanded once something is linked).
  // Once the user clicks the header, their explicit choice wins until they
  // switch to a different task/MEET (see the ownerId reset effect below).
  const [manualOpen, setManualOpen] = useState<boolean | null>(null)

  const linkedSources = useMemo(
    () => contextSources.filter((source) => getLinkedSourceIds(source).includes(ownerId)),
    [contextSources, getLinkedSourceIds, ownerId],
  )
  const linkedItems = useMemo(
    () => contextItems.filter((item) => getLinkedItemIds(item).includes(ownerId)),
    [contextItems, getLinkedItemIds, ownerId],
  )
  const totalLinked = linkedSources.length + linkedItems.length
  const open = manualOpen ?? (totalLinked > 0 || defaultOpenWhenEmpty)

  useEffect(() => {
    setManualOpen(null)
  }, [ownerId])

  // Same-project only, already-linked records are excluded rather than shown disabled —
  // there is nothing useful to do with them here besides unlink, which is already
  // available in the linked list above.
  const candidates = useMemo<Candidate[]>(() => {
    const query = search.trim().toLowerCase()
    const matchesQuery = (title: string) => !query || title.toLowerCase().includes(query)
    const sourceCandidates: Candidate[] = contextSources
      .filter((source) => source.projectId === projectId && !getLinkedSourceIds(source).includes(ownerId))
      .filter((source) => matchesQuery(source.title))
      .map((record) => ({ kind: "source" as const, record }))
    const itemCandidates: Candidate[] = contextItems
      .filter((item) => item.projectId === projectId && !getLinkedItemIds(item).includes(ownerId))
      .filter((item) => matchesQuery(item.title))
      .map((record) => ({ kind: "item" as const, record }))
    return [...sourceCandidates, ...itemCandidates]
  }, [contextSources, contextItems, projectId, ownerId, search, getLinkedSourceIds, getLinkedItemIds])

  function linkCandidate(candidate: Candidate) {
    if (candidate.kind === "source") {
      onLinkSource(candidate.record.id)
    } else {
      onLinkItem(candidate.record.id)
    }
  }

  return (
    <>
      <Panel className="group/card mt-3">
        {/* Collapsed by default when nothing is linked; expanded by default
            once something is, so the user isn't shown a large empty card.
            Always click-to-toggle regardless of the default. */}
        <DetailsSection
          icon={<Layers className="size-3.5 shrink-0 text-muted-foreground" aria-hidden />}
          title="Context"
          meta={
            totalLinked > 0 && (
              <span className="text-[11px] font-semibold tabular-nums text-primary">
                {totalLinked} linked
              </span>
            )
          }
          open={open}
          onOpenChange={setManualOpen}
        >
          {totalLinked === 0 ? (
            <p className="text-[12px] text-muted-foreground">No context linked yet.</p>
          ) : (
            <>
              {linkedSources.length > 0 && (
                <div className="space-y-1">
                  <span className="text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
                    Sources ({linkedSources.length})
                  </span>
                  {linkedSources.map((source) => (
                    <LinkedSourceRow
                      key={source.id}
                      source={source}
                      locked={locked}
                      onUnlink={() => onUnlinkSource(source.id)}
                    />
                  ))}
                </div>
              )}
              {linkedItems.length > 0 && (
                <div className="space-y-1">
                  <span className="text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
                    Context items ({linkedItems.length})
                  </span>
                  {linkedItems.map((item) => (
                    <LinkedItemRow
                      key={item.id}
                      item={item}
                      locked={locked}
                      onUnlink={() => onUnlinkItem(item.id)}
                    />
                  ))}
                </div>
              )}
            </>
          )}

          {/* Three neutral, equal-hierarchy actions - never status toggles.
              A CSS grid (not flex) guarantees exactly equal thirds at the
              narrow inspector width regardless of each label's text
              width, so none of the three can ever overflow the card. */}
          <div className="grid grid-cols-3 gap-1.5 pt-0.5">
            <Button
              tone="secondary"
              size="xs"
              disabled={locked}
              onClick={() => setModalOpen(true)}
              title="Link existing context"
            >
              <Plus className="size-3 shrink-0" aria-hidden />
              Link
            </Button>
            <Button tone="secondary" size="xs" onClick={onOpenContextHub} title="Open ContextHUB">
              <Link2 className="size-3 shrink-0" aria-hidden />
              Hub
            </Button>
            <Button
              tone="secondary"
              size="xs"
              onClick={() => setPackMarkdown(contextPack.buildMarkdown())}
              title="Context Pack export"
            >
              <ClipboardList className="size-3 shrink-0" aria-hidden />
              Export
            </Button>
          </div>
        </DetailsSection>
      </Panel>

      {modalOpen && (
        <LinkExistingModal
          candidates={candidates}
          search={search}
          onSearchChange={setSearch}
          onLink={(candidate) => linkCandidate(candidate)}
          onClose={() => setModalOpen(false)}
        />
      )}

      {packMarkdown !== null && (
        <ContextPackModal
          subtitle={contextPack.subtitle}
          markdown={packMarkdown}
          onClose={() => setPackMarkdown(null)}
        />
      )}
    </>
  )
}

function LinkedSourceRow({
  source,
  onUnlink,
  locked,
}: {
  source: WorkspaceContextSourceSnapshot
  onUnlink: () => void
  locked: boolean
}) {
  return (
    <div className="group/row flex items-start gap-2 rounded px-1 py-1 transition-colors hover:bg-accent/30">
      <SourceBadge type={source.sourceType} />
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-x-1.5 gap-y-0.5">
          <span className="truncate text-[12px] font-medium text-foreground/90">{source.title}</span>
          {source.sourceApp && (
            <span className="shrink-0 text-[10px] text-muted-foreground">{sourceAppMeta[source.sourceApp]}</span>
          )}
        </div>
        {(source.summary || source.body) && (
          <p className="line-clamp-1 text-[11px] leading-snug text-muted-foreground">
            {source.summary || source.body}
          </p>
        )}
      </div>
      <button
        type="button"
        disabled={locked}
        aria-label={`Unlink source: ${source.title}`}
        onClick={onUnlink}
        className="shrink-0 rounded p-0.5 text-muted-foreground opacity-0 transition-opacity hover:text-destructive focus-visible:opacity-100 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary group-hover/row:opacity-100 disabled:cursor-not-allowed disabled:opacity-30"
      >
        <X className="size-3.5" />
      </button>
    </div>
  )
}

function LinkedItemRow({
  item,
  onUnlink,
  locked,
}: {
  item: WorkspaceContextItemSnapshot
  onUnlink: () => void
  locked: boolean
}) {
  return (
    <div className="group/row flex items-start gap-2 rounded px-1 py-1 transition-colors hover:bg-accent/30">
      <ItemBadge type={item.itemType} />
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-x-1.5 gap-y-0.5">
          <span className="truncate text-[12px] font-medium text-foreground/90">{item.title}</span>
          <StatusLabel status={item.status} />
        </div>
        {item.body && (
          <p className="line-clamp-1 text-[11px] leading-snug text-muted-foreground">{item.body}</p>
        )}
      </div>
      <button
        type="button"
        disabled={locked}
        aria-label={`Unlink context item: ${item.title}`}
        onClick={onUnlink}
        className="shrink-0 rounded p-0.5 text-muted-foreground opacity-0 transition-opacity hover:text-destructive focus-visible:opacity-100 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary group-hover/row:opacity-100 disabled:cursor-not-allowed disabled:opacity-30"
      >
        <X className="size-3.5" />
      </button>
    </div>
  )
}

/**
 * Shared geometry for every small badge that can sit on the same context row
 * (source type, item type, status) - height, radius, padding, font size/
 * weight, and baseline all match exactly; only the semantic color (each
 * caller's own bg/border/text) differs. Fixes badges that previously had
 * their own slightly different radius/padding/font (including one that used
 * font-mono at a smaller size), which read as unrelated controls rather than
 * peers on the same row.
 */
const contextBadgeClass =
  "mt-0.5 inline-flex shrink-0 items-center rounded-md border px-1.5 py-0.5 text-[10px] font-semibold leading-none"

function SourceBadge({ type }: { type: ContextSourceType }) {
  return (
    <span className={cn(contextBadgeClass, "border-border bg-muted text-muted-foreground")}>
      {sourceTypeMeta[type].short}
    </span>
  )
}

function ItemBadge({ type }: { type: ContextItemType }) {
  const meta = contextTypeMeta[type]
  return (
    <span className={cn(contextBadgeClass, meta.bg, meta.border, meta.text)}>
      {meta.label}
    </span>
  )
}

function StatusLabel({ status }: { status: ContextItemStatus }) {
  const meta = contextStatusMeta[status]
  return (
    <span
      className={cn(
        contextBadgeClass,
        meta.bg, meta.border, meta.text,
        status === "deprecated" && "line-through",
      )}
    >
      {meta.label}
    </span>
  )
}

function LinkExistingModal({
  candidates,
  search,
  onSearchChange,
  onLink,
  onClose,
}: {
  candidates: Candidate[]
  search: string
  onSearchChange: (value: string) => void
  onLink: (candidate: Candidate) => void
  onClose: () => void
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose() }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [onClose])

  return (
    // Backdrop intentionally has no onClick: closing happens only via Cancel/Close/X (or Escape above), never an outside click.
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-background/70 p-6 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
    >
      <div
        className="flex max-h-[70vh] w-full max-w-md flex-col overflow-hidden rounded-xl border border-border bg-popover shadow-2xl shadow-black/50"
      >
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <div className="flex flex-col">
            <span className="text-sm font-semibold text-foreground">Link existing context</span>
            <span className="text-[11px] text-muted-foreground">Same-project sources and context items only</span>
          </div>
          <IconButton label="Close" onClick={onClose}>
            <X className="size-4" />
          </IconButton>
        </div>
        <div className="border-b border-border px-4 py-2.5">
          <input
            autoFocus
            value={search}
            onChange={(e) => onSearchChange(e.target.value)}
            placeholder="Search decisions, risks, requirements, source documents…"
            className="w-full rounded-md border border-input bg-background px-2.5 py-1.5 text-[13px] text-foreground outline-none placeholder:text-muted-foreground/60 focus:border-primary/60 focus:ring-1 focus:ring-primary/40"
          />
        </div>
        <div className="flex-1 overflow-y-auto">
          {candidates.length === 0 ? (
            <p className="px-4 py-6 text-center text-[12px] text-muted-foreground">
              No existing context records found. Create context in ContextHUB first.
            </p>
          ) : (
            candidates.map((candidate) => (
              <button
                key={`${candidate.kind}-${candidate.record.id}`}
                type="button"
                onClick={() => onLink(candidate)}
                className="flex w-full items-center gap-2 border-b border-border/60 px-4 py-2.5 text-left transition-colors hover:bg-accent/40"
              >
                {candidate.kind === "source" ? (
                  <SourceBadge type={candidate.record.sourceType} />
                ) : (
                  <ItemBadge type={candidate.record.itemType} />
                )}
                <span className="min-w-0 flex-1 truncate text-[13px] text-foreground/90">
                  {candidate.record.title}
                </span>
                <Plus className="size-3.5 shrink-0 text-muted-foreground" aria-hidden />
              </button>
            ))
          )}
        </div>
      </div>
    </div>
  )
}
