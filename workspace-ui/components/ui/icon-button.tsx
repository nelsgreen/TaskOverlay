import * as React from 'react'
import { Button as ButtonPrimitive } from '@base-ui/react/button'
import { cva, type VariantProps } from 'class-variance-authority'
import { LoaderCircle } from 'lucide-react'

import { cn } from '@/lib/utils'
import { buttonDisabledClasses, buttonToneClasses, type PublicButtonTone } from './button-tone'

/**
 * Icon-only button primitive, per the design spec §07 Button family. Shares
 * `buttonToneClasses`/`buttonDisabledClasses` with Button so both primitives
 * read the same canonical tokens per role, but keeps its own fixed square
 * geometry and `--radius-sm` corner (spec `.iconbtn`), which never matches
 * Button's rectangular `--radius-md` shape.
 *
 * `label` is required, not optional: an icon-only control has no visible
 * text, so it renders as `aria-label` (or is forwarded as `aria-labelledby`
 * text via `children` screen-reader-only, callers should prefer the plain
 * `label` string). There is no way to construct an IconButton without one.
 */
const iconButtonVariants = cva(
  'group/icon-button inline-flex shrink-0 items-center justify-center rounded-sm ' +
    'border outline-none select-none transition-colors ' +
    'active:not-disabled:translate-y-px motion-reduce:active:translate-y-0 ' +
    'focus-visible:shadow-[var(--focus-ring)] ' +
    buttonDisabledClasses,
  {
    variants: {
      tone: buttonToneClasses,
      size: {
        default: 'size-7',
        sm: 'size-6',
        lg: 'size-8',
      },
    },
    defaultVariants: {
      tone: 'quiet',
      size: 'default',
    },
  },
)

interface IconButtonProps
  extends Omit<
      ButtonPrimitive.Props,
      'className' | 'children' | 'aria-label' | 'aria-pressed' | 'aria-busy' | 'disabled'
    >,
    Omit<VariantProps<typeof iconButtonVariants>, 'tone'> {
  className?: string
  /**
   * `selected` is deliberately not part of this public type (see
   * `PublicButtonTone`) - it only ever applies through `pressed` below.
   */
  tone?: PublicButtonTone
  /**
   * Accessible name. Required - an icon-only control has no visible text.
   * This is the control's only source of `aria-label`; the prop is omitted
   * from the accepted native props above so a caller cannot pass a second,
   * conflicting `aria-label` directly.
   */
  label: string
  /** The icon element, e.g. `<X className="size-4" />`. */
  children: React.ReactNode
  /** See `Button`'s `pressed` - this is the only source of `aria-pressed` and the `selected` tone. */
  pressed?: boolean
  /** See `Button`'s `loading` - this is the only source of `aria-busy`. */
  loading?: boolean
  /** Native disabled state. Combined with `loading` (see below) - this is the only source of the rendered `disabled` attribute. */
  disabled?: boolean
}

function IconButton({
  className,
  tone,
  size,
  label,
  pressed,
  loading = false,
  disabled,
  children,
  ...props
}: IconButtonProps) {
  const resolvedTone = pressed ? 'selected' : tone
  return (
    <ButtonPrimitive
      {...props}
      data-slot="icon-button"
      aria-label={label}
      aria-pressed={pressed}
      aria-busy={loading || undefined}
      disabled={disabled || loading}
      className={cn(iconButtonVariants({ tone: resolvedTone, size, className }))}
    >
      {loading ? <LoaderCircle className="size-3.5 animate-spin motion-reduce:animate-none" aria-hidden /> : children}
    </ButtonPrimitive>
  )
}

export { IconButton }
export type { IconButtonProps }
