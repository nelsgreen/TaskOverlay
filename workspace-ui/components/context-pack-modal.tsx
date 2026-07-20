"use client"

import { useEffect, useRef, useState } from "react"
import { Check, Copy, X } from "lucide-react"
import { Button } from "@/components/ui/button"
import { IconButton } from "@/components/ui/icon-button"
import { Textarea } from "@/components/ui/textarea"

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
    // Backdrop intentionally has no onClick: closing happens only via Cancel/Close/X (or Escape above), never an outside click.
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-background/70 p-6 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
    >
      <div
        className="flex max-h-[85vh] w-full max-w-2xl flex-col overflow-hidden rounded-xl border border-border bg-popover shadow-2xl shadow-black/50"
      >
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <div className="flex flex-col">
            <span className="text-sm font-semibold text-foreground">Context Pack</span>
            <span className="text-[11px] text-muted-foreground">{subtitle}</span>
          </div>
          <IconButton label="Close" onClick={onClose}>
            <X className="size-4" />
          </IconButton>
        </div>

        <div className="flex-1 overflow-hidden px-4 py-3">
          <Textarea
            ref={textareaRef}
            readOnly
            value={markdown}
            onFocus={(e) => e.currentTarget.select()}
            className="h-full min-h-[320px] resize-none font-mono text-[12px]"
          />
        </div>

        <div className="flex items-center justify-between gap-3 border-t border-border px-4 py-3">
          <span className="min-w-0 flex-1 text-[11px] text-muted-foreground">
            {copyState === "copied" && "Copied to clipboard."}
            {copyState === "failed" && "Copy failed — text selected, copy manually (Ctrl/Cmd+C)."}
            {copyState === "idle" && "Based on stored TaskOverlay data only."}
          </span>
          <div className="flex shrink-0 items-center gap-2">
            <Button tone="primary" size="sm" onClick={handleCopy}>
              {copyState === "copied" ? <Check className="size-3.5" aria-hidden /> : <Copy className="size-3.5" aria-hidden />}
              Copy markdown
            </Button>
            <Button tone="secondary" size="sm" onClick={onClose}>
              Close
            </Button>
          </div>
        </div>
      </div>
    </div>
  )
}
