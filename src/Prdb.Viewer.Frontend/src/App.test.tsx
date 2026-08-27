import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'

import { App } from './App'

function renderApp() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={queryClient}><App /></QueryClientProvider>)
}

function json(body: unknown) {
  return Promise.resolve(new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  }))
}

describe('App', () => {
  afterEach(() => vi.restoreAllMocks())

  it('guides an unclaimed installation to create its first Administrator', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      if (input === '/api/access/state') return json({ claimed: false, signedIn: false })
      return json({
        verdict: 'Created',
        account: {
          id: '01994dd4-2a0a-7000-8000-000000000001',
          username: 'administrator',
          email: null,
          authority: 'Administrator',
          csrfToken: 'csrf-token',
        },
      })
    })

    renderApp()
    expect(await screen.findByRole('heading', { name: 'Claim this installation' })).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('One-time authorization'), { target: { value: 'authorization' } })
    fireEvent.change(screen.getByLabelText('Administrator username'), { target: { value: 'administrator' } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'administrator password' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create Administrator' }))

    expect(await screen.findByRole('heading', { name: 'Your collection starts here' })).toBeInTheDocument()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      '/api/access/bootstrap',
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('offers sign-in and an approval-gated registration request', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      if (input === '/api/access/state') return json({ claimed: true, signedIn: false })
      return json({ verdict: 'Submitted' })
    })

    renderApp()
    fireEvent.click(await screen.findByRole('button', { name: 'Request access' }))
    fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'viewer' } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'a secure password' } })
    fireEvent.click(screen.getByRole('button', { name: 'Submit request' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Access begins only after approval.')
  })
})
