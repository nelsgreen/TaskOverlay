import assert from "node:assert/strict"
import test from "node:test"
import {
  MEET_WORKSPACE_TABS,
  selectActiveMeetingTranscript,
  selectLatestTranscriptAnalysis,
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
