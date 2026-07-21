"use client"

import { useCallback, useEffect, useRef, useState } from "react"

import {
  activeTranscriptSegmentIndex,
  captureSafeMediaFailureState,
  isNativeAudioPlaybackEvent,
  mediaPlaybackFailureLabel,
  postNativeAudioPlaybackCommand,
  projectNativeTranscriptPlayback,
  projectTranscriptScreenshots,
  probeTranscriptAudioEndpoint,
  selectTranscriptPlaybackMode,
  shouldAutoScrollTranscript,
  transcriptAudioUnavailableLabel,
  transcriptSpeakerLabel,
} from "@/lib/meet-transcript-audio"
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
import { Button } from "@/components/ui/button"
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
  setDraftCurrentUser,
  setDraftSegmentText,
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
  /** Opens the internal discard dialog when dirty; runs onDiscard after a discard. */
  requestExit: (onDiscard?: () => void) => boolean
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
  const transcriptLineages = groupTranscriptLineages(meetTranscripts)
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
          <section className="space-y-2 rounded-lg border border-border bg-surface-sunken p-3" role="radiogroup" aria-label="Transcript versions">
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
            ) : transcriptLineages.map((lineage) => (
              <TranscriptLineage
                key={lineage.original.id}
                meet={meet}
                lineage={lineage}
                readOnly={readOnly}
                send={send}
                operations={operations}
              />
            ))}
          </section>

          <section className="space-y-2 rounded-lg border border-border bg-surface-sunken p-3">
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
  const [discardDialogOpen, setDiscardDialogOpen] = useState(false)
  const draftRef = useRef(draft)
  draftRef.current = draft
  const savingRef = useRef(saving)
  savingRef.current = saving
  const confirmOpenRef = useRef(false)
  const pendingDiscardActionRef = useRef<(() => void) | null>(null)

  const closeEditor = useCallback(() => {
    setDraft(null)
    setSaveError(null)
  }, [])

  const requestExit = useCallback((onDiscard?: () => void): boolean => {
    const current = draftRef.current
    if (!current) {
      onDiscard?.()
      return true
    }
    if (savingRef.current) return false
    if (isDraftDirty(current)) {
      confirmOpenRef.current = true
      pendingDiscardActionRef.current = onDiscard ?? null
      setDiscardDialogOpen(true)
      return false
    }
    closeEditor()
    onDiscard?.()
    return true
  }, [closeEditor])

  const keepEditing = useCallback(() => {
    confirmOpenRef.current = false
    pendingDiscardActionRef.current = null
    setDiscardDialogOpen(false)
  }, [])

  const discardEdits = useCallback(() => {
    const onDiscard = pendingDiscardActionRef.current
    pendingDiscardActionRef.current = null
    confirmOpenRef.current = false
    setDiscardDialogOpen(false)
    closeEditor()
    onDiscard?.()
  }, [closeEditor])

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
    <>
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
                  ? transcriptMetadata(activeTranscript)
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
                    className="flex h-7 items-center gap-1 rounded border border-primary/40 bg-primary/10 px-2 text-[10px] font-medium text-primary outline-none hover:bg-primary/20 focus-visible:ring-2 focus-visible:ring-ring"
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
                  className="flex h-7 items-center gap-1 rounded border border-primary/40 bg-primary/10 px-2 text-[10px] font-medium text-primary outline-none hover:bg-primary/20 focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-40"
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
              <TranscriptPlayback transcript={activeTranscript} screenshots={meetScreenshots} send={send} />
            ) : (
              <EmptySource text="Review will use the explicitly active transcript." />
            )}
          </div>
        </section>
      </div>

      <div className="flex min-h-0 flex-col gap-3 overflow-y-auto p-4 [scrollbar-gutter:stable]">
          {/* Peer panel to "Active transcript" on the left - same bg-card tier. */}
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
                    className="flex h-7 items-center gap-1 rounded border border-primary/40 bg-primary/10 px-2 text-[10px] font-medium text-primary disabled:opacity-40"
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

          {/* Peer panel to Active transcript / Meeting Assistant - same tier. */}
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
    {discardDialogOpen && (
      <div className="absolute inset-0 z-30 flex items-center justify-center bg-black/55 p-4" role="presentation">
        <section
          role="dialog"
          aria-modal="true"
          aria-labelledby="discard-transcript-title"
          className="w-full max-w-sm rounded-lg border border-border bg-card p-4 shadow-xl"
        >
          <h3 id="discard-transcript-title" className="text-sm font-semibold text-foreground">
            Discard unsaved transcript edits?
          </h3>
          <p className="mt-1.5 text-xs leading-relaxed text-muted-foreground">
            Your changes are only in this editor and have not been saved as a revision.
          </p>
          <div className="mt-4 flex justify-end gap-2">
            <button type="button" onClick={keepEditing} className="rounded border border-border px-3 py-1.5 text-xs text-foreground hover:bg-accent">
              Keep editing
            </button>
            <button type="button" onClick={discardEdits} className="rounded border border-destructive/45 bg-destructive/10 px-3 py-1.5 text-xs font-medium text-destructive hover:bg-destructive/20">
              Discard
            </button>
          </div>
        </section>
      </div>
    )}
    </>
  )
}

interface TranscriptLineageGroup {
  original: MeetingTranscriptSnapshot
  latest: MeetingTranscriptSnapshot
  previousEdits: MeetingTranscriptSnapshot[]
}

function groupTranscriptLineages(transcripts: MeetingTranscriptSnapshot[]): TranscriptLineageGroup[] {
  const byId = new Map(transcripts.map((transcript) => [transcript.id, transcript]))
  const rootIdFor = (transcript: MeetingTranscriptSnapshot) => {
    let current = transcript
    const visited = new Set<string>()
    while (current.sourceTranscriptId && !visited.has(current.id)) {
      visited.add(current.id)
      const source = byId.get(current.sourceTranscriptId)
      if (!source) break
      current = source
    }
    return current.id
  }
  const grouped = new Map<string, MeetingTranscriptSnapshot[]>()
  for (const transcript of transcripts) {
    const rootId = rootIdFor(transcript)
    grouped.set(rootId, [...(grouped.get(rootId) ?? []), transcript])
  }
  return [...grouped.values()].map((lineage) => {
    const ordered = [...lineage].sort((left, right) => left.createdAtUtc.localeCompare(right.createdAtUtc))
    const original = ordered.find((transcript) => transcript.sourceTranscriptId === null) ?? ordered[0]
    const latest = ordered.at(-1) ?? original
    return {
      original,
      latest,
      previousEdits: ordered.filter((transcript) =>
        transcript.id !== original.id && transcript.id !== latest.id),
    }
  }).sort((left, right) => right.latest.createdAtUtc.localeCompare(left.latest.createdAtUtc))
}

function TranscriptLineage({
  meet,
  lineage,
  readOnly,
  send,
  operations,
}: {
  meet: MeetItem
  lineage: TranscriptLineageGroup
  readOnly: boolean
  send: (command: WorkspaceMeetingAssistantCommand) => boolean
  operations: MeetingOperationSnapshot[]
}) {
  const hasEditedLatest = lineage.latest.id !== lineage.original.id
  return (
    <div className="space-y-1.5">
      <p className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">Original</p>
      <TranscriptSourceCard meet={meet} transcript={lineage.original} readOnly={readOnly} send={send} operations={operations} />
      {hasEditedLatest && (
        <>
          <p className="pt-1 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">Latest edited revision</p>
          <TranscriptSourceCard meet={meet} transcript={lineage.latest} readOnly={readOnly} send={send} operations={operations} />
        </>
      )}
      {lineage.previousEdits.length > 0 && (
        <details className="rounded border border-border bg-card px-2 py-1.5">
          <summary className="cursor-pointer text-[10px] font-medium text-muted-foreground hover:text-foreground">
            Previous revisions ({lineage.previousEdits.length})
          </summary>
          <div className="mt-2 space-y-1.5">
            {[...lineage.previousEdits].reverse().map((transcript) => (
              <TranscriptSourceCard key={transcript.id} meet={meet} transcript={transcript} readOnly={readOnly} send={send} operations={operations} />
            ))}
          </div>
        </details>
      )}
    </div>
  )
}

function transcriptMetadata(transcript: MeetingTranscriptSnapshot): string {
  const origin = transcriptOriginLabel(transcript.origin)
  const source = transcript.sourceLabel || transcript.provider
  const parts = [origin]
  if (source && source.trim().toLocaleLowerCase() !== origin.toLocaleLowerCase()) {
    parts.push(source)
  }
  parts.push(new Date(transcript.createdAtUtc).toLocaleDateString([], { day: "numeric", month: "short" }))
  return parts.join(" · ")
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
    // Whole-card selection with identical geometry in both states. Resting
    // cards use the canonical --card surface (already distinct from the
    // section's own --surface-sunken tray, see TranscriptLineage/screenshot
    // section wrappers). Selected uses the canonical row-selected pair
    // (--row-selected/--row-selected-border, the same tokens Tree row
    // selection already uses): mixing --selection into --surface reads as
    // RAISED/lighter in Dark and TINTED/slightly darker in Light - never
    // "sunken" or disabled in either theme - plus a stronger selection-tinted
    // border and the existing Active check as a second, explicit marker.
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
      "space-y-2 rounded-md border p-2.5 outline-none transition-colors focus-visible:shadow-[var(--focus-ring)]",
      transcript.isActive
        ? "border-row-selected-border bg-row-selected"
        : "border-border bg-card",
      !readOnly && !transcript.isActive && "cursor-pointer hover:border-border-strong hover:bg-surface-raised",
    )}>
      <div className="flex min-w-0 flex-wrap items-center gap-2">
        <span className={cn(
          "rounded px-1.5 py-0.5 text-[9px] font-semibold uppercase ring-1 ring-inset",
          transcript.origin === "Imported"
            ? "bg-status-wait/10 text-status-wait ring-status-wait/30"
            : transcript.origin === "UserEdited"
              ? "bg-status-focus/10 text-status-focus ring-status-focus/30"
              : "bg-status-meet/10 text-status-meet ring-status-meet/30",
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
  const [mergeSourceId, setMergeSourceId] = useState("")
  const [mergeTargetId, setMergeTargetId] = useState("")
  const speakerName = (speakerId: string) => {
    const speaker = draft.speakers.find((candidate) => candidate.speakerId === speakerId)
    return speaker ? speaker.displayName.trim() || speaker.originalLabel : speakerId
  }
  const mergeSpeakers = () => {
    if (!mergeSourceId || !mergeTargetId || mergeSourceId === mergeTargetId) return
    onChange(mergeDraftSpeaker(draft, mergeSourceId, mergeTargetId))
    setMergeSourceId("")
    setMergeTargetId("")
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
      <p className="rounded border border-border bg-surface-sunken p-2 text-[10px] text-muted-foreground">
        Saving creates a new revision; the current transcript stays unchanged.
        Ctrl+Enter saves, Escape cancels.
      </p>
      {saveError && (
        <div className="flex items-start gap-2 rounded border border-destructive/30 bg-destructive/10 p-2 text-[10px] text-destructive" role="alert">
          <span className="min-w-0 flex-1">{saveError}</span>
        </div>
      )}
      {draft.speakers.length > 0 && (
        <section className="space-y-2 rounded-md border border-border bg-surface-sunken p-2.5">
          <h4 className="flex items-center gap-1.5 text-[10px] font-bold uppercase tracking-widest text-muted-foreground">
            <Users className="size-3" /> Speakers
          </h4>
          <label className="block space-y-1">
            <span className="text-[10px] font-medium text-muted-foreground">You</span>
            <select
              value={speakers.find((speaker) => speaker.isCurrentUser)?.speakerId ?? ""}
              aria-label="Current user speaker"
              onChange={(event) => onChange(setDraftCurrentUser(draft, event.target.value || null))}
              className="h-7 w-full rounded border border-input bg-background px-2 text-[11px] text-foreground outline-none focus-visible:border-primary/60 focus-visible:ring-2 focus-visible:ring-ring/25"
            >
              <option value="">No speaker selected</option>
              {speakers.map((speaker) => (
                <option key={speaker.speakerId} value={speaker.speakerId}>
                  {speakerName(speaker.speakerId)}
                </option>
              ))}
            </select>
          </label>
          {speakers.map((speaker) => (
            <div key={speaker.speakerId} className="flex flex-wrap items-center gap-1.5">
              <input
                type="text"
                value={speaker.displayName}
                placeholder={speaker.originalLabel}
                aria-label={`Display name for speaker ${speaker.originalLabel || speaker.speakerId}`}
                onChange={(event) =>
                  onChange(renameDraftSpeaker(draft, speaker.speakerId, event.target.value))}
                className="h-7 min-w-0 flex-1 rounded border border-input bg-background px-2 text-[11px] text-foreground outline-none focus-visible:border-primary/60 focus-visible:ring-2 focus-visible:ring-ring/25"
              />
              {speaker.isCurrentUser && (
                <span className="rounded border border-status-meet/45 bg-status-meet/15 px-2 py-1 text-[10px] font-medium text-status-meet">
                  You
                </span>
              )}
            </div>
          ))}
          {speakers.length > 1 && (
            <div className="space-y-1.5 border-t border-border pt-2">
              <div className="flex flex-wrap items-center gap-1.5">
                <select
                  value={mergeSourceId}
                  aria-label="Merge source speaker"
                  onChange={(event) => setMergeSourceId(event.target.value)}
                  className="h-7 min-w-0 flex-1 rounded border border-input bg-background px-2 text-[10px] text-foreground"
                >
                  <option value="">Source speaker</option>
                  {speakers.map((speaker) => <option key={speaker.speakerId} value={speaker.speakerId}>{speakerName(speaker.speakerId)}</option>)}
                </select>
                <span className="text-[10px] text-muted-foreground">into</span>
                <select
                  value={mergeTargetId}
                  aria-label="Merge target speaker"
                  onChange={(event) => setMergeTargetId(event.target.value)}
                  className="h-7 min-w-0 flex-1 rounded border border-input bg-background px-2 text-[10px] text-foreground"
                >
                  <option value="">Target speaker</option>
                  {speakers.filter((speaker) => speaker.speakerId !== mergeSourceId).map((speaker) => <option key={speaker.speakerId} value={speaker.speakerId}>{speakerName(speaker.speakerId)}</option>)}
                </select>
                <button
                  type="button"
                  disabled={!mergeSourceId || !mergeTargetId || mergeSourceId === mergeTargetId}
                  onClick={mergeSpeakers}
                  className="h-7 rounded border border-border px-2 text-[10px] font-medium text-foreground hover:bg-accent disabled:cursor-not-allowed disabled:opacity-40"
                >
                  Merge
                </button>
              </div>
              <p className="text-[9px] leading-relaxed text-muted-foreground">
                The source speaker disappears from this revision; the target speaker remains.
              </p>
            </div>
          )}
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
                  className="w-full resize-y rounded border border-input bg-background px-2 py-1.5 text-[12px] leading-relaxed text-foreground outline-none focus-visible:border-primary/60 focus-visible:ring-2 focus-visible:ring-ring/25 disabled:opacity-60"
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
    <div className="flex items-start gap-2 rounded border border-primary/30 bg-primary/10 p-2 text-[10px] text-foreground" role="status">
      <Info className="mt-0.5 size-3 shrink-0 text-primary" aria-hidden="true" />
      <span className="min-w-0 flex-1">{message}</span>
      {onDismiss && (
        <button type="button" onClick={onDismiss} aria-label="Dismiss notice" className="text-muted-foreground hover:text-foreground">
          <X className="size-3" />
        </button>
      )}
    </div>
  )
}

function TranscriptPlayback({
  transcript,
  screenshots,
  send,
}: {
  transcript: MeetingTranscriptSnapshot
  screenshots: MeetingScreenshotSnapshot[]
  send: (command: WorkspaceMeetingAssistantCommand) => boolean
}) {
  const audioRef = useRef<HTMLAudioElement | null>(null)
  const segmentRefs = useRef(new Map<number, HTMLDivElement>())
  const programmaticScrollRef = useRef(false)
  const lastManualScrollAtRef = useRef<number | null>(null)
  const previousActiveIndexRef = useRef<number | null>(null)
  const [activeSegmentIndex, setActiveSegmentIndex] = useState<number | null>(null)
  const [audioDuration, setAudioDuration] = useState(transcript.audio.durationSeconds)
  const [isPlaying, setIsPlaying] = useState(false)
  const [autoScroll, setAutoScroll] = useState(true)
  const [runtimeUnavailable, setRuntimeUnavailable] = useState<string | null>(null)
  const [nativePosition, setNativePosition] = useState(0)
  const [nativeCommandPending, setNativeCommandPending] = useState(false)
  const [nativeSessionReady, setNativeSessionReady] = useState(false)
  const [playbackEnvironment, setPlaybackEnvironment] = useState<
    "Detecting" | "Connected" | "Browser"
  >("Detecting")
  const playbackMode = playbackEnvironment === "Detecting" ? null :
    selectTranscriptPlaybackMode({
      hasWebViewBridge: playbackEnvironment === "Connected",
      audioStatus: transcript.audio.status,
      recordingId: transcript.recordingId,
      audioUrl: transcript.audio.url,
    })
  const audioAvailable = (playbackMode === "Native" || playbackMode === "Browser") &&
    !runtimeUnavailable
  const browserSourceStart = transcript.audio.startSeconds ?? 0
  const browserSourceEnd = transcript.audio.endSeconds ??
    browserSourceStart + transcript.audio.durationSeconds

  useEffect(() => {
    setPlaybackEnvironment(window.chrome?.webview ? "Connected" : "Browser")
  }, [])

  useEffect(() => {
    setActiveSegmentIndex(null)
    setAudioDuration(transcript.audio.durationSeconds)
    setIsPlaying(false)
    setRuntimeUnavailable(null)
    setNativePosition(0)
    setNativeCommandPending(false)
    setNativeSessionReady(false)
    previousActiveIndexRef.current = null
    lastManualScrollAtRef.current = null
  }, [transcript.id, transcript.audio.url, transcript.audio.durationSeconds])

  useEffect(() => {
    const changed = previousActiveIndexRef.current !== activeSegmentIndex
    previousActiveIndexRef.current = activeSegmentIndex
    if (activeSegmentIndex == null || !shouldAutoScrollTranscript({
      enabled: autoScroll,
      isPlaying,
      activeSegmentChanged: changed,
      nowMs: Date.now(),
      lastManualScrollAtMs: lastManualScrollAtRef.current,
    })) return

    const element = segmentRefs.current.get(activeSegmentIndex)
    if (!element) return
    programmaticScrollRef.current = true
    element.scrollIntoView({ block: "nearest", behavior: "smooth" })
    window.setTimeout(() => { programmaticScrollRef.current = false }, 350)
  }, [activeSegmentIndex, autoScroll, isPlaying])

  useEffect(() => {
    const firstSegment = segmentRefs.current.values().next().value as HTMLDivElement | undefined
    let scrollContainer = firstSegment?.parentElement ?? null
    while (scrollContainer) {
      const style = window.getComputedStyle(scrollContainer)
      if (scrollContainer.scrollHeight > scrollContainer.clientHeight &&
          ["auto", "scroll"].includes(style.overflowY)) break
      scrollContainer = scrollContainer.parentElement
    }
    if (!scrollContainer) return

    const onScroll = () => {
      if (isPlaying && !programmaticScrollRef.current) {
        lastManualScrollAtRef.current = Date.now()
      }
    }
    scrollContainer.addEventListener("scroll", onScroll, { passive: true })
    return () => scrollContainer?.removeEventListener("scroll", onScroll)
  }, [isPlaying, transcript.id])

  useEffect(() => {
    const webview = window.chrome?.webview
    const recordingId = transcript.recordingId
    if (playbackMode !== "Native" || !webview || !recordingId) return
    const onMessage = (event: { data: unknown }) => {
      if (!isNativeAudioPlaybackEvent(event.data) ||
          event.data.recordingId !== recordingId ||
          event.data.transcriptId !== transcript.id) return
      const projection = projectNativeTranscriptPlayback(
        event.data,
        transcript.segments,
        transcript.audio.durationSeconds,
      )
      setNativeCommandPending(false)
      setNativeSessionReady(event.data.state !== "Failed")
      setNativePosition(projection.positionSeconds)
      setAudioDuration(projection.durationSeconds)
      setIsPlaying(projection.isPlaying)
      setActiveSegmentIndex(projection.activeSegmentIndex)
      if (projection.failureReason) setRuntimeUnavailable(projection.failureReason)
    }
    webview.addEventListener("message", onMessage)
    return () => {
      webview.removeEventListener("message", onMessage)
      postNativeAudioPlaybackCommand({
        action: "stop",
        recordingId,
        transcriptId: transcript.id,
      })
    }
  }, [playbackMode, transcript.id, transcript.recordingId])

  const updateActiveSegment = () => {
    const player = audioRef.current
    if (!player) return
    if (player.currentTime >= browserSourceEnd) {
      player.pause()
      player.currentTime = browserSourceEnd
    }
    setActiveSegmentIndex(activeTranscriptSegmentIndex(
      transcript.segments,
      Math.max(0, player.currentTime - browserSourceStart),
      audioDuration,
    ))
  }

  const seekToSegment = async (startSeconds: number | null) => {
    if (startSeconds == null || !audioAvailable) return
    if (playbackMode === "Native" && transcript.recordingId) {
      setNativePosition(startSeconds)
      const posted = postNativeAudioPlaybackCommand({
        action: "play",
        recordingId: transcript.recordingId,
        transcriptId: transcript.id,
        positionSeconds: startSeconds,
      })
      if (posted) setNativeCommandPending(true)
      else setRuntimeUnavailable("Native playback command failed")
      return
    }
    const player = audioRef.current
    if (!player) return
    try {
      player.currentTime = browserSourceStart + startSeconds
      await player.play()
      updateActiveSegment()
    } catch {
      setRuntimeUnavailable("Unknown playback failure")
      setIsPlaying(false)
    }
  }

  const activeSegment = transcript.segments.find((segment) =>
    segment.index === activeSegmentIndex)
  const nowSpeaking = transcriptSpeakerLabel(activeSegment, transcript.speakers)

  return (
    <div className="space-y-2">
      {audioAvailable && playbackMode === "Browser" && (
        <div className="sticky top-0 z-10 space-y-1.5 rounded-md border border-border bg-card px-2.5 py-2 shadow-sm">
          <div className="flex items-center gap-2">
            <audio
              ref={audioRef}
              src={transcript.audio.url ?? undefined}
              controls
              preload="metadata"
              aria-label="Transcript recording"
              className="h-8 min-w-0 flex-1"
              onLoadedMetadata={(event) => {
                event.currentTarget.currentTime = browserSourceStart
                updateActiveSegment()
              }}
              onTimeUpdate={updateActiveSegment}
              onSeeked={updateActiveSegment}
              onPlay={() => { setIsPlaying(true); updateActiveSegment() }}
              onPause={() => setIsPlaying(false)}
              onEnded={() => { setIsPlaying(false); setActiveSegmentIndex(null) }}
              onError={(event) => {
                const media = event.currentTarget
                const safeState = captureSafeMediaFailureState(media)
                setIsPlaying(false)
                void probeTranscriptAudioEndpoint(transcript.audio.url ?? "")
                  .then((probe) => {
                    setRuntimeUnavailable(mediaPlaybackFailureLabel(safeState, probe))
                  })
              }}
            />
            <label className="flex shrink-0 items-center gap-1.5 text-[10px] text-muted-foreground">
              <input
                type="checkbox"
                checked={autoScroll}
                onChange={(event) => setAutoScroll(event.target.checked)}
                className="size-3 accent-[var(--accent)]"
              />
              Auto-scroll
            </label>
          </div>
          <p className="min-h-4 text-[10px] text-muted-foreground" aria-live="polite">
            {isPlaying && nowSpeaking ? <>Now speaking: <strong className="text-foreground">{nowSpeaking}</strong></> : " "}
          </p>
        </div>
      )}
      {audioAvailable && playbackMode === "Native" && transcript.recordingId && (
        <div className="sticky top-0 z-10 space-y-1.5 rounded-md border border-border bg-card px-2.5 py-2 shadow-sm">
          <div className="flex items-center gap-2">
            <Button
              type="button"
              tone="secondary"
              size="sm"
              disabled={nativeCommandPending}
              onClick={() => {
                const action = isPlaying ? "pause" : "play"
                const posted = postNativeAudioPlaybackCommand({
                  action,
                  recordingId: transcript.recordingId!,
                  transcriptId: transcript.id,
                  positionSeconds: nativePosition,
                })
                if (posted && action === "play") setNativeCommandPending(true)
                if (!posted) setRuntimeUnavailable("Native playback command failed")
              }}
            >
              {nativeCommandPending ? "Loading…" : isPlaying ? "Pause" : "Play"}
            </Button>
            <input
              type="range"
              min={0}
              max={Math.max(audioDuration, 0.1)}
              step={0.1}
              value={Math.min(nativePosition, Math.max(audioDuration, 0.1))}
              disabled={!nativeSessionReady || nativeCommandPending}
              aria-label="Transcript recording position"
              className="min-w-0 flex-1 accent-[var(--accent)]"
              onChange={(event) => {
                const positionSeconds = Number(event.currentTarget.value)
                setNativePosition(positionSeconds)
                const posted = postNativeAudioPlaybackCommand({
                  action: "seek",
                  recordingId: transcript.recordingId!,
                  transcriptId: transcript.id,
                  positionSeconds,
                })
                if (!posted) setRuntimeUnavailable("Native playback command failed")
              }}
            />
            <label className="flex shrink-0 items-center gap-1.5 text-[10px] text-muted-foreground">
              <input
                type="checkbox"
                checked={autoScroll}
                onChange={(event) => setAutoScroll(event.target.checked)}
                className="size-3 accent-[var(--accent)]"
              />
              Auto-scroll
            </label>
          </div>
          <p className="min-h-4 text-[10px] text-muted-foreground" aria-live="polite">
            {isPlaying && nowSpeaking ? <>Now speaking: <strong className="text-foreground">{nowSpeaking}</strong></> : " "}
          </p>
        </div>
      )}
        {(transcript.audio.status === "Unavailable" || runtimeUnavailable) && (
          <div className="rounded border border-border px-2.5 py-1.5 text-[10px] text-muted-foreground" role="status">
            Audio unavailable: {runtimeUnavailable
              ? runtimeUnavailable
              : transcriptAudioUnavailableLabel(transcript.audio.unavailableReason)}
          </div>
      )}
      <TranscriptContent
        transcript={transcript}
        screenshots={screenshots}
        send={send}
        activeSegmentIndex={activeSegmentIndex}
        audioAvailable={audioAvailable}
        onSeekSegment={(startSeconds) => void seekToSegment(startSeconds)}
        registerSegment={(index, element) => {
          if (element) segmentRefs.current.set(index, element)
          else segmentRefs.current.delete(index)
        }}
      />
    </div>
  )
}

function TranscriptContent({
  transcript,
  screenshots,
  send,
  activeSegmentIndex,
  audioAvailable,
  onSeekSegment,
  registerSegment,
}: {
  transcript: MeetingTranscriptSnapshot
  screenshots: MeetingScreenshotSnapshot[]
  send: (command: WorkspaceMeetingAssistantCommand) => boolean
  activeSegmentIndex: number | null
  audioAvailable: boolean
  onSeekSegment: (startSeconds: number | null) => void
  registerSegment: (index: number, element: HTMLDivElement | null) => void
}) {
  const displaySpeakerName = (segment: MeetingTranscriptSnapshot["segments"][number]) => {
    const speaker = segment.speakerId
      ? transcript.speakers.find((candidate) => candidate.speakerId === segment.speakerId)
      : null
    return speaker?.isCurrentUser ? "You" : segment.speakerName
  }
  const inlineScreenshots = projectTranscriptScreenshots(
    screenshots,
    transcript.recordingId,
    transcript.audio.startSeconds ?? 0,
    transcript.audio.endSeconds ?? transcript.audio.durationSeconds,
  )
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
      at: screenshot.transcriptOffsetSeconds,
      screenshot,
    })),
  ].sort((left, right) => left.at - right.at)

  if (rows.length === 0) {
    return <p className="whitespace-pre-wrap text-[12px] leading-relaxed text-foreground">{transcript.text}</p>
  }

  return (
    <div className="space-y-2">
      {rows.map((row) => row.kind === "segment" ? (
        <div
          key={row.key}
          ref={(element) => registerSegment(row.segment.index, element)}
          className={cn(
            "grid grid-cols-[auto_minmax(0,1fr)] gap-2 rounded px-1 py-0.5 text-[12px] leading-relaxed transition-colors",
            activeSegmentIndex === row.segment.index && "bg-primary/10 ring-1 ring-inset ring-primary/35",
          )}
        >
          <button
            type="button"
            disabled={!audioAvailable || row.segment.startSeconds == null}
            onClick={() => onSeekSegment(row.segment.startSeconds)}
            className="w-14 self-start pt-0.5 text-left font-mono text-[10px] text-muted-foreground outline-none enabled:hover:text-primary enabled:focus-visible:ring-2 enabled:focus-visible:ring-ring disabled:cursor-default"
            aria-label={row.segment.startSeconds == null
              ? "Untimed transcript segment"
              : `Play transcript from ${formatOffset(row.segment.startSeconds)}`}
          >
            {row.segment.startSeconds == null ? "" : formatOffset(row.segment.startSeconds)}
          </button>
          <p className="min-w-0 whitespace-pre-wrap text-foreground">
            {displaySpeakerName(row.segment) && (
              <strong className="mr-1.5 text-status-meet">{displaySpeakerName(row.segment)}:</strong>
            )}
            {row.segment.text}
          </p>
        </div>
      ) : (
        <button
          type="button"
          key={row.key}
          onClick={() => send({ type: "openMeetingScreenshot", screenshotId: row.screenshot.id })}
          className="flex w-full items-center gap-2 rounded-md border border-border bg-card p-2 text-left hover:bg-accent"
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
    <div className="min-w-0 overflow-hidden rounded-md border border-border bg-card">
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
      className="space-y-1.5 rounded-md border border-primary/30 bg-primary/10 p-3 text-[11px]"
      role="status"
      aria-live="polite"
      aria-busy="true"
    >
      <div className="flex items-center gap-2 font-medium text-primary">
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
