// Release metadata of THIS web bundle, embedded at build time by vite.config.ts (VERSION file + deploy build arguments). The only place that reads it.
declare const __APP_VERSION__: string
declare const __GIT_COMMIT__: string
declare const __BUILD_TIME__: string

export const BUILD = { version: __APP_VERSION__, gitCommit: __GIT_COMMIT__, buildTime: __BUILD_TIME__ } as const
