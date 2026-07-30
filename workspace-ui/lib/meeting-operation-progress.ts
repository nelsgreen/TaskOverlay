import type { MeetingOperationSnapshot } from "./types"

/**
 * Single source of the operation-progress label shown for the authoritative
 * runtime operation. Long transcriptions add multi-part stages, which are
 * rendered through this same infrastructure rather than a second progress
 * system.
 */
export function describeMeetingOperationStage(
  operation: Pick<MeetingOperationSnapshot, "stage" | "stageIndex" | "stageTotal">,
): string {
  const index = operation.stageIndex ?? 0
  const total = operation.stageTotal ?? 0

  switch (operation.stage) {
    case "StartingTranscription":
      return "Starting transcription..."
    case "PreparingAudio":
      return "Preparing audio..."
    case "PreparingParts":
      // Falls back to the single-request wording when the part count is
      // unknown, so an older snapshot never renders "Preparing 0 parts".
      return total > 0 ? `Preparing ${total} parts` : "Preparing audio..."
    case "Transcribing":
      return "Transcribing..."
    case "TranscribingPart":
      return index > 0 && total > 0
        ? `Transcribing ${index} of ${total}`
        : "Transcribing..."
    case "MergingTranscript":
      return "Merging transcript"
    case "StartingAnalysis":
      return "Starting analysis..."
    case "Cancelling":
      return "Cancelling..."
    default:
      return "Analyzing transcript..."
  }
}

/**
 * Compact label for surfaces that only distinguish transcription from
 * analysis, while still naming the current part of a long transcription.
 */
export function describeMeetingOperationShortStage(
  operation: Pick<MeetingOperationSnapshot, "kind" | "stage" | "stageIndex" | "stageTotal">,
): string {
  if (operation.kind === "Analysis") {
    return operation.stage === "StartingAnalysis" ? "Starting analysis..." : "Analyzing..."
  }

  return describeMeetingOperationStage(operation)
}

/**
 * Cancellation must read as a neutral outcome, never as a provider failure.
 */
export function isMeetingOperationCancelling(
  operation: Pick<MeetingOperationSnapshot, "stage" | "cancellationRequested">,
): boolean {
  return operation.stage === "Cancelling" || operation.cancellationRequested
}
