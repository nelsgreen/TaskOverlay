import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import test from "node:test"
import { buttonDisabledClasses, buttonToneClasses } from "../components/ui/button-tone.ts"

const buttonToneSrc = readFileSync(new URL("../components/ui/button-tone.ts", import.meta.url), "utf8")

const buttonSrc = readFileSync(new URL("../components/ui/button.tsx", import.meta.url), "utf8")
const iconButtonSrc = readFileSync(new URL("../components/ui/icon-button.tsx", import.meta.url), "utf8")
const taskContextBlock = readFileSync(new URL("../components/task-context-block.tsx", import.meta.url), "utf8")
const contextPackModal = readFileSync(new URL("../components/context-pack-modal.tsx", import.meta.url), "utf8")

/**
 * Catches hardcoded colors in both normal CSS syntax (`rgb(`, `rgba(`) and
 * Tailwind arbitrary-value syntax, where spaces are written as underscores
 * (`rgb_`, `rgba_`) - a plain `rgb\(` regex misses `rgb(0_0_0/0.18)` because
 * the space before the opening paren became an underscore, not a real space.
 * Hex colors are matched in either position.
 */
// No `\b` before `rgba?`: Tailwind arbitrary values often precede it with an
// underscore (`shadow-[0_1px_2px_rgb(0_0_0/0.18)]`), and `_` counts as a word
// character - `\brgb` would never match right after it, silently missing the
// exact underscore-arbitrary-value form this pattern exists to catch.
const hardcodedColorPattern = /#[0-9a-fA-F]{3,8}\b|rgba?[\s_]*\(/i

test("every button tone reads a canonical token for its background/text/border, never a raw color", () => {
  for (const [tone, classes] of Object.entries(buttonToneClasses)) {
    assert.equal(hardcodedColorPattern.test(classes), false, `${tone} must reference tokens, not a hardcoded color`)
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
  // Regression: selected's shadow must reference the canonical shadow token,
  // not a hardcoded rgb() (the bug the strengthened pattern below now catches).
  assert.match(buttonToneClasses.selected, /var\(--shadow-1\)/)
})

test("the hardcoded-color pattern itself catches the underscore-arbitrary-value form a plain rgb\\( regex misses", () => {
  // This is the exact shape that leaked through the original, weaker test:
  // Tailwind arbitrary values write spaces as underscores, so `rgb(0 0 0/0.18)`
  // becomes `rgb(0_0_0/0.18)` - no literal "rgb(" substring survives immediately
  // followed by a digit, but there's still a raw color hidden in the class list.
  assert.equal(hardcodedColorPattern.test('shadow-[0_1px_2px_rgb(0_0_0/0.18)]'), true)
  assert.equal(hardcodedColorPattern.test('bg-[rgba(0,0,0,0.5)]'), true)
  assert.equal(hardcodedColorPattern.test('text-[#fff]'), true)
  // Canonical token references must not false-positive.
  assert.equal(hardcodedColorPattern.test('shadow-[var(--shadow-1)]'), false)
})

test("no tone contains any hardcoded color anywhere, in normal CSS or Tailwind arbitrary-value syntax", () => {
  for (const [tone, classes] of Object.entries(buttonToneClasses)) {
    assert.equal(hardcodedColorPattern.test(classes), false, `${tone} must not contain a literal color`)
  }
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

test("PublicButtonTone excludes 'selected' - selected only ever applies through pressed, never as a directly selectable tone", () => {
  assert.match(buttonToneSrc, /export type PublicButtonTone = Exclude<ButtonTone, ['"]selected['"]>/)
  // buttonToneClasses itself keeps the internal 'selected' entry (Button/
  // IconButton still need to resolve it internally via `pressed`).
  assert.ok("selected" in buttonToneClasses)
})

test("Button and IconButton's public tone prop is typed as PublicButtonTone, not the raw cva/VariantProps tone (which would include 'selected')", () => {
  for (const src of [buttonSrc, iconButtonSrc]) {
    assert.match(src, /import \{[^}]*\bPublicButtonTone\b[^}]*\} from ['"]\.\/button-tone['"]/)
    assert.match(src, /tone\?:\s*PublicButtonTone/)
    // The VariantProps intersection must have 'tone' omitted, or the raw
    // (selected-inclusive) cva type would leak back in through it.
    assert.match(src, /Omit<VariantProps<typeof \w+Variants>,\s*['"]tone['"]>/)
  }
})

test("controlled accessibility props (aria-pressed, aria-busy, disabled) are excluded from Button's accepted native props, so a caller cannot pass a conflicting value", () => {
  const extendsLine = buttonSrc.match(/interface ButtonProps\s*\n\s*extends Omit<ButtonPrimitive\.Props,([^>]+)>/)
  assert.ok(extendsLine, "expected an Omit<ButtonPrimitive.Props, ...> extends clause")
  for (const excluded of ["aria-pressed", "aria-busy", "disabled"]) {
    assert.match(extendsLine[1], new RegExp(`['"]${excluded}['"]`))
  }
})

test("controlled accessibility props (aria-label, aria-pressed, aria-busy, disabled) are excluded from IconButton's accepted native props", () => {
  const extendsBlock = iconButtonSrc.match(/interface IconButtonProps\s*\n\s*extends Omit<\s*\n?\s*ButtonPrimitive\.Props,([\s\S]+?)>/)
  assert.ok(extendsBlock, "expected an Omit<ButtonPrimitive.Props, ...> extends clause")
  for (const excluded of ["aria-label", "aria-pressed", "aria-busy", "disabled"]) {
    assert.match(extendsBlock[1], new RegExp(`['"]${excluded}['"]`))
  }
})

test("Button and IconButton spread caller props before the controlled accessibility attributes in JSX, so even an unexpected/loosely-typed caller prop can never win", () => {
  for (const src of [buttonSrc, iconButtonSrc]) {
    const spreadIdx = src.indexOf("{...props}")
    const ariaPressedIdx = src.indexOf("aria-pressed={pressed}")
    const ariaBusyIdx = src.indexOf("aria-busy={loading")
    const disabledIdx = src.indexOf("disabled={disabled || loading}")
    assert.notEqual(spreadIdx, -1, "expected {...props} in the returned JSX")
    assert.ok(spreadIdx < ariaPressedIdx, "{...props} must come before aria-pressed")
    assert.ok(spreadIdx < ariaBusyIdx, "{...props} must come before aria-busy")
    assert.ok(spreadIdx < disabledIdx, "{...props} must come before disabled")
  }
  const labelIdx = iconButtonSrc.indexOf("{...props}")
  const ariaLabelIdx = iconButtonSrc.indexOf("aria-label={label}")
  assert.ok(labelIdx < ariaLabelIdx, "{...props} must come before aria-label in IconButton")
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

/**
 * Every `export { ... }` / `export type { ... }` statement in `src`, so a
 * test can assert on exactly what a module-level import can see - not just
 * grep the whole file text, which would also match the internal `const
 * buttonVariants = cva(...)` declaration and false-negative on a leak.
 */
function namedExportStatements(src) {
  return src.match(/export\s+(?:type\s+)?\{[^}]*\}/g) ?? []
}

test("buttonVariants stays module-local: it is declared but never appears in any export statement", () => {
  assert.match(buttonSrc, /const buttonVariants = cva\(/, "buttonVariants must still exist internally")
  const exportsText = namedExportStatements(buttonSrc).join("\n")
  assert.equal(/\bbuttonVariants\b/.test(exportsText), false)
  assert.match(exportsText, /\bButton\b/)
  assert.match(exportsText, /\bButtonProps\b/)
})

test("iconButtonVariants stays module-local: it is declared but never appears in any export statement", () => {
  assert.match(iconButtonSrc, /const iconButtonVariants = cva\(/, "iconButtonVariants must still exist internally")
  const exportsText = namedExportStatements(iconButtonSrc).join("\n")
  assert.equal(/\biconButtonVariants\b/.test(exportsText), false)
  assert.match(exportsText, /\bIconButton\b/)
  assert.match(exportsText, /\bIconButtonProps\b/)
})

test("Button/IconButton modules export nothing beyond the component and its props type - no back door to the raw cva variants (and therefore no back door to the internal 'selected' tone)", () => {
  const buttonExports = namedExportStatements(buttonSrc)
  const iconButtonExports = namedExportStatements(iconButtonSrc)
  assert.equal(buttonExports.length, 2, "expected exactly one value export and one type export from button.tsx")
  assert.equal(iconButtonExports.length, 2, "expected exactly one value export and one type export from icon-button.tsx")
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
