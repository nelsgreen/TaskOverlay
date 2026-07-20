import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import test from "node:test"
import {
  resolveAppearanceDevOverrides,
  resolveEffectiveTheme,
} from "./appearance.ts"

const appearanceSrc = readFileSync(new URL("./appearance.ts", import.meta.url), "utf8")
const layoutSrc = readFileSync(new URL("../app/layout.tsx", import.meta.url), "utf8")
const taskManagerSrc = readFileSync(new URL("../components/task-manager.tsx", import.meta.url), "utf8")
const workspaceBridgeSrc = readFileSync(new URL("./workspace-bridge.ts", import.meta.url), "utf8")
const typesSrc = readFileSync(new URL("./types.ts", import.meta.url), "utf8")

// ---------------------------------------------------------------------------
// Pure functions: resolveAppearanceDevOverrides / resolveEffectiveTheme
// ---------------------------------------------------------------------------

test("resolveAppearanceDevOverrides: reads ?ds-theme=dark|light and ?ds-accent=neutral|warm only", () => {
  assert.deepEqual(resolveAppearanceDevOverrides(""), { theme: null, accent: null })
  assert.deepEqual(resolveAppearanceDevOverrides("?ds-theme=dark"), { theme: "dark", accent: null })
  assert.deepEqual(resolveAppearanceDevOverrides("?ds-theme=light"), { theme: "light", accent: null })
  assert.deepEqual(resolveAppearanceDevOverrides("?ds-accent=warm"), { theme: null, accent: "warm" })
  assert.deepEqual(resolveAppearanceDevOverrides("?ds-accent=neutral"), { theme: null, accent: "neutral" })
  assert.deepEqual(
    resolveAppearanceDevOverrides("?ds-theme=dark&ds-accent=warm"),
    { theme: "dark", accent: "warm" },
  )
})

test("resolveAppearanceDevOverrides: an unrecognized value is ignored (no override), not a crash or a passthrough", () => {
  assert.deepEqual(resolveAppearanceDevOverrides("?ds-theme=system"), { theme: null, accent: null })
  assert.deepEqual(resolveAppearanceDevOverrides("?ds-accent=bogus"), { theme: null, accent: null })
})

test("resolveEffectiveTheme: System resolves from the OS preference; Dark/Light are fixed and ignore it", () => {
  assert.equal(resolveEffectiveTheme("system", true), "dark")
  assert.equal(resolveEffectiveTheme("system", false), "light")
  assert.equal(resolveEffectiveTheme("dark", false), "dark")
  assert.equal(resolveEffectiveTheme("light", true), "light")
})

// ---------------------------------------------------------------------------
// lib/appearance.ts: the single centralized appearance-application layer
// ---------------------------------------------------------------------------

test("appearance.ts never touches localStorage, sessionStorage, or cookies", () => {
  for (const forbidden of ["localStorage", "sessionStorage", "document.cookie"]) {
    assert.equal(appearanceSrc.includes(forbidden), false, `must not reference ${forbidden}`)
  }
})

test("appearance.ts never sends a bridge command or posts a message - dev overrides and preference application are local DOM attribute writes only", () => {
  for (const forbidden of ["postMessage", "webview", "sendCommand", "chrome?.webview"]) {
    assert.equal(appearanceSrc.includes(forbidden), false, `must not reference ${forbidden}`)
  }
})

test("appearance.ts sets both data-theme and data-accent together in one place (applyAppearanceAttributes), not scattered .setAttribute mutations", () => {
  const setAttributeCalls = appearanceSrc.match(/\.setAttribute\(/g) ?? []
  assert.equal(setAttributeCalls.length, 2, "exactly the two setAttribute calls inside the single apply function, nothing scattered elsewhere")
  assert.match(appearanceSrc, /function applyAppearanceAttributes\(/)
  assert.match(appearanceSrc, /root\.setAttribute\(["']data-theme["'],\s*theme\)/)
  assert.match(appearanceSrc, /root\.setAttribute\(["']data-accent["'],\s*accent\)/)
})

test("useWorkspaceAppearance only live-follows prefers-color-scheme when the effective preference is system, and cleans up its listener", () => {
  assert.match(appearanceSrc, /effectiveThemePreference !== ["']system["']/)
  assert.match(appearanceSrc, /window\.matchMedia\(["']\(prefers-color-scheme: dark\)["']\)/)
  assert.match(appearanceSrc, /mediaQuery\.addEventListener\?\.\(["']change["'], listener\)/)
  assert.match(appearanceSrc, /return \(\) => mediaQuery\.removeEventListener\?\.\(["']change["'], listener\)/)
})

test("useWorkspaceAppearance re-checks dev overrides independently for theme and accent (?? per field, not all-or-nothing)", () => {
  assert.match(appearanceSrc, /overrides\.accent \?\? accentPreference/)
  assert.match(appearanceSrc, /overrides\.theme \?\? themePreference/)
})

// ---------------------------------------------------------------------------
// app/layout.tsx: pre-hydration FOUC-prevention script, unchanged contract
// ---------------------------------------------------------------------------

test("layout.tsx still resolves a best-guess theme/accent before hydration and never calls localStorage/sessionStorage", () => {
  assert.match(layoutSrc, /ds-theme/)
  assert.match(layoutSrc, /ds-accent/)
  assert.match(layoutSrc, /prefers-color-scheme: dark/)
  // Checks actual usage (property/method access), not mere prose mentions -
  // the doc comment above THEME_INIT_SCRIPT explicitly discusses localStorage
  // in prose to explain why it is NOT used.
  assert.equal(/localStorage\./.test(layoutSrc), false)
  assert.equal(/sessionStorage\./.test(layoutSrc), false)
})

// ---------------------------------------------------------------------------
// components/task-manager.tsx: wiring from the bridge snapshot to the layer
// ---------------------------------------------------------------------------

test("task-manager.tsx derives appearance from the bridge snapshot with System/Neutral fallbacks and calls useWorkspaceAppearance once", () => {
  assert.match(taskManagerSrc, /from ["']@\/lib\/appearance["']/)
  assert.match(taskManagerSrc, /bridge\.data\?\.appearanceTheme \?\? ["']system["']/)
  assert.match(taskManagerSrc, /bridge\.data\?\.appearanceAccent \?\? ["']neutral["']/)
  assert.match(taskManagerSrc, /useWorkspaceAppearance\(appearanceTheme, appearanceAccent\)/)
})

// ---------------------------------------------------------------------------
// Snapshot contract: schema bump and new fields wired through the bridge
// ---------------------------------------------------------------------------

test("WorkspaceSnapshotContract carries appearanceTheme/appearanceAccent at schema 7", () => {
  assert.match(typesSrc, /schemaVersion:\s*7/)
  assert.match(typesSrc, /appearanceTheme:\s*["']system["']\s*\|\s*["']dark["']\s*\|\s*["']light["']/)
  assert.match(typesSrc, /appearanceAccent:\s*["']neutral["']\s*\|\s*["']warm["']/)
})

test("isWorkspaceSnapshot validates schema 7 and the new appearance fields", () => {
  assert.match(workspaceBridgeSrc, /candidate\.schemaVersion === 7/)
  assert.match(workspaceBridgeSrc, /\["system", "dark", "light"\]\.includes\(candidate\.appearanceTheme/)
  assert.match(workspaceBridgeSrc, /\["neutral", "warm"\]\.includes\(candidate\.appearanceAccent/)
})

test("adaptWorkspaceSnapshot passes appearanceTheme/appearanceAccent through to WorkspaceData unchanged (no default-guessing at this layer)", () => {
  assert.match(workspaceBridgeSrc, /appearanceTheme:\s*snapshot\.appearanceTheme,/)
  assert.match(workspaceBridgeSrc, /appearanceAccent:\s*snapshot\.appearanceAccent,/)
})
