export type MeetSaveStatus = "saving" | "saved" | "failed"
export type MeetSaveMode = "debounced" | "immediate"

interface FlushWaiter {
  targetRevision: number
  resolve: (saved: boolean) => void
}

export class MeetingAutosaveQueue<T, F extends string> {
  private latestValue: T
  private latestRevision = 0
  private savedRevision = 0
  private pendingFields = new Set<F>()
  private inFlight: Promise<void> | null = null
  private debounceTimer: ReturnType<typeof setTimeout> | null = null
  private blockedByFailure = false
  private disposed = false
  private status: MeetSaveStatus = "saved"
  private readonly waiters: FlushWaiter[] = []
  private readonly save: (
    value: T,
    fields: ReadonlySet<F>,
    mutationSequence: number,
  ) => Promise<void>
  private readonly onStatus: (status: MeetSaveStatus) => void
  private readonly debounceMilliseconds: number

  constructor(
    initialValue: T,
    save: (
      value: T,
      fields: ReadonlySet<F>,
      mutationSequence: number,
    ) => Promise<void>,
    onStatus: (status: MeetSaveStatus) => void,
    debounceMilliseconds = 350,
  ) {
    this.latestValue = initialValue
    this.save = save
    this.onStatus = onStatus
    this.debounceMilliseconds = debounceMilliseconds
  }

  enqueue(value: T, fields: Iterable<F>, mode: MeetSaveMode): number {
    if (this.disposed) return this.latestRevision

    this.latestValue = value
    for (const field of fields) this.pendingFields.add(field)
    this.latestRevision += 1
    this.blockedByFailure = false
    this.emit("saving")
    this.clearDebounce()

    if (mode === "immediate") {
      void this.pump()
    } else {
      this.debounceTimer = setTimeout(() => {
        this.debounceTimer = null
        void this.pump()
      }, this.debounceMilliseconds)
    }

    return this.latestRevision
  }

  hasPending(): boolean {
    return this.debounceTimer !== null ||
      this.inFlight !== null ||
      this.pendingFields.size > 0 ||
      this.blockedByFailure
  }

  getStatus(): MeetSaveStatus {
    return this.status
  }

  async flush(): Promise<boolean> {
    if (this.disposed) return false
    this.clearDebounce()
    const targetRevision = this.latestRevision
    if (this.savedRevision >= targetRevision && !this.hasPending()) return true
    if (this.blockedByFailure) return false

    const result = new Promise<boolean>((resolve) => {
      this.waiters.push({ targetRevision, resolve })
    })
    void this.pump()
    return result
  }

  retry(): Promise<boolean> {
    if (this.disposed) return Promise.resolve(false)
    this.blockedByFailure = false
    this.emit("saving")
    return this.flush()
  }

  dispose(): void {
    this.disposed = true
    this.clearDebounce()
    for (const waiter of this.waiters.splice(0)) waiter.resolve(false)
  }

  private async pump(): Promise<void> {
    if (this.disposed || this.inFlight || this.blockedByFailure || this.pendingFields.size === 0) {
      return this.inFlight ?? Promise.resolve()
    }

    const mutationSequence = this.latestRevision
    const value = this.latestValue
    const fields = new Set(this.pendingFields)
    this.pendingFields.clear()

    const operation = Promise.resolve()
      .then(() => this.save(value, fields, mutationSequence))
      .then(() => {
        this.savedRevision = Math.max(this.savedRevision, mutationSequence)
        this.resolveSuccessfulWaiters()
        if (this.latestRevision === mutationSequence && this.pendingFields.size === 0) {
          this.emit("saved")
        }
      })
      .catch(() => {
        for (const field of fields) this.pendingFields.add(field)
        if (this.latestRevision > mutationSequence) {
          this.blockedByFailure = false
          return
        }

        this.blockedByFailure = true
        this.emit("failed")
        this.resolveFailedWaiters(mutationSequence)
      })
      .finally(() => {
        this.inFlight = null
        if (!this.blockedByFailure && this.pendingFields.size > 0) void this.pump()
      })

    this.inFlight = operation
    await operation
  }

  private emit(status: MeetSaveStatus): void {
    if (this.status === status) return
    this.status = status
    this.onStatus(status)
  }

  private clearDebounce(): void {
    if (this.debounceTimer === null) return
    clearTimeout(this.debounceTimer)
    this.debounceTimer = null
  }

  private resolveSuccessfulWaiters(): void {
    for (let index = this.waiters.length - 1; index >= 0; index -= 1) {
      const waiter = this.waiters[index]
      if (waiter.targetRevision > this.savedRevision) continue
      this.waiters.splice(index, 1)
      waiter.resolve(true)
    }
  }

  private resolveFailedWaiters(failedRevision: number): void {
    for (let index = this.waiters.length - 1; index >= 0; index -= 1) {
      const waiter = this.waiters[index]
      if (waiter.targetRevision <= this.savedRevision) {
        this.waiters.splice(index, 1)
        waiter.resolve(true)
        continue
      }
      if (waiter.targetRevision > failedRevision) continue
      this.waiters.splice(index, 1)
      waiter.resolve(false)
    }
  }
}
