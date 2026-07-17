import type { MeetingProposedActionSnapshot } from "./types"

/**
 * Human-readable labels for the production `MeetingProposedActionSnapshot`
 * types only — never invent a label for a type the backend cannot produce.
 */
const PROPOSED_ACTION_LABELS: Record<MeetingProposedActionSnapshot["type"], string> = {
  CreateTask: "Create task",
  CreateWaitingTask: "Create waiting task",
  CreateFollowUpTask: "Create follow-up task",
  AddMeetingContextNote: "Add to context",
}

export function proposedActionLabel(type: MeetingProposedActionSnapshot["type"]): string {
  return PROPOSED_ACTION_LABELS[type] ?? type
}
