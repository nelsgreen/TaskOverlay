import type { StatusFilterKey, Task } from "@/lib/types"

export function matchesStatusFilter(task: Task, filter: StatusFilterKey): boolean {
  if (filter === "all") return true
  if (filter === "panel") return task.pinned
  if (filter === "remind") return task.reminder !== "none"
  return task.status === filter
}
