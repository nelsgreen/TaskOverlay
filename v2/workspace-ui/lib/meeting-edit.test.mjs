import assert from "node:assert/strict"
import test from "node:test"
import {
  applyMeetingTitleInput,
  generatedMeetingTitle,
} from "./meeting-title.ts"

const projects = [{ id: "project-a", name: "PLHIV", color: "#22c55e" }]
const generatedMeeting = {
  id: "meeting-a",
  projectId: "project-a",
  title: "MEET - placeholder",
  titleIsGenerated: true,
  date: "2026-07-17",
  startTime: "09:00",
  duration: "30m",
}

test("generated meeting titles include project and local schedule", () => {
  assert.equal(
    generatedMeetingTitle("PLHIV", "2026-07-17", "09:00"),
    "MEET \u2014 PLHIV \u2014 17.07.2026, 09:00",
  )
  assert.equal(
    generatedMeetingTitle(undefined, "2026-07-17", "09:00"),
    "MEET \u2014 17.07.2026, 09:00",
  )
})

test("first user title input replaces generated title state", () => {
  const authored = applyMeetingTitleInput(generatedMeeting, "Planning session", projects)
  assert.equal(authored.title, "Planning session")
  assert.equal(authored.titleIsGenerated, false)
  assert.equal(authored.notes, undefined)
})

test("clearing title restores a generated fallback", () => {
  const restored = applyMeetingTitleInput(
    { ...generatedMeeting, title: "Authored", titleIsGenerated: false },
    "   ",
    projects,
  )
  assert.equal(restored.title, "MEET \u2014 PLHIV \u2014 17.07.2026, 09:00")
  assert.equal(restored.titleIsGenerated, true)
})
