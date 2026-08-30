import { fireEvent, screen, within } from '@testing-library/react'

import { json, renderApp, signedInAs } from '../test/fixtures'

/// What an Administrator can do about who reaches this installation.
describe('Accounts', () => {
  afterEach(() => vi.restoreAllMocks())

  it('offers a disabled Account a way back, and says how anyone asks for access', async () => {
    signedInAs('Administrator', (input) => {
      if (input === '/api/admin/accounts/') {
        return json([
          account({ username: 'administrator', authority: 'Administrator' }),
          account({
            id: '01994dd4-2a0a-7000-8000-0000000000b2',
            username: 'former-user',
            state: 'Disabled',
          }),
        ])
      }
      return undefined
    })

    renderApp('/admin/accounts')

    // The heading renders before the list answers, so the row is what to wait for.
    const disabled = (await screen.findByText('former-user')).closest('article')!

    // Disabling was a one-way door: approval needs a waiting request, which a disabled Account
    // does not have.
    expect(within(disabled).getByRole('button', { name: 'Reinstate' })).toBeInTheDocument()
    expect(within(disabled).queryByRole('button', { name: 'Disable' })).not.toBeInTheDocument()

    // And the screen listing Accounts says how a second person comes to be on it.
    expect(screen.getByText(/Request access/)).toBeInTheDocument()
  })

  it('puts the request waiting for a decision first, in words rather than in the enum', async () => {
    signedInAs('Administrator', (input) => {
      if (input === '/api/admin/accounts/') {
        return json([
          account({ username: 'administrator', authority: 'Administrator' }),
          account({
            id: '01994dd4-2a0a-7000-8000-0000000000b4',
            username: 'applicant',
            state: 'PendingApproval',
            approvedAt: null,
          }),
        ])
      }
      return undefined
    })

    renderApp('/admin/accounts')

    // The heading counts the requests waiting, so the row it counts is the row to open the screen
    // on rather than wherever the list happened to put it.
    await screen.findByText('applicant')
    const rows = document.querySelectorAll('.account-row strong')
    expect([...rows].map((row) => row.textContent)).toEqual(['applicant', 'administrator'])

    // And the state is said rather than spelled the way the database spells it.
    expect(screen.getByText(/Waiting for approval/)).toBeInTheDocument()
    expect(screen.queryByText(/PendingApproval/)).not.toBeInTheDocument()
  })

  it('refuses to leave the installation without an Administrator, in as many words', async () => {
    signedInAs('Administrator', (input) => {
      if (input === '/api/admin/accounts/') {
        return json([
          account({ username: 'administrator', authority: 'Administrator' }),
          account({
            id: '01994dd4-2a0a-7000-8000-0000000000b3',
            username: 'second-administrator',
            authority: 'Administrator',
          }),
        ])
      }
      if (typeof input === 'string' && input.endsWith('/disable')) {
        return json({ verdict: 'LastAdministrator' })
      }
      return undefined
    })

    renderApp('/admin/accounts')

    const other = (await screen.findByText('second-administrator')).closest('article')!
    fireEvent.click(within(other).getByRole('button', { name: 'Disable' }))

    expect(await screen.findByText(/only approved Administrator/)).toBeInTheDocument()
  })
})

function account(overrides: Record<string, unknown> = {}) {
  return {
    id: '01994dd4-2a0a-7000-8000-000000000001',
    username: 'someone',
    email: null,
    authority: 'User',
    state: 'Approved',
    requestedAt: '2026-08-29T10:00:00Z',
    approvedAt: '2026-08-29T10:05:00Z',
    disabledAt: null,
    ...overrides,
  }
}
