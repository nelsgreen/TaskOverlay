import * as React from 'react'
import { ChevronDown } from 'lucide-react'

import { cn } from '@/lib/utils'
import { selectStateClasses } from './field'

/**
 * A styled native `<select>` sharing the Editable/Disabled portion of the
 * field-state contract with Input and Textarea. Native `<select>` has no
 * `readOnly` concept (the HTML attribute doesn't apply to it) - and unlike
 * `:read-write`, `:read-only` still matches elements that don't support the
 * attribute at all, so `<select>` must use its own `selectStateClasses`
 * composition with no `read-only:*` rule, never `fieldStateClasses` (see
 * field.ts). See DECISIONS.md "Design System" for the deliberately
 * out-of-scope custom picker/"select trigger" components this does not
 * replace.
 */
function Select({ className, disabled, children, ...props }: React.ComponentProps<'select'>) {
  return (
    <div className="relative">
      <select
        data-slot="select"
        disabled={disabled}
        className={cn(
          'h-8 w-full min-w-0 appearance-none rounded-md border py-1.5 pr-8 pl-2.5 text-[13px] outline-none transition-colors',
          selectStateClasses,
          className,
        )}
        {...props}
      >
        {children}
      </select>
      <ChevronDown
        className={cn(
          'pointer-events-none absolute top-1/2 right-2.5 size-3.5 -translate-y-1/2 text-text-muted',
          disabled && 'text-text-disabled',
        )}
        aria-hidden
      />
    </div>
  )
}

export { Select }
