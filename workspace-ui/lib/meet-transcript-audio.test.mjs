import assert from "node:assert/strict"
import fs from "node:fs"
import test from "node:test"
import {
  activeTranscriptSegmentIndex,
  resolveSegmentIntervals,
  seekTranscriptSegment,
  shouldAutoScrollTranscript,
  transcriptAudioUnavailableLabel,
  transcriptSpeakerLabel,
} from "./meet-transcript-audio.ts"

const segments = [
  { index: 0, startSeconds: 0, endSeconds: 4, speakerId: "a", speakerName: "Alex" },
  { index: 1, startSeconds: 5, endSeconds: null, speakerId: "b", speakerName: "Bea" },
  { index: 2, startSeconds: 9, endSeconds: 0, speakerId: "a", speakerName: "Alex" },
]

test("segment intervals prefer valid ends, then next start, then audio duration", () => {
  assert.deepEqual(resolveSegmentIntervals(segments, 14), [
    { index: 0, startSeconds: 0, endSeconds: 4 },
    { index: 1, startSeconds: 5, endSeconds: 9 },
    { index: 2, startSeconds: 9, endSeconds: 14 },
  ])
})

test("active segment follows playback across boundaries and leaves real gaps inactive", () => {
  assert.equal(activeTranscriptSegmentIndex(segments, 3.999, 14), 0)
  assert.equal(activeTranscriptSegmentIndex(segments, 4.5, 14), null)
  assert.equal(activeTranscriptSegmentIndex(segments, 5, 14), 1)
  assert.equal(activeTranscriptSegmentIndex(segments, 9, 14), 2)
  assert.equal(activeTranscriptSegmentIndex(segments, 14, 14), null)
})

test("timestamp activation seeks and starts playback", async () => {
  let played = 0
  const player = { currentTime: 0, play: async () => { played += 1 } }
  assert.equal(await seekTranscriptSegment(player, 9), true)
  assert.equal(player.currentTime, 9)
  assert.equal(played, 1)
  assert.equal(await seekTranscriptSegment(player, null), false)
})

test("Now speaking resolves edited names and the current user as You", () => {
  const speakers = [
    { speakerId: "a", displayName: "Alexandra", originalLabel: "Alex", isCurrentUser: false },
    { speakerId: "b", displayName: "Beatrice", originalLabel: "Bea", isCurrentUser: true },
  ]
  assert.equal(transcriptSpeakerLabel(segments[0], speakers), "Alexandra")
  assert.equal(transcriptSpeakerLabel(segments[1], speakers), "You")
})

test("auto-scroll follows boundary changes but respects off and recent manual scrolling", () => {
  const base = { isPlaying: true, activeSegmentChanged: true, nowMs: 10_000 }
  assert.equal(shouldAutoScrollTranscript({ ...base, enabled: true, lastManualScrollAtMs: null }), true)
  assert.equal(shouldAutoScrollTranscript({ ...base, enabled: false, lastManualScrollAtMs: null }), false)
  assert.equal(shouldAutoScrollTranscript({ ...base, enabled: true, lastManualScrollAtMs: 9_000 }), false)
  assert.equal(shouldAutoScrollTranscript({ ...base, enabled: true, lastManualScrollAtMs: 6_000 }), true)
  assert.equal(shouldAutoScrollTranscript({ ...base, enabled: true, isPlaying: false, lastManualScrollAtMs: null }), false)
})

test("missing or unsupported audio never gates transcript usability", () => {
  const source = fs.readFileSync(
    new URL("../components/meet-sources-review.tsx", import.meta.url),
    "utf8",
  )
  const unavailableState = source.indexOf('Audio unavailable')
  const transcriptContent = source.indexOf('<TranscriptContent', unavailableState)
  assert.ok(unavailableState >= 0)
  assert.ok(transcriptContent > unavailableState,
    "TranscriptContent must remain rendered after the optional unavailable-audio state.")
})

test("safe unavailable reason codes map to useful Review labels", () => {
  assert.deepEqual([
    "NoRecordingLinked",
    "MultipleRecordingsMatch",
    "RecordingMissing",
    "DifferentMeeting",
    "ManagedAudioUnavailable",
    "ManagedAudioFileMissing",
    "MixedTrackUnavailable",
    "UnsupportedAudioFormat",
  ].map(transcriptAudioUnavailableLabel), [
    "No recording linked",
    "Multiple recordings match",
    "Linked recording is missing",
    "Linked recording belongs to another MEET",
    "Managed audio is unavailable",
    "Managed audio file missing",
    "Mixed track unavailable",
    "Unsupported audio format",
  ])
  assert.equal(transcriptAudioUnavailableLabel("UnexpectedInternalValue"),
    "Audio could not be resolved")
  assert.equal(transcriptAudioUnavailableLabel("C:\\private\\audio.m4a"),
    "Audio could not be resolved")
})
