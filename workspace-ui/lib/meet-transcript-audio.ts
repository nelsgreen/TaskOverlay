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
