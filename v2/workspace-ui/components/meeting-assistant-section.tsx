"use client"

import { useEffect, useMemo, useRef, useState } from "react"
import {
  Bot,
  Check,
  CircleStop,
  ExternalLink,
  FileAudio,
  FolderOpen,
  Info,
  Mic,
  Play,
  Sparkles,
  Trash2,
  Video,
  Volume2,
  X,
  type LucideIcon,
} from "lucide-react"
import type {
  MeetItem,
  MeetingAnalysisSnapshot,
  MeetingOperationSnapshot,
  MeetingProposedActionOverride,
  MeetingProposedActionSnapshot,
  MeetingRecordingPolicy,
  MeetingRecordingSnapshot,
  Project,
  Status,
  WorkspaceMeetingAssistantCommand,
} from "@/lib/types"
import { cn } from "@/lib/utils"
import { deriveMeetingRecordingControlState } from "@/lib/meeting-recording-controls"
import { resolveMeetingRecordingSelection } from "@/lib/meeting-recording-selection"
import { isValidMeetingLinkUrl } from "@/lib/meeting-link"
import { proposedActionLabel } from "@/lib/meeting-proposed-action"

interface Props {
  meet: MeetItem
  projects: Project[]
  recordings: MeetingRecordingSnapshot[]
  analyses: MeetingAnalysisSnapshot[]
  operations: MeetingOperationSnapshot[]
  unclassifiedRecordings: MeetingRecordingSnapshot[]
  activeRecording: MeetingRecordingSnapshot | null
  activeRecordingOwnerTitle?: string
  readOnly: boolean
  commandError?: string | null
  commandNotice?: string | null
  onClearError?: () => void
  onClearNotice?: () => void
  onCommand: (command: WorkspaceMeetingAssistantCommand) => boolean
  defaultRecordingPolicy?: Exclude<MeetingRecordingPolicy, "Inherit">
  onRecordingPolicyChange?: (policy: MeetingRecordingPolicy) => void
  onBeforeRecordingStart?: () => Promise<boolean>
  showAnalysis?: boolean
  showTranscript?: boolean
}

const statusOptions: Status[] = ["TODO", "FOCUS", "WAIT", "DONE"]
const policyOptions: { value: MeetingRecordingPolicy; label: string }[] = [
  { value: "Inherit", label: "Use app default" },
  { value: "Manual", label: "Manual" },
  { value: "AutoRecord", label: "Auto-record" },
]

export function MeetingAssistantSection({
  meet,
  projects,
  recordings,
  analyses,
  operations,
  unclassifiedRecordings,
  activeRecording,
  activeRecordingOwnerTitle,
  readOnly,
  commandError,
  commandNotice,
  onClearError,
  onClearNotice,
  onCommand,
  defaultRecordingPolicy = "Manual",
  onRecordingPolicyChange,
  onBeforeRecordingStart,
  showAnalysis = true,
  showTranscript = true,
}: Props) {
  const meetingRecordings = useMemo(
    () => recordings
      .filter((recording) => recording.meetingId === meet.id)
      .sort((left, right) => right.createdAtUtc.localeCompare(left.createdAtUtc)),
    [recordings, meet.id],
  )
  const [selectedRecordingId, setSelectedRecordingId] = useState<string | null>(null)
  const previousLatestRecordingId = useRef<string | null>(null)
  const [pendingRecordingAction, setPendingRecordingAction] = useState<"start" | "stop" | null>(null)
  const [runtimeClock, setRuntimeClock] = useState(() => Date.now())
  const selectedRecording = meetingRecordings.find((recording) => recording.id === selectedRecordingId)
    ?? meetingRecordings[0]
    ?? null
  const selectedAnalysis = analyses
    .filter((analysis) => analysis.recordingId === selectedRecording?.id)
    .sort((left, right) => right.updatedAtUtc.localeCompare(left.updatedAtUtc))[0]
    ?? null
  const selectedOperation = operations.find((operation) =>
    operation.recordingId === selectedRecording?.id)

  const latestRecordingId = meetingRecordings[0]?.id ?? null
  const availableRecordingIds = useMemo(
    () => meetingRecordings.map((recording) => recording.id),
    [meetingRecordings],
  )

  useEffect(() => {
    const nextSelection = resolveMeetingRecordingSelection(
      selectedRecordingId,
      previousLatestRecordingId.current,
      latestRecordingId,
      availableRecordingIds,
    )
    previousLatestRecordingId.current = latestRecordingId
    if (nextSelection !== selectedRecordingId) {
      setSelectedRecordingId(nextSelection)
    }
  }, [availableRecordingIds, latestRecordingId, selectedRecordingId])

  useEffect(() => {
    setPendingRecordingAction(null)
  }, [meet.id])

  useEffect(() => {
    if (commandError ||
        (pendingRecordingAction === "start" && activeRecording) ||
        (pendingRecordingAction === "stop" && !activeRecording)) {
      setPendingRecordingAction(null)
    }
  }, [activeRecording?.id, commandError, pendingRecordingAction])

  useEffect(() => {
    if (!activeRecording) return

    setRuntimeClock(Date.now())
    const timer = window.setInterval(() => setRuntimeClock(Date.now()), 1_000)
    return () => window.clearInterval(timer)
  }, [activeRecording?.id])

  const send = (command: WorkspaceMeetingAssistantCommand) => {
    onClearError?.()
    return onCommand(command)
  }
  const startRecording = async () => {
    if (onBeforeRecordingStart && !await onBeforeRecordingStart()) return
    if (send({ type: "startMeetingRecording", meetingId: meet.id })) {
      setPendingRecordingAction("start")
    }
  }

  const recordingControls = deriveMeetingRecordingControlState(
    meet.id,
    activeRecording,
    pendingRecordingAction,
  )
  const isProcessing = Boolean(selectedOperation) || selectedRecording?.state === "Processing"
    || selectedRecording?.state === "Transcribing"
    || selectedRecording?.state === "Analyzing"

  return (
    <section className="space-y-3 rounded-lg border border-border bg-card/40 p-3">
      <div className="flex min-w-0 items-center gap-2">
        <FileAudio className="size-4 shrink-0 text-status-meet" />
        <div className="min-w-0 flex-1">
          <h3 className="text-[11px] font-bold uppercase tracking-widest text-foreground">
            Recording &amp; Meeting Assistant
          </h3>
          <p className="mt-0.5 text-[10px] text-muted-foreground">
            Local two-track capture. AI suggestions require explicit review.
          </p>
        </div>
      </div>

      {commandError && (
        <div className="flex items-start gap-2 rounded-md border border-destructive/30 bg-destructive/10 p-2 text-[11px] text-destructive">
          <span className="min-w-0 flex-1">{commandError}</span>
          <button type="button" onClick={onClearError} aria-label="Dismiss error">
            <X className="size-3.5" />
          </button>
        </div>
      )}

      {commandNotice && (
        <div className="flex items-start gap-2 rounded-md border border-status-meet/30 bg-status-meet/10 p-2 text-[11px] text-foreground" role="status">
          <Info className="mt-0.5 size-3.5 shrink-0 text-status-meet" aria-hidden="true" />
          <span className="min-w-0 flex-1">{commandNotice}</span>
          {onClearNotice && (
            <button type="button" onClick={onClearNotice} aria-label="Dismiss notice" className="text-muted-foreground hover:text-foreground">
              <X className="size-3.5" />
            </button>
          )}
        </div>
      )}

      <div>
        <div className="mb-1.5 flex flex-wrap items-baseline gap-x-1.5">
          <span className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
            Recording policy
          </span>
          {(meet.recordingPolicy ?? "Inherit") === "Inherit" && (
            <span className="text-[10px] text-muted-foreground">
              · Effective: {defaultRecordingPolicy === "AutoRecord" ? "Auto-record" : "Manual"}
            </span>
          )}
        </div>
        <div className="flex flex-wrap gap-1.5">
          {policyOptions.map((option) => (
            <button
              type="button"
              key={option.value}
              disabled={readOnly || !onRecordingPolicyChange}
              onClick={() => onRecordingPolicyChange?.(option.value)}
              className={cn(
                "rounded-md border px-2 py-1 text-[10px] font-medium",
                (meet.recordingPolicy ?? "Inherit") === option.value
                  ? "border-status-meet/50 bg-status-meet/10 text-status-meet"
                  : "border-border text-muted-foreground hover:bg-accent",
              )}
            >
              {option.label}
            </button>
          ))}
        </div>
      </div>

      <div className="flex flex-wrap gap-1.5">
        {isValidMeetingLinkUrl(meet.link) && (
          <ActionButton
            label="Join call"
            icon={ExternalLink}
            onClick={() => send({ type: "openMeetingLink", meetingId: meet.id })}
          />
        )}
        {recordingControls.mode === "start" && (
          <ActionButton
            label="Start recording"
            icon={Play}
            primary
            disabled={readOnly}
            onClick={() => void startRecording()}
          />
        )}
        {recordingControls.mode === "starting" && (
          <ActionButton
            label="Starting recording..."
            icon={Play}
            primary
            disabled
            onClick={() => undefined}
          />
        )}
        {recordingControls.mode === "stop" && recordingControls.ownedActiveRecording && (
          <ActionButton
            label="Stop recording"
            icon={CircleStop}
            danger
            disabled={readOnly}
            onClick={() => {
              if (send({
                type: "stopMeetingRecording",
                recordingId: recordingControls.ownedActiveRecording!.id,
              })) {
                setPendingRecordingAction("stop")
              }
            }}
          />
        )}
        {recordingControls.mode === "stopping" && (
          <ActionButton
            label="Finalizing recording..."
            icon={CircleStop}
            danger
            disabled
            onClick={() => undefined}
          />
        )}
        {recordingControls.mode === "conflict" && (
          <ActionButton
            label="Another recording is active"
            icon={CircleStop}
            disabled
            onClick={() => undefined}
          />
        )}
      </div>

      {activeRecording && activeRecording.meetingId !== meet.id && (
        <p className="rounded-md border border-amber-500/30 bg-amber-500/10 p-2 text-[10px] text-amber-300">
          {activeRecording.meetingId
            ? `Recording is active for ${activeRecordingOwnerTitle ?? "another MEET"}.`
            : "An emergency recording is active."}
        </p>
      )}

      {activeRecording?.meetingId === meet.id && (
        <div className="grid grid-cols-[auto_1fr] items-center gap-x-3 gap-y-1 rounded-md border border-red-500/30 bg-red-500/10 p-2 text-[10px]">
          <span className="flex items-center gap-1.5 font-bold text-red-300">
            <span className="size-2 animate-pulse rounded-full bg-red-500" />
            REC {formatElapsed(activeRecording.startedAtUtc, runtimeClock)}
          </span>
          <span className="text-right text-muted-foreground">
            {formatRecordingFormat(activeRecording.recordingFormat)}
            {" - "}System {formatTrackHealth(activeRecording.systemAudioHealth)}
            {" · "}
            Mic {formatTrackHealth(activeRecording.microphoneHealth)}
          </span>
        </div>
      )}

      {meetingRecordings.length === 0 ? (
        <p className="rounded-md border border-dashed border-border p-3 text-center text-[11px] text-muted-foreground">
          No recordings for this MEET.
        </p>
      ) : (
        <>
          {meetingRecordings.length > 1 && (
            <select
              value={selectedRecording?.id ?? ""}
              onChange={(event) => setSelectedRecordingId(event.target.value)}
              className="h-8 w-full rounded-md border border-input bg-background px-2 text-[11px] text-foreground"
            >
              {meetingRecordings.map((recording, index) => (
                <option key={recording.id} value={recording.id}>
                  {index === 0 ? "Latest" : `Recording ${meetingRecordings.length - index}`} - {formatRecordingTimestamp(recording)}
                </option>
              ))}
            </select>
          )}

          {selectedRecording && (
            <RecordingCard
              recording={selectedRecording}
              isRuntimeActive={selectedRecording.id === activeRecording?.id}
              isProcessing={isProcessing}
              operation={selectedOperation}
              readOnly={readOnly}
              send={send}
              showTranscript={showTranscript}
              onStartAnother={recordingControls.mode === "start"
                ? () => void startRecording()
                : undefined}
            />
          )}

          {showAnalysis && selectedAnalysis && (
            <AnalysisReview
              analysis={selectedAnalysis}
              meet={meet}
              projects={projects}
              readOnly={readOnly}
              send={send}
            />
          )}
        </>
      )}

      {unclassifiedRecordings.length > 0 && (
        <div className="space-y-2 border-t border-border pt-3">
          <div>
            <h4 className="text-[10px] font-bold uppercase tracking-widest text-amber-300">
              Emergency recordings
            </h4>
            <p className="mt-0.5 text-[10px] text-muted-foreground">
              Link, classify, transcribe, or keep each capture standalone.
            </p>
          </div>
          {unclassifiedRecordings.slice(0, 5).map((recording) => (
            <div key={recording.id} className="rounded-md border border-border bg-background/50 p-2">
              <div className="flex items-center justify-between gap-2 text-[10px]">
                <span className="font-medium text-foreground">{formatRecordingTimestamp(recording)}</span>
                <span className="text-muted-foreground">{recording.state}</span>
              </div>
              <div className="mt-2 flex flex-wrap gap-1.5">
                <ActionButton
                  label="Link to this MEET"
                  icon={ExternalLink}
                  disabled={readOnly || recording.state === "Recording"}
                  onClick={() => send({
                    type: "linkMeetingRecording",
                    recordingId: recording.id,
                    meetingId: meet.id,
                  })}
                />
                <ActionButton
                  label="Save as new MEET"
                  icon={Video}
                  disabled={readOnly || recording.state === "Recording"}
                  onClick={() => {
                    const title = window.prompt("New MEET title", "Emergency MEET")
                    if (title?.trim()) {
                      send({
                        type: "createMeetingFromRecording",
                        recordingId: recording.id,
                        projectId: meet.projectId,
                        title: title.trim(),
                      })
                    }
                  }}
                />
                <ActionButton
                  label="Keep standalone"
                  icon={Check}
                  disabled={readOnly || recording.keepLocalOnly}
                  onClick={() => send({
                    type: "setMeetingRecordingLocalOnly",
                    recordingId: recording.id,
                    keepLocalOnly: true,
                  })}
                />
                <ActionButton
                  label="Open folder"
                  icon={FolderOpen}
                  onClick={() => send({
                    type: "openMeetingRecordingFolder",
                    recordingId: recording.id,
                  })}
                />
              </div>
            </div>
          ))}
        </div>
      )}
    </section>
  )
}

function RecordingCard({
  recording,
  isRuntimeActive,
  isProcessing,
  operation,
  readOnly,
  send,
  onStartAnother,
  showTranscript,
}: {
  recording: MeetingRecordingSnapshot
  isRuntimeActive: boolean
  isProcessing: boolean
  operation?: MeetingOperationSnapshot
  readOnly: boolean
  send: (command: WorkspaceMeetingAssistantCommand) => boolean
  onStartAnother?: () => void
  showTranscript: boolean
}) {
  const [rangeFrom, setRangeFrom] = useState(
    recording.processFromSeconds == null ? "" : String(recording.processFromSeconds),
  )
  const [rangeUntil, setRangeUntil] = useState(
    recording.processUntilSeconds == null ? "" : String(recording.processUntilSeconds),
  )
  useEffect(() => {
    setRangeFrom(recording.processFromSeconds == null ? "" : String(recording.processFromSeconds))
    setRangeUntil(recording.processUntilSeconds == null ? "" : String(recording.processUntilSeconds))
  }, [recording.id, recording.processFromSeconds, recording.processUntilSeconds])

  const canTranscribe = !recording.keepLocalOnly
    && recording.hasMixedAudio
    && ["Recorded", "TranscriptReady", "Ready", "Failed"].includes(recording.state)
  const canAnalyze = recording.hasTranscript && !isProcessing
  const technicalErrors = recording.tracks.filter((track) => track.error.trim().length > 0)
  const transcriptionSource = recording.tracks.find((track) =>
    track.kind === "Mixed"
      && track.finalizationState === "Finalized"
      && track.validationState === "Valid"
      && track.fileName.trim().length > 0)

  return (
    <div className="space-y-2 rounded-md border border-border bg-background/50 p-2.5">
      <div className="flex min-w-0 items-center gap-2">
        <span className={cn(
          "size-2 shrink-0 rounded-full",
          recording.state === "Recording" ? "animate-pulse bg-red-500" :
            recording.state === "Failed" ? "bg-destructive" : "bg-status-meet",
        )} />
        <span className="min-w-0 flex-1 truncate text-[11px] font-semibold text-foreground">
          {formatRecordingStateLabel(recording)}
          {recording.plannedEndPassed ? " - planned end passed" : ""}
        </span>
        <span className="text-[10px] text-muted-foreground">{formatRecordingTimestamp(recording)}</span>
      </div>

      <div className="grid grid-cols-2 gap-1.5 text-[10px]">
        <TrackHealth icon={Volume2} label="System" value={recording.systemAudioHealth} />
        <TrackHealth icon={Mic} label="Microphone" value={recording.microphoneHealth} />
      </div>

      <div className="space-y-1 rounded border border-border/70 bg-card/40 p-2 text-[10px] text-muted-foreground">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span>Format: <strong className="text-foreground">{formatRecordingFormat(recording.recordingFormat)}</strong></span>
          <span>{formatDuration(recording.durationSeconds)} - {formatBytes(recording.totalBytes)}</span>
        </div>
        <div className="flex flex-wrap gap-x-3 gap-y-1">
          <span>Microphone: {recording.hasMicrophoneAudio ? "available" : "missing"}</span>
          <span>System: {recording.hasSystemAudio ? "available" : "missing"}</span>
          <span>Mixed: {recording.hasMixedAudio ? "available" : "missing"}</span>
        </div>
        {recording.recordingFormat === "Wav" && (
          <p className="text-amber-300">Lossless WAV recordings use substantially more disk space.</p>
        )}
        {recording.sourceKind === "Imported" && (
          <p className="break-words">
            Origin: <strong className="text-foreground">Imported</strong>
            {recording.originalFileName ? ` - ${recording.originalFileName}` : ""}
          </p>
        )}
      </div>

      {recording.sourceKind === "Imported" && (
        <div className="space-y-2 rounded border border-border/70 bg-card/30 p-2">
          <div className="flex items-center justify-between gap-2">
            <span className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
              Transcription range
            </span>
            <button
              type="button"
              disabled={readOnly || isProcessing}
              onClick={() => {
                setRangeFrom("")
                setRangeUntil("")
                send({
                  type: "setImportedAudioRange",
                  recordingId: recording.id,
                  fromSeconds: null,
                  untilSeconds: null,
                })
              }}
              className="text-[10px] font-medium text-status-meet disabled:opacity-40"
            >
              Use full recording
            </button>
          </div>
          <div className="grid grid-cols-[1fr_1fr_auto] gap-1.5">
            <input
              type="number"
              min={0}
              step="0.1"
              value={rangeFrom}
              onChange={(event) => setRangeFrom(event.target.value)}
              placeholder="From"
              className="h-7 min-w-0 rounded border border-input bg-background px-2 text-[10px] text-foreground"
            />
            <input
              type="number"
              min={0}
              max={recording.durationSeconds || undefined}
              step="0.1"
              value={rangeUntil}
              onChange={(event) => setRangeUntil(event.target.value)}
              placeholder="Until"
              className="h-7 min-w-0 rounded border border-input bg-background px-2 text-[10px] text-foreground"
            />
            <button
              type="button"
              disabled={readOnly || isProcessing}
              onClick={() => {
                const from = rangeFrom.trim() ? Number(rangeFrom) : null
                const until = rangeUntil.trim() ? Number(rangeUntil) : null
                if ((from !== null && !Number.isFinite(from)) ||
                    (until !== null && !Number.isFinite(until))) return
                send({
                  type: "setImportedAudioRange",
                  recordingId: recording.id,
                  fromSeconds: from,
                  untilSeconds: until,
                })
              }}
              className="h-7 rounded border border-border px-2 text-[10px] font-medium text-foreground hover:bg-accent disabled:opacity-40"
            >
              Save range
            </button>
          </div>
          <p className="text-[9px] text-muted-foreground">
            The original is unchanged. This range is used the next time you transcribe.
          </p>
        </div>
      )}

      {transcriptionSource && (
        <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 rounded border border-status-meet/25 bg-status-meet/5 px-2 py-1.5 text-[10px] text-muted-foreground">
          <span>
            Transcription source:{" "}
            <strong className="text-foreground">{transcriptionSource.fileName}</strong>
          </span>
          <span>
            Size: {formatBytes(transcriptionSource.bytes)}{" - "}
            Duration: {formatDuration(transcriptionSource.durationSeconds)}
          </span>
        </div>
      )}

      {recording.lastError && (
        <p className="rounded border border-destructive/30 bg-destructive/10 p-2 text-[10px] text-destructive">
          {recording.lastError}
        </p>
      )}

      {operation && <RecordingOperationStatus operation={operation} />}

      {technicalErrors.length > 0 && (
        <details className="min-w-0 rounded border border-border/70 bg-card/30 p-2">
          <summary className="cursor-pointer text-[10px] font-semibold text-muted-foreground">
            Show technical details
          </summary>
          <div className="mt-2 max-h-40 min-w-0 space-y-2 overflow-y-auto">
            {technicalErrors.map((track) => (
              <div key={track.kind} className="min-w-0 text-[10px] leading-relaxed text-muted-foreground">
                <strong className="text-foreground">{track.kind}:</strong>{" "}
                <span className="break-words [overflow-wrap:anywhere]">{track.error}</span>
              </div>
            ))}
          </div>
        </details>
      )}

      <div className="flex flex-wrap gap-1.5">
        {recording.state === "Failed" && onStartAnother && (
          <ActionButton
            label="Start another recording"
            icon={Play}
            primary
            disabled={readOnly}
            onClick={onStartAnother}
          />
        )}
        {isRuntimeActive ? (
          <ActionButton
            label="Stop"
            icon={CircleStop}
            danger
            disabled={readOnly || recording.state === "Stopping"}
            onClick={() => send({ type: "stopMeetingRecording", recordingId: recording.id })}
          />
        ) : (
          <>
            <ActionButton
              label={operation?.kind === "Transcription"
                ? operation.stage === "PreparingAudio" ? "Preparing audio..." : "Transcribing..."
                : recording.hasTranscript ? "Retry transcription" : "Transcribe now"}
              icon={FileAudio}
              primary
              disabled={readOnly || !canTranscribe}
              busy={operation?.kind === "Transcription"}
              onClick={() => {
                if (window.confirm(
                  "The mixed recording will be uploaded to the configured transcription provider. Continue?",
                )) {
                  send({
                    type: "transcribeMeetingRecording",
                    recordingId: recording.id,
                    acceptUploadDisclosure: true,
                  })
                }
              }}
            />
            <ActionButton
              label={operation?.kind === "Analysis"
                ? operation.stage === "StartingAnalysis" ? "Starting analysis..." : "Analyzing..."
                : recording.hasAnalysis ? "Retry analysis" : "Analyze"}
              icon={Sparkles}
              disabled={readOnly || !canAnalyze}
              busy={operation?.kind === "Analysis"}
              onClick={() => send({ type: "analyzeMeetingRecording", recordingId: recording.id })}
            />
          </>
        )}
        {isProcessing && (
          <ActionButton
            label="Cancel processing"
            icon={X}
            onClick={() => send({ type: "cancelMeetingProcessing", recordingId: recording.id })}
          />
        )}
        <ActionButton
          label="Open folder"
          icon={FolderOpen}
          onClick={() => send({ type: "openMeetingRecordingFolder", recordingId: recording.id })}
        />
        {recording.state === "Failed" && recording.recordingFormat === "AacM4a" && (
          <ActionButton
            label="Switch to Lossless WAV"
            icon={FileAudio}
            disabled={readOnly || isRuntimeActive}
            onClick={() => send({ type: "setMeetingRecordingFormat", format: "Wav" })}
          />
        )}
        <ActionButton
          label={recording.keepLocalOnly ? "Audio stays local" : "Keep audio local"}
          icon={Check}
          disabled={readOnly || recording.state === "Recording"}
          onClick={() => send({
            type: "setMeetingRecordingLocalOnly",
            recordingId: recording.id,
            keepLocalOnly: !recording.keepLocalOnly,
          })}
        />
        <ActionButton
          label="Delete"
          icon={Trash2}
          danger
          disabled={readOnly || recording.state === "Recording" || recording.state === "Stopping"}
          onClick={() => {
            if (window.confirm(
              "Delete this recording and all derived transcript/analysis files? This cannot be undone.",
            )) {
              send({ type: "deleteMeetingRecording", recordingId: recording.id })
            }
          }}
        />
      </div>

      {recording.keepLocalOnly && (
        <p className="text-[10px] text-muted-foreground">
          Cloud transcription is disabled for this recording. Existing transcripts and analyses are kept.
        </p>
      )}

      {!isRuntimeActive && (recording.state === "Recording" || recording.state === "Stopping") && (
        <p className="rounded border border-amber-500/30 bg-amber-500/10 p-2 text-[10px] text-amber-300">
          This persisted recording state has no live recorder session. Start remains available while recovery marks it retryable.
        </p>
      )}

      {showTranscript && recording.transcriptText && (
        <details className="rounded-md border border-border p-2">
          <summary className="cursor-pointer text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
            Transcript
          </summary>
          <p className="mt-2 max-h-48 overflow-y-auto whitespace-pre-wrap text-[11px] leading-relaxed text-foreground">
            {recording.transcriptText}
          </p>
        </details>
      )}
    </div>
  )
}

export function AnalysisReview({
  analysis,
  meet,
  projects,
  readOnly,
  send,
}: {
  analysis: MeetingAnalysisSnapshot
  meet: MeetItem
  projects: Project[]
  readOnly: boolean
  send: (command: WorkspaceMeetingAssistantCommand) => boolean
}) {
  const meetProjectName = projects.find((project) => project.id === meet.projectId)?.name ?? "MEET project"
  const pending = analysis.proposedActions.filter((action) => action.reviewState === "Pending" || action.reviewState === "Failed")
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set())
  const [overrides, setOverrides] = useState<Record<string, MeetingProposedActionOverride>>({})

  useEffect(() => {
    setSelectedIds(new Set())
    setOverrides(Object.fromEntries(analysis.proposedActions.map((action) => [
      action.id,
      {
        actionId: action.id,
        title: action.title,
        projectId: action.proposedProjectId,
        status: action.proposedStatus,
        waitingFor: action.waitingFor,
        deadlineAtUtc: action.deadlineAtUtc,
        reminderAtUtc: action.reminderAtUtc,
      },
    ])))
  }, [analysis.id])

  const updateOverride = (id: string, patch: Partial<MeetingProposedActionOverride>) =>
    setOverrides((current) => ({
      ...current,
      [id]: { ...(current[id] ?? { actionId: id }), ...patch },
    }))

  return (
    <div className="space-y-2 rounded-md border border-status-meet/30 bg-status-meet/5 p-2.5">
      <div className="flex items-center gap-2">
        <Bot className="size-4 text-status-meet" />
        <span className="text-[11px] font-semibold text-foreground">Meeting Assistant</span>
        <span className="ml-auto text-[10px] text-muted-foreground">{analysis.state}</span>
      </div>
      {analysis.lastError && (
        <p className="text-[10px] text-destructive">{analysis.lastError}</p>
      )}
      {analysis.summary && (
        <p className="whitespace-pre-wrap text-[11px] leading-relaxed text-foreground">{analysis.summary}</p>
      )}
      <AnalysisLists analysis={analysis} />

      {analysis.proposedActions.length > 0 && (
        <div className="space-y-2 border-t border-border/60 pt-2">
          <div className="flex items-center justify-between">
            <span className="text-[10px] font-bold uppercase tracking-widest text-muted-foreground">
              Proposed actions
            </span>
            <span className="text-[10px] text-muted-foreground">Review before applying</span>
          </div>
          {analysis.proposedActions.map((action) => {
            const edit = overrides[action.id] ?? { actionId: action.id }
            const editable = action.reviewState === "Pending" || action.reviewState === "Failed"
            return (
              <div key={action.id} className="space-y-2 rounded-md border border-border bg-background/60 p-2">
                <div className="flex items-start gap-2">
                  <input
                    type="checkbox"
                    className="mt-1"
                    checked={selectedIds.has(action.id)}
                    disabled={readOnly || !editable}
                    onChange={(event) => setSelectedIds((current) => {
                      const next = new Set(current)
                      if (event.target.checked) next.add(action.id)
                      else next.delete(action.id)
                      return next
                    })}
                  />
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-1.5 text-[9px] uppercase tracking-wide text-muted-foreground">
                      <span>{proposedActionLabel(action.type)}</span>
                      <span>{action.reviewState}</span>
                    </div>
                    <input
                      value={edit.title ?? ""}
                      disabled={!editable || readOnly}
                      onChange={(event) => updateOverride(action.id, { title: event.target.value })}
                      className="mt-1.5 h-8 w-full rounded border border-input bg-background px-2 text-[11px] text-foreground"
                    />
                  </div>
                </div>
                <div className="grid grid-cols-2 gap-1.5">
                  <select
                    value={edit.projectId ?? ""}
                    disabled={!editable || readOnly}
                    onChange={(event) => updateOverride(action.id, { projectId: event.target.value || null })}
                    className="h-7 rounded border border-input bg-background px-1.5 text-[10px] text-foreground"
                  >
                    <option value="">{meetProjectName}</option>
                    {projects.map((project) => (
                      <option key={project.id} value={project.id}>{project.name}</option>
                    ))}
                  </select>
                  <select
                    value={edit.status ?? action.proposedStatus}
                    disabled={!editable || readOnly}
                    onChange={(event) => updateOverride(action.id, { status: event.target.value as Status })}
                    className="h-7 rounded border border-input bg-background px-1.5 text-[10px] text-foreground"
                  >
                    {statusOptions.map((status) => <option key={status}>{status}</option>)}
                  </select>
                </div>
                {(action.type === "CreateWaitingTask" || (edit.status ?? action.proposedStatus) === "WAIT") && (
                  <input
                    value={edit.waitingFor ?? ""}
                    disabled={!editable || readOnly}
                    placeholder="Waiting for"
                    onChange={(event) => updateOverride(action.id, { waitingFor: event.target.value })}
                    className="h-7 w-full rounded border border-input bg-background px-2 text-[10px] text-foreground"
                  />
                )}
                <div className="grid grid-cols-2 gap-1.5">
                  <label className="text-[9px] uppercase text-muted-foreground">
                    Deadline
                    <input
                      type="datetime-local"
                      value={toLocalInput(edit.deadlineAtUtc)}
                      disabled={!editable || readOnly}
                      onChange={(event) => updateOverride(action.id, { deadlineAtUtc: fromLocalInput(event.target.value) })}
                      className="mt-1 h-7 w-full rounded border border-input bg-background px-1.5 text-[10px] text-foreground"
                    />
                  </label>
                  <label className="text-[9px] uppercase text-muted-foreground">
                    Reminder
                    <input
                      type="datetime-local"
                      value={toLocalInput(edit.reminderAtUtc)}
                      disabled={!editable || readOnly}
                      onChange={(event) => updateOverride(action.id, { reminderAtUtc: fromLocalInput(event.target.value) })}
                      className="mt-1 h-7 w-full rounded border border-input bg-background px-1.5 text-[10px] text-foreground"
                    />
                  </label>
                </div>
                {action.sourceExcerpt && (
                  <blockquote className="border-l-2 border-status-meet/40 pl-2 text-[10px] italic text-muted-foreground">
                    {formatSegment(action)} {action.sourceExcerpt}
                  </blockquote>
                )}
                {action.rationale && (
                  <p className="text-[10px] text-muted-foreground">
                    {action.rationale}
                    {action.confidence > 0 && ` (model confidence: ${Math.round(action.confidence * 100)}%)`}
                  </p>
                )}
                {editable && (
                  <button
                    type="button"
                    disabled={readOnly}
                    onClick={() => {
                      if (window.confirm("Reject this proposed action? No task or context item will be created.")) {
                        send({
                          type: "rejectMeetingProposedAction",
                          analysisId: analysis.id,
                          actionId: action.id,
                        })
                      }
                    }}
                    className="text-[10px] font-medium text-muted-foreground hover:text-destructive"
                  >
                    Reject suggestion
                  </button>
                )}
              </div>
            )
          })}
          <button
            type="button"
            disabled={readOnly || selectedIds.size === 0}
            onClick={() => {
              const ids = [...selectedIds].filter((id) => pending.some((action) => action.id === id))
              if (ids.length > 0 && window.confirm(
                `Create/apply ${ids.length} selected action${ids.length === 1 ? "" : "s"} through TaskOverlay services?`,
              )) {
                send({
                  type: "applyMeetingProposedActions",
                  analysisId: analysis.id,
                  actionIds: ids,
                  overrides: ids.map((id) => overrides[id] ?? { actionId: id }),
                })
              }
            }}
            className="flex h-8 w-full items-center justify-center gap-1.5 rounded-md bg-primary text-[11px] font-semibold text-primary-foreground disabled:cursor-not-allowed disabled:opacity-40"
          >
            <Check className="size-3.5" />
            Apply selected actions
          </button>
        </div>
      )}
    </div>
  )
}

function AnalysisLists({ analysis }: { analysis: MeetingAnalysisSnapshot }) {
  const groups: [string, string[]][] = [
    ["Decisions", analysis.decisions],
    ["My actions", analysis.myActionItems],
    ["Other people actions", analysis.otherPeopleActionItems],
    ["Waiting for", analysis.waitingFor],
    ["Risks", analysis.risks],
    ["Questions", analysis.questionsToClarify],
    ["Deadlines", analysis.deadlines],
  ]
  return (
    <div className="space-y-1.5">
      {groups.filter(([, items]) => items.length > 0).map(([label, items]) => (
        <details key={label} className="rounded border border-border/70 px-2 py-1.5">
          <summary className="cursor-pointer text-[10px] font-semibold text-muted-foreground">
            {label} ({items.length})
          </summary>
          <ul className="mt-1.5 space-y-1 pl-4 text-[10px] text-foreground">
            {items.map((item, index) => <li key={`${label}:${index}`} className="list-disc">{item}</li>)}
          </ul>
        </details>
      ))}
    </div>
  )
}

function TrackHealth({
  icon: Icon,
  label,
  value,
}: {
  icon: LucideIcon
  label: string
  value: MeetingRecordingSnapshot["microphoneHealth"]
}) {
  return (
    <span className="flex min-w-0 items-center gap-1.5 rounded border border-border px-2 py-1 text-muted-foreground">
      <Icon className="size-3 shrink-0" />
      <span className="truncate">{label}: {value}</span>
    </span>
  )
}

function RecordingOperationStatus({ operation }: { operation: MeetingOperationSnapshot }) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1_000)
    return () => window.clearInterval(timer)
  }, [operation.id])
  const elapsed = formatElapsed(operation.startedAtUtc, now)
  const label = operation.stage === "StartingTranscription" ? "Starting transcription..."
    : operation.stage === "PreparingAudio" ? "Preparing audio..."
    : operation.stage === "Transcribing" ? "Transcribing..."
    : operation.stage === "StartingAnalysis" ? "Starting analysis..."
    : operation.stage === "Cancelling" ? "Cancelling..."
    : "Analyzing transcript..."
  return (
    <div
      role="status"
      aria-live="polite"
      aria-busy="true"
      className="space-y-1.5 rounded border border-status-meet/30 bg-status-meet/10 p-2 text-[10px]"
    >
      <div className="flex items-center gap-2 font-medium text-status-meet">
        <span className="size-3 animate-spin rounded-full border border-current border-t-transparent motion-reduce:animate-none" aria-hidden="true" />
        <span>{label}</span>
        <span className="ml-auto font-mono" aria-hidden="true">{elapsed}</span>
      </div>
    </div>
  )
}

function ActionButton({
  label,
  icon: Icon,
  onClick,
  disabled = false,
  primary = false,
  danger = false,
  busy = false,
}: {
  label: string
  icon: LucideIcon
  onClick: () => void
  disabled?: boolean
  primary?: boolean
  danger?: boolean
  busy?: boolean
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      aria-busy={busy}
      onClick={onClick}
      className={cn(
        "flex h-7 items-center gap-1.5 rounded-md border px-2 text-[10px] font-medium disabled:cursor-not-allowed disabled:opacity-40",
        primary && "border-status-meet/50 bg-status-meet/10 text-status-meet",
        danger && "border-destructive/40 text-destructive hover:bg-destructive/10",
        !primary && !danger && "border-border text-muted-foreground hover:bg-accent hover:text-foreground",
      )}
    >
      {busy
        ? <span className="size-3 animate-spin rounded-full border border-current border-t-transparent motion-reduce:animate-none" aria-hidden="true" />
        : <Icon className="size-3" />}
      {label}
    </button>
  )
}

function formatRecordingTimestamp(recording: MeetingRecordingSnapshot): string {
  const timestamp = recording.startedAtUtc ?? recording.createdAtUtc
  const value = new Date(timestamp)
  return Number.isNaN(value.getTime())
    ? "Unknown time"
    : value.toLocaleString([], { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" })
}

function formatElapsed(startedAtUtc: string | null, now: number): string {
  if (!startedAtUtc) return "00:00"

  const elapsedSeconds = Math.max(0, Math.floor((now - new Date(startedAtUtc).getTime()) / 1_000))
  const hours = Math.floor(elapsedSeconds / 3_600)
  const minutes = Math.floor((elapsedSeconds % 3_600) / 60)
  const seconds = elapsedSeconds % 60
  return hours > 0
    ? `${hours.toString().padStart(2, "0")}:${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`
    : `${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`
}

function formatTrackHealth(health: MeetingRecordingSnapshot["systemAudioHealth"]): string {
  switch (health) {
    case "Healthy": return "OK"
    case "Unavailable": return "unavailable"
    case "Failed": return "failed"
    default: return "starting"
  }
}

function formatRecordingFormat(format: MeetingRecordingSnapshot["recordingFormat"]): string {
  if (format === "Wav") return "WAV"
  if (format === "Mp3") return "MP3"
  return "AAC/M4A"
}

function formatRecordingStateLabel(recording: MeetingRecordingSnapshot): string {
  if (recording.state === "Stopping") return "Finalizing recording..."
  if (recording.state === "TranscriptReady") return "Transcription completed"
  if (recording.state === "Failed") {
    if (recording.lastError.toLowerCase().includes("cancel")) {
      return recording.hasTranscript ? "Analysis cancelled" : "Transcription cancelled"
    }
    return recording.hasTranscript ? "Analysis failed - Retry" : "Transcription failed - Retry"
  }
  return recording.state
}

function formatDuration(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) return "0:00"
  const rounded = Math.round(seconds)
  const hours = Math.floor(rounded / 3_600)
  const minutes = Math.floor((rounded % 3_600) / 60)
  const remainder = rounded % 60
  return hours > 0
    ? `${hours}:${minutes.toString().padStart(2, "0")}:${remainder.toString().padStart(2, "0")}`
    : `${minutes}:${remainder.toString().padStart(2, "0")}`
}

function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return "0 B"
  const units = ["B", "KB", "MB", "GB"]
  let value = bytes
  let unit = 0
  while (value >= 1_024 && unit < units.length - 1) {
    value /= 1_024
    unit++
  }
  return `${value.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`
}

function toLocalInput(value?: string | null): string {
  if (!value) return ""
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ""
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}

function fromLocalInput(value: string): string | null {
  if (!value) return null
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date.toISOString()
}

function formatSegment(action: MeetingProposedActionSnapshot): string {
  if (action.sourceSegmentStart == null) return ""
  const start = formatSeconds(action.sourceSegmentStart)
  const end = action.sourceSegmentEnd == null ? "" : `-${formatSeconds(action.sourceSegmentEnd)}`
  return `[${start}${end}]`
}

function formatSeconds(value: number): string {
  const seconds = Math.max(0, Math.round(value))
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`
}
