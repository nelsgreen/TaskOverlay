import * as React from 'react'
import { Button as ButtonPrimitive } from '@base-ui/react/button'
import { cva, type VariantProps } from 'class-variance-authority'
import { LoaderCircle } from 'lucide-react'

import { cn } from '@/lib/utils'
import { buttonDisabledClasses, buttonToneClasses, type PublicButtonTone } from './button-tone'

/**
 * Canonical Button primitive, per the design spec §07 Button family
 * (https://claude.ai/code/artifact/8042b7b0-1759-40a3-afdf-1b12285466e3).
 * See DECISIONS.md "Design System" for the PR-3 scope this implements.
 *
 * `pressed` drives real `aria-pressed` toggle semantics (Toolbar-style
 * selected/toggle controls) and always swaps in the `selected` tone
 * together with it, so the state is never color-only and never silently
 * out of sync with the accessibility attribute.
 */
const buttonVariants = cva(
  'group/button inline-flex shrink-0 items-center justify-center gap-1.5 rounded-md ' +
    'border font-medium whitespace-nowrap outline-none select-none transition-colors ' +
    'active:not-disabled:translate-y-px motion-reduce:active:translate-y-0 ' +
    'focus-visible:shadow-[var(--focus-ring)] ' +
    buttonDisabledClasses,
  {
    variants: {
      tone: buttonToneClasses,
      size: {
        default: 'h-8 px-3 text-[13px]',
        sm: 'h-7 px-2.5 text-[12.5px]',
        xs: 'h-6 rounded-sm px-2 text-[11.5px]',
      },
    },
    defaultVariants: {
      // Deliberately neutral, not primary: a caller must opt into the
      // primary tone explicitly rather than every unstyled call site
      // silently reading as the page's main call-to-action.
      tone: 'secondary',
      size: 'default',
    },
  },
)

interface ButtonProps
  extends Omit<ButtonPrimitive.Props, 'className' | 'aria-pressed' | 'aria-busy' | 'disabled'>,
    Omit<VariantProps<typeof buttonVariants>, 'tone'> {
  className?: string
  /**
   * `selected` is deliberately not part of this public type (see
   * `PublicButtonTone`) - it only ever applies through `pressed` below.
   */
  tone?: PublicButtonTone
  /**
   * Toggle/selected state for status-like controls. This is the only source
   * of `aria-pressed` and of the `selected` tone: setting it also swaps in
   * the `selected` visual regardless of `tone`, so the state is never
   * color-only and never silently out of sync with the accessibility
   * attribute. `aria-pressed` is omitted from the accepted props above so a
   * caller cannot pass a conflicting value directly.
   */
  pressed?: boolean
  /**
   * Replaces the leading icon with a spinner and marks the control busy
   * without changing its label, so width is preserved. Callers must not
   * also render their own leading icon while `loading` is true. `aria-busy`
   * is omitted from the accepted props above - this is its only source.
   */
  loading?: boolean
  /** Native disabled state. Combined with `loading` (see below) - this is the only source of the rendered `disabled` attribute. */
  disabled?: boolean
}

function Button({
  className,
  tone,
  size,
  pressed,
  loading = false,
  disabled,
  children,
  ...props
}: ButtonProps) {
  const resolvedTone = pressed ? 'selected' : tone
  return (
    <ButtonPrimitive
      {...props}
      data-slot="button"
      aria-pressed={pressed}
      aria-busy={loading || undefined}
      disabled={disabled || loading}
      className={cn(buttonVariants({ tone: resolvedTone, size, className }))}
    >
      {loading && (
        <LoaderCircle className="size-3.5 shrink-0 animate-spin motion-reduce:animate-none" aria-hidden />
      )}
      {children}
    </ButtonPrimitive>
  )
}

export { Button, buttonVariants }
export type { ButtonProps }
