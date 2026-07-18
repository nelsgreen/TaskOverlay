export type MeetingCreatePhase =
  | "idle"
  | "creating"
  | "awaiting-reconciliation"
  | "refresh-required"

export interface MeetingCreateResult {
  success: boolean
  createdMeetingId?: string | null
  warningCode?: string | null
  errorMessage?: string | null
}

export class MeetingCreateGuard {
  private phase: MeetingCreatePhase = "idle"
  private expectedMeetingId: string | null = null
  private knownMeetingIds = new Set<string>()
  private readonly onPhaseChanged: (phase: MeetingCreatePhase) => void

  constructor(onPhaseChanged: (phase: MeetingCreatePhase) => void = () => undefined) {
    this.onPhaseChanged = onPhaseChanged
  }

  getPhase(): MeetingCreatePhase {
    return this.phase
  }

  isCreateBlocked(): boolean {
    return this.phase !== "idle"
  }

  async create(
    send: () => Promise<MeetingCreateResult>,
  ): Promise<MeetingCreateResult | null> {
    if (this.isCreateBlocked()) return null

    this.setPhase("creating")
    try {
      const result = await send()
      if (!result.success) {
        this.expectedMeetingId = null
        this.setPhase("idle")
        return result
      }

      this.expectedMeetingId = result.createdMeetingId?.trim() || null
      if (this.expectedMeetingId && this.knownMeetingIds.has(this.expectedMeetingId)) {
        this.expectedMeetingId = null
        this.setPhase("idle")
        return result
      }
      this.setPhase(
        result.warningCode === "snapshotFailed" || !this.expectedMeetingId
          ? "refresh-required"
          : "awaiting-reconciliation",
      )
      return result
    } catch (error) {
      this.expectedMeetingId = null
      this.setPhase("idle")
      throw error
    }
  }

  reconcile(meetingIds: Iterable<string>): boolean {
    this.knownMeetingIds = new Set(meetingIds)
    if (!this.expectedMeetingId) return false
    if (!this.knownMeetingIds.has(this.expectedMeetingId)) return false

    this.expectedMeetingId = null
    this.setPhase("idle")
    return true
  }

  requestRefresh(refresh: () => boolean): boolean {
    if (this.phase !== "refresh-required") return false
    return refresh()
  }

  private setPhase(phase: MeetingCreatePhase): void {
    if (this.phase === phase) return
    this.phase = phase
    this.onPhaseChanged(phase)
  }
}
