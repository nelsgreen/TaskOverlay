import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import test from "node:test"
import { buttonDisabledClasses, buttonToneClasses } from "../components/ui/button-tone.ts"

const buttonSrc = readFileSync(new URL("../components/ui/button.tsx", import.meta.url), "utf8")
const iconButtonSrc = readFileSync(new URL("../components/ui/icon-button.tsx", import.meta.url), "utf8")
const taskContextBlock = readFileSync(new URL("../components/task-context-block.tsx", import.meta.url), "utf8")
const contextPackModal = readFileSync(new URL("../components/context-pack-modal.tsx", import.meta.url), "utf8")

test("every button tone reads a canonical token for its background/text/border, never a raw color", () => {
  const hexOrRawColor = /#[0-9a-fA-F]{3,8}\b|\brgb\(|\brgba\(/
  for (const [tone, classes] of Object.entries(buttonToneClasses)) {
    assert.equal(hexOrRawColor.test(classes), false, `${tone} must reference tokens, not a hardcoded color`)
  }
  assert.match(buttonToneClasses.primary, /\bbg-primary\b/)
  assert.match(buttonToneClasses.primary, /\btext-primary-foreground\b/)
  assert.match(buttonToneClasses.secondary, /\bbg-field\b/)
  assert.match(buttonToneClasses.secondary, /\bborder-border-strong\b/)
  assert.match(buttonToneClasses.quiet, /\bbg-transparent\b/)
  assert.match(buttonToneClasses.destructive, /\bbg-destructive-soft\b/)
  assert.match(buttonToneClasses.destructive, /\btext-destructive\b/)
  assert.match(buttonToneClasses.recording, /\bbg-recording-soft\b/)
  assert.match(buttonToneClasses.recording, /\btext-recording\b/)
})

test("recording and destructive are independent contracts: recording never reads --destructive and destructive never reads --recording", () => {
  assert.equal(/destructive/.test(buttonToneClasses.recording), false)
  assert.equal(/recording/.test(buttonToneClasses.destructive), false)
  // Only destructive escalates to the error focus ring; recording is an
  // operational state and keeps the shared default focus ring (D10).
  assert.match(buttonToneClasses.destructive, /focus-visible:shadow-\[var\(--focus-ring-error\)\]/)
  assert.equal(/focus-visible:shadow-\[var\(--focus-ring-error\)\]/.test(buttonToneClasses.recording), false)
})

test("no tone reuses a domain task-status/MEET/warning color as a generic button color", () => {
  for (const [tone, classes] of Object.entries(buttonToneClasses)) {
    for (const forbidden of ["sem-todo", "sem-focus", "sem-wait", "sem-done", "sem-remind", "sem-meet", "sem-panel", "sem-deadline", "sem-now", "warning"]) {
      assert.equal(classes.includes(forbidden), false, `${tone} must not reference --${forbidden}`)
    }
  }
})

test("selected/toggle tone is not color-only: it also carries a distinct ring and font-weight signal", () => {
  assert.match(buttonToneClasses.selected, /font-semibold/)
  assert.match(buttonToneClasses.selected, /shadow-\[/)
})

test("disabled is native-semantics driven (opacity + not-allowed), matching the spec's Focus & states model for buttons", () => {
  assert.match(buttonDisabledClasses, /\bdisabled:opacity-50\b/)
  assert.match(buttonDisabledClasses, /\bdisabled:cursor-not-allowed\b/)
  // Buttons deliberately avoid pointer-events-none, same reasoning as
  // Input/Textarea/Select's disabledBase: it would hide cursor-not-allowed.
  assert.equal(buttonDisabledClasses.includes("pointer-events-none"), false)
})

test("Button and IconButton both apply the shared focus-visible ring and disabled classes", () => {
  for (const src of [buttonSrc, iconButtonSrc]) {
    assert.match(src, /focus-visible:shadow-\[var\(--focus-ring\)\]/)
    assert.match(src, /buttonDisabledClasses/)
  }
})

test("Button forwards disabled to the native button element (no aria-disabled substitute)", () => {
  assert.match(buttonSrc, /disabled=\{disabled \|\| loading\}/)
  assert.equal(/aria-disabled/.test(buttonSrc), false)
})

test("Button's pressed prop always keeps aria-pressed and the selected tone in sync", () => {
  assert.match(buttonSrc, /aria-pressed=\{pressed\}/)
  assert.match(buttonSrc, /pressed \? 'selected' : tone/)
})

test("IconButton requires a label prop and renders it as aria-label", () => {
  assert.match(iconButtonSrc, /label: string/)
  assert.match(iconButtonSrc, /aria-label=\{label\}/)
})

test("IconButton uses a distinct fixed square/radius-sm geometry from Button's rectangular radius-md shape", () => {
  assert.match(iconButtonSrc, /rounded-sm\b/)
  assert.match(iconButtonSrc, /\bsize-7\b/)
  assert.equal(/rounded-md/.test(iconButtonSrc), false)
})

test("Context actions (Link/Hub/Export) render as three equal-width buttons via CSS grid, never flex (which cannot guarantee equal thirds)", () => {
  assert.match(taskContextBlock, /grid grid-cols-3 gap-1\.5/)
})

/**
 * Slice the whole `<Button ...>...</Button>` element containing `marker`
 * (found by walking back to the nearest preceding `<Button` tag), so
 * assertions can see both the opening tag's props and its children
 * regardless of attribute order or exact JSX formatting.
 */
function buttonElementAround(src, marker) {
  const markerIdx = src.indexOf(marker)
  assert.notEqual(markerIdx, -1, `expected to find "${marker}"`)
  const openIdx = src.lastIndexOf("<Button", markerIdx)
  assert.notEqual(openIdx, -1, `expected a preceding <Button before "${marker}"`)
  const closeIdx = src.indexOf("</Button>", markerIdx)
  assert.notEqual(closeIdx, -1, `expected a following </Button> after "${marker}"`)
  return src.slice(openIdx, closeIdx)
}

test("Context actions keep all three original handlers, each reachable through its new short label", () => {
  assert.match(buttonElementAround(taskContextBlock, "setModalOpen(true)"), /\bLink\b/)
  assert.match(buttonElementAround(taskContextBlock, "onClick={onOpenContextHub}"), /\bHub\b/)
  assert.match(buttonElementAround(taskContextBlock, "setPackMarkdown(contextPack.buildMarkdown())"), /\bExport\b/)
})

test("Context actions: only Link (a mutation-adjacent action) is gated by locked; Hub/Export (navigation/read-only export) stay enabled", () => {
  const linkBlock = buttonElementAround(taskContextBlock, "setModalOpen(true)")
  const hubBlock = buttonElementAround(taskContextBlock, "onClick={onOpenContextHub}")
  const exportBlock = buttonElementAround(taskContextBlock, "setPackMarkdown(contextPack.buildMarkdown())")
  assert.match(linkBlock, /disabled=\{locked\}/)
  assert.equal(/disabled=\{locked\}/.test(hubBlock), false)
  assert.equal(/disabled=\{locked\}/.test(exportBlock), false)
})

test("Context actions are visually neutral (secondary tone), not primary - no single action should read as the primary one", () => {
  const actionsBlock = taskContextBlock.slice(
    taskContextBlock.indexOf("grid grid-cols-3"),
    taskContextBlock.indexOf("grid grid-cols-3") + 900,
  )
  assert.equal(/tone="primary"/.test(actionsBlock), false)
  const secondaryCount = (actionsBlock.match(/tone="secondary"/g) || []).length
  assert.equal(secondaryCount, 3)
})

test("migrated callers: task-context-block and context-pack-modal import the shared Button/IconButton primitives", () => {
  assert.match(taskContextBlock, /from ["']@\/components\/ui\/button["']/)
  assert.match(taskContextBlock, /from ["']@\/components\/ui\/icon-button["']/)
  assert.match(contextPackModal, /from ["']@\/components\/ui\/button["']/)
  assert.match(contextPackModal, /from ["']@\/components\/ui\/icon-button["']/)
})

test("migrated callers: context-pack-modal's Close icon button still forwards onClose and keeps its accessible name", () => {
  assert.match(contextPackModal, /<IconButton label="Close" onClick=\{onClose\}>/)
})
