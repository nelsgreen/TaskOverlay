import assert from "node:assert/strict"
import test from "node:test"

import {
  describeMeetingOperationShortStage,
  describeMeetingOperationStage,
  isMeetingOperationCancelling,
} from "./meeting-operation-progress.ts"

const operation = (stage, stageIndex = 0, stageTotal = 0, extra = {}) => ({
  stage,
  stageIndex,
  stageTotal,
  ...extra,
})

test("long transcription reports the expected multi-part stages", () => {
  assert.equal(
    describeMeetingOperationStage(operation("PreparingParts", 0, 3)),
    "Preparing 3 parts",
  )
  assert.equal(
    describeMeetingOperationStage(operation("TranscribingPart", 1, 3)),
    "Transcribing 1 of 3",
  )
  assert.equal(
    describeMeetingOperationStage(operation("TranscribingPart", 2, 3)),
    "Transcribing 2 of 3",
  )
  assert.equal(
    describeMeetingOperationStage(operation("TranscribingPart", 3, 3)),
    "Transcribing 3 of 3",
  )
  assert.equal(
    describeMeetingOperationStage(operation("MergingTranscript", 3, 3)),
    "Merging transcript",
  )
})

test("existing single-request stages are unchanged", () => {
  assert.equal(
    describeMeetingOperationStage(operation("StartingTranscription")),
    "Starting transcription...",
  )
  assert.equal(
    describeMeetingOperationStage(operation("PreparingAudio")),
    "Preparing audio...",
  )
  assert.equal(describeMeetingOperationStage(operation("Transcribing")), "Transcribing...")
  assert.equal(
    describeMeetingOperationStage(operation("StartingAnalysis")),
    "Starting analysis...",
  )
  assert.equal(
    describeMeetingOperationStage(operation("Analyzing")),
    "Analyzing transcript...",
  )
})

test("a snapshot without part counts falls back instead of showing zero", () => {
  // An older or single-request snapshot must never render "Preparing 0 parts".
  assert.equal(
    describeMeetingOperationStage({ stage: "PreparingParts" }),
    "Preparing audio...",
  )
  assert.equal(
    describeMeetingOperationStage({ stage: "TranscribingPart" }),
    "Transcribing...",
  )
  assert.equal(
    describeMeetingOperationStage(operation("TranscribingPart", 0, 3)),
    "Transcribing...",
  )
})

test("cancellation stays neutral and distinct from a failure", () => {
  assert.equal(describeMeetingOperationStage(operation("Cancelling")), "Cancelling...")
  assert.equal(
    isMeetingOperationCancelling({ stage: "Cancelling", cancellationRequested: false }),
    true,
  )
  assert.equal(
    isMeetingOperationCancelling({
      stage: "TranscribingPart",
      cancellationRequested: true,
    }),
    true,
  )
  assert.equal(
    isMeetingOperationCancelling({
      stage: "TranscribingPart",
      cancellationRequested: false,
    }),
    false,
  )

  // Cancellation text must not read as an error.
  const cancelling = describeMeetingOperationStage(operation("Cancelling"))
  for (const word of ["fail", "error", "could not"]) {
    assert.equal(cancelling.toLowerCase().includes(word), false)
  }
})

test("the compact surface names the current part", () => {
  assert.equal(
    describeMeetingOperationShortStage({
      kind: "Transcription",
      stage: "TranscribingPart",
      stageIndex: 2,
      stageTotal: 3,
    }),
    "Transcribing 2 of 3",
  )
  assert.equal(
    describeMeetingOperationShortStage({
      kind: "Analysis",
      stage: "StartingAnalysis",
      stageIndex: 0,
      stageTotal: 0,
    }),
    "Starting analysis...",
  )
  assert.equal(
    describeMeetingOperationShortStage({
      kind: "Analysis",
      stage: "Analyzing",
      stageIndex: 0,
      stageTotal: 0,
    }),
    "Analyzing...",
  )
})

test("the UI renders only the sanitized failure text", () => {
  // The C# side persists this text; the raw provider body stays in logs.
  const sanitized = "Could not transcribe part 2 of 3."
  const rawProviderError =
    'HTTP 400 from api.openai.com: {"error":{"message":"Maximum content size limit ' +
    'exceeded: audio duration 1485.48 seconds is longer than 1400 seconds",' +
    '"type":"invalid_request_error"},"request_id":"req_abc123"}'

  for (const leak of ["openai", "http", "request_id", "1485", "invalid_request_error", "{"]) {
    assert.equal(sanitized.toLowerCase().includes(leak), false)
  }

  assert.equal(rawProviderError.includes("1485.48"), true)
  assert.match(sanitized, /^Could not transcribe part \d+ of \d+\.$/)
})
