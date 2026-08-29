import { useMutation, useQueryClient } from '@tanstack/react-query'

import { api, type BootstrapRequest } from '../api/client'
import { bootstrapMessage } from '../lib/format'
import { queryKeys } from '../queryKeys'
import { Brand, CenteredCard, Field, Notice, RequestError, SubmitButton, submitting, values } from '../ui'

export function BootstrapPanel() {
  const queryClient = useQueryClient()
  const mutation = useMutation({
    mutationFn: api.bootstrap,
    onSuccess: (result) => {
      if (result.account) {
        queryClient.setQueryData(queryKeys.account, result.account)
        queryClient.setQueryData(queryKeys.state, { claimed: true, signedIn: true })
      }
    },
  })

  return (
    <CenteredCard>
      <Brand />
      <h2>Claim this installation</h2>
      <p>Use the one-time authorization written by the operator command, then create the first Administrator.</p>
      <form onSubmit={submitting((form) => mutation.mutate(
        values<BootstrapRequest>(form, ['authorization', 'username', 'password', 'email']),
      ))}>
        <Field name="authorization" label="One-time authorization" autoComplete="off" required />
        <Field name="username" label="Administrator username" autoComplete="username" required />
        <Field name="email" label="Email (optional)" type="email" autoComplete="email" />
        <Field name="password" label="Password" type="password" autoComplete="new-password" minLength={12} required />
        <SubmitButton pending={mutation.isPending}>Create Administrator</SubmitButton>
      </form>
      {mutation.data && !mutation.data.account && (
        <Notice kind="error">{bootstrapMessage(mutation.data.verdict)}</Notice>
      )}
      {mutation.isError && <RequestError error={mutation.error} />}
    </CenteredCard>
  )
}
