import assert from "node:assert/strict"
import test from "node:test"
import { isValidMeetingLinkUrl } from "./meeting-link.ts"

test("valid http/https URLs are accepted", () => {
  assert.equal(isValidMeetingLinkUrl("https://meet.example.com/plhiv-sync"), true)
  assert.equal(isValidMeetingLinkUrl("http://zoom.us/j/123"), true)
  assert.equal(isValidMeetingLinkUrl("  https://teams.microsoft.com/x  "), true)
})

test("missing, blank, or non-http(s) values are rejected", () => {
  assert.equal(isValidMeetingLinkUrl(undefined), false)
  assert.equal(isValidMeetingLinkUrl(null), false)
  assert.equal(isValidMeetingLinkUrl(""), false)
  assert.equal(isValidMeetingLinkUrl("   "), false)
  assert.equal(isValidMeetingLinkUrl("meet.example.com/plhiv-sync"), false)
  assert.equal(isValidMeetingLinkUrl("Room 4B"), false)
  assert.equal(isValidMeetingLinkUrl("ftp://files.example.com/x"), false)
  assert.equal(isValidMeetingLinkUrl("javascript:alert(1)"), false)
})
