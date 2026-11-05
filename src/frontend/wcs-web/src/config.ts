/** Demo Mode is selected at build/dev time with VITE_DEMO_MODE=true. Anything else keeps the real (API-backed) behaviour. */
export const DEMO_MODE: boolean = import.meta.env.VITE_DEMO_MODE === 'true'
