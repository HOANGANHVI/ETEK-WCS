import type { FleetAmr, FleetView } from '../api/fleetTypes'
import { classifyStatus, type StatusCategory } from './rcsStatus'

/** A reported number, or null when it is missing or not a number (nothing is filled in). */
export function num(v: string | null | undefined): number | null {
  if (v === null || v === undefined || v.trim() === '') return null
  const n = Number(v)
  return Number.isFinite(n) ? n : null
}

/** Battery percent 0-100, or null when the RCS gave nothing usable (an out-of-range value is not shown as a battery level). */
export function batteryPercent(v: string | null | undefined): number | null {
  const n = num(v)
  return n !== null && n >= 0 && n <= 100 ? n : null
}

/** An AMR is "moving" only when the RCS reports a speed above zero in current data. */
export const isMoving = (a: FleetAmr): boolean => a.state === 'Fresh' && (num(a.speed) ?? 0) > 0

export interface FleetKpis { total: number; updating: number; old: number; moving: number; abnormal: number; unknownStatus: number; avgBattery: number | null; batterySamples: number }

/** Every figure is counted from the AMRs the RCS reported; nothing is assumed. Old = stale or missing. Abnormal = a documented fault status or a status that is not classified. */
export function computeKpis(f: FleetView): FleetKpis {
  const cats = f.amrs.map((a) => classifyStatus(a.status).category)
  const bat = f.amrs.map((a) => batteryPercent(a.battery)).filter((x): x is number => x !== null)
  return {
    total: f.amrs.length,
    updating: f.amrs.filter((a) => a.state === 'Fresh').length,
    old: f.amrs.filter((a) => a.state !== 'Fresh').length,
    moving: f.amrs.filter(isMoving).length,
    abnormal: cats.filter((c) => c === 'Fault' || c === 'Unknown').length,
    unknownStatus: cats.filter((c) => c === 'Unknown').length,
    avgBattery: bat.length === 0 ? null : Math.round((bat.reduce((s, x) => s + x, 0) / bat.length) * 10) / 10,
    batterySamples: bat.length,
  }
}

export interface FleetFilter { text: string; category: StatusCategory | 'NoStatus' | ''; map: string; data: FleetAmr['state'] | '' }
export const NO_FILTER: FleetFilter = { text: '', category: '', map: '', data: '' }

/** By robot code / raw status code / business AGV code (text), status category, map and data freshness. */
export function filterAmrs(amrs: readonly FleetAmr[], f: FleetFilter): FleetAmr[] {
  const q = f.text.trim().toLowerCase()
  return amrs.filter((a) => {
    if (q && ![a.robotCode, a.status ?? '', a.linkedAgvCode ?? '', a.mapCode ?? ''].some((s) => s.toLowerCase().includes(q))) return false
    if (f.category) {
      const c = classifyStatus(a.status).category
      if (f.category === 'NoStatus' ? c !== null : c !== f.category) return false
    }
    if (f.map && (a.mapCode ?? '') !== f.map) return false
    if (f.data && a.state !== f.data) return false
    return true
  })
}

export const distinctMaps = (amrs: readonly FleetAmr[]): string[] => [...new Set(amrs.map((a) => a.mapCode).filter((m): m is string => !!m))].sort()
