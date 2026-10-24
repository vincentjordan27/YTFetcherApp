/**
 * Login-screen copy and the user-readable failure messages. Every message here is non-technical: provider,
 * token, and HTTP details stay in the backend log.
 */
export const authCopy = {
  productName: 'YT Fetcher',
  login: {
    notice: 'Sign in with your Google account to continue.',
    submitLabel: 'Login with Google',
    submittingLabel: 'Signing in…',
    retryLabel: 'Try again',
  },
  errors: {
    signInFailed: 'Google sign-in could not be verified. Try again.',
    unavailable: 'Authentication is unavailable right now. Reload the page or try again in a moment.',
  },
} as const
