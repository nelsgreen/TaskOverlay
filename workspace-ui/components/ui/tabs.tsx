import * as React from 'react'
import { Tabs as TabsPrimitive } from '@base-ui/react/tabs'

import { cn } from '@/lib/utils'

/**
 * Canonical Tabs primitive for real content navigation, per the design spec
 * §07 (`.tabbar`/`.tabbtn`)
 * (https://claude.ai/code/artifact/8042b7b0-1759-40a3-afdf-1b12285466e3).
 * See DECISIONS.md "Design System" for the PR-4 scope this implements.
 *
 * Built on `@base-ui/react/tabs`, which already owns the full WAI-ARIA tabs
 * contract - `role="tablist"`/`"tab"`/`"tabpanel"`, `aria-selected`,
 * `aria-controls`/`aria-labelledby`, roving `tabIndex`, ArrowLeft/ArrowRight,
 * Home/End, and skipping disabled tabs - so this file only supplies canonical
 * token styling on top. Do not reimplement any of that keyboard/ARIA logic
 * here: it belongs to the primitive, not the design layer.
 *
 * `Tabs` is deliberately controlled-or-uncontrolled passthrough (`value` /
 * `defaultValue` / `onValueChange` forward directly to `Tabs.Root`) and does
 * not expose any raw class-name helper a caller could use to fake a selected
 * look without the real `role="tab"` + `aria-selected` state behind it.
 */

interface TabsProps extends Omit<TabsPrimitive.Root.Props, 'className'> {
  className?: string
}

function Tabs({ className, ...props }: TabsProps) {
  return <TabsPrimitive.Root data-slot="tabs" className={cn('flex min-h-0 flex-col', className)} {...props} />
}

interface TabListProps extends Omit<TabsPrimitive.List.Props, 'className'> {
  className?: string
}

function TabList({ className, ...props }: TabListProps) {
  return (
    <TabsPrimitive.List
      data-slot="tab-list"
      className={cn('flex shrink-0 gap-0.5 border-b border-border', className)}
      {...props}
    />
  )
}

interface TabProps extends Omit<TabsPrimitive.Tab.Props, 'className'> {
  className?: string
}

/**
 * Selected state is carried by real `aria-selected` (via the `aria-selected:`
 * Tailwind variant), never by color alone: the underline is paired with a
 * font-weight shift, matching the spec's `.tabbtn[aria-selected="true"]`
 * contract.
 */
function Tab({ className, ...props }: TabProps) {
  return (
    <TabsPrimitive.Tab
      data-slot="tab"
      className={cn(
        'relative shrink-0 whitespace-nowrap px-3.5 py-2 text-[12.5px] font-medium text-text-muted outline-none',
        'transition-colors select-none',
        'hover:text-text',
        'focus-visible:rounded-sm focus-visible:shadow-[var(--focus-ring)]',
        'aria-selected:font-semibold aria-selected:text-text',
        'aria-selected:after:absolute aria-selected:after:inset-x-[9px] aria-selected:after:-bottom-px',
        'aria-selected:after:h-0.5 aria-selected:after:rounded-t-sm aria-selected:after:bg-selection',
        'disabled:cursor-not-allowed disabled:opacity-50',
        className,
      )}
      {...props}
    />
  )
}

interface TabPanelProps extends Omit<TabsPrimitive.Panel.Props, 'className'> {
  className?: string
}

function TabPanel({ className, ...props }: TabPanelProps) {
  return (
    <TabsPrimitive.Panel
      data-slot="tab-panel"
      className={cn('min-h-0 outline-none focus-visible:shadow-[var(--focus-ring)]', className)}
      {...props}
    />
  )
}

export { Tabs, TabList, Tab, TabPanel }
export type { TabsProps, TabListProps, TabProps, TabPanelProps }
