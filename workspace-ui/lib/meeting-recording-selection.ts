export function resolveMeetingRecordingSelection(
  selectedRecordingId: string | null,
  previousLatestRecordingId: string | null,
  latestRecordingId: string | null,
  availableRecordingIds: readonly string[],
): string | null {
  if (!latestRecordingId) return null

  const selectionStillExists = selectedRecordingId !== null
    && availableRecordingIds.includes(selectedRecordingId)
  if (!selectionStillExists || latestRecordingId !== previousLatestRecordingId) {
    return latestRecordingId
  }

  return selectedRecordingId
}
