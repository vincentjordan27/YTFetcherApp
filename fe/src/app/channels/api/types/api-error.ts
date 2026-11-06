export interface ApiValidationProblem {
  readonly title?: string
  readonly status?: number
  readonly detail?: string
  readonly errors?: Readonly<Record<string, readonly string[]>>
}
