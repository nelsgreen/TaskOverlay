import type { ReactNode } from "react"
import { cn } from "@/lib/utils"

interface ErrorStateProps {
  message: string
  action?: ReactNode
  className?: string
}

/** Error block using destructive semantics, announced via role="alert". Distinct from SavedState's calmer failed contract. */
export function ErrorState({ message, action, className }: ErrorStateProps) {
  return (
    <div
      data-slot="error-state"
      role="alert"
      className={cn(
        "rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-xs text-destructive",
        className,
      )}
    >
      <span>{message}</span>
      {action}
    </div>
  )
}
