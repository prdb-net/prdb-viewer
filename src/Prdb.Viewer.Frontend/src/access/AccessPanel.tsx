import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import {
  api,
  type RecoverRequest,
  type RegistrationRequest,
  type SignInRequest,
} from '../api/client'
import { signInMessage } from '../lib/format'
import { queryKeys } from '../queryKeys'
import { Brand, CenteredCard, Field, Notice, RequestError, SubmitButton, Tab, submitting, values } from '../ui'

type AccessMode = 'sign-in' | 'register' | 'recover'

export function AccessPanel() {
  const [mode, setMode] = useState<AccessMode>('sign-in')
  const queryClient = useQueryClient()
  const signIn = useMutation({
    mutationFn: api.signIn,
    onSuccess: (result) => {
      if (result.account) {
        queryClient.setQueryData(queryKeys.account, result.account)
        queryClient.setQueryData(queryKeys.state, { claimed: true, signedIn: true })
      }
    },
  })
  const register = useMutation({ mutationFn: api.register })
  const recover = useMutation({ mutationFn: api.recover })

  return (
    <CenteredCard>
      <Brand />
      <div className="tabs" aria-label="Account access">
        <Tab active={mode === 'sign-in'} onClick={() => setMode('sign-in')}>Sign in</Tab>
        <Tab active={mode === 'register'} onClick={() => setMode('register')}>Request access</Tab>
        <Tab active={mode === 'recover'} onClick={() => setMode('recover')}>Recover</Tab>
      </div>

      {mode === 'sign-in' && (
        <form onSubmit={submitting((form) => signIn.mutate(
          values<SignInRequest>(form, ['username', 'password']),
        ))}>
          <Field name="username" label="Username" autoComplete="username" required />
          <Field name="password" label="Password" type="password" autoComplete="current-password" required />
          <SubmitButton pending={signIn.isPending}>Sign in</SubmitButton>
          {signIn.data && !signIn.data.account && <Notice kind="error">{signInMessage(signIn.data.verdict)}</Notice>}
          {signIn.isError && <RequestError error={signIn.error} />}
        </form>
      )}

      {mode === 'register' && (
        <form onSubmit={submitting((form) => register.mutate(
          values<RegistrationRequest>(form, ['username', 'password', 'email']),
        ))}>
          <p>Ask an Administrator to approve your request after submitting it.</p>
          <Field name="username" label="Username" autoComplete="username" required />
          <Field name="email" label="Email (optional)" type="email" autoComplete="email" />
          <Field name="password" label="Password" type="password" autoComplete="new-password" minLength={12} required />
          <SubmitButton pending={register.isPending}>Submit request</SubmitButton>
          {register.data?.verdict === 'Submitted' && <Notice kind="success">Request submitted. Access begins only after approval.</Notice>}
          {register.data?.verdict === 'InvalidInput' && <Notice kind="error">Check the username, email, and password.</Notice>}
          {register.isError && <RequestError error={register.error} />}
        </form>
      )}

      {mode === 'recover' && (
        <form onSubmit={submitting((form) => recover.mutate(
          values<RecoverRequest>(form, ['username', 'recoveryCode', 'newPassword']),
        ))}>
          <Field name="username" label="Username" autoComplete="username" required />
          <Field name="recoveryCode" label="Recovery code" autoComplete="off" required />
          <Field name="newPassword" label="New password" type="password" autoComplete="new-password" minLength={12} required />
          <SubmitButton pending={recover.isPending}>Replace password</SubmitButton>
          {recover.data?.verdict === 'PasswordReplaced' && <Notice kind="success">Password replaced. You can now sign in.</Notice>}
          {recover.data && recover.data.verdict !== 'PasswordReplaced' && <Notice kind="error">The recovery code or account details are invalid.</Notice>}
          {recover.isError && <RequestError error={recover.error} />}
        </form>
      )}
    </CenteredCard>
  )
}
