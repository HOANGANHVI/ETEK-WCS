import type { ApiError } from './types'

export class ApiRequestError extends Error {
  readonly code: string
  readonly status: number
  readonly details?: unknown
  constructor(status: number, error: ApiError) {
    super(error.message)
    this.name = 'ApiRequestError'
    this.status = status
    this.code = error.code
    this.details = error.details
  }
}
