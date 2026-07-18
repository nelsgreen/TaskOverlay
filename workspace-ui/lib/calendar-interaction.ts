export class CalendarActivationGuard {
  private suppressPointerClick = false

  beginPointerInteraction(): void {
    this.suppressPointerClick = false
  }

  completeManipulation(): void {
    this.suppressPointerClick = true
  }

  shouldActivate(eventDetail: number): boolean {
    if (eventDetail === 0) {
      this.suppressPointerClick = false
      return true
    }
    if (!this.suppressPointerClick) return true

    this.suppressPointerClick = false
    return false
  }
}
