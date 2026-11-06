import { useEffect, useState } from 'react'

/** Current time, refreshed every second (used by the header clock and "x giây trước" labels). */
export function useClock(intervalMs = 1000) {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), intervalMs)
    return () => clearInterval(id)
  }, [intervalMs])
  return now
}
