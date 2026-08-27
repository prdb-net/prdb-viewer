import { render, screen } from '@testing-library/react'

import { App } from './App'

describe('App', () => {
  it('presents the running application shell', () => {
    render(<App />)

    expect(screen.getByRole('heading', { name: 'prdb-viewer' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Walking Skeleton online')
  })
})
