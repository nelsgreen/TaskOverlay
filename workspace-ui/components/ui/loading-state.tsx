import { LoaderCircle } from "lucide-react"
import { cn } from "@/lib/utils"

interface LoadingStateProps {
  message?: string
  className?: string
}

/** Neutral loading block with accessible busy/status semantics. No fabricated progress percentage. */
export function LoadingState({ message = "Loading…", className }: LoadingStateProps) {
  return (
    <div
      data-slot="loading-state"
      role="status"
      aria-busy="true"
      aria-live="polite"
      className={cn("flex items-center gap-2 px-3 py-8 text-[12px] text-muted-foreground", className)}
    >
      <LoaderCircle className="size-3.5 animate-spin motion-reduce:animate-none" aria-hidden />
      <span>{message}</span>
    </div>
  )
}
