import assert from "node:assert/strict"
import fs from "node:fs"
import test from "node:test"
import {
  mergeMeetingOperations,
  operationsMatch,
  shouldStartMeetingOperation,
} from "./meeting-operation-state.ts"

const operation = (overrides = {}) => ({
  id: "server-op",
  kind: "Analysis",
  stage: "Analyzing",
  meetingId: "meet-a",
  recordingId: null,
  transcriptId: "transcript-a",
  startedAtUtc: "2026-07-17T10:00:00Z",
  cancellationRequested: false,
  ...overrides,
})

test("analysis operation identity is shared by transcript across Sources and Review", () => {
  assert.equal(operationsMatch(operation(), {
    kind: "Analysis",
    recordingId: null,
    transcriptId: "transcript-a",
  }), true)
  assert.equal(operationsMatch(operation(), {
    kind: "Analysis",
    recordingId: null,
    transcriptId: "transcript-b",
  }), false)
})

test("authoritative snapshot replaces optimistic state without a duplicate busy row", () => {
  const optimistic = operation({ id: "local-op", stage: "StartingAnalysis" })
  assert.deepEqual(mergeMeetingOperations([operation()], [optimistic]).map((item) => item.id), ["server-op"])
})

test("rapid double click admits exactly one analysis command", () => {
  const command = { type: "analyzeMeetingTranscript", transcriptId: "transcript-a" }
  const operations = []
  let sends = 0
  const click = () => {
    if (!shouldStartMeetingOperation(operations, command)) return
    sends++
    operations.push(operation({ id: "optimistic", stage: "StartingAnalysis" }))
  }
  click()
  click()
  assert.equal(sends, 1)
})

test("imported transcript operation does not require a recording id", () => {
  const imported = operation({ recordingId: null })
  assert.equal(operationsMatch(imported, {
    kind: "Analysis",
    recordingId: null,
    transcriptId: "transcript-a",
  }), true)
})

test("connected controls expose immediate busy, cancellation, live status, and reduced motion", () => {
  const sources = fs.readFileSync(new URL("../components/meet-sources-review.tsx", import.meta.url), "utf8")
  const assistant = fs.readFileSync(new URL("../components/meeting-assistant-section.tsx", import.meta.url), "utf8")
  const controller = fs.readFileSync(new URL("./meeting-operation-state.ts", import.meta.url), "utf8")
  assert.match(controller, /shouldStartMeetingOperation\(operationsRef\.current/)
  assert.match(controller, /operationsRef\.current = \[\.\.\.operationsRef\.current, candidate\]/)
  assert.match(controller, /already has an analysis operation/)
  assert.match(sources, /aria-busy/)
  assert.match(sources, /A new analysis is running/)
  assert.match(sources, /cancelMeetingProcessing/)
  assert.match(assistant, /Preparing audio\.\.\./)
  assert.match(assistant, /motion-reduce:animate-none/)
})

test("operation UI is indeterminate and never displays a fabricated percentage", () => {
  const sources = fs.readFileSync(new URL("../components/meet-sources-review.tsx", import.meta.url), "utf8")
  const assistant = fs.readFileSync(new URL("../components/meeting-assistant-section.tsx", import.meta.url), "utf8")
  assert.doesNotMatch(`${sources}\n${assistant}`, /progressPercent|% complete|aria-valuenow/)
})
