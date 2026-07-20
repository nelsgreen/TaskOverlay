import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import test from "node:test"
import { resolveSegmentedControlValue } from "../components/ui/segmented-control-value.ts"

const tabsSrc = readFileSync(new URL("../components/ui/tabs.tsx", import.meta.url), "utf8")
const segmentedControlSrc = readFileSync(
  new URL("../components/ui/segmented-control.tsx", import.meta.url),
  "utf8",
)

// Base UI packages we build on (@base-ui/react/tabs, /toggle-group, /toggle)
// are vendored, compiled JS - no JSX, so they can be read directly to
// characterize the exact accessibility/keyboard contract our thin wrapper
// relies on instead of reimplementing, matching this repo's node --test
// convention of not introducing a DOM renderer. If a base-ui upgrade ever
// changes these internals, these assertions are the tripwire.
const tabsTabPkg = readFileSync(
  new URL(
    "../node_modules/@base-ui/react/tabs/tab/TabsTab.js",
    import.meta.url,
  ),
  "utf8",
)
const tabsListPkg = readFileSync(
  new URL(
    "../node_modules/@base-ui/react/tabs/list/TabsList.js",
    import.meta.url,
  ),
  "utf8",
)
const tabsPanelPkg = readFileSync(
  new URL(
    "../node_modules/@base-ui/react/tabs/panel/TabsPanel.js",
    import.meta.url,
  ),
  "utf8",
)
const toggleGroupPkg = readFileSync(
  new URL(
    "../node_modules/@base-ui/react/toggle-group/ToggleGroup.js",
    import.meta.url,
  ),
  "utf8",
)
const togglePkg = readFileSync(
  new URL("../node_modules/@base-ui/react/toggle/Toggle.js", import.meta.url),
  "utf8",
)
const compositeItemPkg = readFileSync(
  new URL(
    "../node_modules/@base-ui/react/internals/composite/item/useCompositeItem.js",
    import.meta.url,
  ),
  "utf8",
)
const compositePkg = readFileSync(
  new URL("../node_modules/@base-ui/react/internals/composite/composite.js", import.meta.url),
  "utf8",
)

// Same hardcoded-color detector as lib/button-primitives.test.mjs (matches
// both normal CSS `rgb(`/`rgba(` and Tailwind's underscore arbitrary-value
// form, e.g. `rgb(0_0_0/0.18)`).
const hardcodedColorPattern = /#[0-9a-fA-F]{3,8}\b|rgba?[\s_]*\(/i

function namedExportStatements(src) {
  return src.match(/export\s+(?:type\s+)?\{[^}]*\}/g) ?? []
}

/** Strips `/** ... *\/` and `// ...` comments so hand-authored-attribute
 * checks don't false-positive on doc comments that merely *describe* an
 * attribute the underlying primitive owns (e.g. `` `role="tab"` `` in a
 * docblock explaining what @base-ui/react/tabs already provides). */
function stripComments(src) {
  return src.replace(/\/\*[\s\S]*?\*\//g, "").replace(/\/\/.*$/gm, "")
}

// ---------------------------------------------------------------------------
// Tabs: characterize the base-ui contract our primitive depends on
// ---------------------------------------------------------------------------

test("base-ui Tabs.Tab owns role=tab, aria-selected, and aria-controls (not reimplemented in our wrapper)", () => {
  assert.match(tabsTabPkg, /role:\s*'tab'/)
  assert.match(tabsTabPkg, /'aria-selected':\s*active/)
  assert.match(tabsTabPkg, /'aria-controls':\s*tabPanelId/)
})

test("base-ui Tabs.List owns role=tablist and Home/End keyboard navigation", () => {
  assert.match(tabsListPkg, /role:\s*'tablist'/)
  assert.match(tabsListPkg, /enableHomeAndEndKeys:\s*true/)
})

test("base-ui Tabs.Panel owns role=tabpanel and aria-labelledby, and fully unmounts (returns null) when not the active tab unless keepMounted", () => {
  assert.match(tabsPanelPkg, /role:\s*'tabpanel'/)
  assert.match(tabsPanelPkg, /'aria-labelledby':\s*correspondingTabId/)
  assert.match(tabsPanelPkg, /shouldRender\s*=\s*keepMounted\s*\|\|\s*mounted/)
  assert.match(tabsPanelPkg, /if\s*\(!shouldRender\)\s*\{\s*\n\s*return null/)
})

test("roving tabIndex (0 for the highlighted/active item, -1 otherwise) is shared composite-item logic, used by both Tabs.Tab and Toggle", () => {
  assert.match(compositeItemPkg, /tabIndex:\s*isHighlighted\s*\?\s*0\s*:\s*-1/)
})

test("ArrowLeft/ArrowRight are the horizontal composite keys, and Home/End extend them when enableHomeAndEndKeys is set", () => {
  assert.match(compositePkg, /HORIZONTAL_KEYS\s*=.*=\s*new Set\(\[ARROW_LEFT, ARROW_RIGHT\]\)/)
  assert.match(compositePkg, /HORIZONTAL_KEYS_WITH_EXTRA_KEYS\s*=.*=\s*new Set\(\[ARROW_LEFT, ARROW_RIGHT, HOME, END\]\)/)
})

test("disabled items are excluded from composite highlight/focus (the disabled-skip contract) via a real disabled attribute/aria-disabled check", () => {
  assert.match(compositeItemPkg, /disabled\s*=\s*item\.hasAttribute\('disabled'\)\s*\|\|\s*item\.ariaDisabled\s*===\s*'true'/)
  assert.match(compositeItemPkg, /!isHighlighted\s*&&\s*!disabled/)
})

test("Tabs primitive: Tab/TabList/TabPanel forward to the real @base-ui/react/tabs parts and set no conflicting role/aria-selected/aria-controls of their own", () => {
  assert.match(tabsSrc, /import \{ Tabs as TabsPrimitive \} from ['"]@base-ui\/react\/tabs['"]/)
  assert.match(tabsSrc, /<TabsPrimitive\.Root/)
  assert.match(tabsSrc, /<TabsPrimitive\.List/)
  assert.match(tabsSrc, /<TabsPrimitive\.Tab/)
  assert.match(tabsSrc, /<TabsPrimitive\.Panel/)
  const tabsCode = stripComments(tabsSrc)
  for (const forbidden of [/\brole=["']tab["']/, /\brole=["']tablist["']/, /\brole=["']tabpanel["']/, /aria-selected=\{/, /aria-controls=\{/]) {
    assert.equal(forbidden.test(tabsCode), false, `tabs.tsx must not hand-author ${forbidden}`)
  }
})

test("Tab's selected visual is carried by the real aria-selected attribute (Tailwind aria-selected: variant), never a separate boolean/color-only prop", () => {
  assert.match(tabsSrc, /aria-selected:font-semibold/)
  assert.match(tabsSrc, /aria-selected:text-text/)
  assert.match(tabsSrc, /aria-selected:after:bg-selection/)
})

test("Tab and TabPanel carry the canonical focus-visible ring token", () => {
  assert.equal((tabsSrc.match(/focus-visible:shadow-\[var\(--focus-ring\)\]/g) ?? []).length >= 2, true)
})

test("disabled Tab styling is native-disabled-driven, matching the Button/Field convention", () => {
  assert.match(tabsSrc, /disabled:cursor-not-allowed disabled:opacity-50/)
})

test("tabs.tsx contains no hardcoded color, in normal CSS or Tailwind arbitrary-value syntax", () => {
  assert.equal(hardcodedColorPattern.test(tabsSrc), false)
})

test("tabs.tsx exports only the four components and their prop types - no raw variant/class helper", () => {
  const exportsText = namedExportStatements(tabsSrc).join("\n")
  assert.match(exportsText, /\bTabs\b/)
  assert.match(exportsText, /\bTabList\b/)
  assert.match(exportsText, /\bTab\b/)
  assert.match(exportsText, /\bTabPanel\b/)
  assert.equal(/Variants|Classes\b/.test(exportsText), false)
})

// ---------------------------------------------------------------------------
// SegmentedControl: characterize the base-ui contract, then our own
// single-value guarantee on top of it
// ---------------------------------------------------------------------------

test("base-ui ToggleGroup owns role=group and Home/End + arrow-key composite navigation between enabled items", () => {
  assert.match(toggleGroupPkg, /role:\s*'group'/)
  assert.match(toggleGroupPkg, /enableHomeAndEndKeys:\s*true/)
})

test("base-ui Toggle owns the real aria-pressed attribute used for selected styling", () => {
  assert.match(togglePkg, /'aria-pressed':\s*pressed/)
})

test("plain ToggleGroup(multiple: false) can report an empty selection when the pressed item is clicked again - this is exactly the gap SegmentedControl closes", () => {
  assert.match(toggleGroupPkg, /newGroupValue\s*=\s*nextPressed\s*\?\s*\[newValue\]\s*:\s*\[\]/)
})

test("resolveSegmentedControlValue: always resolves to the single pressed value", () => {
  assert.equal(resolveSegmentedControlValue(["day"]), "day")
  assert.equal(resolveSegmentedControlValue(["day", "week"]), "week")
})

test("resolveSegmentedControlValue: an empty selection (the click-to-deselect case) resolves to null so the caller can cancel it, never silently accepting 'nothing selected'", () => {
  assert.equal(resolveSegmentedControlValue([]), null)
})

test("SegmentedControl cancels the base-ui change event whenever resolveSegmentedControlValue reports no selection, so a caller's onValueChange is never called with 'nothing selected'", () => {
  assert.match(
    segmentedControlSrc,
    /const resolved = resolveSegmentedControlValue\(next\)\s*\n\s*if \(resolved === null\) \{\s*\n\s*eventDetails\.cancel\(\)/,
  )
})

test("SegmentedControl actually renders its children inside ToggleGroup (regression: destructuring `children` out of props and forgetting to render it silently drops every item)", () => {
  const rootFn = segmentedControlSrc.slice(
    segmentedControlSrc.indexOf("function SegmentedControl<"),
    segmentedControlSrc.indexOf("function SegmentedControlItem<"),
  )
  assert.match(rootFn, /<ToggleGroup[\s\S]*?>\s*\{children\}\s*<\/ToggleGroup>/)
})

test("SegmentedControl/SegmentedControlItem forward to the real @base-ui/react toggle-group/toggle parts and set no conflicting role/aria-pressed of their own", () => {
  assert.match(segmentedControlSrc, /import \{ ToggleGroup \} from ['"]@base-ui\/react\/toggle-group['"]/)
  assert.match(segmentedControlSrc, /import \{ Toggle \} from ['"]@base-ui\/react\/toggle['"]/)
  assert.match(segmentedControlSrc, /<ToggleGroup/)
  assert.match(segmentedControlSrc, /<Toggle\b/)
  const segmentedControlCode = stripComments(segmentedControlSrc)
  for (const forbidden of [/\brole=["']group["']/, /aria-pressed=\{/]) {
    assert.equal(forbidden.test(segmentedControlCode), false, `segmented-control.tsx must not hand-author ${forbidden}`)
  }
})

test("SegmentedControlItem's selected visual is carried by the real aria-pressed attribute (Tailwind aria-pressed: variant), matching the frozen spec's .segment button[aria-pressed] contract", () => {
  assert.match(segmentedControlSrc, /aria-pressed:bg-surface-raised/)
  assert.match(segmentedControlSrc, /aria-pressed:font-semibold/)
  assert.match(segmentedControlSrc, /aria-pressed:text-text/)
})

test("selected state cannot be produced through an unrelated public visual variant: SegmentedControlItemProps exposes no tone/variant/selected prop, only value/disabled/className/children", () => {
  const propsBlock = segmentedControlSrc.slice(
    segmentedControlSrc.indexOf("interface SegmentedControlItemProps"),
    segmentedControlSrc.indexOf("function SegmentedControlItem"),
  )
  for (const forbidden of ["tone", "variant", "selected", "pressed"]) {
    assert.equal(new RegExp(`\\b${forbidden}\\b`).test(propsBlock), false, `SegmentedControlItemProps must not expose a '${forbidden}' prop`)
  }
})

test("SegmentedControl's group disabled styling is native-disabled-driven, matching the Button/Field/Tab convention", () => {
  assert.match(segmentedControlSrc, /disabled:cursor-not-allowed disabled:opacity-50/)
})

test("segmented-control.tsx contains no hardcoded color, in normal CSS or Tailwind arbitrary-value syntax", () => {
  assert.equal(hardcodedColorPattern.test(segmentedControlSrc), false)
})

test("segmented-control.tsx exports only the two components and their prop types - no raw variant/class helper", () => {
  const exportsText = namedExportStatements(segmentedControlSrc).join("\n")
  assert.match(exportsText, /\bSegmentedControl\b/)
  assert.match(exportsText, /\bSegmentedControlItem\b/)
  assert.equal(/Variants|Classes\b/.test(exportsText), false)
})

test("segmented-control-value.ts (the pure single-value resolver) has no JSX/React import - it stays a plain, directly-testable module", () => {
  const src = readFileSync(new URL("../components/ui/segmented-control-value.ts", import.meta.url), "utf8")
  assert.equal(/from ['"]react['"]/.test(src), false)
  // No JSX: neither a closing tag nor a self-closing tag ever appears in a
  // plain generic type parameter like `<Value extends string>`.
  assert.equal(/<\//.test(src), false)
  assert.equal(/\/>/.test(src), false)
})
