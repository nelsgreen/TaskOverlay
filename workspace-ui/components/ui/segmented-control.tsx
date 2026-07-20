import * as React from 'react'
import { ToggleGroup } from '@base-ui/react/toggle-group'
import { Toggle } from '@base-ui/react/toggle'

import { cn } from '@/lib/utils'
import { resolveSegmentedControlValue } from './segmented-control-value'

/**
 * Canonical SegmentedControl primitive for mutually exclusive view/filter
 * choices, per the design spec §07 (`.segment` + `button[aria-pressed]`)
 * (https://claude.ai/code/artifact/8042b7b0-1759-40a3-afdf-1b12285466e3).
 * See DECISIONS.md "Design System" for the PR-4 scope this implements.
 *
 * Built on `@base-ui/react/toggle-group` + `@base-ui/react/toggle`
 * (`multiple: false`), which already own `role="group"`, roving `tabIndex`,
 * and ArrowLeft/ArrowRight/Home/End navigation between enabled items via the
 * shared composite-list internals also used by Tabs. This file only adds the
 * "always exactly one selected value" contract on top: a plain
 * `ToggleGroup(multiple: false)` is a toggle group, not a radio group, so
 * clicking the already-pressed item would otherwise deselect it down to zero
 * pressed items. `SegmentedControl` cancels that empty-selection change and
 * exposes a single `value`/`onValueChange` pair instead of ToggleGroup's
 * array-shaped API, so a caller can never observe or produce a "nothing
 * selected" state.
 *
 * `Segment`'s selected visual is driven by real `aria-pressed` (via the
 * `aria-pressed:` Tailwind variant) - the same attribute `useSegmentedItem`
 * exposes to assistive tech - so the look can never drift out of sync with
 * the accessibility state, and there is no separate raw class-name helper a
 * caller could use to fake it.
 */

interface SegmentedControlProps<Value extends string> {
  /** The single currently selected value. A SegmentedControl always has exactly one. */
  value: Value
  onValueChange: (value: Value) => void
  /** Required accessible name for the `role="group"` control. */
  'aria-label': string
  disabled?: boolean
  className?: string
  children: React.ReactNode
}

function SegmentedControl<Value extends string>({
  value,
  onValueChange,
  disabled,
  className,
  children,
  ...props
}: SegmentedControlProps<Value>) {
  return (
    <ToggleGroup
      data-slot="segmented-control"
      value={[value]}
      onValueChange={(next, eventDetails) => {
        const resolved = resolveSegmentedControlValue(next)
        if (resolved === null) {
          eventDetails.cancel()
          return
        }
        onValueChange(resolved)
      }}
      disabled={disabled}
      className={cn(
        'inline-flex h-7 items-center gap-0.5 rounded-md border border-border bg-surface-sunken p-0.5',
        className,
      )}
      {...props}
    >
      {children}
    </ToggleGroup>
  )
}

interface SegmentedControlItemProps<Value extends string> {
  value: Value
  disabled?: boolean
  className?: string
  children: React.ReactNode
}

function SegmentedControlItem<Value extends string>({
  className,
  ...props
}: SegmentedControlItemProps<Value>) {
  return (
    <Toggle
      data-slot="segmented-control-item"
      className={cn(
        'h-6 rounded px-2.5 text-[11.5px] font-medium text-text-muted outline-none transition-colors select-none',
        'hover:text-text',
        'focus-visible:shadow-[var(--focus-ring)]',
        'aria-pressed:bg-surface-raised aria-pressed:font-semibold aria-pressed:text-text',
        'aria-pressed:shadow-[0_1px_2px_oklch(0_0_0_/_0.18),inset_0_0_0_1px_color-mix(in_oklch,var(--selection)_25%,transparent)]',
        'disabled:cursor-not-allowed disabled:opacity-50',
        className,
      )}
      {...props}
    />
  )
}

export { SegmentedControl, SegmentedControlItem }
export type { SegmentedControlProps, SegmentedControlItemProps }
