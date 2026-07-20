/**
 * Pure resolution helper for `SegmentedControl`'s single-value contract.
 *
 * `@base-ui/react/toggle-group` with `multiple: false` is a toggle group, not
 * a radio group: clicking the already-pressed item reports an empty
 * selection (`[]`), which would otherwise let a segmented control end up
 * with nothing selected. Kept dependency-free (no JSX/React import) so it can
 * be unit-tested directly with `node --test`, matching the rest of this
 * repo's pure-logic-extraction convention (see `lib/meet-shell.ts`).
 */
export function resolveSegmentedControlValue<Value extends string>(
  next: readonly Value[],
): Value | null {
  if (next.length === 0) return null
  return next[next.length - 1]
}
