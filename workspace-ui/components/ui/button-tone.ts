/**
 * Shared color/interaction contract for Button and IconButton, per the
 * canonical design spec §07 Button family
 * (https://claude.ai/code/artifact/8042b7b0-1759-40a3-afdf-1b12285466e3).
 *
 * Six semantic roles - primary / secondary / quiet / selected / destructive /
 * recording - drive background/text/border from canonical tokens only.
 * Disabled is not a seventh role: it is driven by the native `disabled`
 * attribute (see `disabledBase` below), matching the Input/Textarea/Select
 * contract in `./field.ts`.
 *
 * `destructive` and `recording` share the same "soft fill + line border"
 * shape but each reads its own `--destructive` / `--recording` custom
 * property - recording is never derived from destructive (D10: recording is
 * an operational state, not a destructive action). Only `destructive` gets
 * the error focus ring; `recording`'s focus-visible stays on the shared
 * `--focus-ring`, the same as every non-destructive role.
 *
 * Like `disabledBase` in `./field.ts`, disabled buttons intentionally avoid
 * `pointer-events-none`: an element excluded from hit-testing is never
 * consulted for cursor rendering, which would silently drop the
 * `cursor-not-allowed` affordance. Native `disabled` already blocks clicks and
 * keyboard activation on its own, so each hover rule is instead guarded with
 * `hover:not-disabled:*` to stop the hover tint from bleeding through in
 * browsers that still match `:hover` on a disabled control.
 */
export const buttonToneClasses = {
  primary:
    'border-transparent bg-primary text-primary-foreground ' +
    'hover:not-disabled:bg-[color-mix(in_oklch,var(--accent)_86%,var(--text))]',
  secondary:
    'border-border-strong bg-field text-text hover:not-disabled:bg-surface-sunken',
  quiet: 'border-transparent bg-transparent text-text-muted hover:not-disabled:bg-surface-sunken hover:not-disabled:text-text',
  // Toggle/selected state (aria-pressed="true"): the ring + semibold weight
  // are the state signal, not just a color swap, so pressed controls remain
  // legible without relying on color alone.
  selected:
    'border-transparent bg-surface-raised text-text font-semibold ' +
    'shadow-[0_1px_2px_rgb(0_0_0/0.18),inset_0_0_0_1px_color-mix(in_oklch,var(--selection)_40%,transparent)]',
  destructive:
    'border-[color-mix(in_oklch,var(--destructive)_32%,transparent)] bg-destructive-soft text-destructive ' +
    'hover:not-disabled:bg-[color-mix(in_oklch,var(--destructive)_17%,transparent)] ' +
    'focus-visible:shadow-[var(--focus-ring-error)]',
  recording:
    'border-[color-mix(in_oklch,var(--recording)_32%,transparent)] bg-recording-soft text-recording ' +
    'hover:not-disabled:bg-[color-mix(in_oklch,var(--recording)_17%,transparent)]',
} as const

export type ButtonTone = keyof typeof buttonToneClasses

/** Shared with Input/Textarea/Select's disabledBase in intent, not literally
 * reused, because Button also needs the visible `opacity-50` the field
 * contract deliberately avoids (fields use dedicated disabled tokens
 * instead of opacity; the canonical spec's Focus & states model (§04)
 * explicitly allows buttons and other controls to keep the "opacity + not-
 * allowed" treatment). */
export const buttonDisabledClasses = 'disabled:cursor-not-allowed disabled:opacity-50'
