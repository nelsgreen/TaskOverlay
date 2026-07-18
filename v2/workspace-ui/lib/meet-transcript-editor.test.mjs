import assert from "node:assert/strict"
import fs from "node:fs"
import test from "node:test"
import {
  buildSaveRevisionCommand,
  createTranscriptDraft,
  draftSegmentSpeakerName,
  effectiveDraftSpeakerId,
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
} from "./meet-transcript-editor.ts"

const snapshotTranscript = () => ({
  id: "transcript-1",
  meetingId: "meet-1",
  revisionId: "revision-1",
  segments: [
    { index: 0, startSeconds: 0, endSeconds: 4, text: "First point", speakerId: "speaker-a", speakerName: "A" },
    { index: 1, startSeconds: 5, endSeconds: 9, text: "Second point", speakerId: "speaker-b", speakerName: "B" },
    { index: 2, startSeconds: 10, endSeconds: 14, text: "Follow-up", speakerId: "speaker-a", speakerName: "A" },
  ],
  speakers: [
    { speakerId: "speaker-a", originalLabel: "A", displayName: "A", isCurrentUser: false },
    { speakerId: "speaker-b", originalLabel: "B", displayName: "B", isCurrentUser: false },
    { speakerId: "speaker-c", originalLabel: "C", displayName: "C", isCurrentUser: true },
  ],
})

test("creating a draft copies the snapshot and starts clean", () => {
  const transcript = snapshotTranscript()
  const draft = createTranscriptDraft(transcript)
  assert.equal(draft.transcriptId, "transcript-1")
  assert.equal(draft.parentRevisionId, "revision-1")
  assert.equal(isDraftDirty(draft), false)
  assert.equal(validateTranscriptDraft(draft), null)

  // Editing the draft never mutates the snapshot objects it was created from.
  const edited = setDraftSegmentText(draft, 0, "Rewritten point")
  assert.equal(transcript.segments[0].text, "First point")
  assert.equal(draft.segments[0].text, "First point")
  assert.equal(edited.segments[0].text, "Rewritten point")
  assert.equal(edited.segments[0].originalText, "First point")
  assert.equal(isDraftDirty(edited), true)
})

test("speaker rename updates every matching draft segment name", () => {
  const draft = renameDraftSpeaker(
    createTranscriptDraft(snapshotTranscript()),
    "speaker-a",
    "Alexandra",
  )
  const names = draft.segments.map((segment) => draftSegmentSpeakerName(draft, segment))
  assert.deepEqual(names, ["Alexandra", "B", "Alexandra"])
  assert.equal(isDraftDirty(draft), true)
})

test("Mark as You is exclusive and toggling clears the previous marker", () => {
  let draft = createTranscriptDraft(snapshotTranscript())
  assert.equal(draft.speakers.find((speaker) => speaker.isCurrentUser)?.speakerId, "speaker-c")

  draft = toggleDraftSpeakerAsYou(draft, "speaker-a")
  assert.deepEqual(
    draft.speakers.filter((speaker) => speaker.isCurrentUser).map((speaker) => speaker.speakerId),
    ["speaker-a"],
  )

  // Toggling the same speaker again leaves zero You markers.
  draft = toggleDraftSpeakerAsYou(draft, "speaker-a")
  assert.equal(draft.speakers.some((speaker) => speaker.isCurrentUser), false)
})

test("merge reassigns segments, hides the merged speaker, and can be undone", () => {
  let draft = createTranscriptDraft(snapshotTranscript())
  draft = mergeDraftSpeaker(draft, "speaker-a", "speaker-b")
  assert.equal(isSpeakerMergedAway(draft, "speaker-a"), true)
  assert.deepEqual(
    visibleDraftSpeakers(draft).map((speaker) => speaker.speakerId),
    ["speaker-b", "speaker-c"],
  )
  assert.deepEqual(
    draft.segments.map((segment) => draftSegmentSpeakerName(draft, segment)),
    ["B", "B", "B"],
  )
  assert.equal(isDraftDirty(draft), true)

  draft = undoDraftSpeakerMerge(draft, "speaker-a")
  assert.equal(isSpeakerMergedAway(draft, "speaker-a"), false)
  assert.equal(isDraftDirty(draft), false)
})

test("merge into self, unknown, or merged-away speakers is refused", () => {
  const draft = createTranscriptDraft(snapshotTranscript())
  assert.equal(mergeDraftSpeaker(draft, "speaker-a", "speaker-a"), draft)
  assert.equal(mergeDraftSpeaker(draft, "speaker-a", "missing"), draft)
  const merged = mergeDraftSpeaker(draft, "speaker-a", "speaker-b")
  assert.equal(mergeDraftSpeaker(merged, "speaker-c", "speaker-a"), merged)
  assert.equal(mergeDraftSpeaker(merged, "speaker-a", "speaker-c"), merged)
})

test("chained merges stay resolved to final targets", () => {
  let draft = createTranscriptDraft(snapshotTranscript())
  draft = mergeDraftSpeaker(draft, "speaker-a", "speaker-b")
  draft = mergeDraftSpeaker(draft, "speaker-b", "speaker-c")
  assert.equal(effectiveDraftSpeakerId(draft, "speaker-a"), "speaker-c")
  assert.equal(effectiveDraftSpeakerId(draft, "speaker-b"), "speaker-c")
  const command = buildSaveRevisionCommand(draft)
  assert.deepEqual(command.merges.sort((a, b) => a.fromSpeakerId.localeCompare(b.fromSpeakerId)), [
    { fromSpeakerId: "speaker-a", intoSpeakerId: "speaker-c" },
    { fromSpeakerId: "speaker-b", intoSpeakerId: "speaker-c" },
  ])
})

test("save command carries only changed segments and the surviving speakers", () => {
  let draft = createTranscriptDraft(snapshotTranscript())
  draft = setDraftSegmentText(draft, 1, "Edited second point ")
  draft = renameDraftSpeaker(draft, "speaker-a", "Alexandra")
  draft = toggleDraftSpeakerAsYou(draft, "speaker-a")
  draft = mergeDraftSpeaker(draft, "speaker-b", "speaker-a")

  const command = buildSaveRevisionCommand(draft)
  assert.equal(command.type, "saveMeetingTranscriptRevision")
  assert.equal(command.meetingId, "meet-1")
  assert.equal(command.transcriptId, "transcript-1")
  assert.equal(command.parentRevisionId, "revision-1")
  assert.deepEqual(command.segmentEdits, [{ index: 1, text: "Edited second point" }])
  assert.deepEqual(command.speakers, [
    { speakerId: "speaker-a", displayName: "Alexandra", isCurrentUser: true },
    { speakerId: "speaker-c", displayName: "C", isCurrentUser: false },
  ])
  assert.deepEqual(command.merges, [
    { fromSpeakerId: "speaker-b", intoSpeakerId: "speaker-a" },
  ])
})

test("validation rejects empty segments and empty transcripts", () => {
  const draft = createTranscriptDraft(snapshotTranscript())
  assert.equal(validateTranscriptDraft(setDraftSegmentText(draft, 0, "   ")), "Segment text cannot be empty.")
  assert.equal(
    validateTranscriptDraft(createTranscriptDraft({ ...snapshotTranscript(), segments: [] })),
    "This transcript has no editable segments.",
  )
})

test("keyboard policy: Ctrl+Enter saves, Escape cancels, nothing fires behind a confirmation", () => {
  const closed = { confirmOpen: false }
  const open = { confirmOpen: true }
  assert.equal(transcriptEditorKeyAction({ key: "Enter", ctrlKey: true, metaKey: false }, closed), "save")
  assert.equal(transcriptEditorKeyAction({ key: "Enter", ctrlKey: false, metaKey: true }, closed), "save")
  assert.equal(transcriptEditorKeyAction({ key: "Enter", ctrlKey: false, metaKey: false }, closed), null)
  assert.equal(transcriptEditorKeyAction({ key: "Escape", ctrlKey: false, metaKey: false }, closed), "cancel")
  assert.equal(transcriptEditorKeyAction({ key: "Enter", ctrlKey: true, metaKey: false }, open), null)
  assert.equal(transcriptEditorKeyAction({ key: "Escape", ctrlKey: false, metaKey: false }, open), null)
})

test("revision labels never expose raw enum names", () => {
  assert.equal(transcriptOriginLabel("Generated"), "Generated")
  assert.equal(transcriptOriginLabel("Imported"), "Imported")
  assert.equal(transcriptOriginLabel("UserEdited"), "Edited")
})

test("the transcript editor never touches localStorage", () => {
  const source = fs.readFileSync(new URL("./meet-transcript-editor.ts", import.meta.url), "utf8")
  assert.equal(source.includes("localStorage."), false)
  const component = fs.readFileSync(
    new URL("../components/meet-sources-review.tsx", import.meta.url),
    "utf8",
  )
  assert.equal(component.includes("localStorage."), false)
})
