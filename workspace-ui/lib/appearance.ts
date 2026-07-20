"use client"

import { useEffect } from "react"

/**
 * Persisted Workspace theme preference (native Settings -> AppState ->
 * snapshot). "system" resolves from `prefers-color-scheme` and keeps
 * following it live; "dark"/"light" are explicit and ignore later OS changes.
 */
export type AppearanceThemePreference = "system" | "dark" | "light"

/** Persisted Workspace accent preference. Affects only accent/selection/focus-ring. */
export type AppearanceAccentPreference = "neutral" | "warm"

export interface AppearanceDevOverrides {
  theme: "dark" | "light" | null
  accent: AppearanceAccentPreference | null
}

/**
 * Reads the dev/QA-only `?ds-theme=light|dark` and `?ds-accent=neutral|warm`
 * query params. Pure function, no DOM writes - these overrides never persist,
 * never mutate AppState, and never emit a bridge command; they only affect
 * the current rendered session.
 */
export function resolveAppearanceDevOverrides(search: string): AppearanceDevOverrides {
  const params = new URLSearchParams(search)
  const theme = params.get("ds-theme")
  const accent = params.get("ds-accent")
  return {
    theme: theme === "dark" || theme === "light" ? theme : null,
    accent: accent === "neutral" || accent === "warm" ? accent : null,
  }
}

/** Resolves a theme preference plus the current OS preference into a concrete value. */
export function resolveEffectiveTheme(
  preference: AppearanceThemePreference,
  prefersDark: boolean,
): "dark" | "light" {
  if (preference === "dark" || preference === "light") {
    return preference
  }
  return prefersDark ? "dark" : "light"
}

function applyAppearanceAttributes(theme: "dark" | "light", accent: AppearanceAccentPreference): void {
  const root = document.documentElement
  root.setAttribute("data-theme", theme)
  root.setAttribute("data-accent", accent)
}

/**
 * Single centralized appearance-application layer for the Workspace root.
 * Call once with the resolved snapshot theme/accent preferences - do not
 * scatter additional `document.documentElement` mutations through other
 * components. Switching preference only updates these two attributes; it
 * never reloads the page or resets Workspace view state.
 */
export function useWorkspaceAppearance(
  themePreference: AppearanceThemePreference,
  accentPreference: AppearanceAccentPreference,
): void {
  useEffect(() => {
    if (typeof window === "undefined") {
      return
    }

    const overrides = resolveAppearanceDevOverrides(window.location.search)
    const effectiveAccent = overrides.accent ?? accentPreference
    const effectiveThemePreference: AppearanceThemePreference = overrides.theme ?? themePreference

    if (effectiveThemePreference !== "system") {
      applyAppearanceAttributes(effectiveThemePreference, effectiveAccent)
      return
    }

    const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)")
    const apply = (prefersDark: boolean) => {
      applyAppearanceAttributes(resolveEffectiveTheme("system", prefersDark), effectiveAccent)
    }
    apply(mediaQuery.matches)

    const listener = (event: MediaQueryListEvent) => apply(event.matches)
    mediaQuery.addEventListener?.("change", listener)
    return () => mediaQuery.removeEventListener?.("change", listener)
  }, [themePreference, accentPreference])
}
