import type { MeetingRecordingSnapshot } from "@/lib/types"

export type MeetingRecordingControlMode =
  | "start"
  | "starting"
  | "stop"
  | "stopping"
  | "conflict"

export interface MeetingRecordingControlState {
  mode: MeetingRecordingControlMode
  ownedActiveRecording: MeetingRecordingSnapshot | null
}

export function deriveMeetingRecordingControlState(
  selectedMeetingId: string,
  activeRecording: MeetingRecordingSnapshot | null,
  pendingAction: "start" | "stop" | null,
): MeetingRecordingControlState {
  if (activeRecording) {
    if (activeRecording.meetingId !== selectedMeetingId) {
      return { mode: "conflict", ownedActiveRecording: null }
    }

    return {
      mode: pendingAction === "stop" ? "stopping" : "stop",
      ownedActiveRecording: activeRecording,
    }
  }

  return {
    mode: pendingAction === "start" ? "starting" : "start",
    ownedActiveRecording: null,
  }
}
