/**
 * Shared Editable / Read-only / Disabled visual contract for text-like form
 * fields (Input, Textarea, Select), per the canonical design spec §05
 * (https://claude.ai/code/artifact/8042b7b0-1759-40a3-afdf-1b12285466e3).
 *
 * State is driven entirely by the native `readOnly` / `disabled` attributes
 * via the `:read-only` / `:disabled` pseudo-classes, not a separate variant
 * prop - every consumer's existing readOnly/disabled usage gets the correct
 * look for free, and native focus/selection/copy behavior is untouched.
 *
 * `read-only:hover:*` and `read-only:focus-visible:*` are two-pseudo-class
 * compounds so they always out-specificity the plain single-pseudo-class
 * `hover:*` / `focus-visible:*` rules above them, regardless of the
 * generated stylesheet's rule order. Disabled fields never need an
 * equivalent override: native `disabled` elements never match `:hover`.
 *
 * `border-primary` (not `border-accent`) is deliberate: PR-1 renamed the
 * legacy shadcn `--accent`/`bg-accent` to mean a neutral hover-surface tint,
 * so `--primary` is the alias that resolves to the canonical brand/
 * interaction `--accent` token (see globals.css / DECISIONS.md "Design
 * System").
 */
export const fieldStateClasses = [
  'border-border-strong bg-field text-text placeholder:text-text-faint',
  'hover:border-[color-mix(in_oklch,var(--border-strong)_55%,var(--text-muted))]',
  'focus-visible:border-primary focus-visible:shadow-[var(--focus-ring)]',
  'read-only:cursor-default read-only:bg-field-readonly read-only:border-border',
  'read-only:hover:border-border read-only:focus-visible:border-border-strong',
  'disabled:pointer-events-none disabled:cursor-not-allowed disabled:bg-field-disabled disabled:border-border-disabled disabled:text-text-disabled',
].join(' ')
