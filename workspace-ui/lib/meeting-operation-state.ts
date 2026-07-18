import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import type {
  MeetingOperationSnapshot,
  MeetingRecordingSnapshot,
  MeetingTranscriptSnapshot,
  WorkspaceCommandResult,
  WorkspaceMeetingAssistantCommand,
} from "@/lib/types"

type TrackedSender = (command: WorkspaceMeetingAssistantCommand) => Promise<WorkspaceCommandResult>

function commandTarget(command: WorkspaceMeetingAssistantCommand): {
  kind: "Transcription" | "Analysis"
  recordingId: string | null
  transcriptId: string | null
} | null {
  if (command.type === "transcribeMeetingRecording") {
    return { kind: "Transcription", recordingId: command.recordingId, transcriptId: null }
  }
  if (command.type === "analyzeMeetingRecording") {
    return { kind: "Analysis", recordingId: command.recordingId, transcriptId: null }
  }
  if (command.type === "analyzeMeetingTranscript") {
    return { kind: "Analysis", recordingId: null, transcriptId: command.transcriptId }
  }
  return null
}

export function operationsMatch(
  operation: MeetingOperationSnapshot,
  target: ReturnType<typeof commandTarget>,
): boolean {
  if (!target || operation.kind !== target.kind) return false
  return (target.transcriptId !== null && operation.transcriptId === target.transcriptId)
    || (target.recordingId !== null && operation.recordingId === target.recordingId)
}

export function mergeMeetingOperations(
  authoritative: MeetingOperationSnapshot[],
  optimistic: MeetingOperationSnapshot[],
): MeetingOperationSnapshot[] {
  return [
    ...authoritative,
    ...optimistic.filter((candidate) =>
      !authoritative.some((operation) => operationsMatch(operation, {
        kind: candidate.kind,
        recordingId: candidate.recordingId,
        transcriptId: candidate.transcriptId,
      }))),
  ]
}

export function shouldStartMeetingOperation(
  operations: MeetingOperationSnapshot[],
  command: WorkspaceMeetingAssistantCommand,
): boolean {
  const target = commandTarget(command)
  return target === null || !operations.some((operation) => operationsMatch(operation, target))
}

export function useMeetingOperationController(
  authoritative: MeetingOperationSnapshot[],
  recordings: MeetingRecordingSnapshot[],
  transcripts: MeetingTranscriptSnapshot[],
  sendTracked: TrackedSender,
  requestSnapshot: () => boolean,
) {
  const [optimistic, setOptimistic] = useState<MeetingOperationSnapshot[]>([])
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const operationsRef = useRef<MeetingOperationSnapshot[]>([])
  const operations = useMemo(
    () => mergeMeetingOperations(authoritative, optimistic),
    [authoritative, optimistic],
  )
  operationsRef.current = operations

  useEffect(() => {
    setOptimistic((current) => current.filter((candidate) =>
      !authoritative.some((operation) => operationsMatch(operation, {
        kind: candidate.kind,
        recordingId: candidate.recordingId,
        transcriptId: candidate.transcriptId,
      }))))
  }, [authoritative])

  const send = useCallback((command: WorkspaceMeetingAssistantCommand): boolean => {
    const target = commandTarget(command)
    if (!target) {
      void sendTracked(command).then((result) => {
        if (!result.success) {
          setError(result.errorMessage ?? "Meeting Assistant command failed.")
        }
        // Range-save success is confirmed locally beside the Save range button
        // (a reserved, fixed-size slot) — never as a document-flow banner that
        // shifts the whole Sources layout downward.
      })
      return true
    }
    if (!shouldStartMeetingOperation(operationsRef.current, command)) return false

    const recording = target.recordingId
      ? recordings.find((item) => item.id === target.recordingId)
      : undefined
    const transcript = target.transcriptId
      ? transcripts.find((item) => item.id === target.transcriptId)
      : undefined
    const operationId = crypto.randomUUID()
    const candidate: MeetingOperationSnapshot = {
      id: operationId,
      kind: target.kind,
      stage: target.kind === "Analysis" ? "StartingAnalysis" : "StartingTranscription",
      meetingId: recording?.meetingId ?? transcript?.meetingId ?? null,
      recordingId: target.recordingId ?? transcript?.recordingId ?? null,
      transcriptId: target.transcriptId ?? null,
      startedAtUtc: new Date().toISOString(),
      cancellationRequested: false,
    }
    setError(null)
    setNotice(null)
    operationsRef.current = [...operationsRef.current, candidate]
    setOptimistic((current) => [...current, candidate])

    void sendTracked(command).then((result) => {
      if (!result.success && result.errorMessage?.includes("already has an analysis operation")) {
        requestSnapshot()
        return
      }
      operationsRef.current = operationsRef.current.filter((item) => item.id !== operationId)
      setOptimistic((current) => current.filter((item) => item.id !== operationId))
      if (result.outcomeCode === "cancelled") {
        setNotice(result.outcomeMessage ?? "Operation cancelled.")
      } else if (!result.success) {
        setError(result.errorMessage ?? "Meeting Assistant operation failed.")
      }
    })
    return true
  }, [recordings, requestSnapshot, sendTracked, transcripts])

  return {
    operations,
    send,
    error,
    notice,
    clearError: () => setError(null),
    clearNotice: () => setNotice(null),
  }
}
