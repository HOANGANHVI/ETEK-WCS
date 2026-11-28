import { useState } from 'react'
import type { AmrLiveRun } from '../api/simTypes'

const sameRoute = (a: AmrLiveRun, b: AmrLiveRun) => a.requestCode === b.requestCode && a.path.length === b.path.length && a.path[0]?.join() === b.path[0]?.join()

/** CR-070 / CR-071: the runs with the distance of two answers before, for the same route: what the drawing of the AMR has certainly passed. */
export function useTrail(runs: AmrLiveRun[]): AmrLiveRun[] {
  const key = runs.map((r) => `${r.requestCode}:${r.path.length}:${r.path[0]?.join()}:${r.travelled}`).join('|')
  const [memory, setMemory] = useState<{ key: string; last: AmrLiveRun[]; before: AmrLiveRun[]; shown: AmrLiveRun[] }>({ key: '', last: [], before: [], shown: [] })
  if (memory.key !== key) {
    const shown = runs.map((r) => {
      const old = memory.before.find((p) => sameRoute(p, r))
      return { ...r, travelled: old ? Math.min(old.travelled, r.travelled) : 0 }
    })
    setMemory({ key, last: runs, before: memory.last, shown })
    return shown
  }
  return memory.shown
}
