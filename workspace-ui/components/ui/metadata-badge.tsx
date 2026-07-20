import type { ComponentType, ReactNode } from "react"
import { cn } from "@/lib/utils"

/**
 * Neutral metadata label (source, count, date category, type, origin) -
 * deliberately not a general arbitrary-color badge API. "neutral" is the
 * only tone today; a new tone must be an explicit, justified addition here,
 * not a caller-supplied color.
 */
export type MetadataBadgeTone = "neutral"

const toneClasses: Record<MetadataBadgeTone, string> = {
  neutral: "border-border bg-muted text-muted-foreground",
}

interface MetadataBadgeProps {
  icon?: ComponentType<{ className?: string; "aria-hidden"?: boolean }>
  label: ReactNode
  tone?: MetadataBadgeTone
  className?: string
}

export function MetadataBadge({ icon: Icon, label, tone = "neutral", className }: MetadataBadgeProps) {
  return (
    <span
      data-slot="metadata-badge"
      className={cn(
        "inline-flex shrink-0 items-center gap-1 rounded border px-1.5 py-0.5 text-[10px] font-medium",
        toneClasses[tone],
        className,
      )}
    >
      {Icon && <Icon className="size-2.5" aria-hidden />}
      {label}
    </span>
  )
}
