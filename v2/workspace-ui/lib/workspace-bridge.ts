"use client"

import { useCallback, useEffect, useMemo, useState } from "react"
import type {
  MeetItem,
  Project,
  Section,
  Task,
  TimelineItem,
  PendingWorkspaceCommand,
  WorkspaceCommand,
  WorkspaceCommandEnvelope,
  WorkspaceCommandResult,
  WorkspaceContextCommand,
  WorkspaceContextSnapshot,
  WorkspaceCreateSectionCommand,
  WorkspaceCreateTaskCommand,
  WorkspaceSnapshotContract,
  WorkspaceTaskCommand,
} from "@/lib/types"
import { dateKeyFromIso } from "@/lib/calendar-date"

interface WebViewMessageEvent {
  data: unknown
}

interface WebViewMessageSource {
  addEventListener(type: "message", listener: (event: WebViewMessageEvent) => void): void
  removeEventListener(type: "message", listener: (event: WebViewMessageEvent) => void): void
  postMessage(message: unknown): void
}

declare global {
  interface Window {
    __taskOverlayWorkspaceMessages?: unknown[]
    chrome?: {
      webview?: WebViewMessageSource
    }
  }
}

export interface WorkspaceData {
  projects: Project[]
  sections: Section[]
  tasks: Task[]
  activeNowTaskIds: string[]
  timelineItems: TimelineItem[]
  meetItems: MeetItem[]
  context: WorkspaceContextSnapshot
}

export interface WorkspaceBridgeState {
  status: "loading" | "mock" | "bridged"
  data: WorkspaceData | null
  canEdit: boolean
  pendingCommands: PendingWorkspaceCommand[]
  error: string | null
  /** Id of the most recently created task once the bridge confirms it, until cleared. */
  lastCreatedTaskId: string | null
  /** Id of the most recently created section/workstream once the bridge confirms it, until cleared. */
  lastCreatedSectionId: string | null
  sendCommand(command: WorkspaceTaskCommand): boolean
  sendCreateTask(input: Omit<WorkspaceCreateTaskCommand, "type">): boolean
  sendCreateSection(input: Omit<WorkspaceCreateSectionCommand, "type">): boolean
  sendWorkspaceContext(command: Omit<WorkspaceContextCommand, "type">): boolean
  clearError(): void
  clearLastCreatedTaskId(): void
  clearLastCreatedSectionId(): void
}

export function useWorkspaceBridge(): WorkspaceBridgeState {
  const [snapshot, setSnapshot] = useState<WorkspaceSnapshotContract | null>(null)
  const [bridgeAvailable, setBridgeAvailable] = useState<boolean | null>(null)
  const [pendingCommands, setPendingCommands] = useState<PendingWorkspaceCommand[]>([])
  const [error, setError] = useState<string | null>(null)
  const [lastCreatedTaskId, setLastCreatedTaskId] = useState<string | null>(null)
  const [lastCreatedSectionId, setLastCreatedSectionId] = useState<string | null>(null)

  useEffect(() => {
    const webview = window.chrome?.webview
    if (!webview) {
      setBridgeAvailable(false)
      return
    }

    setBridgeAvailable(true)
    const handleMessage = (data: unknown) => {
      if (isWorkspaceSnapshot(data)) {
        setSnapshot(data)
        return
      }

      if (isWorkspaceCommandResult(data)) {
        const result = data
        setPendingCommands((pending) =>
          pending.filter((command) => command.commandId !== result.commandId))
        if (!result.success) {
          setError(result.errorMessage ?? "Workspace command failed.")
        } else {
          if (result.createdTaskId) setLastCreatedTaskId(result.createdTaskId)
          if (result.createdSectionId) setLastCreatedSectionId(result.createdSectionId)
        }
      }
    }
    const onMessage = (event: WebViewMessageEvent) => handleMessage(event.data)
    webview.addEventListener("message", onMessage)
    window.__taskOverlayWorkspaceMessages?.forEach(handleMessage)
    window.__taskOverlayWorkspaceMessages = undefined
    return () => webview.removeEventListener("message", onMessage)
  }, [])

  const data = useMemo(
    () => snapshot ? adaptWorkspaceSnapshot(snapshot) : null,
    [snapshot],
  )

  const postCommand = useCallback((
    command: WorkspaceCommand,
    commandId = createCommandId(),
  ): boolean => {
    const webview = window.chrome?.webview
    if (!webview || snapshot?.mode !== "connected") return false

    const { type, ...payload } = command
    const envelope: WorkspaceCommandEnvelope = {
      schemaVersion: 1,
      commandId,
      type,
      payload,
    }
    setError(null)
    try {
      webview.postMessage(envelope)
      return true
    } catch {
      setError("Workspace command could not be sent.")
      return false
    }
  }, [snapshot?.mode])

  const sendCommand = useCallback((command: WorkspaceTaskCommand): boolean => {
    const commandId = createCommandId()
    setPendingCommands((pending) => [
      ...pending,
      { commandId, taskId: command.taskId, type: command.type },
    ])
    if (postCommand(command, commandId)) return true

    setPendingCommands((pending) =>
      pending.filter((pendingCommand) => pendingCommand.commandId !== commandId))
    return false
  }, [postCommand])

  const sendCreateTask = useCallback((
    input: Omit<WorkspaceCreateTaskCommand, "type">,
  ): boolean => postCommand({ type: "createTask", ...input }), [postCommand])

  const sendCreateSection = useCallback((
    input: Omit<WorkspaceCreateSectionCommand, "type">,
  ): boolean => postCommand({ type: "createSection", ...input }), [postCommand])

  const sendWorkspaceContext = useCallback((
    context: Omit<WorkspaceContextCommand, "type">,
  ): boolean => postCommand({ type: "updateWorkspaceContext", ...context }), [postCommand])

  const clearError = useCallback(() => setError(null), [])
  const clearLastCreatedTaskId = useCallback(() => setLastCreatedTaskId(null), [])
  const clearLastCreatedSectionId = useCallback(() => setLastCreatedSectionId(null), [])

  const shared = {
    pendingCommands,
    error,
    lastCreatedTaskId,
    lastCreatedSectionId,
    sendCommand,
    sendCreateTask,
    sendCreateSection,
    sendWorkspaceContext,
    clearError,
    clearLastCreatedTaskId,
    clearLastCreatedSectionId,
  }

  if (data) {
    return {
      status: "bridged",
      data,
      canEdit: snapshot?.mode === "connected",
      ...shared,
    }
  }
  if (bridgeAvailable === false) {
    return { status: "mock", data: null, canEdit: true, ...shared }
  }
  return { status: "loading", data: null, canEdit: false, ...shared }
}

function isWorkspaceSnapshot(value: unknown): value is WorkspaceSnapshotContract {
  if (!value || typeof value !== "object") return false
  const candidate = value as Partial<WorkspaceSnapshotContract>
  return candidate.schemaVersion === 1 &&
    (candidate.mode === "readonly" || candidate.mode === "connected") &&
    typeof candidate.generatedAtUtc === "string" &&
    Array.isArray(candidate.projects) &&
    Array.isArray(candidate.sections) &&
    Array.isArray(candidate.tasks) &&
    Array.isArray(candidate.activeNow) &&
    Array.isArray(candidate.timelineItems) &&
    isWorkspaceContext(candidate.context)
}

function isWorkspaceContext(value: unknown): value is WorkspaceContextSnapshot {
  if (!value || typeof value !== "object") return false
  const candidate = value as Partial<WorkspaceContextSnapshot>
  return ["tree", "status", "timeline", "calendar", "workstreams"].includes(candidate.activeTab ?? "") &&
    Array.isArray(candidate.selectedProjectIds) &&
    candidate.selectedProjectIds.every((id) => typeof id === "string") &&
    (candidate.selectedTaskId === null || typeof candidate.selectedTaskId === "string") &&
    (candidate.selectedTimelineItemId === null || typeof candidate.selectedTimelineItemId === "string") &&
    (candidate.selectedWorkstreamId === null || typeof candidate.selectedWorkstreamId === "string") &&
    ["all", "active", "active-path"].includes(candidate.filter ?? "")
}

function isWorkspaceCommandResult(value: unknown): value is WorkspaceCommandResult {
  if (!value || typeof value !== "object") return false
  const candidate = value as Partial<WorkspaceCommandResult>
  return candidate.schemaVersion === 1 &&
    candidate.messageType === "commandResult" &&
    typeof candidate.commandId === "string" &&
    typeof candidate.success === "boolean"
}

function createCommandId(): string {
  if (typeof crypto.randomUUID === "function") return crypto.randomUUID()
  return `workspace-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

function adaptWorkspaceSnapshot(snapshot: WorkspaceSnapshotContract): WorkspaceData {
  const projects = snapshot.projects.map((project) => ({
    id: project.id,
    name: project.name,
    color: project.color,
  }))
  const sections = snapshot.sections.map((section) => ({
    id: section.id,
    projectId: section.projectId,
    name: section.name,
  }))
  const tasks = snapshot.tasks.map(adaptTask)
  const timelineItems = snapshot.timelineItems.map(adaptTimelineItem)

  return {
    projects,
    sections,
    tasks,
    activeNowTaskIds: snapshot.activeNow.map((item) => item.taskId),
    timelineItems,
    meetItems: [],
    context: snapshot.context,
  }
}

/** Maps the backend's flat repeat-minutes back to a repeat-interval chip, so reopening the
 * Reminder editor after a fresh snapshot re-highlights the interval that's actually scheduled. */
function reminderIntervalFromMinutes(minutes: number | null): Task["reminderInterval"] {
  switch (minutes) {
    case 120: return "every2h"
    case 1440: return "daily"
    case 10080: return "weekly"
    default: return minutes ? "custom" : undefined
  }
}

function adaptTask(source: WorkspaceSnapshotContract["tasks"][number]): Task {
  const reminder = source.reminderAtUtc ? "custom" : "none"
  const reminderLocal = source.reminderAtUtc
    ? toLocalDateTime(source.reminderAtUtc)
    : null
  const deadlineLocal = source.deadlineAtUtc
    ? toLocalDateTime(source.deadlineAtUtc)
    : null

  return {
    id: source.id,
    projectId: source.projectId,
    sectionId: source.sectionId,
    parentId: source.parentId,
    title: source.title,
    status: source.status,
    pinned: source.pinToPanel,
    reminder,
    reminderRepeat: (source.reminderEveryMinutes ?? 0) > 0,
    reminderInterval: reminderIntervalFromMinutes(source.reminderEveryMinutes),
    reminderDate: reminderLocal?.date,
    reminderTime: reminderLocal?.time,
    waitingFor: source.waitingFor || undefined,
    deadlineDate: deadlineLocal?.date,
    deadlineTime: deadlineLocal?.time,
    deadline: deadlineLocal ? `${deadlineLocal.date} ${deadlineLocal.time}` : undefined,
    notes: source.description || undefined,
    remindAtUtc: source.reminderAtUtc ?? undefined,
    reminderActive: source.reminderActive,
    deadlineAtUtc: source.deadlineAtUtc ?? undefined,
    plannedStartAtUtc: source.plannedStartAtUtc ?? undefined,
    plannedDurationMinutes: source.plannedDurationMinutes ?? undefined,
    checkpoints: (source.checkpoints ?? []).map((checkpoint) => ({
      id: checkpoint.id,
      title: checkpoint.title,
      done: checkpoint.done,
      order: checkpoint.sortOrder,
      completedAtUtc: checkpoint.completedAtUtc,
    })),
  }
}

function adaptTimelineItem(source: WorkspaceSnapshotContract["timelineItems"][number]): TimelineItem {
  const occurrence = new Date(source.occursAtUtc)
  return {
    id: source.id,
    kind: source.kind,
    title: source.title,
    projectPath: source.projectPath,
    meta: source.meta ?? undefined,
    projectId: source.projectId,
    linkedTaskId: source.linkedTaskId,
    time: formatTimelineTime(occurrence),
    bucket: timelineBucket(occurrence),
    dateKey: dateKeyFromIso(source.occursAtUtc) ?? undefined,
  }
}

function toLocalDateTime(value: string): { date: string; time: string } {
  const date = new Date(value)
  const pad = (part: number) => String(part).padStart(2, "0")
  return {
    date: `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`,
    time: `${pad(date.getHours())}:${pad(date.getMinutes())}`,
  }
}

function formatTimelineTime(date: Date): string {
  const today = startOfDay(new Date())
  const target = startOfDay(date)
  const days = Math.round((target.getTime() - today.getTime()) / 86400000)
  const time = date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })
  if (days <= 1) return time
  if (days <= 7) return `${date.toLocaleDateString([], { weekday: "short" })} ${time}`
  return date.toLocaleDateString([], { month: "short", day: "numeric" })
}

function timelineBucket(date: Date): TimelineItem["bucket"] {
  const today = startOfDay(new Date())
  const target = startOfDay(date)
  const days = Math.round((target.getTime() - today.getTime()) / 86400000)
  if (days <= 0) return "today"
  if (days === 1) return "tomorrow"
  if (days <= 7) return "week"
  return "later"
}

function startOfDay(value: Date): Date {
  return new Date(value.getFullYear(), value.getMonth(), value.getDate())
}
