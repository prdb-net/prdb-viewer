import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { RemoteCatalogueNotice } from './RemoteCatalogueNotice'

describe('remote catalogue observations', () => {
  it('explains a merge without offering a local merge or external request', () => {
    render(<RemoteCatalogueNotice status={{ state: 'Merged', prdbVideoId: 'old',
      updatedAt: '2026-10-01T00:00:00Z', deletedAt: '2026-10-01T00:00:00Z', mergedIntoId: 'survivor' }} />)
    expect(screen.getByText(/Local files, identification and personal viewing state remain intact/)).toBeInTheDocument()
    expect(screen.getByText('survivor')).toBeInTheDocument()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })
  it('names a withdrawn proposal as stale', () => {
    render(<RemoteCatalogueNotice proposed status={{ state: 'Deleted', prdbVideoId: 'old',
      updatedAt: null, deletedAt: null, mergedIntoId: null }} />)
    expect(screen.getByText(/This proposal is stale and cannot be accepted/)).toBeInTheDocument()
  })
})
