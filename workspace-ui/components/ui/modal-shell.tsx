import * as React from 'react'

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
 * Semantic appearance (scrim, surface, border, radius, shadow, text color) is
 * fixed and cannot be overridden by a caller: `ModalShell`/`ModalHeader`/
 * `ModalBody`/`ModalFooter` accept no `className` or `style` prop of any
 * kind, and this file exports no raw class-string/variant helper a caller
 * could import to restyle them from outside. A caller that needs its own
 * internal layout renders its own wrapper element as `children` inside
 * `ModalHeader`/`ModalBody`/`ModalFooter` instead of restyling the canonical
 * region itself.
 *
 * The only thing a caller controls is bounded geometry, through the typed
 * numeric `maxWidthPx`/`maxHeightPx`/`viewportWidthPercent`/
 * `viewportHeightPercent` props below - plain numbers, not a class string or
 * style object, so there is no way to smuggle a color/border/shadow override
 * through them. `ModalShell` computes `width: min(${maxWidthPx}px,
 * ${viewportWidthPercent}vw)` and `height: min(${maxHeightPx}px,
 * ${viewportHeightPercent}dvh)` itself. The accepted MEET geometry - `min(
 * 1280px, 90vw)` by `min(820px, 88dvh)` - is expressed as `maxWidthPx={1280}
 * maxHeightPx={820} viewportWidthPercent={90} viewportHeightPercent={88}`.
 */
interface ModalShellProps {
  /**
   * Id of the title element the caller renders inside `ModalHeader` (e.g.
   * `<h2 id={titleId}>`). Required so the dialog/title relationship is
   * always a real, stable `aria-labelledby` pointer, never an unlabelled
   * dialog or a guessed id.
   */
  titleId: string
  /** Bounded width cap in pixels, e.g. `1280`. */
  maxWidthPx: number
  /** Bounded height cap in pixels, e.g. `820`. */
  maxHeightPx: number
  /** Viewport-width fallback percentage, e.g. `90` for `90vw`. */
  viewportWidthPercent: number
  /** Viewport-height fallback percentage, e.g. `88` for `88dvh`. */
  viewportHeightPercent: number
  children: React.ReactNode
}

function ModalShell({
  titleId,
  maxWidthPx,
  maxHeightPx,
  viewportWidthPercent,
  viewportHeightPercent,
  children,
}: ModalShellProps) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-[var(--scrim)] p-2 backdrop-blur-sm">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="flex flex-col overflow-hidden rounded-xl border border-border bg-surface-raised text-text shadow-[var(--shadow-3)]"
        style={{
          width: `min(${maxWidthPx}px, ${viewportWidthPercent}vw)`,
          height: `min(${maxHeightPx}px, ${viewportHeightPercent}dvh)`,
        }}
      >
        {children}
      </div>
    </div>
  )
}

interface ModalHeaderProps {
  children: React.ReactNode
}

/** Fixed header region: never scrolls, always reachable. */
function ModalHeader({ children }: ModalHeaderProps) {
  return <header className="flex shrink-0 items-center gap-3 border-b border-border px-4 py-3">{children}</header>
}

interface ModalBodyProps {
  children: React.ReactNode
}

/**
 * Fills the remaining shell height between header and footer. Deliberately
 * `overflow-hidden`, not `overflow-y-auto`: the shell's own geometry stays
 * fixed regardless of tab/content, and inner content regions (tab panels,
 * split columns, etc.) own their own scrolling - this region never scrolls
 * as a whole.
 */
function ModalBody({ children }: ModalBodyProps) {
  return <div className="flex min-h-0 flex-1 flex-col overflow-hidden">{children}</div>
}

interface ModalFooterProps {
  children: React.ReactNode
}

/**
 * Fixed footer region: never scrolls, always reachable. Generous vertical
 * padding (py-4) so the footer reads as a real action bar proportionate to
 * a large modal, not a thin strip - matching ModalHeader's own visual
 * weight (py-3) rather than a cramped compact-density row.
 *
 * `bg-surface-sunken` is a light-theme-only choice: in Dark, `--surface-
 * sunken` (0.155) is darker than the app's own base background (0.185),
 * which reads as a separate near-black slab rather than part of the shell.
 * `dark:bg-card` keeps the footer integrated with ModalShell's own
 * `bg-surface-raised` body in Dark while leaving Light untouched.
 */
function ModalFooter({ children }: ModalFooterProps) {
  return (
    <footer className="flex shrink-0 items-center justify-between gap-3 border-t border-border bg-surface-sunken px-5 py-4 dark:bg-card">
      {children}
    </footer>
  )
}

export { ModalShell, ModalHeader, ModalBody, ModalFooter }
export type { ModalShellProps, ModalHeaderProps, ModalBodyProps, ModalFooterProps }
