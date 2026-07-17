export interface MeetingTitleValue {
  projectId?: string
  title: string
  titleIsGenerated?: boolean
  date: string
  startTime: string
}

export interface MeetingTitleProject {
  id: string
  name: string
}

export function generatedMeetingTitle(
  projectName: string | undefined,
  date: string,
  startTime: string,
): string {
  const [year = "", month = "", day = ""] = date.split("-")
  const dateLabel = year && month && day ? `${day}.${month}.${year}` : date
  const project = projectName?.trim()
  return project
    ? `MEET \u2014 ${project} \u2014 ${dateLabel}, ${startTime}`
    : `MEET \u2014 ${dateLabel}, ${startTime}`
}

export function generatedTitleForMeeting(
  meeting: MeetingTitleValue,
  projects: MeetingTitleProject[],
): string {
  const projectName = projects.find((project) => project.id === meeting.projectId)?.name
  return generatedMeetingTitle(projectName, meeting.date, meeting.startTime)
}

export function applyMeetingTitleInput<T extends MeetingTitleValue>(
  meeting: T,
  value: string,
  projects: MeetingTitleProject[],
): T {
  if (value.trim()) {
    return { ...meeting, title: value, titleIsGenerated: false }
  }

  return {
    ...meeting,
    title: generatedTitleForMeeting(meeting, projects),
    titleIsGenerated: true,
  }
}
