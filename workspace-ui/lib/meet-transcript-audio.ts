export interface TimedTranscriptSegment {
  index: number
  startSeconds: number | null
  endSeconds: number | null
  speakerId?: string | null
  speakerName?: string | null
}

export interface TranscriptSpeakerIdentity {
  speakerId: string
  displayName: string
  originalLabel: string
  isCurrentUser: boolean
}

export interface TranscriptSegmentInterval {
  index: number
  startSeconds: number
  endSeconds: number
}

export interface SafeMediaFailureState {
  errorCode: number | null
  networkState: number
  readyState: number
  canPlayMp4: string
  canPlayAac: string
}

export type AudioEndpointProbeResult =
  | "Available"
  | "EndpointUnavailable"
  | "InvalidRangeResponse"
  | "ContentTypeMismatch"

export interface AudioEndpointProbeHeaders {
  status: number
  contentType: string | null
  contentLength: string | null
  contentRange: string | null
  acceptRanges: string | null
}

export interface NativeAudioPlaybackEvent {
  schemaVersion: 1
  messageType: "meetingAudioPlaybackEvent"
  recordingId: string
  transcriptId: string
  state: "Stopped" | "Playing" | "Paused" | "Failed"
  positionSeconds: number
  durationSeconds: number
  failureReason: string | null
}

export type TranscriptPlaybackMode = "Native" | "Browser" | "Unavailable"

export interface NativeTranscriptPlaybackProjection {
  positionSeconds: number
  durationSeconds: number
  isPlaying: boolean
  activeSegmentIndex: number | null
  failureReason: string | null
}

export function selectTranscriptPlaybackMode(input: {
  hasWebViewBridge: boolean
  audioStatus: string
  recordingId: string | null
  audioUrl: string | null
}): TranscriptPlaybackMode {
  if (input.audioStatus !== "Available") return "Unavailable"
  if (input.hasWebViewBridge) {
    return input.recordingId ? "Native" : "Unavailable"
  }
  return input.audioUrl ? "Browser" : "Unavailable"
}

export function projectNativeTranscriptPlayback(
  event: NativeAudioPlaybackEvent,
  segments: TimedTranscriptSegment[],
  snapshotDurationSeconds: number,
): NativeTranscriptPlaybackProjection {
  const positionSeconds = Math.max(0, event.positionSeconds)
  const durationSeconds = event.durationSeconds > 0
    ? event.durationSeconds
    : Math.max(0, snapshotDurationSeconds)
  return {
    positionSeconds,
    durationSeconds,
    isPlaying: event.state === "Playing",
    activeSegmentIndex: activeTranscriptSegmentIndex(
      segments,
      positionSeconds,
      durationSeconds,
    ),
    failureReason: event.state === "Failed"
      ? event.failureReason ?? "Native playback unavailable"
      : null,
  }
}

export function isNativeAudioPlaybackEvent(value: unknown): value is NativeAudioPlaybackEvent {
  if (!value || typeof value !== "object") return false
  const event = value as Partial<NativeAudioPlaybackEvent>
  return event.schemaVersion === 1 &&
    event.messageType === "meetingAudioPlaybackEvent" &&
    typeof event.recordingId === "string" &&
    typeof event.transcriptId === "string" &&
    ["Stopped", "Playing", "Paused", "Failed"].includes(event.state ?? "") &&
    typeof event.positionSeconds === "number" && Number.isFinite(event.positionSeconds) &&
    typeof event.durationSeconds === "number" && Number.isFinite(event.durationSeconds) &&
    (event.failureReason == null || event.failureReason === "Native playback unavailable")
}

export function postNativeAudioPlaybackCommand(input: {
  action: "play" | "pause" | "seek" | "stop"
  recordingId: string
  transcriptId: string
  positionSeconds?: number
}): boolean {
  const webview = window.chrome?.webview
  if (!webview) return false
  try {
    webview.postMessage({
      schemaVersion: 1,
      messageType: "meetingAudioPlaybackCommand",
      action: input.action,
      recordingId: input.recordingId,
      transcriptId: input.transcriptId,
      positionSeconds: Number.isFinite(input.positionSeconds) ? input.positionSeconds : 0,
    })
    return true
  } catch {
    return false
  }
}

export function captureSafeMediaFailureState(
  media: Pick<HTMLMediaElement, "error" | "networkState" | "readyState" | "canPlayType">,
): SafeMediaFailureState {
  return {
    errorCode: media.error?.code ?? null,
    networkState: media.networkState,
    readyState: media.readyState,
    canPlayMp4: media.canPlayType("audio/mp4"),
    canPlayAac: media.canPlayType('audio/mp4; codecs="mp4a.40.2"'),
  }
}

export function evaluateAudioEndpointProbe(
  headers: AudioEndpointProbeHeaders,
): AudioEndpointProbeResult {
  if (headers.status !== 206) return headers.status >= 400 || headers.status === 0
    ? "EndpointUnavailable"
    : "InvalidRangeResponse"
  if (!headers.contentType || ![
    "audio/mp4",
    "audio/wav",
    "audio/mpeg",
  ].includes(headers.contentType.split(";", 1)[0].trim().toLowerCase())) {
    return "ContentTypeMismatch"
  }
  const length = Number(headers.contentLength)
  const range = /^bytes 0-(\d+)\/(\d+)$/.exec(headers.contentRange ?? "")
  if (!Number.isInteger(length) || length < 1 || length > 16 ||
      !range || Number(range[1]) + 1 !== length || Number(range[2]) < length ||
      headers.acceptRanges?.trim().toLowerCase() !== "bytes") {
    return "InvalidRangeResponse"
  }
  return "Available"
}

export async function probeTranscriptAudioEndpoint(
  url: string,
  fetcher: typeof fetch = fetch,
): Promise<AudioEndpointProbeResult> {
  try {
    const response = await fetcher(url, {
      method: "GET",
      headers: { Range: "bytes=0-15" },
      cache: "no-store",
    })
    const result = evaluateAudioEndpointProbe({
      status: response.status,
      contentType: response.headers.get("Content-Type"),
      contentLength: response.headers.get("Content-Length"),
      contentRange: response.headers.get("Content-Range"),
      acceptRanges: response.headers.get("Accept-Ranges"),
    })
    try {
      await response.body?.cancel()
    } catch {
      // The response classification is already complete; cleanup is best effort.
    }
    return result
  } catch {
    return "EndpointUnavailable"
  }
}

export function mediaPlaybackFailureLabel(
  state: SafeMediaFailureState,
  probe: AudioEndpointProbeResult,
): string {
  if (probe === "EndpointUnavailable") return "Audio request failed"
  if (probe !== "Available") return "Invalid media response"
  switch (state.errorCode) {
    case 1: return "Playback was aborted"
    case 2: return "Audio request failed"
    case 3: return "Audio could not be decoded"
    case 4:
      return state.canPlayMp4 === "" && state.canPlayAac === ""
        ? "AAC playback is not supported by this WebView2 runtime"
        : "Audio source or codec is not supported"
    default: return "Unknown playback failure"
  }
}

const unavailableAudioLabels: Record<string, string> = {
  NoRecordingLinked: "No recording linked",
  MultipleRecordingsMatch: "Multiple recordings match",
  RecordingMissing: "Linked recording is missing",
  DifferentMeeting: "Linked recording belongs to another MEET",
  ManagedAudioUnavailable: "Managed audio is unavailable",
  ManagedAudioFileMissing: "Managed audio file missing",
  MixedTrackUnavailable: "Mixed track unavailable",
  UnsupportedAudioFormat: "Unsupported audio format",
}

export function transcriptAudioUnavailableLabel(reason: string | null | undefined): string {
  return reason ? unavailableAudioLabels[reason] ?? "Audio could not be resolved" :
    "Audio could not be resolved"
}

export function resolveSegmentIntervals(
  segments: TimedTranscriptSegment[],
  audioDurationSeconds: number,
): TranscriptSegmentInterval[] {
  const timed = segments
    .filter((segment): segment is TimedTranscriptSegment & { startSeconds: number } =>
      segment.startSeconds != null && Number.isFinite(segment.startSeconds) && segment.startSeconds >= 0)
    .sort((left, right) => left.startSeconds - right.startSeconds || left.index - right.index)

  return timed.map((segment, position) => {
    const explicitEnd = segment.endSeconds
    const nextStart = timed[position + 1]?.startSeconds
    const fallbackEnd = nextStart ?? audioDurationSeconds
    const endSeconds = explicitEnd != null && Number.isFinite(explicitEnd) &&
      explicitEnd > segment.startSeconds
      ? explicitEnd
      : Math.max(segment.startSeconds, fallbackEnd)
    return { index: segment.index, startSeconds: segment.startSeconds, endSeconds }
  })
}

export function activeTranscriptSegmentIndex(
  segments: TimedTranscriptSegment[],
  currentTimeSeconds: number,
  audioDurationSeconds: number,
): number | null {
  if (!Number.isFinite(currentTimeSeconds) || currentTimeSeconds < 0) return null
  const intervals = resolveSegmentIntervals(segments, audioDurationSeconds)
  for (let position = intervals.length - 1; position >= 0; position -= 1) {
    const interval = intervals[position]
    if (currentTimeSeconds >= interval.startSeconds && currentTimeSeconds < interval.endSeconds) {
      return interval.index
    }
  }
  return null
}

export async function seekTranscriptSegment(
  player: Pick<HTMLMediaElement, "currentTime" | "play">,
  startSeconds: number | null,
): Promise<boolean> {
  if (startSeconds == null || !Number.isFinite(startSeconds) || startSeconds < 0) return false
  player.currentTime = startSeconds
  await player.play()
  return true
}

export function transcriptSpeakerLabel(
  segment: TimedTranscriptSegment | undefined,
  speakers: TranscriptSpeakerIdentity[],
): string | null {
  if (!segment) return null
  const speaker = segment.speakerId
    ? speakers.find((candidate) => candidate.speakerId === segment.speakerId)
    : undefined
  if (speaker?.isCurrentUser) return "You"
  return speaker?.displayName.trim() || speaker?.originalLabel.trim() ||
    segment.speakerName?.trim() || null
}

export function shouldAutoScrollTranscript(input: {
  enabled: boolean
  isPlaying: boolean
  activeSegmentChanged: boolean
  nowMs: number
  lastManualScrollAtMs: number | null
  manualScrollGraceMs?: number
}): boolean {
  if (!input.enabled || !input.isPlaying || !input.activeSegmentChanged) return false
  if (input.lastManualScrollAtMs == null) return true
  return input.nowMs - input.lastManualScrollAtMs >= (input.manualScrollGraceMs ?? 3000)
}
