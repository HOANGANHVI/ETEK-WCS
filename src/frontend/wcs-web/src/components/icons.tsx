import type { ReactNode, SVGProps } from 'react'

function Svg({ children, ...rest }: SVGProps<SVGSVGElement> & { children: ReactNode }) {
  return (
    <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" {...rest}>
      {children}
    </svg>
  )
}

export const IconOverview = (p: SVGProps<SVGSVGElement>) => (
  <Svg {...p}><rect x="3" y="3" width="8" height="9" rx="1" /><rect x="13" y="3" width="8" height="5" rx="1" /><rect x="13" y="12" width="8" height="9" rx="1" /><rect x="3" y="16" width="8" height="5" rx="1" /></Svg>
)
export const IconAgv = (p: SVGProps<SVGSVGElement>) => (
  <Svg {...p}><rect x="3" y="9" width="18" height="8" rx="2" /><path d="M7 9V6h6v3" /><circle cx="8" cy="17.5" r="1.8" /><circle cx="16" cy="17.5" r="1.8" /></Svg>
)
export const IconTasks = (p: SVGProps<SVGSVGElement>) => (
  <Svg {...p}><path d="M9 6h11M9 12h11M9 18h11" /><path d="m3.5 6 1.2 1.2L6.8 5M3.5 12l1.2 1.2L6.8 11M3.5 18l1.2 1.2L6.8 17" /></Svg>
)
export const IconWarehouse = (p: SVGProps<SVGSVGElement>) => (
  <Svg {...p}><path d="M3 10 12 4l9 6v10H3z" /><path d="M8 20v-6h8v6M8 17h8" /></Svg>
)
export const IconClock = (p: SVGProps<SVGSVGElement>) => (
  <Svg {...p}><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></Svg>
)
export const IconPlay = (p: SVGProps<SVGSVGElement>) => <Svg {...p}><path d="M7 5v14l11-7z" fill="currentColor" /></Svg>
export const IconPause = (p: SVGProps<SVGSVGElement>) => <Svg {...p}><path d="M8 5v14M16 5v14" strokeWidth="3" /></Svg>
export const IconStep = (p: SVGProps<SVGSVGElement>) => <Svg {...p}><path d="M5 5v14l9-7z" fill="currentColor" /><path d="M18 5v14" strokeWidth="2.6" /></Svg>
export const IconReset = (p: SVGProps<SVGSVGElement>) => <Svg {...p}><path d="M4 12a8 8 0 1 0 3-6.2" /><path d="M4 4v5h5" /></Svg>
export const IconChevronUp = (p: SVGProps<SVGSVGElement>) => <Svg {...p}><path d="m6 15 6-6 6 6" /></Svg>
export const IconChevronDown = (p: SVGProps<SVGSVGElement>) => <Svg {...p}><path d="m6 9 6 6 6-6" /></Svg>
export const IconFlask = (p: SVGProps<SVGSVGElement>) => <Svg {...p}><path d="M9 3h6M10 3v6l-5.5 9.5A1.5 1.5 0 0 0 5.8 21h12.4a1.5 1.5 0 0 0 1.3-2.5L14 9V3" /></Svg>
export const IconRefresh = (p: SVGProps<SVGSVGElement>) => <Svg {...p}><path d="M20 12a8 8 0 1 1-2.5-5.8" /><path d="M20 4v5h-5" /></Svg>

export const IconSettings = (p: SVGProps<SVGSVGElement>) => (
  <Svg {...p}><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z" /></Svg>
)
