import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import test from "node:test"

const read = (relativePath) => readFileSync(new URL(relativePath, import.meta.url), "utf8")

const statusBadgeSrc = read("../components/ui/status-badge.tsx")
const metadataBadgeSrc = read("../components/ui/metadata-badge.tsx")
const emptyStateSrc = read("../components/ui/empty-state.tsx")
const loadingStateSrc = read("../components/ui/loading-state.tsx")
const errorStateSrc = read("../components/ui/error-state.tsx")
const savedStateSrc = read("../components/ui/saved-state.tsx")
const panelSrc = read("../components/ui/panel.tsx")
const detailsSectionSrc = read("../components/ui/details-section.tsx")

const taskRowSrc = read("../components/task-row.tsx")
const contextHubViewSrc = read("../components/context-hub-view.tsx")
const meetDetailsPanelSrc = read("../components/meet-details-panel.tsx")
const taskContextBlockSrc = read("../components/task-context-block.tsx")

// Same detector used by lib/tabs-segmented-control-primitives.test.mjs:
// catches hex plus every raw color function this codebase could plausibly
// leak - rgb/rgba, hsl/hsla, oklch/oklab - in normal CSS and Tailwind's
// underscore arbitrary-value form.
const hardcodedColorPattern = /#[0-9a-fA-F]{3,8}\b|(?:rgba?|hsla?|oklch|oklab)[\s_]*\(/i

function namedExportStatements(src) {
  return src.match(/export\s+(?:type\s+)?\{[^}]*\}/g) ?? []
}

const allPrimitives = [
  ["status-badge.tsx", statusBadgeSrc],
  ["metadata-badge.tsx", metadataBadgeSrc],
  ["empty-state.tsx", emptyStateSrc],
  ["loading-state.tsx", loadingStateSrc],
  ["error-state.tsx", errorStateSrc],
  ["saved-state.tsx", savedStateSrc],
  ["panel.tsx", panelSrc],
  ["details-section.tsx", detailsSectionSrc],
]

test("no shared primitive hardcodes a literal color (hex, rgb/rgba, hsl/hsla, oklch/oklab) instead of a canonical token", () => {
  for (const [name, src] of allPrimitives) {
    assert.equal(hardcodedColorPattern.test(src), false, `${name} must reference tokens, not a hardcoded color`)
  }
})

// ---------------------------------------------------------------------------
// StatusBadge - domain-semantic status only, no raw exported helper
// ---------------------------------------------------------------------------

test("StatusBadge does not export the raw status/class config - only the component", () => {
  assert.equal(/export\s+(?:const|function)\s+statusConfig/.test(statusBadgeSrc), false)
  assert.match(statusBadgeSrc, /export function StatusBadge/)
})

test("StatusBadge's public props are status + className only - semantic state cannot be faked through an unrelated visual prop", () => {
  const signature = statusBadgeSrc.match(/export function StatusBadge\(\{[^}]*\}: \{[^}]*\}\)/)?.[0] ?? ""
  assert.match(signature, /status: Status/)
  for (const forbidden of ["color", "tone", "variant"]) {
    assert.equal(new RegExp(`\\b${forbidden}\\b`).test(signature), false, `StatusBadge props must not expose a '${forbidden}' prop`)
  }
})

test("StatusBadge always renders a visible text label, not color alone", () => {
  assert.match(statusBadgeSrc, /\{c\.label\}/)
})

test("StatusBadge uses domain sem/status tokens (FOCUS stays task-domain green), never the global accent, for status color", () => {
  assert.match(statusBadgeSrc, /bg-status-focus/)
  assert.equal(/\btext-accent\b|\bbg-accent\b/.test(statusBadgeSrc), false)
})

// ---------------------------------------------------------------------------
// MetadataBadge - neutral by default, tiny justified tone set, no raw helper
// ---------------------------------------------------------------------------

test("MetadataBadge does not export its tone-to-class map - only the component and its tone type", () => {
  assert.equal(/export\s+const\s+toneClasses/.test(metadataBadgeSrc), false)
  assert.match(metadataBadgeSrc, /export function MetadataBadge/)
})

test("MetadataBadge defaults to neutral and its tone set stays small (not an arbitrary-color API)", () => {
  assert.match(metadataBadgeSrc, /tone = ["']neutral["']/)
  assert.match(metadataBadgeSrc, /export type MetadataBadgeTone = ["']neutral["']/)
})

// ---------------------------------------------------------------------------
// Empty/Loading/Error/Saved state contracts
// ---------------------------------------------------------------------------

test("LoadingState exposes accessible busy/status semantics and no fabricated progress percentage", () => {
  assert.match(loadingStateSrc, /role=["']status["']/)
  assert.match(loadingStateSrc, /aria-busy=["']true["']/)
  assert.match(loadingStateSrc, /aria-live=["']polite["']/)
  assert.equal(/%/.test(loadingStateSrc), false, "LoadingState must never render a percentage")
})

test("ErrorState uses error semantics (role=alert) and destructive tokens", () => {
  assert.match(errorStateSrc, /role=["']alert["']/)
  assert.match(errorStateSrc, /destructive/)
})

test("SavedState is calm/non-prominent and stays a distinct contract from ErrorState: no role=alert, no destructive banner background", () => {
  assert.equal(/role=["']alert["']/.test(savedStateSrc), false)
  assert.equal(/bg-destructive/.test(savedStateSrc), false)
  assert.match(savedStateSrc, /aria-live=["']polite["']/)
})

test("EmptyState is neutral (no destructive/warning/status coloring) and has no decorative oversized illustration (no <svg>/<img>)", () => {
  for (const forbidden of ["destructive", "warning", "status-", "<svg", "<img"]) {
    assert.equal(emptyStateSrc.includes(forbidden), false, `EmptyState must not reference ${forbidden}`)
  }
})

test("EmptyState/LoadingState/ErrorState/SavedState expose only a small title/message/action-style API, not a generic children/render-prop escape hatch", () => {
  for (const [name, src] of [
    ["empty-state.tsx", emptyStateSrc],
    ["loading-state.tsx", loadingStateSrc],
    ["error-state.tsx", errorStateSrc],
  ]) {
    assert.equal(/\bchildren\b/.test(src), false, `${name} should not expose a children escape hatch`)
  }
})

// ---------------------------------------------------------------------------
// Panel - bounded surface, no mandatory shadow, layout-only className
// ---------------------------------------------------------------------------

test("Panel supplies the canonical surface/border/radius and has no mandatory shadow utility of its own", () => {
  assert.match(panelSrc, /rounded-lg border border-border bg-card\/40/)
  assert.equal(/\bshadow-/.test(panelSrc), false)
})

test("Panel merges className after its canonical classes (layout-only, cannot restyle the semantic surface via cn precedence)", () => {
  assert.match(panelSrc, /cn\(["']rounded-lg border border-border bg-card\/40["'], className\)/)
})

// ---------------------------------------------------------------------------
// DetailsSection - title + optional action + body, no new accordion system
// ---------------------------------------------------------------------------

test("DetailsSection has no internal multi-section registry/state - it is a single controlled section, not a new accordion system", () => {
  assert.equal(/useState|useReducer|useContext/.test(detailsSectionSrc), false)
  assert.match(detailsSectionSrc, /open\?: boolean/)
  assert.match(detailsSectionSrc, /onOpenChange\?: \(open: boolean\) => void/)
})

test("DetailsSection renders the title and supports optional collapse only when the caller controls open/onOpenChange", () => {
  assert.match(detailsSectionSrc, /const collapsible = open !== undefined && onOpenChange !== undefined/)
  assert.match(detailsSectionSrc, /\{title\}/)
})

// ---------------------------------------------------------------------------
// Proof migrations: exact behavior/labels/handlers preserved
// ---------------------------------------------------------------------------

test("proof migration: task-row.tsx renders StatusBadge from the canonical components/ui location", () => {
  assert.match(taskRowSrc, /from ["']@\/components\/ui\/status-badge["']/)
  assert.match(taskRowSrc, /<StatusBadge status=\{task\.status\} \/>/)
})

test("proof migration: context-hub-view.tsx's SourceChip delegates to MetadataBadge, same call sites/props unchanged", () => {
  assert.match(contextHubViewSrc, /from ["']@\/components\/ui\/metadata-badge["']/)
  assert.match(contextHubViewSrc, /function SourceChip\(\{ type \}: \{ type: ContextSourceType \}\) \{\s*\n\s*return <MetadataBadge icon=\{FileText\} label=\{sourceTypeMeta\[type\]\.short\} \/>/)
  assert.match(contextHubViewSrc, /<SourceChip type=\{sourceType\} \/>/)
  assert.match(contextHubViewSrc, /<SourceChip type=\{source\.sourceType\} \/>/)
})

test("proof migration: meet-details-panel.tsx's autosave indicator uses SavedState with the same retry handler and labels", () => {
  assert.match(meetDetailsPanelSrc, /from ["']@\/components\/ui\/saved-state["']/)
  assert.match(meetDetailsPanelSrc, /<SavedState/)
  assert.match(meetDetailsPanelSrc, /status=\{saveStatus\}/)
  assert.match(meetDetailsPanelSrc, /onRetry=\{\(\) => void autosaveRef\.current\?\.retry\(\)\}/)
})

test("proof migration: task-context-block.tsx's Context card uses Panel + DetailsSection, preserving the Context title, linked-count meta, and all three actions", () => {
  assert.match(taskContextBlockSrc, /from ["']@\/components\/ui\/panel["']/)
  assert.match(taskContextBlockSrc, /from ["']@\/components\/ui\/details-section["']/)
  assert.match(taskContextBlockSrc, /<Panel className="group\/card mt-3">/)
  assert.match(taskContextBlockSrc, /<DetailsSection/)
  assert.match(taskContextBlockSrc, /title="Context"/)
  assert.match(taskContextBlockSrc, /open=\{open\}/)
  assert.match(taskContextBlockSrc, /onOpenChange=\{setManualOpen\}/)
  // Preserved handlers/labels: Link/Hub/Export row untouched.
  assert.match(taskContextBlockSrc, /title="Link existing context"/)
  assert.match(taskContextBlockSrc, /title="Open ContextHUB"/)
  assert.match(taskContextBlockSrc, /title="Context Pack export"/)
})
