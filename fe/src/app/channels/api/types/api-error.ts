/**
 * RFC 7807 validation problem returned by the channel endpoints. Shared because more than one
 * endpoint contract can return it.
 */
export interface ApiValidationProblem {
  readonly title?: string
  readonly status?: number
  readonly detail?: string
  readonly errors?: Readonly<Record<string, readonly string[]>>
}
