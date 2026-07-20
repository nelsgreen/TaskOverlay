import type { ReactNode } from "react"
import { cn } from "@/lib/utils"

interface PanelProps {
  children: ReactNode
  className?: string
}

/**
 * Canonical bounded surface: border, radius, surface tint. No mandatory
 * shadow. `className` is for layout/spacing only (margins, group markers) -
 * it is appended after the canonical surface classes, not a restyle hook.
 */
export function Panel({ children, className }: PanelProps) {
  return (
    <div data-slot="panel" className={cn("rounded-lg border border-border bg-card/40", className)}>
      {children}
    </div>
  )
}
