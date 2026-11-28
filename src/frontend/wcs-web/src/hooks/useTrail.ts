import { useState } from 'react'
import type { AmrLiveRun } from '../api/simTypes'

/** CR-070: the runs with the distance of the answer before, for the same route: what the drawing of the AMR has certainly passed. */
export function useTrail(runs: AmrLiveRun[]): AmrLiveRun[] {
  const key = runs.map((r) => `${r.requestCode}:${r.path.length}:${r.path[0]?.join()}:${r.travelled}`).join('|')
  const [memory, setMemory] = useState<{ key: string; now: AmrLiveRun[]; shown: AmrLiveRun[] }>({ key: '', now: [], shown: [] })
  if (memory.key !== key) {
    const shown = runs.map((r) => {
      const before = memory.now.find((p) => p.requestCode === r.requestCode)
      const same = before && before.path.length === r.path.length && before.path[0]?.join() === r.path[0]?.join()
      return { ...r, travelled: same ? Math.min(before.travelled, r.travelled) : 0 }
    })
    setMemory({ key, now: runs, shown })
    return shown
  }
  return memory.shown
}
