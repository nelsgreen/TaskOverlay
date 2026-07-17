"use client"

import {
  Bot,
  Camera,
  Check,
  Copy,
  ExternalLink,
  FileAudio,
  FileText,
  Image as ImageIcon,
  Sparkles,
  Trash2,
  Upload,
} from "lucide-react"
import type {
  MeetItem,
  MeetingAnalysisSnapshot,
  MeetingRecordingPolicy,
  MeetingRecordingSnapshot,
  MeetingScreenshotSnapshot,
  MeetingTranscriptSnapshot,
  Project,
  WorkspaceMeetingAssistantCommand,
} from "@/lib/types"
import { cn } from "@/lib/utils"
import {
  selectActiveMeetingTranscript,
  selectLatestTranscriptAnalysis,
  sortMeetingScreenshots,
} from "@/lib/meet-workspace-policy"
import { AnalysisReview, MeetingAssistantSection } from "./meeting-assistant-section"

interface SharedProps {
  meet: MeetItem
  projects: Project[]
  recordings: MeetingRecordingSnapshot[]
  transcripts: MeetingTranscriptSnapshot[]
  screenshots: MeetingScreenshotSnapshot[]
  analyses: MeetingAnalysisSnapshot[]
  activeRecording: MeetingRecordingSnapshot | null
  activeRecordingOwnerTitle?: string
  readOnly: boolean
  commandError?: string | null
  onClearError?: () => void
  onCommand?: (command: WorkspaceMeetingAssistantCommand) => boolean
  defaultRecordingPolicy?: Exclude<MeetingRecordingPolicy, "Inherit">
  onRecordingPolicyChange?: (policy: MeetingRecordingPolicy) => void
  onBeforeRecordingStart?: () => Promise<boolean>
}

export function MeetingSourcesWorkspace({
  meet,
  projects,
  recordings,
  transcripts,
  screenshots,
  analyses,
  activeRecording,
  activeRecordingOwnerTitle,
  readOnly,
  commandError,
  onClearError,
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
    <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4 [scrollbar-gutter:stable]">
      <div className="mx-auto max-w-[1120px] space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h3 className="text-sm font-semibold text-foreground">MEET sources</h3>
            <p className="mt-0.5 text-[11px] text-muted-foreground">
              Record locally or add durable managed copies. Imports never depend on the original external path.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <SourceAction
              label="Import audio"
              icon={FileAudio}
              disabled={sourceActionsDisabled}
              onClick={() => send({ type: "importMeetingAudio", meetingId: meet.id })}
            />
            <SourceAction
              label="Import transcript"
              icon={Upload}
              disabled={sourceActionsDisabled}
              onClick={() => send({ type: "importMeetingTranscript", meetingId: meet.id })}
            />
            <SourceAction
              label="Capture screenshot"
              icon={Camera}
              disabled={sourceActionsDisabled}
              onClick={() => send({ type: "captureMeetingScreenshot", meetingId: meet.id })}
            />
          </div>
        </div>

        {onCommand && (
          <MeetingAssistantSection
            meet={meet}
            projects={projects}
            recordings={recordings}
            analyses={analyses}
            unclassifiedRecordings={recordings.filter((recording) => !recording.meetingId)}
            activeRecording={activeRecording}
            activeRecordingOwnerTitle={activeRecordingOwnerTitle}
            readOnly={readOnly}
            commandError={commandError}
            onClearError={onClearError}
            onCommand={onCommand}
            defaultRecordingPolicy={defaultRecordingPolicy}
            onRecordingPolicyChange={onRecordingPolicyChange}
            onBeforeRecordingStart={onBeforeRecordingStart}
            showAnalysis={false}
            showTranscript={false}
          />
        )}

        <section className="space-y-2 rounded-lg border border-border bg-card/40 p-3">
          <div className="flex items-center gap-2">
            <FileText className="size-4 text-status-meet" />
            <h3 className="text-[11px] font-bold uppercase tracking-widest text-foreground">Transcripts</h3>
            <span className="ml-auto text-[10px] text-muted-foreground">{meetTranscripts.length}</span>
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
            />
          ))}
        </section>

        <section className="space-y-2 rounded-lg border border-border bg-card/40 p-3">
          <div className="flex items-center gap-2">
            <ImageIcon className="size-4 text-status-meet" />
            <h3 className="text-[11px] font-bold uppercase tracking-widest text-foreground">Screenshots</h3>
            <span className="ml-auto text-[10px] text-muted-foreground">{meetScreenshots.length}</span>
          </div>
          {meetScreenshots.length === 0 ? (
            <EmptySource text="No manual screenshots captured for this MEET." />
          ) : (
            <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
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
  readOnly,
  onCommand,
}: SharedProps) {
  const activeTranscript = selectActiveMeetingTranscript(
    meet.id,
    meet.activeTranscriptId,
    transcripts,
  )
  const selectedAnalysis = selectLatestTranscriptAnalysis(activeTranscript?.id, analyses)
  const meetScreenshots = sortMeetingScreenshots(meet.id, screenshots)
  const send = (command: WorkspaceMeetingAssistantCommand) => onCommand?.(command) ?? false

  return (
    <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4 [scrollbar-gutter:stable]">
      <div className="grid min-h-0 gap-4 lg:grid-cols-[minmax(0,1.08fr)_minmax(340px,0.92fr)]">
        <section className="flex min-h-[360px] min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-card/40">
          <div className="flex flex-wrap items-center gap-2 border-b border-border px-3 py-2.5">
            <FileText className="size-4 text-status-meet" />
            <div className="min-w-0 flex-1">
              <h3 className="truncate text-[11px] font-bold uppercase tracking-widest text-foreground">
                Active transcript
              </h3>
              <p className="truncate text-[10px] text-muted-foreground">
                {activeTranscript
                  ? `${activeTranscript.origin} - ${activeTranscript.sourceLabel || activeTranscript.provider}`
                  : "Select or create a transcript in Sources"}
              </p>
            </div>
            {activeTranscript && (
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
              </>
            )}
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto p-3 [scrollbar-gutter:stable] lg:max-h-[calc(100vh-14rem)]">
            {activeTranscript ? (
              <TranscriptContent transcript={activeTranscript} screenshots={meetScreenshots} send={send} />
            ) : (
              <EmptySource text="Review will use the explicitly active transcript." />
            )}
          </div>
        </section>

        <div className="min-w-0 space-y-3">
          <section className="space-y-2 rounded-lg border border-border bg-card/40 p-3">
            <div className="flex flex-wrap items-center gap-2">
              <Bot className="size-4 text-status-meet" />
              <h3 className="text-[11px] font-bold uppercase tracking-widest text-foreground">
                Meeting Assistant
              </h3>
              {selectedAnalysis?.isStale && (
                <span className="rounded bg-amber-500/15 px-1.5 py-0.5 text-[9px] font-semibold text-amber-300">
                  Stale transcript revision
                </span>
              )}
              {activeTranscript && onCommand && (
                <button
                  type="button"
                  disabled={readOnly}
                  onClick={() => send({ type: "analyzeMeetingTranscript", transcriptId: activeTranscript.id })}
                  className="ml-auto flex h-7 items-center gap-1 rounded border border-status-meet/40 bg-status-meet/10 px-2 text-[10px] font-medium text-status-meet disabled:opacity-40"
                >
                  <Sparkles className="size-3" />
                  {selectedAnalysis ? "Re-run analysis" : "Analyze transcript"}
                </button>
              )}
            </div>
            {selectedAnalysis ? (
              <AnalysisReview
                analysis={selectedAnalysis}
                projects={projects}
                readOnly={readOnly}
                send={send}
              />
            ) : (
              <EmptySource text={activeTranscript
                ? "No analysis exists for this transcript version."
                : "Analysis needs an active transcript."} />
            )}
          </section>

          <section className="space-y-2 rounded-lg border border-border bg-card/40 p-3">
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
    </div>
  )
}

function TranscriptSourceCard({
  meet,
  transcript,
  readOnly,
  send,
}: {
  meet: MeetItem
  transcript: MeetingTranscriptSnapshot
  readOnly: boolean
  send: (command: WorkspaceMeetingAssistantCommand) => boolean
}) {
  return (
    <div className={cn(
      "space-y-2 rounded-md border bg-background/50 p-2.5",
      transcript.isActive ? "border-status-meet/50" : "border-border",
    )}>
      <div className="flex min-w-0 flex-wrap items-center gap-2">
        <span className={cn(
          "rounded px-1.5 py-0.5 text-[9px] font-semibold uppercase",
          transcript.origin === "Imported"
            ? "bg-sky-500/15 text-sky-300"
            : "bg-status-meet/15 text-status-meet",
        )}>
          {transcript.origin}
        </span>
        <span className="min-w-0 flex-1 truncate text-[11px] font-medium text-foreground">
          {transcript.originalFileName || transcript.sourceLabel || "Transcript"}
        </span>
        {transcript.isActive && (
          <span className="flex items-center gap-1 text-[9px] font-semibold uppercase text-status-meet">
            <Check className="size-3" /> Active
          </span>
        )}
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
        {!transcript.isActive && (
          <SourceAction
            label="Set active"
            icon={Check}
            disabled={readOnly}
            onClick={() => send({
              type: "setActiveMeetingTranscript",
              meetingId: meet.id,
              transcriptId: transcript.id,
            })}
          />
        )}
        <SourceAction
          label="Analyze"
          icon={Sparkles}
          disabled={readOnly}
          onClick={() => send({ type: "analyzeMeetingTranscript", transcriptId: transcript.id })}
        />
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

function SourceAction({
  label,
  icon: Icon,
  onClick,
  disabled = false,
  danger = false,
}: {
  label: string
  icon: typeof Upload
  onClick: () => void
  disabled?: boolean
  danger?: boolean
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className={cn(
        "flex h-8 items-center gap-1.5 rounded-md border px-2.5 text-[10px] font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-40",
        danger
          ? "border-destructive/40 text-destructive hover:bg-destructive/10"
          : "border-border text-muted-foreground hover:bg-accent hover:text-foreground",
      )}
    >
      <Icon className="size-3.5" />
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
