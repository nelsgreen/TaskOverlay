import type { Status } from "@/lib/types"
import { cn } from "@/lib/utils"

/**
 * Domain-semantic task status only (TODO/FOCUS/WAIT/DONE) - never an
 * arbitrary caller color. FOCUS stays the task-domain green regardless of
 * the global accent profile (Neutral/Warm); accent never substitutes for
 * status color. Kept module-private (not exported) so callers cannot reach
 * past the component for raw class strings - use MetadataBadge for neutral
 * non-status labels instead.
 */
const statusConfig: Record<
  Status,
  { label: string; dot: string; text: string; ring: string; soft: string }
> = {
  TODO: {
    label: "TODO",
    dot: "bg-status-todo",
    text: "text-status-todo",
    ring: "ring-status-todo/30",
    soft: "bg-status-todo/10",
  },
  FOCUS: {
    label: "FOCUS",
    dot: "bg-status-focus",
    text: "text-status-focus",
    ring: "ring-status-focus/30",
    soft: "bg-status-focus/10",
  },
  WAIT: {
    label: "WAIT",
    dot: "bg-status-wait",
    text: "text-status-wait",
    ring: "ring-status-wait/30",
    soft: "bg-status-wait/10",
  },
  DONE: {
    label: "DONE",
    dot: "bg-status-done",
    text: "text-status-done",
    ring: "ring-status-done/30",
    soft: "bg-status-done/10",
  },
}

/** Compact TODO/FOCUS/WAIT/DONE badge. Status is shown by a dot plus a visible text label, never color alone. */
export function StatusBadge({ status, className }: { status: Status; className?: string }) {
  const c = statusConfig[status]
  return (
    <span
      data-slot="status-badge"
      className={cn(
        "inline-flex items-center gap-1.5 rounded-md px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide ring-1 ring-inset",
        c.soft,
        c.text,
        c.ring,
        className,
      )}
    >
      <span className={cn("size-1.5 rounded-full", c.dot)} aria-hidden />
      {c.label}
    </span>
  )
}
