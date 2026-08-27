import type { components } from './schema'

export type AccessState = components['schemas']['AccessStateResponse']
export type Account = components['schemas']['SignedInAccountResponse']
export type AccountSummary = components['schemas']['AccountSummary']
export type BootstrapRequest = components['schemas']['BootstrapRequest']
export type BootstrapResponse = components['schemas']['BootstrapResponse']
export type SignInRequest = components['schemas']['SignInRequest']
export type SignInResponse = components['schemas']['SignInResponse']
export type RegistrationRequest = components['schemas']['RegistrationRequest']
export type RegistrationResponse = components['schemas']['RegistrationRequestResponse']
export type RecoverRequest = components['schemas']['RecoverRequest']
export type RecoverResponse = components['schemas']['RecoverResponse']
export type RecoveryCodeResponse = components['schemas']['RecoveryCodeResponse']
export type AccountActionResponse = components['schemas']['AccountActionResponse']

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    credentials: 'same-origin',
    ...init,
    headers: {
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
  })

  if (!response.ok) {
    throw new Error(`Request failed with status ${response.status}.`)
  }

  return response.json() as Promise<T>
}

function post<T>(path: string, body?: unknown, csrfToken?: string) {
  return request<T>(path, {
    method: 'POST',
    body: body === undefined ? undefined : JSON.stringify(body),
    headers: csrfToken ? { 'X-CSRF-Token': csrfToken } : undefined,
  })
}

export const api = {
  state: () => request<AccessState>('/api/access/state'),
  me: () => request<Account>('/api/access/me'),
  bootstrap: (input: BootstrapRequest) => post<BootstrapResponse>('/api/access/bootstrap', input),
  signIn: (input: SignInRequest) => post<SignInResponse>('/api/access/sign-in', input),
  register: (input: RegistrationRequest) =>
    post<RegistrationResponse>('/api/access/registration-requests', input),
  recover: (input: RecoverRequest) => post<RecoverResponse>('/api/access/recover', input),
  accounts: () => request<AccountSummary[]>('/api/admin/accounts/'),
  approve: (accountId: string, csrfToken: string) =>
    post<AccountActionResponse>(`/api/admin/accounts/${accountId}/approve`, undefined, csrfToken),
  disable: (accountId: string, csrfToken: string) =>
    post<AccountActionResponse>(`/api/admin/accounts/${accountId}/disable`, undefined, csrfToken),
  recoveryCode: (accountId: string, csrfToken: string) =>
    post<RecoveryCodeResponse>(`/api/admin/accounts/${accountId}/recovery-code`, undefined, csrfToken),
  signOut: async (csrfToken: string) => {
    const response = await fetch('/api/access/sign-out', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'X-CSRF-Token': csrfToken },
    })

    if (!response.ok) {
      throw new Error(`Request failed with status ${response.status}.`)
    }
  },
}
