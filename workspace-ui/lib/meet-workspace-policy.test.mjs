import assert from "node:assert/strict"
import test from "node:test"
import {
  MEET_WORKSPACE_TABS,
  selectActiveMeetingTranscript,
  selectLatestTranscriptAnalysis,
  selectReviewAnalysis,
  shouldShowMeetDetailsActions,
  sortMeetingScreenshots,
} from "./meet-workspace-policy.ts"

test("MEET workspace keeps the bounded Details Sources Review structure", () => {
  assert.deepEqual(MEET_WORKSPACE_TABS, ["details", "sources", "review"])
  assert.equal(shouldShowMeetDetailsActions("details"), true)
  assert.equal(shouldShowMeetDetailsActions("sources"), false)
  assert.equal(shouldShowMeetDetailsActions("review"), false)
})

test("explicit active transcript wins over stale row flags", () => {
  const transcripts = [
    { id: "old", meetingId: "meet-a", isActive: true },
    { id: "current", meetingId: "meet-a", isActive: false },
    { id: "other", meetingId: "meet-b", isActive: true },
  ]
  assert.equal(
    selectActiveMeetingTranscript("meet-a", "current", transcripts)?.id,
    "current",
  )
  assert.equal(selectActiveMeetingTranscript("meet-c", undefined, transcripts), null)
})

test("review analysis remains scoped to the selected transcript revision history", () => {
  const analyses = [
    { id: "other", transcriptId: "transcript-b", updatedAtUtc: "2026-07-16T12:00:00Z" },
    { id: "older", transcriptId: "transcript-a", updatedAtUtc: "2026-07-16T10:00:00Z" },
    { id: "latest", transcriptId: "transcript-a", updatedAtUtc: "2026-07-16T11:00:00Z" },
  ]
  assert.equal(selectLatestTranscriptAnalysis("transcript-a", analyses)?.id, "latest")
  assert.equal(selectLatestTranscriptAnalysis(undefined, analyses), null)
})

test("failed re-analysis does not hide the previous successful result", () => {
  const analyses = [
    { id: "ready", transcriptId: "transcript-a", state: "ReadyForReview", updatedAtUtc: "2026-07-16T10:00:00Z" },
    { id: "failed", transcriptId: "transcript-a", state: "Failed", updatedAtUtc: "2026-07-16T11:00:00Z" },
  ]
  assert.equal(selectLatestTranscriptAnalysis("transcript-a", analyses)?.id, "ready")
})

test("MEET screenshot references are filtered and chronological", () => {
  const screenshots = [
    { id: "late", meetingId: "meet-a", capturedAtUtc: "2026-07-16T12:03:00Z" },
    { id: "other", meetingId: "meet-b", capturedAtUtc: "2026-07-16T12:00:00Z" },
    { id: "early", meetingId: "meet-a", capturedAtUtc: "2026-07-16T12:01:00Z" },
  ]
  assert.deepEqual(
    sortMeetingScreenshots("meet-a", screenshots).map((item) => item.id),
    ["early", "late"],
  )
})

test("review analysis falls back to the parent revision and reports it stale", () => {
  const parent = {
    id: "parent",
    revisionId: "rev-parent",
    sourceTranscriptId: null,
  }
  const edited = {
    id: "edited",
    revisionId: "rev-edited",
    sourceTranscriptId: "parent",
  }
  const analyses = [
    {
      id: "analysis-parent",
      transcriptId: "parent",
      transcriptRevisionId: "rev-parent",
      isStale: false,
      updatedAtUtc: "2026-07-16T10:00:00Z",
    },
  ]

  const direct = selectReviewAnalysis(parent, [parent, edited], analyses)
  assert.equal(direct.analysis?.id, "analysis-parent")
  assert.equal(direct.isStaleForActive, false)

  // A fresh user-edited revision has no analysis of its own yet: the parent's
  // analysis is shown, marked stale, and re-analysis stays an explicit action.
  const fallback = selectReviewAnalysis(edited, [parent, edited], analyses)
  assert.equal(fallback.analysis?.id, "analysis-parent")
  assert.equal(fallback.isStaleForActive, true)
})

test("review analysis fallback survives missing parents and cycles", () => {
  const orphan = {
    id: "orphan",
    revisionId: "rev-orphan",
    sourceTranscriptId: "deleted-parent",
  }
  assert.deepEqual(selectReviewAnalysis(orphan, [orphan], []), {
    analysis: null,
    isStaleForActive: false,
  })

  const loopA = { id: "a", revisionId: "rev-a", sourceTranscriptId: "b" }
  const loopB = { id: "b", revisionId: "rev-b", sourceTranscriptId: "a" }
  assert.deepEqual(selectReviewAnalysis(loopA, [loopA, loopB], []), {
    analysis: null,
    isStaleForActive: false,
  })

  assert.deepEqual(selectReviewAnalysis(null, [], []), {
    analysis: null,
    isStaleForActive: false,
  })
})

test("a new revision of the same transcript keeps the stale badge semantics", () => {
  const transcript = { id: "t", revisionId: "rev-2", sourceTranscriptId: null }
  const analyses = [
    {
      id: "analysis",
      transcriptId: "t",
      transcriptRevisionId: "rev-1",
      isStale: true,
      updatedAtUtc: "2026-07-16T10:00:00Z",
    },
  ]
  const result = selectReviewAnalysis(transcript, [transcript], analyses)
  assert.equal(result.analysis?.id, "analysis")
  assert.equal(result.isStaleForActive, true)
})
