/**
 * Shared Editable / Read-only / Disabled visual contract for text-like form
 * fields (Input, Textarea) and the Editable / Disabled contract for Select,
 * per the canonical design spec §05
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
 *
 * `<select>` does NOT support the HTML `readonly` attribute at all - but per
 * the CSS Selectors spec, `:read-only` still matches elements that don't
 * support the concept of user-alterability in the first place (unlike
 * `:read-write`, which only matches elements that genuinely are editable).
 * That means an enabled, interactive `<select>` matches `:read-only` by
 * default and would wrongly pick up the read-only background/border/hover/
 * focus treatment if it shared `fieldStateClasses`. `selectStateClasses`
 * is a deliberately separate composition with no `read-only:*` rules at
 * all, built from the same editable/disabled base.
 */
const editableBase =
  'border-border-strong bg-field text-text placeholder:text-text-faint ' +
  'hover:border-[color-mix(in_oklch,var(--border-strong)_55%,var(--text-muted))] ' +
  'focus-visible:border-primary focus-visible:shadow-[var(--focus-ring)]'

// Native `disabled` already blocks focus, editing, and all pointer
// interaction on its own - `pointer-events-none` is not needed and would
// actively hide the `cursor-not-allowed` treatment (an element excluded
// from hit-testing is not consulted for cursor rendering, so the cursor
// would fall through to whatever sits behind it instead).
const disabledBase =
  'disabled:cursor-not-allowed disabled:bg-field-disabled disabled:border-border-disabled disabled:text-text-disabled'

const readOnlyOnly =
  'read-only:cursor-default read-only:bg-field-readonly read-only:border-border ' +
  'read-only:hover:border-border read-only:focus-visible:border-border-strong'

/** Input / Textarea: Editable / Read-only / Disabled. */
export const fieldStateClasses = [editableBase, readOnlyOnly, disabledBase].join(' ')

/** Select: Editable / Disabled only - no `read-only:*` rule of any kind. */
export const selectStateClasses = [editableBase, disabledBase].join(' ')
