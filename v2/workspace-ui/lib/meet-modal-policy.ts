export type MeetModalCloseReason = "explicit" | "escape" | "navigate" | "backdrop"

export const MEET_MODAL_ACTIONS = {
  close: "Close",
  delete: "Delete meeting",
} as const

export function shouldCloseMeetModal(reason: MeetModalCloseReason): boolean {
  return reason !== "backdrop"
}

interface MeetingCleanupCandidate {
  projectId: string
  titleIsGenerated?: boolean
  notes?: string
  date: string
  startTime: string
  duration: string
  endTime?: string
  location?: string
  link?: string
  linkedTaskId?: string
  recordingPolicy?: string
}

interface UntouchedMeetingInput {
  initial: MeetingCleanupCandidate
  current: MeetingCleanupCandidate
  hasRecordingOrSource: boolean
  hasContextLink: boolean
}

export function isUntouchedNewMeeting({
  initial,
  current,
  hasRecordingOrSource,
  hasContextLink,
}: UntouchedMeetingInput): boolean {
  if (!current.titleIsGenerated || hasRecordingOrSource || hasContextLink) return false
  if (current.notes?.trim() || current.location?.trim() || current.link?.trim() || current.linkedTaskId) {
    return false
  }

  return current.projectId === initial.projectId &&
    current.date === initial.date &&
    current.startTime === initial.startTime &&
    current.duration === initial.duration &&
    (current.endTime ?? "") === (initial.endTime ?? "") &&
    (current.recordingPolicy ?? "Inherit") === (initial.recordingPolicy ?? "Inherit")
}
