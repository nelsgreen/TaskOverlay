import assert from "node:assert/strict"
import test from "node:test"
import { MeetingAutosaveQueue } from "./meet-autosave.ts"

const wait = (milliseconds = 0) => new Promise((resolve) => setTimeout(resolve, milliseconds))

test("text edits are debounced and coalesced", async () => {
  const saves = []
  const queue = new MeetingAutosaveQueue(
    { title: "Initial", notes: "" },
    async (value, fields) => saves.push({ value, fields: [...fields] }),
    () => undefined,
    20,
  )

  queue.enqueue({ title: "A", notes: "" }, ["title"], "debounced")
  queue.enqueue({ title: "AB", notes: "Agenda" }, ["title", "notes"], "debounced")
  assert.equal(saves.length, 0)

  await wait(40)
  assert.equal(saves.length, 1)
  assert.deepEqual(saves[0].value, { title: "AB", notes: "Agenda" })
  assert.deepEqual(new Set(saves[0].fields), new Set(["title", "notes"]))
  queue.dispose()
})

test("discrete edits save immediately", async () => {
  const saves = []
  const queue = new MeetingAutosaveQueue(
    { projectId: "a" },
    async (value, fields) => saves.push({ value, fields: [...fields] }),
    () => undefined,
  )

  queue.enqueue({ projectId: "b" }, ["projectId"], "immediate")
  assert.equal(await queue.flush(), true)
  assert.equal(saves.length, 1)
  assert.equal(saves[0].value.projectId, "b")
  queue.dispose()
})

for (const boundary of ["Close", "recording start"]) {
  test(`pending text is flushed before ${boundary}`, async () => {
    const saves = []
    const queue = new MeetingAutosaveQueue(
      { notes: "" },
      async (value) => saves.push(value.notes),
      () => undefined,
      60_000,
    )

    queue.enqueue({ notes: "Retain this edit" }, ["notes"], "debounced")
    assert.equal(await queue.flush(), true)
    assert.deepEqual(saves, ["Retain this edit"])
    queue.dispose()
  })
}

test("an older mutation cannot report Saved over a newer edit", async () => {
  const resolvers = []
  const statuses = []
  const saves = []
  const queue = new MeetingAutosaveQueue(
    { title: "Initial" },
    (value) => {
      saves.push(value.title)
      return new Promise((resolve) => resolvers.push(resolve))
    },
    (status) => statuses.push(status),
  )

  queue.enqueue({ title: "First" }, ["title"], "immediate")
  await wait()
  queue.enqueue({ title: "Second" }, ["title"], "immediate")
  const flushed = queue.flush()
  resolvers.shift()()
  await wait()

  assert.deepEqual(saves, ["First", "Second"])
  assert.equal(statuses.includes("saved"), false)

  resolvers.shift()()
  assert.equal(await flushed, true)
  assert.equal(statuses.at(-1), "saved")
  queue.dispose()
})

test("failed edits remain retryable without losing the latest value", async () => {
  let attempts = 0
  const savedValues = []
  const statuses = []
  const queue = new MeetingAutosaveQueue(
    { title: "Initial" },
    async (value) => {
      attempts += 1
      if (attempts === 1) throw new Error("offline")
      savedValues.push(value.title)
    },
    (status) => statuses.push(status),
  )

  queue.enqueue({ title: "Keep me" }, ["title"], "immediate")
  assert.equal(await queue.flush(), false)
  assert.equal(queue.getStatus(), "failed")

  assert.equal(await queue.retry(), true)
  assert.deepEqual(savedValues, ["Keep me"])
  assert.equal(statuses.at(-1), "saved")
  queue.dispose()
})

test("a later failure cannot invalidate an earlier durable flush", async () => {
  const operations = []
  const queue = new MeetingAutosaveQueue(
    { title: "Initial" },
    (value) => new Promise((resolve, reject) => {
      operations.push({ title: value.title, resolve, reject })
    }),
    () => undefined,
  )

  queue.enqueue({ title: "Revision A" }, ["title"], "immediate")
  const flushA = queue.flush()
  await wait()
  queue.enqueue({ title: "Revision B" }, ["title"], "immediate")

  operations[0].resolve()
  await wait()
  operations[1].reject(new Error("B failed"))
  await wait()

  assert.equal(await flushA, true)
  assert.equal(queue.getStatus(), "failed")

  const retryB = queue.retry()
  await wait()
  assert.equal(operations[2].title, "Revision B")
  operations[2].resolve()
  assert.equal(await retryB, true)
  assert.equal(queue.getStatus(), "saved")
  queue.dispose()
})
