/** Demo Mode is selected at build/dev time with VITE_DEMO_MODE=true. Anything else keeps the real (API-backed) behaviour. */
export const DEMO_MODE: boolean = import.meta.env.VITE_DEMO_MODE === 'true'

/**
 * Engineering tools (simulator controls, PLC test-write) exist only in Development builds (`npm run dev`) or when explicitly enabled with
 * VITE_ENABLE_ENGINEERING_TOOLS=true. A production build (`npm run build`) never shows them; the API refuses them in the Production environment too.
 */
export const ENGINEERING_TOOLS: boolean = import.meta.env.DEV || import.meta.env.VITE_ENABLE_ENGINEERING_TOOLS === 'true'
