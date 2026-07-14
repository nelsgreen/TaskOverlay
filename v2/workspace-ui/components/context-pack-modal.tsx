"use client"

import { useEffect, useRef, useState } from "react"
import { Check, Copy, X } from "lucide-react"

/**
 * Shared preview/copy modal for Context Pack export (Project / Task / MEET).
 * Pure UI: the caller already built the markdown (via lib/context-pack-builder)
 * before opening this — no generation, no persistence, no network here.
 */
interface ContextPackModalProps {
  subtitle: string
  markdown: string
  onClose: () => void
}

type CopyState = "idle" | "copied" | "failed"

export function ContextPackModal({ subtitle, markdown, onClose }: ContextPackModalProps) {
  const [copyState, setCopyState] = useState<CopyState>("idle")
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose() }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [onClose])

  async function handleCopy() {
    try {
      if (!navigator.clipboard) throw new Error("Clipboard API unavailable")
      await navigator.clipboard.writeText(markdown)
      setCopyState("copied")
    } catch {
      // Fallback: keep the text selectable so the user can copy it manually.
      textareaRef.current?.focus()
      textareaRef.current?.select()
      setCopyState("failed")
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-background/70 p-6 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      onClick={onClose}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="flex max-h-[85vh] w-full max-w-2xl flex-col overflow-hidden rounded-xl border border-border bg-popover shadow-2xl shadow-black/50"
      >
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <div className="flex flex-col">
            <span className="text-sm font-semibold text-foreground">Context Pack</span>
            <span className="text-[11px] text-muted-foreground">{subtitle}</span>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="flex size-7 items-center justify-center rounded text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
          >
            <X className="size-4" />
          </button>
        </div>

        <div className="flex-1 overflow-hidden px-4 py-3">
          <textarea
            ref={textareaRef}
            readOnly
            value={markdown}
            onFocus={(e) => e.currentTarget.select()}
            className="h-full min-h-[320px] w-full resize-none rounded-md border border-input bg-background px-3 py-2 font-mono text-[12px] leading-relaxed text-foreground outline-none focus:border-primary/60"
          />
        </div>

        <div className="flex items-center justify-between gap-3 border-t border-border px-4 py-3">
          <span className="min-w-0 flex-1 text-[11px] text-muted-foreground">
            {copyState === "copied" && "Copied to clipboard."}
            {copyState === "failed" && "Copy failed — text selected, copy manually (Ctrl/Cmd+C)."}
            {copyState === "idle" && "Based on stored TaskOverlay data only."}
          </span>
          <div className="flex shrink-0 items-center gap-2">
            <button
              type="button"
              onClick={handleCopy}
              className="flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-[12px] font-semibold text-primary-foreground transition-colors hover:bg-primary/90"
            >
              {copyState === "copied" ? <Check className="size-3.5" aria-hidden /> : <Copy className="size-3.5" aria-hidden />}
              Copy markdown
            </button>
            <button
              type="button"
              onClick={onClose}
              className="rounded-md border border-border bg-card px-3 py-1.5 text-[12px] font-medium text-foreground transition-colors hover:bg-accent"
            >
              Close
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
