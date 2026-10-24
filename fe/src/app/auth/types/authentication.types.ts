/** Resolved authentication state for the whole application. */
export type AuthenticationBootstrap =
  | 'pending'
  | 'enabled'
  | 'disabled'
  /** Configuration could not be read, so the deployment mode is unknown and protected routes must not render. */
  | 'failed'
