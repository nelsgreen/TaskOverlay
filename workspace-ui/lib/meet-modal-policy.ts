export type MeetModalCloseReason = "explicit" | "escape" | "navigate" | "backdrop"

export const MEET_MODAL_ACTIONS = {
  close: "Close",
  delete: "Delete meeting",
} as const

export function shouldCloseMeetModal(reason: MeetModalCloseReason): boolean {
  return reason !== "backdrop"
}

export async function closeMeetEditor(
  reason: MeetModalCloseReason,
  flushPendingEdits: () => Promise<boolean>,
  close: () => void,
): Promise<boolean> {
  if (!shouldCloseMeetModal(reason) || !await flushPendingEdits()) return false
  close()
  return true
}
