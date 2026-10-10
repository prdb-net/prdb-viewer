import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { CatalogueSiteFacts } from './CatalogueSiteFacts'

describe('retained Site facts', () => {
  it('renders catalogue text literally and only safe absolute links', () => {
    const { container } = render(<CatalogueSiteFacts site={{
      id: 'site', title: 'Creator', url: null, platformTitle: '<script>bad()</script>',
      accountHandle: 'alex', links: [
        { label: 'Safe', url: 'https://example.test/alex' },
        { label: 'Script', url: 'javascript:alert(1)' },
        { label: 'Relative', url: '/local' },
        { label: 'Credentials', url: 'https://secret:password@example.test' },
      ],
    }} />)
    expect(screen.getByText('Creator Platform: <script>bad()</script>')).toBeInTheDocument()
    expect(screen.getByText('Creator Account Handle: alex')).toBeInTheDocument()
    expect(screen.getAllByRole('link')).toHaveLength(1)
    expect(container.querySelector('script')).toBeNull()
  })
})
