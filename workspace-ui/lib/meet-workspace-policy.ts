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

/**
 * Analysis shown in Review for the active transcript. A freshly saved
 * user-edited revision has no analysis of its own yet, so the nearest ancestor
 * revision's analysis is shown instead and reported as stale through the same
 * revision-mismatch semantics the snapshot already uses — re-analysis stays an
 * explicit user action.
 */
export function selectReviewAnalysis<
  TTranscript extends {
    id: string
    revisionId: string
    sourceTranscriptId: string | null
  },
  TAnalysis extends {
    transcriptId: string
    transcriptRevisionId: string
    isStale: boolean
    updatedAtUtc: string
    state?: string
  },
>(
  activeTranscript: TTranscript | null,
  transcripts: readonly TTranscript[],
  analyses: readonly TAnalysis[],
): { analysis: TAnalysis | null; isStaleForActive: boolean } {
  if (!activeTranscript) return { analysis: null, isStaleForActive: false }

  const visited = new Set<string>()
  let current: TTranscript | null = activeTranscript
  while (current && !visited.has(current.id)) {
    visited.add(current.id)
    const analysis = selectLatestTranscriptAnalysis(current.id, analyses)
    if (analysis) {
      return {
        analysis,
        isStaleForActive:
          analysis.transcriptId !== activeTranscript.id ||
          analysis.transcriptRevisionId !== activeTranscript.revisionId,
      }
    }
    const sourceId: string | null = current.sourceTranscriptId
    current = sourceId
      ? transcripts.find((transcript) => transcript.id === sourceId) ?? null
      : null
  }

  return { analysis: null, isStaleForActive: false }
}

export function sortMeetingScreenshots<
  T extends { meetingId: string; capturedAtUtc: string },
>(meetingId: string, screenshots: readonly T[]): T[] {
  return screenshots
    .filter((screenshot) => screenshot.meetingId === meetingId)
    .sort((left, right) => left.capturedAtUtc.localeCompare(right.capturedAtUtc))
}
