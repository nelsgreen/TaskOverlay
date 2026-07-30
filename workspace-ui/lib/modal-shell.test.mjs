import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import test from "node:test"

const modalShellSrc = readFileSync(new URL("../components/ui/modal-shell.tsx", import.meta.url), "utf8")
const meetDetailsSrc = readFileSync(new URL("../components/meet-details-panel.tsx", import.meta.url), "utf8")
const globalsSrc = readFileSync(new URL("../app/globals.css", import.meta.url), "utf8")
const meetSourcesReviewSrc = readFileSync(new URL("../components/meet-sources-review.tsx", import.meta.url), "utf8")
const meetingAssistantSrc = readFileSync(new URL("../components/meeting-assistant-section.tsx", import.meta.url), "utf8")

// Same detector used by the other design-system primitive tests (see
// lib/design-system-primitives.test.mjs / lib/tabs-segmented-control-primitives.test.mjs).
const hardcodedColorPattern = /#[0-9a-fA-F]{3,8}\b|(?:rgba?|hsla?|oklch|oklab)[\s_]*\(/i

// ---------------------------------------------------------------------------
// Dialog semantics + stable labelling
// ---------------------------------------------------------------------------

test("ModalShell renders real dialog semantics: role=dialog, aria-modal=true, aria-labelledby wired to the caller's titleId", () => {
  assert.match(modalShellSrc, /role="dialog"/)
  assert.match(modalShellSrc, /aria-modal="true"/)
  assert.match(modalShellSrc, /aria-labelledby=\{titleId\}/)
})

test("ModalShell requires a titleId prop (string) - the label relationship is never optional or guessed", () => {
  assert.match(modalShellSrc, /titleId: string/)
})

// ---------------------------------------------------------------------------
// No backdrop-click close contract
// ---------------------------------------------------------------------------

test("ModalShell's scrim has no onClick/close handler - backdrop-click-to-close is structurally impossible, not just unwired", () => {
  const scrimLine = modalShellSrc.split("\n").find((line) => line.includes("bg-[var(--scrim)]"))
  assert.ok(scrimLine, "expected a scrim element referencing --scrim")
  assert.doesNotMatch(scrimLine, /onClick/)
  // No `onClose` prop of any kind is declared or destructured - only the
  // doc comment above may mention the word in prose.
  assert.doesNotMatch(modalShellSrc, /onClose[?]?:/)
  assert.doesNotMatch(modalShellSrc, /\{\s*onClose\s*\}/)
})

// ---------------------------------------------------------------------------
// Canonical scrim/surface/shadow/border/radius tokens
// ---------------------------------------------------------------------------

test("ModalShell uses the canonical scrim, surface, border, radius, and shadow-3 tokens", () => {
  assert.match(modalShellSrc, /bg-\[var\(--scrim\)\]/)
  assert.match(modalShellSrc, /bg-surface-raised/)
  assert.match(modalShellSrc, /border-border\b/)
  assert.match(modalShellSrc, /rounded-xl/)
  assert.match(modalShellSrc, /shadow-\[var\(--shadow-3\)\]/)
})

test("ModalShell has no literal color (hex, rgb/rgba, hsl/hsla, oklch/oklab) - tokens only", () => {
  assert.equal(hardcodedColorPattern.test(modalShellSrc), false)
})

// ---------------------------------------------------------------------------
// No raw exported style/variant helper; no appearance-overriding className
// or style prop of any kind on any of the four components
// ---------------------------------------------------------------------------

test("modal-shell.tsx exports only the four components and their prop types - no raw class-string/variant helper a caller could import to restyle semantic appearance", () => {
  assert.match(modalShellSrc, /export \{ ModalShell, ModalHeader, ModalBody, ModalFooter \}/)
  assert.match(modalShellSrc, /export type \{ ModalShellProps, ModalHeaderProps, ModalBodyProps, ModalFooterProps \}/)
  assert.equal(/export\s+const\s+\w*[Cc]lass/.test(modalShellSrc), false)
})

test("ModalShell/ModalHeader/ModalBody/ModalFooter accept no className or style prop - a caller cannot override surface, border, radius, shadow, or text color from outside", () => {
  for (const propsBlock of [
    modalShellSrc.slice(modalShellSrc.indexOf("interface ModalShellProps"), modalShellSrc.indexOf("function ModalShell(")),
    modalShellSrc.slice(modalShellSrc.indexOf("interface ModalHeaderProps"), modalShellSrc.indexOf("function ModalHeader(")),
    modalShellSrc.slice(modalShellSrc.indexOf("interface ModalBodyProps"), modalShellSrc.indexOf("function ModalBody(")),
    modalShellSrc.slice(modalShellSrc.indexOf("interface ModalFooterProps"), modalShellSrc.indexOf("function ModalFooter(")),
  ]) {
    assert.doesNotMatch(propsBlock, /className/)
    assert.doesNotMatch(propsBlock, /\bstyle\??:/)
  }
  // The four render functions never destructure or forward a className/style
  // parameter, and never spread arbitrary rest props onto the DOM.
  assert.doesNotMatch(modalShellSrc, /\{\s*[\w,\s]*className[\w,\s]*\}/)
  assert.doesNotMatch(modalShellSrc, /\.\.\.props/)
  assert.doesNotMatch(modalShellSrc, /import \{ cn \}/)
})

test("ModalShell's bounded geometry is expressed through typed numeric props (maxWidthPx/maxHeightPx/viewportWidthPercent/viewportHeightPercent), computed into an inline min()/min() style - not a caller-supplied class string", () => {
  assert.match(modalShellSrc, /maxWidthPx: number/)
  assert.match(modalShellSrc, /maxHeightPx: number/)
  assert.match(modalShellSrc, /viewportWidthPercent: number/)
  assert.match(modalShellSrc, /viewportHeightPercent: number/)
  assert.match(modalShellSrc, /width: `min\(\$\{maxWidthPx\}px, \$\{viewportWidthPercent\}vw\)`/)
  assert.match(modalShellSrc, /height: `min\(\$\{maxHeightPx\}px, \$\{viewportHeightPercent\}dvh\)`/)
})

// ---------------------------------------------------------------------------
// Bounded geometry, fixed across content; inner regions own scrolling
// ---------------------------------------------------------------------------

test("ModalBody is overflow-hidden, not overflow-y-auto - the shell region itself never scrolls, only inner content regions do", () => {
  assert.match(modalShellSrc, /function ModalBody[\s\S]{0,200}overflow-hidden/)
  const modalBodyBlock = modalShellSrc.slice(modalShellSrc.indexOf("function ModalBody"), modalShellSrc.indexOf("interface ModalFooterProps"))
  assert.doesNotMatch(modalBodyBlock, /overflow-y-auto/)
})

// ---------------------------------------------------------------------------
// MEET modal composes ModalShell (the required production migration)
// ---------------------------------------------------------------------------

test("MeetDetailsModal uses the canonical ModalShell/ModalHeader/ModalBody/ModalFooter, not a bespoke shell, and applies the accepted geometry through MEET_SHELL_GEOMETRY spread onto ModalShell's typed props", () => {
  assert.match(meetDetailsSrc, /from ["']@\/components\/ui\/modal-shell["']/)
  assert.match(meetDetailsSrc, /MEET_SHELL_GEOMETRY,?\s*\n?\s*meetTabButtonId,/)
  assert.match(meetDetailsSrc, /<ModalShell titleId="meet-details-title" \{\.\.\.MEET_SHELL_GEOMETRY\}>/)
  assert.match(meetDetailsSrc, /<ModalHeader>/)
  assert.match(meetDetailsSrc, /<ModalBody>/)
  assert.match(meetDetailsSrc, /<ModalFooter>/)
  assert.match(meetDetailsSrc, /<h2 id="meet-details-title"/)
})

test("MeetDetailsModal has no leftover role=\"dialog\"/aria-modal of its own - that contract now lives once in ModalShell", () => {
  assert.doesNotMatch(meetDetailsSrc, /role="dialog"/)
  assert.doesNotMatch(meetDetailsSrc, /aria-modal="true"/)
})

// ---------------------------------------------------------------------------
// .meet-shell fully removed - no dark-only override remains anywhere
// ---------------------------------------------------------------------------

test(".meet-shell class is no longer used and its CSS block no longer exists", () => {
  assert.doesNotMatch(meetDetailsSrc, /className="meet-shell\b/)
  assert.doesNotMatch(globalsSrc, /\.meet-shell\b/)
})

test("no --meet-* MEET-shell-only visual variable remains in globals.css or any component", () => {
  for (const src of [globalsSrc, meetDetailsSrc, meetSourcesReviewSrc]) {
    assert.doesNotMatch(src, /--meet-content\b/)
    assert.doesNotMatch(src, /--meet-border-strong\b/)
    assert.doesNotMatch(src, /--meet-active\b/)
    assert.doesNotMatch(src, /--meet-selected\b/)
    assert.doesNotMatch(src, /--meet-selected-surface\b/)
  }
})

test("Light/Dark token inheritance is not overridden by a local dark palette - MEET has no scoped :root-shadowing block left in globals.css", () => {
  // The old block redefined --background/--card/--border/--ring/etc inside a
  // MEET-only selector, forcing a fixed dark look regardless of data-theme.
  // Confirm none of those redefinitions remain anywhere in globals.css outside
  // the canonical :root/[data-theme] blocks (i.e. no second, MEET-scoped
  // redefinition of --background exists).
  const backgroundRedefinitions = (globalsSrc.match(/--background:\s*oklch/g) ?? []).length
  assert.equal(backgroundRedefinitions, 0, "canonical globals.css defines --background via var(--bg-app), never a second literal oklch redefinition")
})

// ---------------------------------------------------------------------------
// MEET violet stays identity-only in the migrated shell/header/footer
// ---------------------------------------------------------------------------

test("REC badge uses the recording role (--recording), not --destructive (D10: recording is an operational state, not a destructive action)", () => {
  assert.match(meetDetailsSrc, /border-recording-line bg-recording-soft px-2 py-0\.5 text-\[11px\] font-semibold text-recording/)
  assert.match(meetDetailsSrc, /animate-pulse rounded-full bg-recording motion-reduce:animate-none/)
  assert.doesNotMatch(meetDetailsSrc, /bg-destructive\/12|animate-pulse rounded-full bg-destructive\b/)
})

test("date-preset and duration-chip selection use the canonical --selection token, not MEET violet (--status-meet/--sem-meet)", () => {
  assert.match(meetDetailsSrc, /chipSelectedClass/)
  const chipConst = meetDetailsSrc.slice(meetDetailsSrc.indexOf("const chipSelectedClass"), meetDetailsSrc.indexOf("const chipUnselectedClass"))
  assert.match(chipConst, /var\(--selection\)/)
  assert.doesNotMatch(chipConst, /status-meet|sem-meet/)
})

test("Details-tab text fields (title, location, link, notes) use the canonical Input/Textarea primitives, not a local MEET-violet focus ring", () => {
  assert.match(meetDetailsSrc, /from ["']@\/components\/ui\/input["']/)
  assert.match(meetDetailsSrc, /from ["']@\/components\/ui\/textarea["']/)
  assert.match(meetDetailsSrc, /from ["']@\/components\/ui\/select["']/)
  assert.doesNotMatch(meetDetailsSrc, /focus-visible:border-status-meet/)
  assert.doesNotMatch(meetDetailsSrc, /focus-visible:ring-status-meet/)
})

// ---------------------------------------------------------------------------
// Sources/Review: MEET violet removed from generic interaction states,
// retained only for MEET identity/domain markers
// ---------------------------------------------------------------------------

test("Sources/Review: no button, field, hover, focus-ring, selected/active-segment, or native accent-color state uses MEET violet as its interactive color", () => {
  for (const src of [meetSourcesReviewSrc, meetingAssistantSrc]) {
    // Hover and keyboard-focus states never key off status-meet - these only
    // ever appeared on interactive controls (buttons/fields), never on the
    // static badges/icons that legitimately keep status-meet as identity.
    assert.doesNotMatch(src, /hover:bg-status-meet/)
    assert.doesNotMatch(src, /hover:text-status-meet/)
    assert.doesNotMatch(src, /focus-visible:(border|ring)-status-meet/)
    // Native form-control accent-color (checkbox tick, range thumb) uses the
    // canonical brand accent, not MEET violet.
    assert.doesNotMatch(src, /accent-\[var\(--status-meet\)\]/)
  }
  // The transcript-playback active-segment highlight (a generic "currently
  // playing" state, not MEET identity) is accent/selection-driven.
  assert.match(meetSourcesReviewSrc, /activeSegmentIndex === row\.segment\.index && "bg-primary\/10 ring-1 ring-inset ring-primary\/35"/)
  // The "Edit transcript" / "Save revision" / "Analyze transcript" primary
  // action buttons and the recording-policy toggle no longer soft-fill with
  // status-meet.
  assert.doesNotMatch(meetSourcesReviewSrc, /border-status-meet\/40 bg-status-meet\/10/)
  assert.doesNotMatch(meetingAssistantSrc, /border-status-meet\/50 bg-status-meet\/10/)
})

test("Sources/Review: replaced generic states now read the canonical --accent alias (text-primary/bg-primary/border-primary) or --accent directly, never a raw literal", () => {
  for (const src of [meetSourcesReviewSrc, meetingAssistantSrc]) {
    assert.match(src, /text-primary|bg-primary|border-primary|accent-\[var\(--accent\)\]/)
    assert.equal(hardcodedColorPattern.test(src), false)
  }
})

test("Sources/Review: legitimate MEET identity markers are retained - section icons, the REC/Meet badges' sibling type badges, and restrained secondary cues stay on --sem-meet/--status-meet", () => {
  // Section-heading icons (non-interactive, purely decorative domain markers).
  assert.match(meetSourcesReviewSrc, /<FileText className="size-4 text-status-meet" \/>/)
  assert.match(meetSourcesReviewSrc, /<ImageIcon className="size-4 text-status-meet" \/>/)
  assert.match(meetSourcesReviewSrc, /<Bot className="size-4 text-status-meet" \/>/)
  // "Generated" transcript-origin type badge (sibling to the Imported/UserEdited badges).
  assert.match(meetSourcesReviewSrc, /"bg-status-meet\/10 text-status-meet ring-status-meet\/30"/)
  // The restrained secondary "Active" check - the card's own selected state
  // (border/surface) already reads canonical tokens; only this small icon
  // keeps the domain accent.
  assert.match(meetSourcesReviewSrc, /<Check className="size-3 text-status-meet" \/> Active/)
  // meeting-assistant-section.tsx keeps its own Bot identity icon and the
  // decorative source-excerpt blockquote accent.
  assert.match(meetingAssistantSrc, /<Bot className="size-4 text-status-meet" \/>/)
  assert.match(meetingAssistantSrc, /border-l-2 border-status-meet\/40/)
})

test("Recording and destructive actions are unaffected by the violet cleanup - they keep their own dedicated roles, never merged into --accent", () => {
  assert.match(meetingAssistantSrc, /recording\.state === "Recording" \? "animate-pulse bg-red-500"/)
  assert.match(meetingAssistantSrc, /recording\.state === "Failed" \? "bg-destructive"/)
  assert.match(meetSourcesReviewSrc, /border-destructive\/30 bg-destructive\/10/)
})
