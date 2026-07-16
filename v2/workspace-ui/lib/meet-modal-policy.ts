export type MeetModalCloseReason = "explicit" | "escape" | "navigate" | "backdrop"

export function shouldCloseMeetModal(
  reason: MeetModalCloseReason,
  hasUnsavedChanges: boolean,
  confirmDiscard: () => boolean,
): boolean {
  if (reason === "backdrop") return false
  return !hasUnsavedChanges || confirmDiscard()
}
