import * as React from 'react'

import { cn } from '@/lib/utils'

/**
 * Canonical modal dialog shell, per the design spec's `.mshell` contract
 * (https://claude.ai/code/artifact/8042b7b0-1759-40a3-afdf-1b12285466e3) and
 * DECISIONS.md "Process": "Modal dialogs never close on an outside/backdrop
 * click." There is deliberately no `onClose`/backdrop `onClick` prop here -
 * the scrim renders with no click handler at all, so backdrop-click-to-close
 * is structurally impossible rather than merely unwired. Closing is entirely
 * the caller's responsibility (an explicit X/Cancel/Close control, and/or its
 * own Escape handling where existing modal-specific guards - e.g. a dirty-
 * draft confirmation - need to intercept Escape first).
 *
 * `className` on `ModalShell` is required to set the bounded width/height
 * (e.g. MEET's accepted `h-[min(820px,88dvh)] w-[min(1280px,90vw)]` clamp) -
 * the base has no built-in max-width of its own (deliberately: a baked-in
 * `max-w-*` would cap `max-width` independently of a caller's `w-[...]`
 * override, since Tailwind's width and max-width utilities are different
 * groups that both apply in the cascade rather than one replacing the
 * other). `className` is appended after the canonical surface/border/
 * radius/shadow classes, the same layout-only convention as `Panel`. It
 * cannot express color, border, or elevation: this file exposes no raw
 * class-string/variant helper a caller could import to restyle the
 * semantic appearance directly.
 */
interface ModalShellProps {
  /**
   * Id of the title element the caller renders inside `ModalHeader` (e.g.
   * `<h2 id={titleId}>`). Required so the dialog/title relationship is
   * always a real, stable `aria-labelledby` pointer, never an unlabelled
   * dialog or a guessed id.
   */
  titleId: string
  children: React.ReactNode
  /** Layout-only: bounded width/height overrides. See file doc comment. */
  className?: string
}

function ModalShell({ titleId, children, className }: ModalShellProps) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-[var(--scrim)] p-2 backdrop-blur-sm">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className={cn(
          'flex w-full flex-col overflow-hidden rounded-xl border border-border bg-surface-raised text-text shadow-[var(--shadow-3)]',
          className,
        )}
      >
        {children}
      </div>
    </div>
  )
}

interface ModalHeaderProps {
  children: React.ReactNode
  className?: string
}

/** Fixed header region: never scrolls, always reachable. */
function ModalHeader({ children, className }: ModalHeaderProps) {
  return (
    <header className={cn('flex shrink-0 items-center gap-3 border-b border-border px-4 py-3', className)}>
      {children}
    </header>
  )
}

interface ModalBodyProps {
  children: React.ReactNode
  className?: string
}

/**
 * Fills the remaining shell height between header and footer. Deliberately
 * `overflow-hidden`, not `overflow-y-auto`: the shell's own geometry stays
 * fixed regardless of tab/content, and inner content regions (tab panels,
 * split columns, etc.) own their own scrolling - this region never scrolls
 * as a whole.
 */
function ModalBody({ children, className }: ModalBodyProps) {
  return <div className={cn('flex min-h-0 flex-1 flex-col overflow-hidden', className)}>{children}</div>
}

interface ModalFooterProps {
  children: React.ReactNode
  className?: string
}

/** Fixed footer region: never scrolls, always reachable. */
function ModalFooter({ children, className }: ModalFooterProps) {
  return (
    <footer
      className={cn(
        'flex shrink-0 items-center justify-between gap-3 border-t border-border bg-surface-sunken px-4 py-2.5',
        className,
      )}
    >
      {children}
    </footer>
  )
}

export { ModalShell, ModalHeader, ModalBody, ModalFooter }
export type { ModalShellProps, ModalHeaderProps, ModalBodyProps, ModalFooterProps }
