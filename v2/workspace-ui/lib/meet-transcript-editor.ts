import type {
  MeetingTranscriptSnapshot,
  WorkspaceMeetingAssistantCommand,
} from "@/lib/types"

/**
 * Ephemeral transcript-edit draft. Lives only in React state: nothing here is
 * persisted per keystroke, written to localStorage, or applied to the active
 * transcript. One deliberate `saveMeetingTranscriptRevision` command turns an
 * accepted draft into a new immutable revision on the C# side.
 */
export interface TranscriptDraftSegment {
  index: number
  startSeconds: number | null
  speakerId: string | null
  text: string
  originalText: string
}

export interface TranscriptDraftSpeaker {
  speakerId: string
  originalLabel: string
  displayName: string
  isCurrentUser: boolean
  originalDisplayName: string
  originalIsCurrentUser: boolean
}

export interface TranscriptDraft {
  meetingId: string
  transcriptId: string
  parentRevisionId: string
  segments: TranscriptDraftSegment[]
  speakers: TranscriptDraftSpeaker[]
  /** fromSpeakerId -> intoSpeakerId, always resolved to final (unmerged) targets. */
  merges: Record<string, string>
}

export function createTranscriptDraft(
  transcript: Pick<
    MeetingTranscriptSnapshot,
    "id" | "meetingId" | "revisionId" | "segments" | "speakers"
  >,
): TranscriptDraft {
  return {
    meetingId: transcript.meetingId,
    transcriptId: transcript.id,
    parentRevisionId: transcript.revisionId,
    segments: transcript.segments.map((segment) => ({
      index: segment.index,
      startSeconds: segment.startSeconds,
      speakerId: segment.speakerId,
      text: segment.text,
      originalText: segment.text,
    })),
    speakers: transcript.speakers.map((speaker) => ({
      speakerId: speaker.speakerId,
      originalLabel: speaker.originalLabel,
      displayName: speaker.displayName,
      isCurrentUser: speaker.isCurrentUser,
      originalDisplayName: speaker.displayName,
      originalIsCurrentUser: speaker.isCurrentUser,
    })),
    merges: {},
  }
}

export function setDraftSegmentText(
  draft: TranscriptDraft,
  index: number,
  text: string,
): TranscriptDraft {
  return {
    ...draft,
    segments: draft.segments.map((segment) =>
      segment.index === index ? { ...segment, text } : segment),
  }
}

export function renameDraftSpeaker(
  draft: TranscriptDraft,
  speakerId: string,
  displayName: string,
): TranscriptDraft {
  return {
    ...draft,
    speakers: draft.speakers.map((speaker) =>
      speaker.speakerId === speakerId ? { ...speaker, displayName } : speaker),
  }
}

/** Marks the speaker as You; marking again clears it. At most one You marker survives. */
export function toggleDraftSpeakerAsYou(
  draft: TranscriptDraft,
  speakerId: string,
): TranscriptDraft {
  const target = draft.speakers.find((speaker) => speaker.speakerId === speakerId)
  const nextValue = !(target?.isCurrentUser ?? false)
  return {
    ...draft,
    speakers: draft.speakers.map((speaker) => ({
      ...speaker,
      isCurrentUser: speaker.speakerId === speakerId ? nextValue : false,
    })),
  }
}

export function isSpeakerMergedAway(draft: TranscriptDraft, speakerId: string): boolean {
  return speakerId in draft.merges
}

/** Speakers that survive the pending merges, in original order. */
export function visibleDraftSpeakers(draft: TranscriptDraft): TranscriptDraftSpeaker[] {
  return draft.speakers.filter((speaker) => !isSpeakerMergedAway(draft, speaker.speakerId))
}

export function effectiveDraftSpeakerId(
  draft: TranscriptDraft,
  speakerId: string | null,
): string | null {
  return speakerId === null ? null : draft.merges[speakerId] ?? speakerId
}

/** Display name a segment renders with, after pending merges and renames. */
export function draftSegmentSpeakerName(
  draft: TranscriptDraft,
  segment: TranscriptDraftSegment,
): string | null {
  const effectiveId = effectiveDraftSpeakerId(draft, segment.speakerId)
  if (effectiveId === null) return null
  const speaker = draft.speakers.find((candidate) => candidate.speakerId === effectiveId)
  if (!speaker) return null
  return speaker.displayName.trim() || speaker.originalLabel
}

/**
 * Merges one speaker into another inside the draft. Existing merges pointing at
 * the merged speaker are retargeted so every stored merge stays resolved to a
 * final target (no chains, no cycles, no dangling references).
 */
export function mergeDraftSpeaker(
  draft: TranscriptDraft,
  fromSpeakerId: string,
  intoSpeakerId: string,
): TranscriptDraft {
  const exists = (id: string) =>
    draft.speakers.some((speaker) => speaker.speakerId === id)
  if (
    fromSpeakerId === intoSpeakerId ||
    !exists(fromSpeakerId) ||
    !exists(intoSpeakerId) ||
    isSpeakerMergedAway(draft, fromSpeakerId) ||
    isSpeakerMergedAway(draft, intoSpeakerId)
  ) {
    return draft
  }

  const merges: Record<string, string> = {}
  for (const [from, into] of Object.entries(draft.merges)) {
    merges[from] = into === fromSpeakerId ? intoSpeakerId : into
  }
  merges[fromSpeakerId] = intoSpeakerId
  return { ...draft, merges }
}

export function undoDraftSpeakerMerge(
  draft: TranscriptDraft,
  fromSpeakerId: string,
): TranscriptDraft {
  if (!isSpeakerMergedAway(draft, fromSpeakerId)) return draft
  const merges = { ...draft.merges }
  delete merges[fromSpeakerId]
  return { ...draft, merges }
}

export function isDraftDirty(draft: TranscriptDraft): boolean {
  return (
    Object.keys(draft.merges).length > 0 ||
    draft.segments.some((segment) => segment.text !== segment.originalText) ||
    draft.speakers.some((speaker) =>
      speaker.displayName !== speaker.originalDisplayName ||
      speaker.isCurrentUser !== speaker.originalIsCurrentUser)
  )
}

/** Returns a human-readable validation error, or null when the draft can be saved. */
export function validateTranscriptDraft(draft: TranscriptDraft): string | null {
  if (draft.segments.length === 0) {
    return "This transcript has no editable segments."
  }
  if (draft.segments.some((segment) => segment.text.trim().length === 0)) {
    return "Segment text cannot be empty."
  }
  if (visibleDraftSpeakers(draft).some((speaker) =>
    speaker.displayName.trim().length === 0 && speaker.originalLabel.trim().length === 0)) {
    return "Speaker names cannot be empty."
  }
  return null
}

/** Builds the single connected save command for an already-validated draft. */
export function buildSaveRevisionCommand(
  draft: TranscriptDraft,
): Extract<WorkspaceMeetingAssistantCommand, { type: "saveMeetingTranscriptRevision" }> {
  return {
    type: "saveMeetingTranscriptRevision",
    meetingId: draft.meetingId,
    transcriptId: draft.transcriptId,
    parentRevisionId: draft.parentRevisionId,
    segmentEdits: draft.segments
      .filter((segment) => segment.text !== segment.originalText)
      .map((segment) => ({ index: segment.index, text: segment.text.trim() })),
    speakers: visibleDraftSpeakers(draft).map((speaker) => ({
      speakerId: speaker.speakerId,
      displayName: speaker.displayName.trim() || speaker.originalLabel,
      isCurrentUser: speaker.isCurrentUser,
    })),
    merges: Object.entries(draft.merges).map(([fromSpeakerId, intoSpeakerId]) => ({
      fromSpeakerId,
      intoSpeakerId,
    })),
  }
}

/**
 * Editor keyboard policy: Ctrl+Enter saves, Escape requests cancellation, and
 * nothing fires while a confirmation dialog is open. Plain Enter is left to the
 * focused control (newline inside segment textareas).
 */
export function transcriptEditorKeyAction(
  key: { key: string; ctrlKey: boolean; metaKey: boolean },
  state: { confirmOpen: boolean },
): "save" | "cancel" | null {
  if (state.confirmOpen) return null
  if (key.key === "Enter" && (key.ctrlKey || key.metaKey)) return "save"
  if (key.key === "Escape") return "cancel"
  return null
}

/** User-facing revision label — never a raw enum name. */
export function transcriptOriginLabel(
  origin: MeetingTranscriptSnapshot["origin"],
): string {
  switch (origin) {
    case "Imported":
      return "Imported"
    case "UserEdited":
      return "Edited"
    default:
      return "Generated"
  }
}
