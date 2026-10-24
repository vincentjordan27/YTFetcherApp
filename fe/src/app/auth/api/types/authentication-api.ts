/** The identity returned by the backend; its session token remains in an HttpOnly cookie. */

export interface AuthenticationIdentityApi {
  readonly userId: string
  readonly email: string
  readonly displayName: string | null
  readonly role: string
}

export interface CurrentSessionApiResponse {
  readonly authenticationEnabled: boolean
  readonly authenticated: boolean
  readonly identity: AuthenticationIdentityApi | null
}

export interface CreateAuthenticationSessionApiRequest {
  readonly firebaseIdToken: string
}
