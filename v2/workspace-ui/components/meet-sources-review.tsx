"use client"

import { useCallback, useEffect, useRef, useState } from "react"

import {
  Bot,
  Camera,
  Check,
  Copy,
  ExternalLink,
  FileAudio,
  FileText,
  Image as ImageIcon,
  Info,
  LoaderCircle,
  PencilLine,
  Sparkles,
  Trash2,
  Undo2,
  Upload,
  Users,
  X,
} from "lucide-react"
import type {
  MeetItem,
  MeetingAnalysisSnapshot,
  MeetingOperationSnapshot,
  MeetingRecordingPolicy,
  MeetingRecordingSnapshot,
  MeetingScreenshotSnapshot,
  MeetingTranscriptSnapshot,
  Project,
  WorkspaceCommandResult,
  WorkspaceMeetingAssistantCommand,
} from "@/lib/types"
import { cn } from "@/lib/utils"
import {
  selectActiveMeetingTranscript,
  selectReviewAnalysis,
  sortMeetingScreenshots,
} from "@/lib/meet-workspace-policy"
import {
  buildSaveRevisionCommand,
  createTranscriptDraft,
  draftSegmentSpeakerName,
  isDraftDirty,
  isSpeakerMergedAway,
  mergeDraftSpeaker,
  renameDraftSpeaker,
  setDraftSegmentText,
  toggleDraftSpeakerAsYou,
  transcriptEditorKeyAction,
  transcriptOriginLabel,
  undoDraftSpeakerMerge,
  validateTranscriptDraft,
  visibleDraftSpeakers,
  type TranscriptDraft,
} from "@/lib/meet-transcript-editor"
import { AnalysisReview, MeetingAssistantSection } from "./meeting-assistant-section"

/** Registered with the MEET modal so close/tab-switch/Escape can protect a dirty draft. */
export interface TranscriptEditGuard {
  isEditing: boolean
  /** Confirms discard when dirty; returns true when the editor exited (or was already closed). */
  requestExit: () => boolean
}

interface SharedProps {
  meet: MeetItem
  projects: Project[]
  recordings: MeetingRecordingSnapshot[]
  transcripts: MeetingTranscriptSnapshot[]
  screenshots: MeetingScreenshotSnapshot[]
  analyses: MeetingAnalysisSnapshot[]
  operations: MeetingOperationSnapshot[]
  activeRecording: MeetingRecordingSnapshot | null
  activeRecordingOwnerTitle?: string
  readOnly: boolean
  commandError?: string | null
  commandNotice?: string | null
  onClearError?: () => void
  onClearNotice?: () => void
  onCommand?: (command: WorkspaceMeetingAssistantCommand) => boolean
  defaultRecordingPolicy?: Exclude<MeetingRecordingPolicy, "Inherit">
  onRecordingPolicyChange?: (policy: MeetingRecordingPolicy) => void
  onBeforeRecordingStart?: () => Promise<boolean>
  /** Tracked sender for the one deliberate revision-save command. */
  onSaveTranscriptRevision?: (
    command: WorkspaceMeetingAssistantCommand,
  ) => Promise<WorkspaceCommandResult>
  /** Lets the MEET modal guard a dirty transcript draft on close/tab switch/Escape. */
  registerTranscriptEditGuard?: (guard: TranscriptEditGuard | null) => void
}

export function MeetingSourcesWorkspace({
  meet,
  projects,
  recordings,
  transcripts,
  screenshots,
  analyses,
  operations,
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
}: SharedProps) {
  const meetTranscripts = transcripts
    .filter((transcript) => transcript.meetingId === meet.id)
    .sort((left, right) => right.createdAtUtc.localeCompare(left.createdAtUtc))
  const meetScreenshots = sortMeetingScreenshots(meet.id, screenshots)
  const sourceActionsDisabled = readOnly || !onCommand
  const send = (command: WorkspaceMeetingAssistantCommand) => onCommand?.(command) ?? false

  return (
    // Two columns, mirroring the Details layout: recording/audio owns the left
    // (wider) column, transcripts + screenshots own the right column. Each
    // column scrolls independently so there is exactly one scroll region per
    // side and the shell never grows to fit content. The selected tab already
    // says "Sources" — no extra page heading or explanatory prose.
    <div className="grid min-h-0 w-full min-w-0 flex-1 grid-cols-1 lg:grid-cols-[minmax(0,1fr)_minmax(320px,0.9fr)]">
        {/* LEFT: Recording and audio sources */}
        <div className="flex min-h-0 flex-col gap-3 overflow-y-auto border-b border-border p-4 [scrollbar-gutter:stable] lg:border-b-0 lg:border-r">
          <div className="flex items-center justify-between gap-2">
            <h4 className="text-[11px] font-bold uppercase tracking-widest text-foreground">Recording &amp; audio</h4>
            <SourceAction
              label="Import audio"
              icon={FileAudio}
              disabled={sourceActionsDisabled}
              onClick={() => send({ type: "importMeetingAudio", meetingId: meet.id })}
            />
          </div>
          {onCommand && (
            <MeetingAssistantSection
              meet={meet}
              projects={projects}
              recordings={recordings}
              analyses={analyses}
              operations={operations}
              unclassifiedRecordings={recordings.filter((recording) => !recording.meetingId)}
              activeRecording={activeRecording}
              activeRecordingOwnerTitle={activeRecordingOwnerTitle}
              readOnly={readOnly}
              commandError={commandError}
              commandNotice={commandNotice}
              onClearError={onClearError}
              onClearNotice={onClearNotice}
              onCommand={onCommand}
              defaultRecordingPolicy={defaultRecordingPolicy}
              onRecordingPolicyChange={onRecordingPolicyChange}
              onBeforeRecordingStart={onBeforeRecordingStart}
              showAnalysis={false}
              showTranscript={false}
            />
          )}
        </div>

        {/* RIGHT: Transcripts and visual sources */}
        <div className="flex min-h-0 flex-col gap-3 overflow-y-auto p-4 [scrollbar-gutter:stable]">
          <h4 className="text-[11px] font-bold uppercase tracking-widest text-foreground">
            Transcripts &amp; screenshots
          </h4>
          <section className="space-y-2 rounded-lg border border-border bg-card p-3" role="radiogroup" aria-label="Transcript versions">
            <div className="flex items-center gap-2">
              <FileText className="size-4 text-status-meet" />
              <h3 className="text-[11px] font-semibold text-foreground">Transcripts</h3>
              <span className="text-[10px] text-muted-foreground">{meetTranscripts.length}</span>
              <SourceAction
                label="Import transcript"
                icon={Upload}
                disabled={sourceActionsDisabled}
                onClick={() => send({ type: "importMeetingTranscript", meetingId: meet.id })}
                className="ml-auto"
              />
            </div>
            {meetTranscripts.length === 0 ? (
              <EmptySource text="No generated or imported transcripts yet." />
            ) : meetTranscripts.map((transcript) => (
              <TranscriptSourceCard
                key={transcript.id}
                meet={meet}
                transcript={transcript}
                readOnly={readOnly}
                send={send}
                operations={operations}
              />
            ))}
          </section>

          <section className="space-y-2 rounded-lg border border-border bg-card p-3">
            <div className="flex items-center gap-2">
              <ImageIcon className="size-4 text-status-meet" />
              <h3 className="text-[11px] font-semibold text-foreground">Screenshots</h3>
              <span className="text-[10px] text-muted-foreground">{meetScreenshots.length}</span>
              <SourceAction
                label="Capture screenshot"
                icon={Camera}
                disabled={sourceActionsDisabled}
                onClick={() => send({ type: "captureMeetingScreenshot", meetingId: meet.id })}
                className="ml-auto"
              />
            </div>
            {meetScreenshots.length === 0 ? (
              <EmptySource text="No manual screenshots captured for this MEET." />
            ) : (
              <div className="grid gap-2 sm:grid-cols-2">
                {meetScreenshots.map((screenshot) => (
                  <ScreenshotCard
                    key={screenshot.id}
                    screenshot={screenshot}
                    readOnly={readOnly}
                    send={send}
                  />
                ))}
              </div>
            )}
          </section>
        </div>
    </div>
  )
}

export function MeetingReviewWorkspace({
  meet,
  projects,
  transcripts,
  screenshots,
  analyses,
  operations,
  readOnly,
  commandError,
  commandNotice,
  onClearError,
  onClearNotice,
  onCommand,
  onSaveTranscriptRevision,
  registerTranscriptEditGuard,
}: SharedProps) {
  const activeTranscript = selectActiveMeetingTranscript(
    meet.id,
    meet.activeTranscriptId,
    transcripts,
  )
  const meetTranscripts = transcripts.filter((transcript) => transcript.meetingId === meet.id)
  const { analysis: selectedAnalysis, isStaleForActive } = selectReviewAnalysis(
    activeTranscript,
    meetTranscripts,
    analyses,
  )
  const meetScreenshots = sortMeetingScreenshots(meet.id, screenshots)
  const latestFailure = activeTranscript
    ? analyses
        .filter((analysis) => analysis.transcriptId === activeTranscript.id && analysis.state === "Failed")
        .sort((left, right) => right.updatedAtUtc.localeCompare(left.updatedAtUtc))[0]
    : undefined
  const hasCurrentFailure = Boolean(latestFailure &&
    (!selectedAnalysis || latestFailure.updatedAtUtc > selectedAnalysis.updatedAtUtc))
  const analysisOperation = activeTranscript
    ? operations.find((operation) => operation.kind === "Analysis" &&
        (operation.transcriptId === activeTranscript.id ||
         (activeTranscript.recordingId && operation.recordingId === activeTranscript.recordingId)))
    : undefined
  const send = (command: WorkspaceMeetingAssistantCommand) => onCommand?.(command) ?? false

  // Ephemeral edit draft — React state only, never persisted per keystroke.
  const [draft, setDraft] = useState<TranscriptDraft | null>(null)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const draftRef = useRef(draft)
  draftRef.current = draft
  const savingRef = useRef(saving)
  savingRef.current = saving
  const confirmOpenRef = useRef(false)

  const requestExit = useCallback((): boolean => {
    const current = draftRef.current
    if (!current) return true
    if (savingRef.current) return false
    if (isDraftDirty(current)) {
      confirmOpenRef.current = true
      const discard = window.confirm("Discard unsaved transcript edits?")
      confirmOpenRef.current = false
      if (!discard) return false
    }
    setDraft(null)
    setSaveError(null)
    return true
  }, [])

  useEffect(() => {
    registerTranscriptEditGuard?.({ isEditing: draft !== null, requestExit })
    return () => registerTranscriptEditGuard?.(null)
  }, [draft, requestExit, registerTranscriptEditGuard])

  const startEditing = () => {
    if (!activeTranscript || readOnly || !onSaveTranscriptRevision) return
    setSaveError(null)
    setDraft(createTranscriptDraft(activeTranscript))
  }

  const saveDraft = async () => {
    const current = draftRef.current
    if (!current || savingRef.current || !onSaveTranscriptRevision) return
    const validation = validateTranscriptDraft(current)
    if (validation) {
      setSaveError(validation)
      return
    }

    setSaving(true)
    setSaveError(null)
    const result = await onSaveTranscriptRevision(buildSaveRevisionCommand(current))
    setSaving(false)
    if (result.success) {
      setDraft(null)
    } else {
      setSaveError(result.errorMessage ?? "The transcript revision could not be saved.")
    }
  }

  const canEdit = Boolean(activeTranscript && !readOnly && onSaveTranscriptRevision &&
    activeTranscript.segments.length > 0)

  return (
    // Two columns, each with exactly one scroll region — mirrors the Details
    // layout so the shell never needs a page-level scroll on top of a nested
    // one, and geometry stays fixed instead of chasing the viewport.
    <div className="grid min-h-0 w-full min-w-0 flex-1 grid-cols-1 lg:grid-cols-[minmax(0,1.08fr)_minmax(340px,0.92fr)]">
      <div className="flex min-h-0 flex-col border-b border-border p-4 lg:border-b-0 lg:border-r">
        <section className="flex h-full min-h-0 min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-card">
          <div className="flex flex-wrap items-center gap-2 border-b border-border px-3 py-2.5">
            <FileText className="size-4 text-status-meet" />
            <div className="min-w-0 flex-1">
              <h3 className="truncate text-[11px] font-bold uppercase tracking-widest text-foreground">
                {draft ? "Editing transcript" : "Active transcript"}
              </h3>
              <p className="truncate text-[10px] text-muted-foreground">
                {activeTranscript
                  ? `${transcriptOriginLabel(activeTranscript.origin)} - ${activeTranscript.sourceLabel || activeTranscript.provider} - ${new Date(activeTranscript.createdAtUtc).toLocaleDateString([], { day: "numeric", month: "short" })}`
                  : "Select or create a transcript in Sources"}
              </p>
            </div>
            {activeTranscript && !draft && (
              <>
                <button
                  type="button"
                  onClick={() => navigator.clipboard?.writeText(activeTranscript.text)}
                  className="flex h-7 items-center gap-1 rounded border border-border px-2 text-[10px] text-muted-foreground hover:bg-accent hover:text-foreground"
                >
                  <Copy className="size-3" /> Copy
                </button>
                {activeTranscript.normalizedAvailable && onCommand && (
                  <button
                    type="button"
                    onClick={() => send({
                      type: "openMeetingTranscriptArtifact",
                      transcriptId: activeTranscript.id,
                      artifact: "normalized",
                    })}
                    className="flex h-7 items-center gap-1 rounded border border-border px-2 text-[10px] text-muted-foreground hover:bg-accent hover:text-foreground"
                  >
                    <ExternalLink className="size-3" /> Open
                  </button>
                )}
                {canEdit && (
                  <button
                    type="button"
                    onClick={startEditing}
                    className="flex h-7 items-center gap-1 rounded border border-status-meet/40 bg-status-meet/10 px-2 text-[10px] font-medium text-status-meet outline-none hover:bg-status-meet/20 focus-visible:ring-2 focus-visible:ring-status-meet/50"
                  >
                    <PencilLine className="size-3" /> Edit transcript
                  </button>
                )}
              </>
            )}
            {draft && (
              <>
                <button
                  type="button"
                  onClick={() => requestExit()}
                  disabled={saving}
                  className="flex h-7 items-center gap-1 rounded border border-border px-2 text-[10px] text-muted-foreground outline-none hover:bg-accent hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-40"
                >
                  <X className="size-3" /> Cancel
                </button>
                <button
                  type="button"
                  onClick={() => void saveDraft()}
                  disabled={saving}
                  aria-busy={saving}
                  className="flex h-7 items-center gap-1 rounded border border-status-meet/40 bg-status-meet/10 px-2 text-[10px] font-medium text-status-meet outline-none hover:bg-status-meet/20 focus-visible:ring-2 focus-visible:ring-status-meet/50 disabled:opacity-40"
                >
                  {saving
                    ? <LoaderCircle className="size-3 animate-spin motion-reduce:animate-none" aria-hidden="true" />
                    : <Check className="size-3" />}
                  {saving ? "Saving..." : "Save revision"}
                </button>
              </>
            )}
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto p-3 [scrollbar-gutter:stable]">
            {draft ? (
              <TranscriptEditor
                draft={draft}
                saving={saving}
                saveError={saveError}
                confirmOpenRef={confirmOpenRef}
                onChange={setDraft}
                onSave={() => void saveDraft()}
                onCancel={() => requestExit()}
              />
            ) : activeTranscript ? (
              <TranscriptContent transcript={activeTranscript} screenshots={meetScreenshots} send={send} />
            ) : (
              <EmptySource text="Review will use the explicitly active transcript." />
            )}
          </div>
        </section>
      </div>

      <div className="flex min-h-0 flex-col gap-3 overflow-y-auto p-4 [scrollbar-gutter:stable]">
          <section className="space-y-2 rounded-lg border border-border bg-card p-3">
            {commandNotice && !analysisOperation && (
              <NeutralNotice message={commandNotice} onDismiss={onClearNotice} />
            )}
            {(commandError || hasCurrentFailure) && !analysisOperation && (
              <div className="flex items-start gap-2 rounded border border-destructive/30 bg-destructive/10 p-2 text-[10px] text-destructive">
                <span className="min-w-0 flex-1">
                  {commandError ?? latestFailure?.lastError ?? "Analysis failed. Retry when ready."}
                </span>
                {commandError && onClearError && (
                  <button type="button" onClick={onClearError} aria-label="Dismiss analysis error">
                    <X className="size-3" />
                  </button>
                )}
              </div>
            )}
            <div className="flex flex-wrap items-center gap-2">
              <Bot className="size-4 text-status-meet" />
              <h3 className="text-[11px] font-bold uppercase tracking-widest text-foreground">
                Meeting Assistant
              </h3>
              {selectedAnalysis && (selectedAnalysis.isStale || isStaleForActive) && (
                <span className="rounded bg-amber-500/15 px-1.5 py-0.5 text-[9px] font-semibold text-amber-300">
                  Stale transcript revision
                </span>
              )}
              {activeTranscript && onCommand && (
                <div className="ml-auto flex items-center gap-1.5">
                  <button
                    type="button"
                    disabled={readOnly || Boolean(analysisOperation)}
                    aria-busy={Boolean(analysisOperation)}
                    onClick={() => send({ type: "analyzeMeetingTranscript", transcriptId: activeTranscript.id })}
                    className="flex h-7 items-center gap-1 rounded border border-status-meet/40 bg-status-meet/10 px-2 text-[10px] font-medium text-status-meet disabled:opacity-40"
                  >
                    {analysisOperation
                      ? <LoaderCircle className="size-3 animate-spin motion-reduce:animate-none" aria-hidden="true" />
                      : <Sparkles className="size-3" />}
                    {analysisOperation ? "Analyzing..." : hasCurrentFailure ? "Retry analysis" : selectedAnalysis ? "Re-run analysis" : "Analyze transcript"}
                  </button>
                  {analysisOperation && (
                    <button
                      type="button"
                      disabled={analysisOperation.stage === "Cancelling"}
                      onClick={() => send({ type: "cancelMeetingProcessing", transcriptId: activeTranscript.id })}
                      className="flex h-7 items-center gap-1 rounded border border-border px-2 text-[10px] text-muted-foreground disabled:opacity-40"
                    >
                      <X className="size-3" />
                      {analysisOperation.stage === "Cancelling" ? "Cancelling..." : "Cancel"}
                    </button>
                  )}
                </div>
              )}
            </div>
            {selectedAnalysis ? (
              <>
                {analysisOperation && (
                  <OperationStatus operation={analysisOperation} message="A new analysis is running. The current result remains available until it completes." />
                )}
                <AnalysisReview
                  analysis={selectedAnalysis}
                  meet={meet}
                  projects={projects}
                  readOnly={readOnly}
                  send={send}
                />
              </>
            ) : analysisOperation ? (
              <OperationStatus operation={analysisOperation} message="Analyzing transcript..." />
            ) : (
              <EmptySource text={activeTranscript
                ? "No analysis exists for this transcript version."
                : "Analysis needs an active transcript."} />
            )}
          </section>

          <section className="space-y-2 rounded-lg border border-border bg-card p-3">
            <div className="flex items-center gap-2">
              <ImageIcon className="size-4 text-status-meet" />
              <h3 className="text-[11px] font-bold uppercase tracking-widest text-foreground">Visual references</h3>
              <span className="ml-auto text-[10px] text-muted-foreground">{meetScreenshots.length}</span>
            </div>
            {meetScreenshots.length === 0 ? (
              <EmptySource text="Captured screenshots will appear here in chronological order." />
            ) : (
              <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-1 xl:grid-cols-2">
                {meetScreenshots.map((screenshot) => (
                  <ScreenshotCard
                    key={screenshot.id}
                    screenshot={screenshot}
                    readOnly={readOnly}
                    send={send}
                    compact
                  />
                ))}
              </div>
            )}
          </section>

          <section className="rounded-lg border border-dashed border-border p-3">
            <h3 className="text-[10px] font-bold uppercase tracking-widest text-muted-foreground">
              Project context updates
            </h3>
            <p className="mt-1 text-[11px] text-muted-foreground">
              No proposed ContextHUB updates. Future candidates will require explicit review here.
            </p>
          </section>
      </div>
    </div>
  )
}

function TranscriptSourceCard({
  meet,
  transcript,
  readOnly,
  send,
  operations,
}: {
  meet: MeetItem
  transcript: MeetingTranscriptSnapshot
  readOnly: boolean
  send: (command: WorkspaceMeetingAssistantCommand) => boolean
  operations: MeetingOperationSnapshot[]
}) {
  const analysisOperation = operations.find((operation) => operation.kind === "Analysis" &&
    (operation.transcriptId === transcript.id ||
     (transcript.recordingId && operation.recordingId === transcript.recordingId)))
  const activate = () => {
    if (readOnly || transcript.isActive) return
    send({
      type: "setActiveMeetingTranscript",
      meetingId: meet.id,
      transcriptId: transcript.id,
    })
  }
  return (
    // Whole-card selection with identical geometry in both states: the active
    // treatment is a lighter neutral surface + stronger light-neutral border,
    // and the header keeps a reserved slot for the Active label so nothing
    // moves when the selection changes.
    <div
      role="radio"
      aria-checked={transcript.isActive}
      tabIndex={0}
      onClick={activate}
      onKeyDown={(event) => {
        if (event.target !== event.currentTarget || (event.key !== "Enter" && event.key !== " ")) return
        event.preventDefault()
        activate()
      }}
      className={cn(
      "space-y-2 rounded-md border p-2.5 outline-none transition-colors focus-visible:ring-2 focus-visible:ring-status-meet/50",
      transcript.isActive
        ? "border-[var(--meet-border-strong)] bg-[var(--meet-selected-surface)]"
        : "border-border bg-card",
      !readOnly && !transcript.isActive && "cursor-pointer hover:border-[var(--meet-border-strong)] hover:bg-secondary",
    )}>
      <div className="flex min-w-0 flex-wrap items-center gap-2">
        <span className={cn(
          "rounded px-1.5 py-0.5 text-[9px] font-semibold uppercase",
          transcript.origin === "Imported"
            ? "bg-sky-500/15 text-sky-300"
            : transcript.origin === "UserEdited"
              ? "bg-emerald-500/15 text-emerald-300"
              : "bg-status-meet/15 text-status-meet",
        )}>
          {transcriptOriginLabel(transcript.origin)}
        </span>
        <span className="min-w-0 flex-1 truncate text-[11px] font-medium text-foreground">
          {transcript.originalFileName || transcript.sourceLabel || "Transcript"}
        </span>
        <span
          className={cn(
            "flex shrink-0 items-center gap-1 text-[10px] font-semibold text-foreground",
            !transcript.isActive && "invisible",
          )}
          aria-hidden={!transcript.isActive}
        >
          <Check className="size-3 text-status-meet" /> Active
        </span>
      </div>
      <div className="flex flex-wrap gap-x-3 gap-y-1 text-[10px] text-muted-foreground">
        <span>{transcript.format}</span>
        <span>{transcript.hasTimestamps ? "Timed" : "Untimed"}</span>
        <span>{transcript.hasSpeakerLabels ? `${transcript.speakers.length} speakers` : "No speaker labels"}</span>
        <span>Revision {transcript.revisionId.slice(0, 8)}</span>
      </div>
      {transcript.warnings.length > 0 && (
        <details className="rounded border border-amber-500/25 bg-amber-500/5 px-2 py-1.5">
          <summary className="cursor-pointer text-[10px] text-amber-300">
            Import warnings ({transcript.warnings.length})
          </summary>
          <ul className="mt-1 space-y-1 pl-4 text-[10px] text-amber-200/80">
            {transcript.warnings.map((warning, index) => <li key={index} className="list-disc">{warning}</li>)}
          </ul>
        </details>
      )}
      <div className="flex flex-wrap gap-1.5">
        <SourceAction
          label={analysisOperation ? "Analyzing..." : "Analyze"}
          icon={Sparkles}
          disabled={readOnly || Boolean(analysisOperation)}
          busy={Boolean(analysisOperation)}
          onClick={() => send({ type: "analyzeMeetingTranscript", transcriptId: transcript.id })}
        />
        {analysisOperation && (
          <SourceAction
            label={analysisOperation.stage === "Cancelling" ? "Cancelling..." : "Cancel"}
            icon={X}
            disabled={analysisOperation.stage === "Cancelling"}
            onClick={() => send({ type: "cancelMeetingProcessing", transcriptId: transcript.id })}
          />
        )}
        {transcript.originalAvailable && (
          <SourceAction
            label="Open original"
            icon={ExternalLink}
            onClick={() => send({
              type: "openMeetingTranscriptArtifact",
              transcriptId: transcript.id,
              artifact: "original",
            })}
          />
        )}
        {transcript.normalizedAvailable && (
          <SourceAction
            label="Open normalized"
            icon={ExternalLink}
            onClick={() => send({
              type: "openMeetingTranscriptArtifact",
              transcriptId: transcript.id,
              artifact: "normalized",
            })}
          />
        )}
        <SourceAction
          label="Delete"
          icon={Trash2}
          danger
          disabled={readOnly}
          onClick={() => {
            if (window.confirm("Delete this transcript version and its analyses? The original managed artifact will be removed.")) {
              send({ type: "deleteMeetingTranscript", transcriptId: transcript.id })
            }
          }}
        />
      </div>
    </div>
  )
}

function TranscriptEditor({
  draft,
  saving,
  saveError,
  confirmOpenRef,
  onChange,
  onSave,
  onCancel,
}: {
  draft: TranscriptDraft
  saving: boolean
  saveError: string | null
  confirmOpenRef: { current: boolean }
  onChange: (draft: TranscriptDraft) => void
  onSave: () => void
  onCancel: () => void
}) {
  const speakers = visibleDraftSpeakers(draft)
  const mergedAway = draft.speakers.filter((speaker) =>
    isSpeakerMergedAway(draft, speaker.speakerId))
  const speakerName = (speakerId: string) => {
    const speaker = draft.speakers.find((candidate) => candidate.speakerId === speakerId)
    return speaker ? speaker.displayName.trim() || speaker.originalLabel : speakerId
  }
  const requestMerge = (fromSpeakerId: string, intoSpeakerId: string) => {
    confirmOpenRef.current = true
    const confirmed = window.confirm(
      `Merge "${speakerName(fromSpeakerId)}" into "${speakerName(intoSpeakerId)}"? ` +
      "All matching segments in the new revision will use the target speaker.")
    confirmOpenRef.current = false
    if (confirmed) onChange(mergeDraftSpeaker(draft, fromSpeakerId, intoSpeakerId))
  }

  return (
    <div
      className="space-y-3"
      onKeyDown={(event) => {
        const action = transcriptEditorKeyAction(event, {
          confirmOpen: confirmOpenRef.current,
        })
        if (!action) return
        event.preventDefault()
        // Escape is consumed here so it cancels the edit instead of closing the MEET modal.
        event.stopPropagation()
        if (action === "save") onSave()
        else onCancel()
      }}
    >
      <p className="rounded border border-border bg-background/40 p-2 text-[10px] text-muted-foreground">
        Saving creates a new revision; the current transcript stays unchanged.
        Ctrl+Enter saves, Escape cancels.
      </p>
      {saveError && (
        <div className="flex items-start gap-2 rounded border border-destructive/30 bg-destructive/10 p-2 text-[10px] text-destructive" role="alert">
          <span className="min-w-0 flex-1">{saveError}</span>
        </div>
      )}
      {draft.speakers.length > 0 && (
        <section className="space-y-2 rounded-md border border-border bg-background/40 p-2.5">
          <h4 className="flex items-center gap-1.5 text-[10px] font-bold uppercase tracking-widest text-muted-foreground">
            <Users className="size-3" /> Speakers
          </h4>
          {speakers.map((speaker) => (
            <div key={speaker.speakerId} className="flex flex-wrap items-center gap-1.5">
              <input
                type="text"
                value={speaker.displayName}
                placeholder={speaker.originalLabel}
                aria-label={`Display name for speaker ${speaker.originalLabel || speaker.speakerId}`}
                onChange={(event) =>
                  onChange(renameDraftSpeaker(draft, speaker.speakerId, event.target.value))}
                className="h-7 min-w-0 flex-1 rounded border border-input bg-background px-2 text-[11px] text-foreground outline-none focus-visible:border-status-meet/60 focus-visible:ring-2 focus-visible:ring-status-meet/25"
              />
              {speaker.originalLabel &&
                speaker.displayName.trim() !== speaker.originalLabel && (
                <span className="max-w-24 truncate text-[9px] text-muted-foreground" title={`Original label: ${speaker.originalLabel}`}>
                  was {speaker.originalLabel}
                </span>
              )}
              <button
                type="button"
                aria-pressed={speaker.isCurrentUser}
                onClick={() => onChange(toggleDraftSpeakerAsYou(draft, speaker.speakerId))}
                className={cn(
                  "flex h-7 shrink-0 items-center gap-1 rounded border px-2 text-[10px] font-medium outline-none transition-colors focus-visible:ring-2 focus-visible:ring-ring",
                  speaker.isCurrentUser
                    ? "border-status-meet/45 bg-status-meet/15 text-status-meet"
                    : "border-border text-muted-foreground hover:bg-accent hover:text-foreground",
                )}
              >
                {speaker.isCurrentUser && <Check className="size-3" />}
                {speaker.isCurrentUser ? "You" : "Mark as You"}
              </button>
              {speakers.length > 1 && (
                <select
                  value=""
                  aria-label={`Merge speaker ${speaker.originalLabel || speaker.speakerId} into`}
                  onChange={(event) => {
                    if (event.target.value) requestMerge(speaker.speakerId, event.target.value)
                    event.target.value = ""
                  }}
                  className="h-7 shrink-0 rounded border border-input bg-background px-1.5 text-[10px] text-muted-foreground outline-none focus-visible:border-status-meet/60 focus-visible:ring-2 focus-visible:ring-status-meet/25"
                >
                  <option value="">Merge into...</option>
                  {speakers
                    .filter((target) => target.speakerId !== speaker.speakerId)
                    .map((target) => (
                      <option key={target.speakerId} value={target.speakerId}>
                        {target.displayName.trim() || target.originalLabel}
                      </option>
                    ))}
                </select>
              )}
            </div>
          ))}
          {mergedAway.map((speaker) => (
            <div key={speaker.speakerId} className="flex items-center gap-1.5 text-[10px] text-muted-foreground">
              <span className="min-w-0 truncate">
                {speaker.displayName.trim() || speaker.originalLabel} merged into{" "}
                {speakerName(draft.merges[speaker.speakerId])}
              </span>
              <button
                type="button"
                onClick={() => onChange(undoDraftSpeakerMerge(draft, speaker.speakerId))}
                className="flex h-6 shrink-0 items-center gap-1 rounded border border-border px-1.5 outline-none hover:bg-accent hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
              >
                <Undo2 className="size-3" /> Undo
              </button>
            </div>
          ))}
        </section>
      )}
      <div className="space-y-2">
        {draft.segments.map((segment) => {
          const name = draftSegmentSpeakerName(draft, segment)
          return (
            <div key={segment.index} className="grid grid-cols-[auto_minmax(0,1fr)] gap-2">
              <div className="w-14 pt-1.5">
                <span className="font-mono text-[10px] text-muted-foreground">
                  {segment.startSeconds == null ? "" : formatOffset(segment.startSeconds)}
                </span>
              </div>
              <div className="min-w-0 space-y-1">
                {name && (
                  <span className="block text-[10px] font-semibold text-status-meet">{name}</span>
                )}
                <textarea
                  value={segment.text}
                  aria-label={`Segment ${segment.index + 1} text`}
                  disabled={saving}
                  rows={Math.min(6, Math.max(2, Math.ceil(segment.text.length / 90) + 1))}
                  onChange={(event) =>
                    onChange(setDraftSegmentText(draft, segment.index, event.target.value))}
                  className="w-full resize-y rounded border border-input bg-background px-2 py-1.5 text-[12px] leading-relaxed text-foreground outline-none focus-visible:border-status-meet/60 focus-visible:ring-2 focus-visible:ring-status-meet/25 disabled:opacity-60"
                />
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}

function NeutralNotice({ message, onDismiss }: { message: string; onDismiss?: () => void }) {
  return (
    <div className="flex items-start gap-2 rounded border border-status-meet/30 bg-status-meet/10 p-2 text-[10px] text-foreground" role="status">
      <Info className="mt-0.5 size-3 shrink-0 text-status-meet" aria-hidden="true" />
      <span className="min-w-0 flex-1">{message}</span>
      {onDismiss && (
        <button type="button" onClick={onDismiss} aria-label="Dismiss notice" className="text-muted-foreground hover:text-foreground">
          <X className="size-3" />
        </button>
      )}
    </div>
  )
}

function TranscriptContent({
  transcript,
  screenshots,
  send,
}: {
  transcript: MeetingTranscriptSnapshot
  screenshots: MeetingScreenshotSnapshot[]
  send: (command: WorkspaceMeetingAssistantCommand) => boolean
}) {
  const inlineScreenshots = screenshots.filter((screenshot) =>
    screenshot.recordingId === transcript.recordingId &&
    screenshot.offsetFromRecordingStartSeconds != null)
  const rows = [
    ...transcript.segments.map((segment) => ({
      kind: "segment" as const,
      key: `segment:${segment.index}`,
      at: segment.startSeconds ?? Number.MAX_SAFE_INTEGER,
      segment,
    })),
    ...inlineScreenshots.map((screenshot) => ({
      kind: "screenshot" as const,
      key: `screenshot:${screenshot.id}`,
      at: screenshot.offsetFromRecordingStartSeconds ?? Number.MAX_SAFE_INTEGER,
      screenshot,
    })),
  ].sort((left, right) => left.at - right.at)

  if (rows.length === 0) {
    return <p className="whitespace-pre-wrap text-[12px] leading-relaxed text-foreground">{transcript.text}</p>
  }

  return (
    <div className="space-y-2">
      {rows.map((row) => row.kind === "segment" ? (
        <div key={row.key} className="grid grid-cols-[auto_minmax(0,1fr)] gap-2 text-[12px] leading-relaxed">
          <span className="w-14 pt-0.5 font-mono text-[10px] text-muted-foreground">
            {row.segment.startSeconds == null ? "" : formatOffset(row.segment.startSeconds)}
          </span>
          <p className="min-w-0 whitespace-pre-wrap text-foreground">
            {row.segment.speakerName && (
              <strong className="mr-1.5 text-status-meet">{row.segment.speakerName}:</strong>
            )}
            {row.segment.text}
          </p>
        </div>
      ) : (
        <button
          type="button"
          key={row.key}
          onClick={() => send({ type: "openMeetingScreenshot", screenshotId: row.screenshot.id })}
          className="flex w-full items-center gap-2 rounded-md border border-status-meet/25 bg-status-meet/5 p-2 text-left"
        >
          {row.screenshot.thumbnailDataUrl && (
            // The image is a WPF-provided managed artifact data URL, never a remote source.
            // eslint-disable-next-line @next/next/no-img-element
            <img src={row.screenshot.thumbnailDataUrl} alt="" className="h-12 w-20 rounded object-cover" />
          )}
          <span className="min-w-0 flex-1">
            <strong className="block text-[11px] text-foreground">
              {formatOffset(row.at)} Screenshot
            </strong>
            <span className="block truncate text-[10px] text-muted-foreground">{row.screenshot.sourceLabel}</span>
          </span>
        </button>
      ))}
    </div>
  )
}

function ScreenshotCard({
  screenshot,
  readOnly,
  send,
  compact = false,
}: {
  screenshot: MeetingScreenshotSnapshot
  readOnly: boolean
  send: (command: WorkspaceMeetingAssistantCommand) => boolean
  compact?: boolean
}) {
  return (
    <div className="min-w-0 overflow-hidden rounded-md border border-border bg-background/50">
      {screenshot.thumbnailDataUrl ? (
        <button
          type="button"
          onClick={() => send({ type: "openMeetingScreenshot", screenshotId: screenshot.id })}
          className="block w-full bg-black/30"
        >
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src={screenshot.thumbnailDataUrl}
            alt={`Screenshot from ${screenshot.sourceLabel}`}
            className={cn("w-full object-cover", compact ? "h-24" : "h-32")}
          />
        </button>
      ) : (
        <div className="flex h-20 items-center justify-center text-muted-foreground">
          <ImageIcon className="size-5" />
        </div>
      )}
      <div className="space-y-1.5 p-2">
        <div className="flex min-w-0 items-center gap-2 text-[10px]">
          <span className="shrink-0 font-mono text-status-meet">
            {screenshot.offsetFromRecordingStartSeconds == null
              ? new Date(screenshot.capturedAtUtc).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })
              : formatOffset(screenshot.offsetFromRecordingStartSeconds)}
          </span>
          <span className="truncate text-muted-foreground">{screenshot.sourceKind}: {screenshot.sourceLabel}</span>
        </div>
        <div className="flex flex-wrap gap-1.5">
          <SourceAction
            label="Open"
            icon={ExternalLink}
            disabled={!screenshot.isAvailable}
            onClick={() => send({ type: "openMeetingScreenshot", screenshotId: screenshot.id })}
          />
          <SourceAction
            label="Delete"
            icon={Trash2}
            danger
            disabled={readOnly}
            onClick={() => {
              if (window.confirm("Delete this managed screenshot?")) {
                send({ type: "deleteMeetingScreenshot", screenshotId: screenshot.id })
              }
            }}
          />
        </div>
      </div>
    </div>
  )
}

function EmptySource({ text }: { text: string }) {
  return (
    <p className="rounded-md border border-dashed border-border p-3 text-center text-[11px] text-muted-foreground">
      {text}
    </p>
  )
}

function OperationStatus({
  operation,
  message,
}: {
  operation: MeetingOperationSnapshot
  message: string
}) {
  const [elapsed, setElapsed] = useState(() => Date.now())
  useEffect(() => {
    const timer = window.setInterval(() => setElapsed(Date.now()), 1_000)
    return () => window.clearInterval(timer)
  }, [operation.id])
  const seconds = Math.max(0, Math.floor((elapsed - Date.parse(operation.startedAtUtc)) / 1_000))
  const status = operation.stage === "StartingAnalysis" ? "Starting analysis..."
    : operation.stage === "StartingTranscription" ? "Starting transcription..."
    : operation.stage === "PreparingAudio" ? "Preparing audio..."
    : operation.stage === "Transcribing" ? "Transcribing..."
    : operation.stage === "Cancelling" ? "Cancelling..."
    : "Analyzing transcript..."
  return (
    <div
      className="space-y-1.5 rounded-md border border-status-meet/30 bg-status-meet/10 p-3 text-[11px]"
      role="status"
      aria-live="polite"
      aria-busy="true"
    >
      <div className="flex items-center gap-2 font-medium text-status-meet">
        <LoaderCircle className="size-3.5 animate-spin motion-reduce:animate-none" aria-hidden="true" />
        <span>{status}</span>
        <span className="ml-auto font-mono text-[10px]" aria-hidden="true">
          {Math.floor(seconds / 60)}:{String(seconds % 60).padStart(2, "0")}
        </span>
      </div>
      <p className="text-muted-foreground">{message}</p>
    </div>
  )
}

function SourceAction({
  label,
  icon: Icon,
  onClick,
  disabled = false,
  danger = false,
  busy = false,
  className,
}: {
  label: string
  icon: typeof Upload
  onClick: () => void
  disabled?: boolean
  danger?: boolean
  busy?: boolean
  className?: string
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      aria-busy={busy}
      onClick={(event) => {
        event.stopPropagation()
        onClick()
      }}
      onKeyDown={(event) => event.stopPropagation()}
      className={cn(
        "flex h-8 shrink-0 items-center gap-1.5 rounded-md border px-2.5 text-[10px] font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-40",
        danger
          ? "border-destructive/40 text-destructive hover:bg-destructive/10"
          : "border-border text-muted-foreground hover:bg-accent hover:text-foreground",
        className,
      )}
    >
      {busy
        ? <LoaderCircle className="size-3.5 animate-spin motion-reduce:animate-none" aria-hidden="true" />
        : <Icon className="size-3.5" />}
      {label}
    </button>
  )
}

function formatOffset(value: number): string {
  const seconds = Math.max(0, Math.round(value))
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  const remainder = seconds % 60
  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, "0")}:${String(remainder).padStart(2, "0")}`
    : `${minutes}:${String(remainder).padStart(2, "0")}`
}
