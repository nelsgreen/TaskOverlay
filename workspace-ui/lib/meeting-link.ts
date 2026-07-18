/**
 * A MEET's Link field is a free-text field, not guaranteed to be a valid URL.
 * `openMeetingLink` must only be offered for values the OS shell can actually
 * open — an absolute http(s) URL — so this is checked on the frontend before
 * showing/enabling the "Join call" action or attempting to open anything.
 */
export function isValidMeetingLinkUrl(value: string | null | undefined): boolean {
  const trimmed = value?.trim()
  if (!trimmed) return false
  try {
    const url = new URL(trimmed)
    return url.protocol === "http:" || url.protocol === "https:"
  } catch {
    return false
  }
}
