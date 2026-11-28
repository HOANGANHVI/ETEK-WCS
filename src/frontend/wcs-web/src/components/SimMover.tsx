import { useEffect, useRef, type ReactNode, type SVGProps } from 'react'
import { headingAt, lengthOf, passed, pointAt, turnTowards, type Point, type Sample } from '../lib/track'

/**
 * CR-070: places a simulated AMR and turns its body, frame by frame, along the positions its route passed since the last answer of the simulator.
 * Positions are drawing units. The drawing runs about one answer behind the simulator, which is what lets it stay on the road.
 */
export function SimMover({ path, travelled, at, heading, upp, children, body, ...rest }: Omit<SVGProps<SVGGElement>, 'path' | 'children'> & {
  path: Point[]; travelled: number; at: Point
  /** Heading while the AMR has not moved yet, degrees clockwise on the screen. */
  heading: number
  upp: number; body: ReactNode; children?: ReactNode
}) {
  const outer = useRef<SVGGElement>(null)
  const turn = useRef<SVGGElement>(null)
  const state = useRef<{ before: Sample | null; track: Point[]; done: number; speed: number; seen: number; angle: number; zoom: number }>(
    { before: null, track: [at], done: 0, speed: 0, seen: 0, angle: heading, zoom: upp })
  const key = `${path.length}:${path[0]?.join()}:${path[path.length - 1]?.join()}:${travelled}:${at.join()}`

  useEffect(() => { state.current.zoom = upp }, [upp])

  useEffect(() => {
    const s = state.current
    const now = performance.now()
    const sample: Sample = { path, travelled, at }
    const more = passed(s.before, sample)
    if (!s.before) s.track = [at]
    else if (more.length > 0) {
      // what is left of the track plus what was passed since: all of it is driven before the next answer is due
      const left = s.track.length > 1 ? [pointAt(s.track, s.done), ...s.track.slice(1).filter((_, i) => lengthOf(s.track.slice(0, i + 2)) > s.done)] : s.track
      s.track = [...left, ...more]; s.done = 0
      const interval = s.seen ? Math.min(Math.max(now - s.seen, 200), 1500) : 400
      s.speed = lengthOf(s.track) / interval
    }
    s.before = sample; s.seen = now
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the key holds everything of the sample that matters
  }, [key])

  useEffect(() => {
    let frame = 0; let last = performance.now()
    const draw = (now: number) => {
      const s = state.current
      const dt = Math.min(now - last, 100); last = now
      const total = lengthOf(s.track)
      s.done = Math.min(total, s.done + s.speed * dt)
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
