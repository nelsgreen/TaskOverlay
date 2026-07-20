import type { ReactNode } from "react"
import { cn } from "@/lib/utils"

interface EmptyStateProps {
  title?: string
  message: string
  action?: ReactNode
  className?: string
}

/** Neutral empty-state block. No decorative illustration - title/message/action only. */
export function EmptyState({ title, message, action, className }: EmptyStateProps) {
  return (
    <div
      data-slot="empty-state"
      className={cn("flex flex-col items-center gap-1 px-3 py-8 text-center", className)}
    >
      {title && <p className="text-[12px] font-semibold text-foreground">{title}</p>}
      <p className="text-[12px] text-muted-foreground">{message}</p>
      {action}
    </div>
  )
}
