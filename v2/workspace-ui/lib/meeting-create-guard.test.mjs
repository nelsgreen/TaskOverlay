import assert from "node:assert/strict"
import test from "node:test"
import { MeetingCreateGuard } from "./meeting-create-guard.ts"

function deferred() {
  let resolve
  const promise = new Promise((next) => { resolve = next })
  return { promise, resolve }
}

test("two immediate create invocations send one command", async () => {
  const pending = deferred()
  const guard = new MeetingCreateGuard()
  let sends = 0
  const send = () => {
    sends += 1
    return pending.promise
  }

  const first = guard.create(send)
  const second = await guard.create(send)
  assert.equal(second, null)
  assert.equal(sends, 1)

  pending.resolve({ success: true, createdMeetingId: "meet-1" })
  await first
})

test("genuine creation failure releases the guard", async () => {
  const guard = new MeetingCreateGuard()
  let sends = 0
  const failed = await guard.create(async () => {
    sends += 1
    return { success: false, errorMessage: "disk full" }
  })
  assert.equal(failed?.success, false)
  assert.equal(guard.getPhase(), "idle")

  await guard.create(async () => {
    sends += 1
    return { success: true, createdMeetingId: "meet-2" }
  })
  assert.equal(sends, 2)
})

test("successful creation stays guarded until snapshot reconciliation", async () => {
  const guard = new MeetingCreateGuard()
  let sends = 0
  const send = async () => {
    sends += 1
    return { success: true, createdMeetingId: "meet-3" }
  }

  await guard.create(send)
  assert.equal(guard.getPhase(), "awaiting-reconciliation")
  assert.equal(await guard.create(send), null)
  assert.equal(sends, 1)
  assert.equal(guard.reconcile(["another-meet"]), false)
  assert.equal(guard.reconcile(["meet-3"]), true)

  await guard.create(send)
  assert.equal(sends, 2)
})

test("snapshot received before command result still reconciles the created MEET", async () => {
  const pending = deferred()
  const guard = new MeetingCreateGuard()
  const creation = guard.create(() => pending.promise)

  assert.equal(guard.reconcile(["meet-before-result"]), false)
  pending.resolve({ success: true, createdMeetingId: "meet-before-result" })
  await creation

  assert.equal(guard.getPhase(), "idle")
  let sends = 0
  await guard.create(async () => {
    sends += 1
    return { success: true, createdMeetingId: "later-meet" }
  })
  assert.equal(sends, 1)
})

test("snapshot failure requests refresh without repeating create", async () => {
  const guard = new MeetingCreateGuard()
  let sends = 0
  await guard.create(async () => {
    sends += 1
    return {
      success: true,
      createdMeetingId: "meet-4",
      warningCode: "snapshotFailed",
    }
  })

  assert.equal(guard.getPhase(), "refresh-required")
  assert.equal(await guard.create(async () => {
    sends += 1
    return { success: true, createdMeetingId: "duplicate" }
  }), null)

  let refreshes = 0
  assert.equal(guard.requestRefresh(() => {
    refreshes += 1
    return true
  }), true)
  assert.equal(refreshes, 1)
  assert.equal(sends, 1)
  assert.equal(guard.reconcile(["meet-4"]), true)
  assert.equal(guard.getPhase(), "idle")
})

test("bridge exception releases the guard", async () => {
  const guard = new MeetingCreateGuard()
  await assert.rejects(
    guard.create(async () => { throw new Error("bridge closed") }),
    /bridge closed/,
  )
  assert.equal(guard.getPhase(), "idle")
})
