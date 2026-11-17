import type { ReactNode } from 'react'

/**
 * Small dependency-free SVG charts (bar, line, donut) in the WCS design tokens. They draw exactly the numbers they are given: no smoothing, no estimates; a missing value
 * (null) leaves a gap. Every mark carries a native tooltip (<title>) and the chart is described for assistive technology.
 */
export type ChartTone = 'ok' | 'bad' | 'info' | 'warn' | 'muted'

const W = 640, H = 230, ML = 40, MR = 10, MT = 12, MB = 30

/** Rounds a maximum up to 1, 2, 2.5, 5 or 10 x 10^n so the axis ticks are round numbers. */
function niceMax(max: number): number {
  if (max <= 0) return 1
  const exp = Math.pow(10, Math.floor(Math.log10(max)))
  const f = max / exp
  return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * exp
}

function Axes({ ticks, format, labels, step }: { ticks: number[]; format: (n: number) => string; labels: string[]; step: number }) {
  const top = ticks[ticks.length - 1] || 1
  const y = (v: number) => MT + (1 - v / top) * (H - MT - MB)
  const slot = (W - ML - MR) / Math.max(1, labels.length)
  return (
    <g className="ch-axes">
      {ticks.map((v) => (
        <g key={v}>
          <line x1={ML} x2={W - MR} y1={y(v)} y2={y(v)} className={v === 0 ? 'ch-base' : 'ch-grid'} />
          <text x={ML - 6} y={y(v) + 4} textAnchor="end" className="ch-tick">{format(v)}</text>
        </g>
      ))}
      {labels.map((l, i) => i % step === 0 && <text key={i} x={ML + slot * (i + 0.5)} y={H - 10} textAnchor="middle" className="ch-tick">{l}</text>)}
    </g>
  )
}

const ticksFor = (max: number): number[] => { const top = niceMax(max); return [0, top / 4, top / 2, (top * 3) / 4, top] }
/** Counts: whole-number ticks (0,1,2,3 ...), never 0.5. */
const countTicks = (max: number): number[] => {
  const top = Math.max(1, Math.ceil(max))
  if (top <= 4) return Array.from({ length: top + 1 }, (_, i) => i)
  const nice = niceMax(top)
  return [0, nice / 4, nice / 2, (nice * 3) / 4, nice]
}
const stepFor = (n: number) => Math.max(1, Math.ceil(n / 12))

export interface BarSeries { label: string; tone: ChartTone; values: number[] }

export function BarChart({ labels, series, ariaLabel, tooltipLabels }: { labels: string[]; series: BarSeries[]; ariaLabel: string; tooltipLabels: string[] }) {
  const max = Math.max(0, ...series.flatMap((s) => s.values))
  const ticks = countTicks(max)
  const top = ticks[ticks.length - 1]
  const slot = (W - ML - MR) / Math.max(1, labels.length)
  const group = slot * 0.7
  const bar = group / Math.max(1, series.length)
  const y = (v: number) => MT + (1 - v / top) * (H - MT - MB)
  return (
    <svg viewBox={`0 0 ${W} ${H}`} className="chart" role="img" aria-label={ariaLabel}>
      <Axes ticks={ticks} format={(n) => String(Math.round(n * 10) / 10)} labels={labels} step={stepFor(labels.length)} />
      {labels.map((_, i) => (
        <g key={i} className="ch-group">
          <title>{tooltipLabels[i]}</title>
          <rect x={ML + slot * i} y={MT} width={slot} height={H - MT - MB} className="ch-hit" />
          {series.map((s, j) => {
            const v = s.values[i] ?? 0
            return v > 0 ? <rect key={s.label} x={ML + slot * i + (slot - group) / 2 + bar * j} y={y(v)} width={Math.max(2, bar - 2)} height={H - MB - y(v)} rx={1.5} className={`ch-bar ch-${s.tone}`} /> : null
          })}
        </g>
      ))}
    </svg>
  )
}

export function LineChart({ labels, values, tone = 'info', ariaLabel, tooltipLabels, formatY }: {
  labels: string[]; values: (number | null)[]; tone?: ChartTone; ariaLabel: string; tooltipLabels: string[]; formatY: (n: number) => string
}) {
  const max = Math.max(0, ...values.filter((v): v is number => v !== null))
  const ticks = ticksFor(max)
  const top = ticks[ticks.length - 1]
  const slot = (W - ML - MR) / Math.max(1, labels.length)
  const x = (i: number) => ML + slot * (i + 0.5)
  const y = (v: number) => MT + (1 - v / top) * (H - MT - MB)
  // a gap (no completed task in that bucket) breaks the line: nothing is interpolated
  const segments: { i: number; v: number }[][] = []
  values.forEach((v, i) => { if (v === null) { segments.push([]) } else { if (!segments.length) segments.push([]); segments[segments.length - 1].push({ i, v }) } })
  return (
    <svg viewBox={`0 0 ${W} ${H}`} className="chart" role="img" aria-label={ariaLabel}>
      <Axes ticks={ticks} format={formatY} labels={labels} step={stepFor(labels.length)} />
      {segments.filter((s) => s.length > 1).map((s, k) => <polyline key={k} points={s.map((p) => `${x(p.i)},${y(p.v)}`).join(' ')} className={`ch-line ch-stroke-${tone}`} />)}
      {values.map((v, i) => v === null ? null : (
        <g key={i} className="ch-point">
          <title>{tooltipLabels[i]}</title>
          <circle cx={x(i)} cy={y(v)} r={9} className="ch-hit" />
          <circle cx={x(i)} cy={y(v)} r={3.5} className={`ch-dot ch-${tone}`} />
        </g>
      ))}
    </svg>
  )
}

export interface DonutSlice { label: string; value: number; tone: ChartTone }

export function Donut({ slices, ariaLabel, center }: { slices: DonutSlice[]; ariaLabel: string; center: ReactNode }) {
  const total = slices.reduce((a, s) => a + s.value, 0)
  const R = 54, C = 2 * Math.PI * R
  let offset = 0
  return (
    <div className="donut">
      <svg viewBox="0 0 140 140" role="img" aria-label={ariaLabel}>
        <circle cx="70" cy="70" r={R} className="ch-ring" />
        {total > 0 && slices.filter((s) => s.value > 0).map((s) => {
          const len = (s.value / total) * C
          const el = (
            <circle key={s.label} cx="70" cy="70" r={R} className={`ch-arc ch-stroke-${s.tone}`} strokeDasharray={`${len} ${C - len}`} strokeDashoffset={-offset} transform="rotate(-90 70 70)">
              <title>{`${s.label}: ${s.value}`}</title>
            </circle>
          )
          offset += len
          return el
        })}
      </svg>
      <div className="donut-center">{center}</div>
    </div>
  )
}
