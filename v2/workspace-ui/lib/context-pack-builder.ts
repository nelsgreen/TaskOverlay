import type {
  ContextItemType,
  ContextSourceApp,
  ContextSourceType,
  MeetItem,
  Project,
  Section,
  Task,
  WorkspaceContextItemSnapshot,
  WorkspaceContextSourceSnapshot,
} from "./types"

/**
 * Deterministic markdown builders for Context Pack export (Project / Task /
 * MEET). Pure and framework-free by design: no React, no bridge calls, no
 * clipboard access, no network. Input is exactly the data already in the
 * Workspace snapshot (projects/sections/tasks/meetings/context sources/
 * context items) - nothing here can read Telegram settings, the bot token,
 * or any other protected/external data, because those types are never
 * passed in. Deprecated/superseded ContextItems are excluded by default.
 *
 * Kept intentionally framework-free and side-effect-free so it stays easy to
 * reason about and, if this repo ever adds a JS test runner, trivial to unit
 * test without any DOM/React setup.
 */

const SOURCE_PREVIEW_LIMIT = 1000
const CONTEXT_ITEM_BODY_LIMIT = 1600
const TASK_NOTES_LIMIT = 1600
const MEET_NOTES_LIMIT = 1600
const RECENT_SOURCES_LIMIT = 12

const ITEM_TYPE_LABEL: Record<ContextItemType, string> = {
  decision: "Decision",
  requirement: "Requirement",
  constraint: "Constraint",
  blocker: "Blocker",
  openQuestion: "Open question",
  actionItem: "Action item",
  projectFact: "Project fact",
  risk: "Risk",
  note: "Note",
}

const SOURCE_TYPE_LABEL: Record<ContextSourceType, string> = {
  meetingSummary: "Meeting summary",
  meetingTranscript: "Meeting transcript",
  chatSummary: "Chat summary",
  manualNote: "Manual note",
  clientRequest: "Client request",
  documentSummary: "Document summary",
  statusUpdate: "Status update",
  telegramCapture: "Telegram capture",
  other: "Other",
}

const SOURCE_APP_LABEL: Record<ContextSourceApp, string> = {
  chatgpt: "ChatGPT",
  claude: "Claude",
  codex: "Codex",
  telegram: "Telegram",
  manual: "Manual",
  other: "Other",
}

const MEET_DURATION_LABEL: Record<string, string> = {
  "15m": "15 min",
  "30m": "30 min",
  "45m": "45 min",
  "1h": "1 hour",
  "90m": "1.5 hours",
  "2h": "2 hours",
}

/** Truncates to a reasonable preview length; never silently drops the whole value. */
export function truncate(text: string, maxLength: number): string {
  const trimmed = text.trim()
  if (trimmed.length <= maxLength) return trimmed
  return `${trimmed.slice(0, maxLength).trimEnd()}... [truncated]`
}

function formatGeneratedAt(now: Date): string {
  return now.toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  })
}

function formatSourceDate(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return d.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" })
}

/** "Project / Section / Parent task" breadcrumb, skipping any missing segment. */
export function taskPathForPack(task: Task, projects: Project[], sections: Section[], tasks: Task[]): string {
  const projectName = projects.find((p) => p.id === task.projectId)?.name
  const sectionName = sections.find((s) => s.id === task.sectionId)?.name
  const parentTitle = task.parentId ? tasks.find((t) => t.id === task.parentId)?.title : undefined
  return [projectName, sectionName, parentTitle].filter(Boolean).join(" / ") || "—"
}

function formatMeetingDateTime(meet: MeetItem): string {
  if (!meet.date) return "—"
  const d = new Date(`${meet.date}T${meet.startTime || "00:00"}:00`)
  if (Number.isNaN(d.getTime())) return meet.date
  return d.toLocaleString(undefined, {
    weekday: "short",
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  })
}

function meetingDurationLabel(meet: MeetItem): string {
  if (meet.duration === "custom") return meet.endTime ? `until ${meet.endTime}` : "custom"
  return MEET_DURATION_LABEL[meet.duration] ?? meet.duration
}

function describeReminder(task: Task): string | null {
  if (task.reminderDate) return task.reminderTime ? `${task.reminderDate} ${task.reminderTime}` : task.reminderDate
  if (task.reminder && task.reminder !== "none") return task.reminder
  return null
}

function describeDeadline(task: Task): string | null {
  if (task.deadlineDate) return task.deadlineTime ? `${task.deadlineDate} ${task.deadlineTime}` : task.deadlineDate
  return task.deadline ?? null
}

/** Everything except deprecated/superseded — the "active" default for a Context Pack. */
function isActiveContextItem(item: WorkspaceContextItemSnapshot): boolean {
  return item.status !== "deprecated" && item.status !== "superseded"
}

function projectScopedItems(items: WorkspaceContextItemSnapshot[], projectId: string): WorkspaceContextItemSnapshot[] {
  return items.filter((item) => item.projectId === projectId && isActiveContextItem(item))
}

function projectScopedSources(
  sources: WorkspaceContextSourceSnapshot[],
  projectId: string,
): WorkspaceContextSourceSnapshot[] {
  return sources.filter((source) => source.projectId === projectId)
}

function renderContextItemEntry(
  item: WorkspaceContextItemSnapshot,
  sources: WorkspaceContextSourceSnapshot[],
  tasks: Task[],
  meetItems: MeetItem[],
): string[] {
  const lines = [`- ${item.title}`, `  Status: ${item.status}`]
  if (item.body) {
    lines.push(`  Details: ${truncate(item.body, CONTEXT_ITEM_BODY_LIMIT)}`)
  }
  const sourceTitles = item.sourceDocumentIds
    .map((id) => sources.find((s) => s.id === id)?.title)
    .filter((title): title is string => !!title)
  if (sourceTitles.length > 0) lines.push(`  Sources: ${sourceTitles.join(", ")}`)
  const taskTitles = item.linkedTaskIds
    .map((id) => tasks.find((t) => t.id === id)?.title || null)
    .filter((title): title is string => !!title)
  if (taskTitles.length > 0) lines.push(`  Linked tasks: ${taskTitles.join(", ")}`)
  const meetTitles = item.linkedMeetingIds
    .map((id) => meetItems.find((m) => m.id === id)?.title || null)
    .filter((title): title is string => !!title)
  if (meetTitles.length > 0) lines.push(`  Linked MEETs: ${meetTitles.join(", ")}`)
  return lines
}

function renderSourceEntry(source: WorkspaceContextSourceSnapshot): string[] {
  const lines = [`- ${source.title}`, `  Type: ${SOURCE_TYPE_LABEL[source.sourceType]}`]
  if (source.sourceApp) lines.push(`  App: ${SOURCE_APP_LABEL[source.sourceApp]}`)
  lines.push(`  Date: ${formatSourceDate(source.sourceDateUtc)}`)
  const preview = source.summary || source.body
  if (preview) lines.push(`  Preview: ${truncate(preview, SOURCE_PREVIEW_LIMIT)}`)
  return lines
}

/** Renders one item-type group as a heading + entries (or "None recorded."), never silently omitted. */
function buildItemTypeSection(
  headingLevel: "##" | "###",
  heading: string,
  items: WorkspaceContextItemSnapshot[],
  types: ContextItemType[],
  sources: WorkspaceContextSourceSnapshot[],
  tasks: Task[],
  meetItems: MeetItem[],
): string[] {
  const matches = items.filter((item) => types.includes(item.itemType))
  const lines = [`${headingLevel} ${heading}`, ""]
  if (matches.length === 0) {
    lines.push("- None recorded.")
  } else {
    for (const item of matches) lines.push(...renderContextItemEntry(item, sources, tasks, meetItems))
  }
  lines.push("")
  return lines
}

function buildMemorySections(
  headingLevel: "##" | "###",
  items: WorkspaceContextItemSnapshot[],
  sources: WorkspaceContextSourceSnapshot[],
  tasks: Task[],
  meetItems: MeetItem[],
): string[] {
  return [
    ...buildItemTypeSection(headingLevel, "Active decisions", items, ["decision"], sources, tasks, meetItems),
    ...buildItemTypeSection(
      headingLevel,
      "Requirements / constraints",
      items,
      ["requirement", "constraint"],
      sources,
      tasks,
      meetItems,
    ),
    ...buildItemTypeSection(headingLevel, "Blockers", items, ["blocker"], sources, tasks, meetItems),
    ...buildItemTypeSection(headingLevel, "Open questions", items, ["openQuestion"], sources, tasks, meetItems),
    ...buildItemTypeSection(headingLevel, "Risks", items, ["risk"], sources, tasks, meetItems),
    ...buildItemTypeSection(headingLevel, "Action items", items, ["actionItem"], sources, tasks, meetItems),
    ...buildItemTypeSection(
      headingLevel,
      "Project facts / notes",
      items,
      ["projectFact", "note"],
      sources,
      tasks,
      meetItems,
    ),
  ]
}

function omittedSection(): string[] {
  return [
    "## Excluded / omitted",
    "",
    "- Deprecated and superseded context items are excluded by default.",
    "- External chat history is not included unless imported into TaskOverlay as SourceDocuments.",
    "- Telegram messages are included only if captured and stored in TaskOverlay.",
    "- Bot token, allowed user id, protected settings, and external secrets are not included.",
    "",
  ]
}

function finalize(lines: string[]): string {
  return `${lines.join("\n").trim()}\n`
}

export interface ProjectContextPackInput {
  project: Project
  tasks: Task[]
  sections: Section[]
  meetItems: MeetItem[]
  contextSources: WorkspaceContextSourceSnapshot[]
  contextItems: WorkspaceContextItemSnapshot[]
  now?: Date
}

export function buildProjectContextPack(input: ProjectContextPackInput): string {
  const { project, tasks, sections, meetItems, contextSources, contextItems } = input
  const now = input.now ?? new Date()

  const items = projectScopedItems(contextItems, project.id)
  const sources = projectScopedSources(contextSources, project.id)
  const projectTasks = tasks.filter((t) => t.projectId === project.id)
  const projectMeets = meetItems.filter((m) => m.projectId === project.id)

  const lines: string[] = []
  lines.push(`# Project Context Pack: ${project.name}`, "")
  lines.push(`Generated: ${formatGeneratedAt(now)}`)
  lines.push("Source: TaskOverlay stored data only", "")
  lines.push("## How to use this pack", "")
  lines.push(
    "Use this as project memory/context for Claude/ChatGPT/Codex. It includes only data saved in TaskOverlay.",
    "",
  )

  lines.push(...buildMemorySections("##", items, sources, projectTasks, projectMeets))

  lines.push("## Recent sources", "")
  if (sources.length === 0) {
    lines.push("- None recorded.")
  } else {
    const recent = [...sources]
      .sort((a, b) => new Date(b.sourceDateUtc).getTime() - new Date(a.sourceDateUtc).getTime())
      .slice(0, RECENT_SOURCES_LIMIT)
    for (const source of recent) lines.push(...renderSourceEntry(source))
  }
  lines.push("")

  const linkedTaskIds = new Set<string>()
  for (const item of items) for (const id of item.linkedTaskIds) linkedTaskIds.add(id)
  for (const source of sources) for (const id of source.linkedTaskIds) linkedTaskIds.add(id)
  const linkedActiveTasks = projectTasks.filter((t) => linkedTaskIds.has(t.id) && t.status !== "DONE")
  lines.push("## Linked active tasks", "")
  if (linkedActiveTasks.length === 0) {
    lines.push("- None recorded.")
  } else {
    for (const task of linkedActiveTasks) {
      const contextCount =
        items.filter((i) => i.linkedTaskIds.includes(task.id)).length +
        sources.filter((s) => s.linkedTaskIds.includes(task.id)).length
      lines.push(`- ${task.title || "(untitled)"}`)
      lines.push(`  Status: ${task.status}`)
      lines.push(`  Path: ${taskPathForPack(task, [project], sections, projectTasks)}`)
      lines.push(`  Linked context: ${contextCount}`)
    }
  }
  lines.push("")

  const linkedMeetingIds = new Set<string>()
  for (const item of items) for (const id of item.linkedMeetingIds) linkedMeetingIds.add(id)
  for (const source of sources) for (const id of source.linkedMeetingIds) linkedMeetingIds.add(id)
  const linkedMeets = projectMeets.filter((m) => linkedMeetingIds.has(m.id))
  lines.push("## Linked MEETs", "")
  if (linkedMeets.length === 0) {
    lines.push("- None recorded.")
  } else {
    for (const meet of linkedMeets) {
      const contextCount =
        items.filter((i) => i.linkedMeetingIds.includes(meet.id)).length +
        sources.filter((s) => s.linkedMeetingIds.includes(meet.id)).length
      lines.push(`- ${meet.title || "(untitled)"}`)
      lines.push(`  Date: ${formatMeetingDateTime(meet)}`)
      lines.push(`  Linked context: ${contextCount}`)
    }
  }
  lines.push("")

  lines.push(...omittedSection())

  return finalize(lines)
}

export interface TaskContextPackInput {
  task: Task
  projects: Project[]
  sections: Section[]
  tasks: Task[]
  meetItems: MeetItem[]
  contextSources: WorkspaceContextSourceSnapshot[]
  contextItems: WorkspaceContextItemSnapshot[]
  now?: Date
}

export function buildTaskContextPack(input: TaskContextPackInput): string {
  const { task, projects, sections, tasks, meetItems, contextSources, contextItems } = input
  const now = input.now ?? new Date()
  const project = projects.find((p) => p.id === task.projectId)
  const projectId = task.projectId

  const linkedSources = contextSources.filter((s) => s.linkedTaskIds.includes(task.id))
  const linkedItems = contextItems.filter((i) => i.linkedTaskIds.includes(task.id))
  const projectItems = projectScopedItems(contextItems, projectId)
  const projectTasksList = tasks.filter((t) => t.projectId === projectId)
  const projectMeetsList = meetItems.filter((m) => m.projectId === projectId)

  const lines: string[] = []
  lines.push(`# Task Context Pack: ${task.title || "(untitled)"}`, "")
  lines.push(`Generated: ${formatGeneratedAt(now)}`)
  lines.push("Source: TaskOverlay stored data only", "")

  lines.push("## Focus task", "")
  lines.push(`Title: ${task.title || "(untitled)"}`)
  lines.push(`Project: ${project?.name ?? "Unknown project"}`)
  lines.push(`Path: ${taskPathForPack(task, projects, sections, tasks)}`)
  lines.push(`Status: ${task.status}`)
  if (task.notes) lines.push(`Notes: ${truncate(task.notes, TASK_NOTES_LIMIT)}`)
  if (task.waitingFor) lines.push(`Waiting for: ${task.waitingFor}`)
  const reminderLine = describeReminder(task)
  if (reminderLine) lines.push(`Reminder: ${reminderLine}`)
  const deadlineLine = describeDeadline(task)
  if (deadlineLine) lines.push(`Deadline: ${deadlineLine}`)
  lines.push("")

  lines.push("## Linked context", "")
  lines.push("### Context items", "")
  if (linkedItems.length === 0) {
    lines.push("- None linked.")
  } else {
    for (const item of linkedItems) lines.push(...renderContextItemEntry(item, contextSources, tasks, meetItems))
  }
  lines.push("")
  lines.push("### Source documents", "")
  if (linkedSources.length === 0) {
    lines.push("- None linked.")
  } else {
    for (const source of linkedSources) lines.push(...renderSourceEntry(source))
  }
  lines.push("")

  lines.push("## Relevant project memory", "")
  lines.push(...buildMemorySections("###", projectItems, contextSources, projectTasksList, projectMeetsList))

  const relatedMeetIds = new Set<string>()
  for (const meet of projectMeetsList) if (meet.linkedTaskId === task.id) relatedMeetIds.add(meet.id)
  for (const item of linkedItems) for (const id of item.linkedMeetingIds) relatedMeetIds.add(id)
  for (const source of linkedSources) for (const id of source.linkedMeetingIds) relatedMeetIds.add(id)
  const relatedMeets = meetItems.filter((m) => relatedMeetIds.has(m.id))
  lines.push("## Related meetings", "")
  if (relatedMeets.length === 0) {
    lines.push("- None recorded.")
  } else {
    for (const meet of relatedMeets) lines.push(`- ${meet.title || "(untitled)"} (${formatMeetingDateTime(meet)})`)
  }
  lines.push("")

  lines.push(...omittedSection())

  return finalize(lines)
}

export interface MeetingContextPackInput {
  meet: MeetItem
  projects: Project[]
  sections: Section[]
  tasks: Task[]
  meetItems: MeetItem[]
  contextSources: WorkspaceContextSourceSnapshot[]
  contextItems: WorkspaceContextItemSnapshot[]
  now?: Date
}

export function buildMeetingContextPack(input: MeetingContextPackInput): string {
  const { meet, projects, sections, tasks, meetItems, contextSources, contextItems } = input
  const now = input.now ?? new Date()
  const project = projects.find((p) => p.id === meet.projectId)
  const projectId = meet.projectId

  const linkedSources = contextSources.filter((s) => s.linkedMeetingIds.includes(meet.id))
  const linkedItems = contextItems.filter((i) => i.linkedMeetingIds.includes(meet.id))
  const projectItems = projectScopedItems(contextItems, projectId)
  const projectTasksList = tasks.filter((t) => t.projectId === projectId)
  const projectMeetsList = meetItems.filter((m) => m.projectId === projectId)

  const lines: string[] = []
  lines.push(`# MEET Context Pack: ${meet.title || "(untitled)"}`, "")
  lines.push(`Generated: ${formatGeneratedAt(now)}`)
  lines.push("Source: TaskOverlay stored data only", "")

  lines.push("## Focus MEET", "")
  lines.push(`Title: ${meet.title || "(untitled)"}`)
  lines.push(`Project: ${project?.name ?? "Unknown project"}`)
  lines.push(`Date/time: ${formatMeetingDateTime(meet)}`)
  lines.push(`Duration: ${meetingDurationLabel(meet)}`)
  const locationLine = [meet.location, meet.link].filter(Boolean).join(" · ")
  if (locationLine) lines.push(`Location/link: ${locationLine}`)
  if (meet.notes) lines.push(`Notes: ${truncate(meet.notes, MEET_NOTES_LIMIT)}`)
  lines.push("")

  lines.push("## Linked task", "")
  const linkedTask = meet.linkedTaskId ? tasks.find((t) => t.id === meet.linkedTaskId) : undefined
  if (!linkedTask) {
    lines.push("- None linked.")
  } else {
    lines.push(`- ${linkedTask.title || "(untitled)"}`)
    lines.push(`  Status: ${linkedTask.status}`)
    lines.push(`  Path: ${taskPathForPack(linkedTask, projects, sections, tasks)}`)
  }
  lines.push("")

  lines.push("## Linked context", "")
  lines.push("### Context items", "")
  if (linkedItems.length === 0) {
    lines.push("- None linked.")
  } else {
    for (const item of linkedItems) lines.push(...renderContextItemEntry(item, contextSources, tasks, meetItems))
  }
  lines.push("")
  lines.push("### Source documents", "")
  if (linkedSources.length === 0) {
    lines.push("- None linked.")
  } else {
    for (const source of linkedSources) lines.push(...renderSourceEntry(source))
  }
  lines.push("")

  lines.push("## Relevant project memory", "")
  lines.push(...buildMemorySections("###", projectItems, contextSources, projectTasksList, projectMeetsList))

  lines.push(...omittedSection())

  return finalize(lines)
}

export type ContextPackInput =
  | ({ mode: "project" } & ProjectContextPackInput)
  | ({ mode: "task" } & TaskContextPackInput)
  | ({ mode: "meeting" } & MeetingContextPackInput)

/** Single dispatch entry point, for callers that want one function keyed by mode. */
export function buildContextPack(input: ContextPackInput): string {
  switch (input.mode) {
    case "project":
      return buildProjectContextPack(input)
    case "task":
      return buildTaskContextPack(input)
    case "meeting":
      return buildMeetingContextPack(input)
  }
}
