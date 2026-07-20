import { Check, RefreshCw } from "lucide-react"
import { cn } from "@/lib/utils"

export type SavedStateStatus = "saving" | "saved" | "failed"

interface SavedStateProps {
  status: SavedStateStatus
  savingLabel?: string
  savedLabel?: string
  failedLabel?: string
  onRetry?: () => void
  className?: string
}

/**
 * Calm, non-prominent autosave indicator. Distinct contract from ErrorState:
 * "failed" here stays inline/quiet (no alert role, no destructive banner) -
 * use ErrorState for a real blocking error, not for a retryable autosave hiccup.
 */
export function SavedState({
  status,
  savingLabel = "Saving…",
  savedLabel = "Saved",
  failedLabel = "Save failed",
  onRetry,
  className,
}: SavedStateProps) {
  return (
    <span
      data-slot="saved-state"
      aria-live="polite"
      className={cn("inline-flex items-center gap-1.5 text-[11px]", className)}
    >
      {status === "saving" && (
        <>
          <span className="size-1.5 animate-pulse rounded-full bg-status-meet motion-reduce:animate-none" aria-hidden />
          <span className="text-muted-foreground">{savingLabel}</span>
        </>
      )}
      {status === "saved" && (
        <>
          <Check className="size-3 text-status-focus" aria-hidden />
          <span className="text-muted-foreground">{savedLabel}</span>
        </>
      )}
      {status === "failed" &&
        (onRetry ? (
          <button
            type="button"
            onClick={onRetry}
            className="inline-flex items-center gap-1 rounded text-destructive outline-none hover:underline focus-visible:ring-2 focus-visible:ring-ring"
          >
            <RefreshCw className="size-3" aria-hidden />
            {failedLabel} · Retry
          </button>
        ) : (
          <span className="text-destructive">{failedLabel}</span>
        ))}
    </span>
  )
}
