"use client"

import { useEffect, useMemo, useState } from "react"
import type {
  MeetItem,
  Project,
  Section,
  Task,
  TimelineItem,
  WorkspaceSnapshotContract,
} from "@/lib/types"

interface WebViewMessageEvent {
  data: unknown
}

interface WebViewMessageSource {
  addEventListener(type: "message", listener: (event: WebViewMessageEvent) => void): void
  removeEventListener(type: "message", listener: (event: WebViewMessageEvent) => void): void
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
}

export type WorkspaceBridgeState =
  | { status: "loading"; data: null }
  | { status: "mock"; data: null }
  | { status: "bridged"; data: WorkspaceData }

export function useWorkspaceBridge(): WorkspaceBridgeState {
  const [snapshot, setSnapshot] = useState<WorkspaceSnapshotContract | null>(null)
  const [bridgeAvailable, setBridgeAvailable] = useState<boolean | null>(null)

  useEffect(() => {
    const webview = window.chrome?.webview
    if (!webview) {
      setBridgeAvailable(false)
      return
    }

    setBridgeAvailable(true)
    const queuedSnapshot = window.__taskOverlayWorkspaceMessages?.find(isWorkspaceSnapshot)
    if (queuedSnapshot) setSnapshot(queuedSnapshot)
    window.__taskOverlayWorkspaceMessages = []
    const onMessage = (event: WebViewMessageEvent) => {
      if (isWorkspaceSnapshot(event.data)) setSnapshot(event.data)
    }
    webview.addEventListener("message", onMessage)
    return () => webview.removeEventListener("message", onMessage)
  }, [])

  const data = useMemo(
    () => snapshot ? adaptWorkspaceSnapshot(snapshot) : null,
    [snapshot],
  )

  if (data) return { status: "bridged", data }
  if (bridgeAvailable === false) return { status: "mock", data: null }
  return { status: "loading", data: null }
}

function isWorkspaceSnapshot(value: unknown): value is WorkspaceSnapshotContract {
  if (!value || typeof value !== "object") return false
  const candidate = value as Partial<WorkspaceSnapshotContract>
  return candidate.schemaVersion === 1 &&
    candidate.mode === "readonly" &&
    typeof candidate.generatedAtUtc === "string" &&
    Array.isArray(candidate.projects) &&
    Array.isArray(candidate.sections) &&
    Array.isArray(candidate.tasks) &&
    Array.isArray(candidate.activeNow) &&
    Array.isArray(candidate.timelineItems)
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
    reminderInterval: source.reminderEveryMinutes === 1440 ? "daily" : "custom",
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
