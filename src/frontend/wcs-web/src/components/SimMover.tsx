import { useEffect, useRef, type ReactNode, type SVGProps } from 'react'
import { DRAW_DELAY_MS, approachOffset, distanceAt, headingAt, lengthOf, nextOffset, passed, pointAt, slice, turnTowards, type Mark, type Point, type Sample } from '../lib/track'

/**
 * CR-070 / CR-071: places a simulated AMR and turns its body, frame by frame. The positions its route passed make a track; each answer of the simulator says at which simulation
 * time the AMR was how far along it. The drawing plays that back a little later at the pace of the simulation, so it stays on the road and does not speed up or stop when the
 * answers arrive unevenly. Positions are drawing units.
 */
export function SimMover({ path, travelled, at, time, heading, upp, children, body, ...rest }: Omit<SVGProps<SVGGElement>, 'path' | 'children'> & {
  path: Point[]; travelled: number; at: Point
  /** Simulation time (ms) of this position. */
  time: number
  /** Heading while the AMR has not moved yet, degrees clockwise on the screen. */
  heading: number
  upp: number; body: ReactNode; children?: ReactNode
}) {
  const outer = useRef<SVGGElement>(null)
  const turn = useRef<SVGGElement>(null)
  const state = useRef<{ before: Sample | null; track: Point[]; marks: Mark[]; offset: number | null; wanted: number | null; done: number; angle: number; zoom: number }>(
    { before: null, track: [at], marks: [], offset: null, wanted: null, done: 0, angle: heading, zoom: upp })
  const key = `${time}:${path.length}:${path[0]?.join()}:${path[path.length - 1]?.join()}:${travelled}:${at.join()}`

  useEffect(() => { state.current.zoom = upp }, [upp])

  useEffect(() => {
    const s = state.current
    const sample: Sample = { path, travelled, at }
    if (!s.before) s.track = [at]
    else {
      const more = passed(s.before, sample)
      if (more.length > 0) s.track = [...s.track, ...more]
    }
    s.before = sample
    const total = lengthOf(s.track)
    const newest = s.marks[s.marks.length - 1]
    if (!newest || time > newest.t) {
      s.marks.push({ t: time, d: total })
      s.wanted = nextOffset(s.wanted, performance.now(), time)
    } else newest.d = Math.max(newest.d, total)
    // forget what has been drawn long ago
    if (s.done > 0 && (s.marks.length > 40 || s.track.length > 200)) {
      const cut = s.done
      s.track = slice(s.track, cut, total); if (s.track.length === 0) s.track = [at]
      s.marks = s.marks.slice(-20).map((m) => ({ t: m.t, d: Math.max(0, m.d - cut) }))
      s.done = 0
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the key holds everything of the sample that matters
  }, [key])

  useEffect(() => {
    let frame = 0; let last = performance.now()
    const draw = (now: number) => {
      const s = state.current
      const dt = Math.min(Math.max(now - last, 0), 100); last = now
      const total = lengthOf(s.track)
      if (s.wanted !== null) s.offset = approachOffset(s.offset, s.wanted, dt)
      if (s.offset !== null && s.marks.length > 0) s.done = Math.max(s.done, Math.min(total, distanceAt(s.marks, now - s.offset - DRAW_DELAY_MS)))
      const p = s.track.length > 1 ? pointAt(s.track, s.done) : s.track[0]
      const wanted = headingAt(s.track, s.done, 7 * s.zoom)
      if (wanted !== null && s.done < total) s.angle = turnTowards(s.angle, wanted, dt * 0.9)
      outer.current?.setAttribute('transform', `translate(${p[0]} ${p[1]}) scale(${s.zoom})`)
      turn.current?.setAttribute('transform', `rotate(${s.angle})`)
      frame = requestAnimationFrame(draw)
    }
    draw(last)
    return () => cancelAnimationFrame(frame)
  }, [])

  return (
    <g ref={outer} {...rest}>
      <g ref={turn}>{body}</g>
      {children}
    </g>
  )
}
