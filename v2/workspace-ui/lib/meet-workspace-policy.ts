export type MeetWorkspaceTab = "details" | "sources" | "review"

export const MEET_WORKSPACE_TABS: readonly MeetWorkspaceTab[] = [
  "details",
  "sources",
  "review",
]

export function shouldShowMeetDetailsActions(tab: MeetWorkspaceTab): boolean {
  return tab === "details"
}

export function selectActiveMeetingTranscript<
  T extends { id: string; meetingId: string; isActive: boolean },
>(
  meetingId: string,
  activeTranscriptId: string | undefined,
  transcripts: readonly T[],
): T | null {
  const meetingTranscripts = transcripts.filter((transcript) => transcript.meetingId === meetingId)
  return (
    (activeTranscriptId
      ? meetingTranscripts.find((transcript) => transcript.id === activeTranscriptId)
      : undefined) ??
    meetingTranscripts.find((transcript) => transcript.isActive) ??
    null
  )
}

export function selectLatestTranscriptAnalysis<
  T extends { transcriptId: string; updatedAtUtc: string; state?: string },
>(transcriptId: string | undefined, analyses: readonly T[]): T | null {
  if (!transcriptId) return null
  return (
    analyses
      .filter((analysis) => analysis.transcriptId === transcriptId)
      .sort((left, right) => {
        const failureOrder = Number(left.state === "Failed") - Number(right.state === "Failed")
        return failureOrder || right.updatedAtUtc.localeCompare(left.updatedAtUtc)
      })[0] ?? null
  )
}

export function sortMeetingScreenshots<
  T extends { meetingId: string; capturedAtUtc: string },
>(meetingId: string, screenshots: readonly T[]): T[] {
  return screenshots
    .filter((screenshot) => screenshot.meetingId === meetingId)
    .sort((left, right) => left.capturedAtUtc.localeCompare(right.capturedAtUtc))
}
