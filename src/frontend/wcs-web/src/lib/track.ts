/**
 * CR-070: how a simulated AMR is moved on the screen between two answers of the simulator. The answer says how far along its route the AMR is; the drawing follows every
 * position of the route it passed meanwhile, so it never cuts a corner. Pure geometry, positions in the units of the caller.
 */
export type Point = [number, number]

const length = (a: Point, b: Point) => Math.hypot(b[0] - a[0], b[1] - a[1])
export const lengthOf = (line: Point[]): number => line.reduce((s, p, i) => (i === 0 ? 0 : s + length(line[i - 1], p)), 0)

/** The position at a distance along a line (clamped to its ends). */
export function pointAt(line: Point[], distance: number): Point {
  let left = Math.max(0, distance)
  for (let i = 1; i < line.length; i++) {
    const d = length(line[i - 1], line[i])
    if (left <= d && d > 0) { const f = left / d; return [line[i - 1][0] + (line[i][0] - line[i - 1][0]) * f, line[i - 1][1] + (line[i][1] - line[i - 1][1]) * f] }
    left -= d
  }
  return line[line.length - 1]
}

/** The part of a line between two distances along it, with every vertex in between. */
export function slice(line: Point[], from: number, to: number): Point[] {
  if (line.length === 0 || to <= from) return []
  const out: Point[] = [pointAt(line, from)]
  let walked = 0
  for (let i = 1; i < line.length; i++) {
    walked += length(line[i - 1], line[i])
    if (walked > from && walked < to) out.push(line[i])
  }
  out.push(pointAt(line, to))
  return out
}

export interface Sample { path: Point[]; travelled: number; at: Point }

const samePath = (a: Point[], b: Point[]) => a.length === b.length && a.length > 0 && length(a[0], b[0]) < 1e-9 && length(a[a.length - 1], b[b.length - 1]) < 1e-9

/** The positions passed between two samples: along the route; when a new route began, first to the end of the old one. */
export function passed(before: Sample | null, now: Sample): Point[] {
  if (!before) return [now.at]
  if (now.path.length > 1) {
    if (samePath(before.path, now.path)) return now.travelled > before.travelled ? slice(now.path, before.travelled, now.travelled) : []
    const rest = before.path.length > 1 ? slice(before.path, before.travelled, lengthOf(before.path)) : []
    return [...rest, ...slice(now.path, 0, now.travelled)]
  }
  // no route now (arrived, waiting, idle): finish the old one, then stand where the simulator says
  const rest = before.path.length > 1 ? slice(before.path, before.travelled, lengthOf(before.path)) : []
  const end = rest.length > 0 ? rest[rest.length - 1] : before.at
  return length(end, now.at) > 1e-6 ? [...rest, now.at] : rest
}

/**
 * Heading in degrees (clockwise on the screen, 0 = right) of the movement around a distance: taken over a short window so that the AMR turns through a corner instead of
 * jumping. NULL = no movement there.
 */
export function headingAt(line: Point[], distance: number, window: number): number | null {
  const total = lengthOf(line)
  if (line.length < 2 || total <= 0) return null
  const a = pointAt(line, Math.max(0, Math.min(distance, total) - window)); const b = pointAt(line, Math.min(total, Math.max(distance, 0) + window))
  return length(a, b) > 1e-9 ? (Math.atan2(b[1] - a[1], b[0] - a[0]) * 180) / Math.PI : null
}

/** CR-071: how far along the track the AMR had come at a simulation time; samples are ordered by time. */
export interface Mark { t: number; d: number }
export function distanceAt(marks: Mark[], time: number): number {
  if (marks.length === 0) return 0
  if (time <= marks[0].t) return marks[0].d
  for (let i = 1; i < marks.length; i++) {
    const a = marks[i - 1]; const b = marks[i]
    if (time <= b.t) return b.t > a.t ? a.d + ((b.d - a.d) * (time - a.t)) / (b.t - a.t) : b.d
  }
  return marks[marks.length - 1].d
}

/** The drawing runs this far behind the newest answer, so that there is always a next position to move to (one answer interval and one simulator step). */
export const DRAW_DELAY_MS = 800

/**
 * The drawing clock: simulation time = screen time minus an offset. Each answer measures the offset; the wanted value follows those measures gently, and after a gap
 * (pause, hidden window) it is taken as it is.
 */
export function nextOffset(wanted: number | null, screenNow: number, newest: number): number {
  const measured = screenNow - newest
  return wanted === null || Math.abs(measured - wanted) > 1500 ? measured : wanted + (measured - wanted) * 0.1
}

/** The offset in use moves to the wanted one a little every frame (at most a tenth of the frame time), so that the correction is never seen as a jump. */
export function approachOffset(current: number | null, wanted: number, frameMs: number): number {
  if (current === null || Math.abs(wanted - current) > 1500) return wanted
  const most = frameMs * 0.1
  return current + Math.max(-most, Math.min(most, wanted - current))
}

/** One step towards a heading by the shorter way round. */
export function turnTowards(current: number, wanted: number, most: number): number {
  const delta = ((((wanted - current) % 360) + 540) % 360) - 180
  return current + Math.max(-most, Math.min(most, delta))
}
