import assert from "node:assert/strict"
import fs from "node:fs"
import test from "node:test"
import {
  activeTranscriptSegmentIndex,
  captureSafeMediaFailureState,
  evaluateAudioEndpointProbe,
  mediaPlaybackFailureLabel,
  isNativeAudioPlaybackEvent,
  postNativeAudioPlaybackCommand,
  probeTranscriptAudioEndpoint,
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

test("HTML media error codes map to safe specific playback failures", () => {
  const base = {
    networkState: 3,
    readyState: 0,
    canPlayMp4: "probably",
    canPlayAac: "probably",
  }
  assert.equal(mediaPlaybackFailureLabel({ ...base, errorCode: 1 }, "Available"),
    "Playback was aborted")
  assert.equal(mediaPlaybackFailureLabel({ ...base, errorCode: 2 }, "Available"),
    "Audio request failed")
  assert.equal(mediaPlaybackFailureLabel({ ...base, errorCode: 3 }, "Available"),
    "Audio could not be decoded")
  assert.equal(mediaPlaybackFailureLabel({
    ...base, errorCode: 4, canPlayMp4: "", canPlayAac: "",
  }, "Available"), "AAC playback is not supported by this WebView2 runtime")
  assert.equal(mediaPlaybackFailureLabel({ ...base, errorCode: 4 }, "Available"),
    "Audio source or codec is not supported")
  assert.equal(mediaPlaybackFailureLabel({ ...base, errorCode: null }, "Available"),
    "Unknown playback failure")
})

test("media failure capture retains only bounded browser capability state", () => {
  const calls = []
  const state = captureSafeMediaFailureState({
    error: { code: 3 },
    networkState: 2,
    readyState: 1,
    canPlayType: (value) => { calls.push(value); return value.includes("codecs") ? "maybe" : "probably" },
  })
  assert.deepEqual(state, {
    errorCode: 3,
    networkState: 2,
    readyState: 1,
    canPlayMp4: "probably",
    canPlayAac: "maybe",
  })
  assert.deepEqual(calls, ["audio/mp4", 'audio/mp4; codecs="mp4a.40.2"'])
  assert.ok(!JSON.stringify(state).includes("path"))
  assert.ok(!JSON.stringify(state).includes("url"))
})

test("endpoint probe validates only the bounded range response headers", async () => {
  const available = {
    status: 206,
    contentType: "audio/mp4",
    contentLength: "16",
    contentRange: "bytes 0-15/2048",
    acceptRanges: "bytes",
  }
  assert.equal(evaluateAudioEndpointProbe(available), "Available")
  assert.equal(evaluateAudioEndpointProbe({ ...available, status: 404 }),
    "EndpointUnavailable")
  assert.equal(evaluateAudioEndpointProbe({ ...available, status: 200 }),
    "InvalidRangeResponse")
  assert.equal(evaluateAudioEndpointProbe({ ...available, contentType: "text/html" }),
    "ContentTypeMismatch")
  assert.equal(evaluateAudioEndpointProbe({ ...available, contentLength: "17" }),
    "InvalidRangeResponse")
  assert.equal(mediaPlaybackFailureLabel({
    errorCode: 3,
    networkState: 3,
    readyState: 0,
    canPlayMp4: "probably",
    canPlayAac: "probably",
  }, "InvalidRangeResponse"), "Invalid media response")

  let request
  const result = await probeTranscriptAudioEndpoint("https://taskoverlay.workspace/__meeting-audio/id", async (
    url, init,
  ) => {
    request = { url, init }
    return new Response(new Uint8Array(16), {
      status: 206,
      headers: {
        "Content-Type": "audio/mp4",
        "Content-Length": "16",
        "Content-Range": "bytes 0-15/2048",
        "Accept-Ranges": "bytes",
      },
    })
  })
  assert.equal(result, "Available")
  assert.equal(request.init.method, "GET")
  assert.equal(request.init.headers.Range, "bytes=0-15")
})

test("native fallback commands and events carry only IDs, seconds, and safe state", () => {
  const messages = []
  const previousWindow = globalThis.window
  globalThis.window = {
    chrome: { webview: { postMessage: (message) => messages.push(message) } },
  }
  try {
    assert.equal(postNativeAudioPlaybackCommand({
      action: "play",
      recordingId: "a".repeat(32),
      transcriptId: "b".repeat(32),
      positionSeconds: 12.5,
    }), true)
  } finally {
    globalThis.window = previousWindow
  }
  assert.deepEqual(messages, [{
    schemaVersion: 1,
    messageType: "meetingAudioPlaybackCommand",
    action: "play",
    recordingId: "a".repeat(32),
    transcriptId: "b".repeat(32),
    positionSeconds: 12.5,
  }])
  const safeEvent = {
    schemaVersion: 1,
    messageType: "meetingAudioPlaybackEvent",
    recordingId: "a".repeat(32),
    transcriptId: "b".repeat(32),
    state: "Playing",
    positionSeconds: 12.5,
    durationSeconds: 30,
    failureReason: null,
  }
  assert.equal(isNativeAudioPlaybackEvent(safeEvent), true)
  assert.equal(isNativeAudioPlaybackEvent({
    ...safeEvent,
    state: "Failed",
    failureReason: "C:\\private\\audio.m4a",
  }), false)
})
