"use client"

import type { ReactNode } from "react"
import { ChevronRight } from "lucide-react"
import { cn } from "@/lib/utils"

interface DetailsSectionProps {
  icon?: ReactNode
  title: string
  /** Trailing header metadata (e.g. a linked-count chip), rendered before any action. */
  meta?: ReactNode
  /**
   * Optional secondary header action. Only rendered in the non-collapsible
   * (no `open`/`onOpenChange`) case - a collapsible header is itself a
   * `<button>`, so nesting another interactive action inside it would be
   * invalid HTML. Collapsible sections show the chevron there instead.
   */
  action?: ReactNode
  /**
   * Collapsed state is controlled by the caller, not owned here - only pass
   * `open`/`onOpenChange` when an existing caller already needs collapse
   * (e.g. collapsed-when-empty/expanded-when-linked). Omit both for a
   * always-open section. This is not a new accordion system: no internal
   * state, no multi-section coordination.
   */
  open?: boolean
  onOpenChange?: (open: boolean) => void
  children: ReactNode
  className?: string
}

/** Repeated title + optional action + body structure used in details/inspector content. */
export function DetailsSection({
  icon,
  title,
  meta,
  action,
  open,
  onOpenChange,
  children,
  className,
}: DetailsSectionProps) {
  const collapsible = open !== undefined && onOpenChange !== undefined
  const isOpen = collapsible ? open : true

  const header = (
    <>
      {icon}
      <span className="text-[11px] font-bold uppercase tracking-widest text-foreground">{title}</span>
      <span className="flex-1" />
      {meta}
      {collapsible ? (
        <ChevronRight
          className={cn("size-3.5 shrink-0 text-muted-foreground transition-transform", isOpen && "rotate-90")}
          aria-hidden
        />
      ) : (
        action
      )}
    </>
  )

  return (
    <div data-slot="details-section" className={className}>
      {collapsible ? (
        <button
          type="button"
          onClick={() => onOpenChange(!open)}
          className="flex w-full items-center gap-2.5 px-3 py-2.5 text-left"
          aria-expanded={isOpen}
        >
          {header}
        </button>
      ) : (
        <div className="flex w-full items-center gap-2.5 px-3 py-2.5 text-left">{header}</div>
      )}
      {isOpen && <div className="space-y-2.5 border-t border-border/50 px-3 pb-3 pt-2.5">{children}</div>}
    </div>
  )
}
