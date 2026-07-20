import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import test from "node:test"
import { fieldStateClasses, selectStateClasses } from "../components/ui/field.ts"

const input = readFileSync(new URL("../components/ui/input.tsx", import.meta.url), "utf8")
const textarea = readFileSync(new URL("../components/ui/textarea.tsx", import.meta.url), "utf8")
const select = readFileSync(new URL("../components/ui/select.tsx", import.meta.url), "utf8")
const contextPackModal = readFileSync(new URL("../components/context-pack-modal.tsx", import.meta.url), "utf8")
const linkedTaskPicker = readFileSync(new URL("../components/linked-task-picker.tsx", import.meta.url), "utf8")
const workspaceHeader = readFileSync(new URL("../components/workspace-header.tsx", import.meta.url), "utf8")
const detailsPanel = readFileSync(new URL("../components/details-panel.tsx", import.meta.url), "utf8")

test("editable state (shared by both contracts) uses the canonical field/border/text tokens and a restrained hover", () => {
  for (const classes of [fieldStateClasses, selectStateClasses]) {
    assert.match(classes, /\bbg-field\b/)
    assert.match(classes, /\bborder-border-strong\b/)
    assert.match(classes, /\btext-text\b/)
    assert.match(classes, /\bhover:border-\[/)
    assert.match(classes, /\bfocus-visible:border-primary\b/)
    assert.match(classes, /focus-visible:shadow-\[var\(--focus-ring\)\]/)
  }
})

test("Input/Textarea's fieldStateClasses includes the read-only contract, distinct from editable and disabled", () => {
  assert.match(fieldStateClasses, /\bread-only:bg-field-readonly\b/)
  assert.match(fieldStateClasses, /\bread-only:border-border\b(?!-)/)
  assert.match(fieldStateClasses, /\bread-only:cursor-default\b/)
  // Two-pseudo-class compounds so read-only always out-specificities the
  // plain single-pseudo-class hover/focus-visible rules above, regardless
  // of generated stylesheet order.
  assert.match(fieldStateClasses, /\bread-only:hover:border-border\b(?!-)/)
  assert.match(fieldStateClasses, /\bread-only:focus-visible:border-border-strong\b/)
  // No opacity dimming anywhere, and read-only keeps full-contrast text
  // (no dedicated read-only text-color override).
  assert.equal(fieldStateClasses.includes("opacity"), false)
  assert.equal(/read-only:text-/.test(fieldStateClasses), false)
  assert.notEqual(
    fieldStateClasses.match(/read-only:bg-(\S+)/)[1],
    fieldStateClasses.match(/disabled:bg-(\S+)/)[1],
  )
  assert.notEqual(
    fieldStateClasses.match(/read-only:border-(\S+)/)[1],
    fieldStateClasses.match(/disabled:border-(\S+)/)[1],
  )
})

test("Select's selectStateClasses has no read-only selector or class of any kind", () => {
  // `<select>` has no HTML `readonly` attribute, but unlike `:read-write`,
  // `:read-only` still matches elements that don't support the attribute at
  // all - so an enabled, interactive <select> would wrongly pick up any
  // `read-only:*` rule if it were present. selectStateClasses must contain
  // none, in any form (class name, compound, or bare pseudo-class).
  assert.equal(/read-only/.test(selectStateClasses), false)
  assert.equal(/:read-only/.test(selectStateClasses), false)
})

test("disabled is styled distinctly from read-only/editable in both contracts, and relies on native disabled semantics rather than pointer-events-none", () => {
  for (const classes of [fieldStateClasses, selectStateClasses]) {
    assert.match(classes, /\bdisabled:bg-field-disabled\b/)
    assert.match(classes, /\bdisabled:border-border-disabled\b/)
    assert.match(classes, /\bdisabled:text-text-disabled\b/)
    assert.match(classes, /\bdisabled:cursor-not-allowed\b/)
    // A disabled element already blocks focus/editing/pointer interaction
    // natively. `pointer-events-none` would additionally exclude it from
    // hit-testing, which prevents `cursor-not-allowed` from ever being
    // shown (the browser falls through to whatever is behind it instead).
    assert.equal(classes.includes("pointer-events-none"), false)
  }
})

test("the shared contracts never use a task-status, MEET, warning, recording, or destructive color as a generic field color", () => {
  for (const classes of [fieldStateClasses, selectStateClasses]) {
    for (const forbidden of ["sem-todo", "sem-focus", "sem-wait", "sem-done", "sem-remind", "sem-meet", "sem-panel", "sem-deadline", "sem-now", "recording", "destructive", "warning"]) {
      assert.equal(classes.includes(forbidden), false, `must not reference --${forbidden}`)
    }
  }
})

test("no primitive hardcodes a literal color instead of a canonical token", () => {
  const hexOrRawColor = /#[0-9a-fA-F]{3,8}\b|\brgb\(|\brgba\(/
  for (const [name, src] of [["input.tsx", input], ["textarea.tsx", textarea], ["select.tsx", select]]) {
    assert.equal(hexOrRawColor.test(src), false, `${name} must reference tokens, not a hardcoded color`)
  }
})

test("Input and Textarea use the read-only-capable fieldStateClasses; Select uses the read-only-free selectStateClasses", () => {
  assert.match(input, /import \{ fieldStateClasses \} from '\.\/field'/)
  assert.match(textarea, /import \{ fieldStateClasses \} from '\.\/field'/)
  // select.tsx's doc comment may name fieldStateClasses in prose (explaining
  // why it must NOT be used) - what matters is it never imports/consumes it.
  assert.equal(/import \{[^}]*\bfieldStateClasses\b[^}]*\} from '\.\/field'/.test(select), false)
  assert.match(select, /import \{ selectStateClasses \} from '\.\/field'/)
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
