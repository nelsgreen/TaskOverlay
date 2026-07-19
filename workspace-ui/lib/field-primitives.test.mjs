import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import test from "node:test"
import { fieldStateClasses } from "../components/ui/field.ts"

const input = readFileSync(new URL("../components/ui/input.tsx", import.meta.url), "utf8")
const textarea = readFileSync(new URL("../components/ui/textarea.tsx", import.meta.url), "utf8")
const select = readFileSync(new URL("../components/ui/select.tsx", import.meta.url), "utf8")
const contextPackModal = readFileSync(new URL("../components/context-pack-modal.tsx", import.meta.url), "utf8")
const linkedTaskPicker = readFileSync(new URL("../components/linked-task-picker.tsx", import.meta.url), "utf8")
const workspaceHeader = readFileSync(new URL("../components/workspace-header.tsx", import.meta.url), "utf8")
const detailsPanel = readFileSync(new URL("../components/details-panel.tsx", import.meta.url), "utf8")

test("editable state uses the canonical field/border/text tokens and a restrained hover", () => {
  assert.match(fieldStateClasses, /\bbg-field\b/)
  assert.match(fieldStateClasses, /\bborder-border-strong\b/)
  assert.match(fieldStateClasses, /\btext-text\b/)
  assert.match(fieldStateClasses, /\bplaceholder:text-text-faint\b/)
  assert.match(fieldStateClasses, /\bhover:border-\[/)
})

test("focus-visible uses the canonical accent alias and focus-ring token, not a hardcoded color", () => {
  assert.match(fieldStateClasses, /\bfocus-visible:border-primary\b/)
  assert.match(fieldStateClasses, /focus-visible:shadow-\[var\(--focus-ring\)\]/)
})

test("read-only is styled distinctly from editable and never as disabled", () => {
  assert.match(fieldStateClasses, /\bread-only:bg-field-readonly\b/)
  assert.match(fieldStateClasses, /\bread-only:border-border\b(?!-)/)
  // Read-only must not fall back to generic opacity dimming anywhere in the contract.
  assert.equal(fieldStateClasses.includes("opacity"), false)
  // Focus and hover while read-only are explicit two-pseudo-class overrides,
  // so they out-specificity the plain single-pseudo-class rules regardless
  // of generated stylesheet order.
  assert.match(fieldStateClasses, /\bread-only:hover:border-border\b(?!-)/)
  assert.match(fieldStateClasses, /\bread-only:focus-visible:border-border-strong\b/)
})

test("disabled is styled distinctly from read-only and blocks native interaction", () => {
  assert.match(fieldStateClasses, /\bdisabled:bg-field-disabled\b/)
  assert.match(fieldStateClasses, /\bdisabled:border-border-disabled\b/)
  assert.match(fieldStateClasses, /\bdisabled:text-text-disabled\b/)
  assert.match(fieldStateClasses, /\bdisabled:cursor-not-allowed\b/)
  assert.match(fieldStateClasses, /\bdisabled:pointer-events-none\b/)
})

test("read-only and disabled never share a background, border, or text token", () => {
  assert.notEqual(
    fieldStateClasses.match(/read-only:bg-(\S+)/)[1],
    fieldStateClasses.match(/disabled:bg-(\S+)/)[1],
  )
  assert.notEqual(
    fieldStateClasses.match(/read-only:border-(\S+)/)[1],
    fieldStateClasses.match(/disabled:border-(\S+)/)[1],
  )
  // Read-only keeps full-contrast text (no dedicated read-only text-color
  // override); disabled is the only state that dims text.
  assert.equal(/read-only:text-/.test(fieldStateClasses), false)
})

test("the shared contract never uses a task-status, MEET, warning, recording, or destructive color as a generic field color", () => {
  for (const forbidden of ["sem-todo", "sem-focus", "sem-wait", "sem-done", "sem-remind", "sem-meet", "sem-panel", "sem-deadline", "sem-now", "recording", "destructive", "warning"]) {
    assert.equal(fieldStateClasses.includes(forbidden), false, `fieldStateClasses must not reference --${forbidden}`)
  }
})

test("no primitive hardcodes a literal color instead of a canonical token", () => {
  const hexOrRawColor = /#[0-9a-fA-F]{3,8}\b|\brgb\(|\brgba\(/
  for (const [name, src] of [["input.tsx", input], ["textarea.tsx", textarea], ["select.tsx", select]]) {
    assert.equal(hexOrRawColor.test(src), false, `${name} must reference tokens, not a hardcoded color`)
  }
})

test("Input, Textarea, and Select all consume the one shared state contract (no per-component duplication)", () => {
  assert.match(input, /fieldStateClasses/)
  assert.match(textarea, /fieldStateClasses/)
  assert.match(select, /fieldStateClasses/)
})

test("Input and Textarea forward native props after the merged className, so readOnly/disabled/onChange/value always reach the element", () => {
  assert.match(input, /className=\{cn\(/)
  assert.match(input, /\.\.\.props/)
  assert.match(textarea, /className=\{cn\(/)
  assert.match(textarea, /\.\.\.props/)
})

test("Select dims its chevron icon to match the disabled text token when disabled", () => {
  assert.match(select, /disabled && ['"]text-text-disabled['"]/)
})

test("migrated callers: context-pack-modal keeps its readOnly export-preview textarea", () => {
  assert.match(contextPackModal, /from ["']@\/components\/ui\/textarea["']/)
  assert.match(contextPackModal, /<Textarea[\s\S]{0,80}readOnly/)
})

test("migrated callers: linked-task-picker search uses the shared Input and drops its own duplicated inputClass", () => {
  assert.match(linkedTaskPicker, /from ["']@\/components\/ui\/input["']/)
  assert.equal(linkedTaskPicker.includes("const inputClass"), false)
})

test("migrated callers: workspace-header global search keeps its disabled gating and placeholder swap", () => {
  assert.match(workspaceHeader, /from ["']@\/components\/ui\/input["']/)
  assert.match(workspaceHeader, /disabled=\{searchDisabled\}/)
  assert.match(workspaceHeader, /Search unavailable in this view/)
})

test("migrated callers: details-panel Location Select keeps its value/onChange/options API unchanged", () => {
  assert.match(detailsPanel, /Select as FieldSelect/)
  assert.match(detailsPanel, /function Select\(\{\s*value,\s*onChange,\s*options,/)
  assert.match(detailsPanel, /<FieldSelect value=\{value\} onChange=\{\(e\) => onChange\(e\.target\.value\)\}>/)
})
