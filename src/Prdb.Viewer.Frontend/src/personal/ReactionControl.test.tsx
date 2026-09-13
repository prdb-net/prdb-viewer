import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { ReactionControl } from './ReactionControl'

/// A Personal Reaction is one choice out of four, plus the absence of one. The control is asked
/// the things a reader depends on: that the four are named rather than drawn, that what is set is
/// readable without operating anything, that setting one says so, and that clearing is offered
/// only where there is something to clear — an absence is not a fifth choice.
describe('ReactionControl', () => {
  it('names all four and marks the one that is set', () => {
    render(<ReactionControl title="Some Video" value="Like" onChange={() => {}} />)

    for (const label of ['Dislike', 'Shrug', 'Like', 'Love']) {
      expect(screen.getByRole('radio', { name: label })).toBeInTheDocument()
    }
    expect(screen.getByRole('radio', { name: 'Like' })).toBeChecked()
    expect(screen.getByRole('radio', { name: 'Love' })).not.toBeChecked()
  })

  it('says which Video it belongs to', () => {
    render(<ReactionControl title="Some Video" value={null} onChange={() => {}} />)

    expect(screen.getByRole('group', { name: 'Your reaction to Some Video' })).toBeInTheDocument()
  })

  it('reports the reaction that was chosen', () => {
    const chosen: (string | null)[] = []
    render(<ReactionControl title="Some Video" value={null} onChange={(value) => chosen.push(value)} />)

    fireEvent.click(screen.getByRole('radio', { name: 'Love' }))

    expect(chosen).toEqual(['Love'])
  })

  it('offers clearing only once something is set, and reports it as an absence', () => {
    const chosen: (string | null)[] = []
    const { rerender } = render(
      <ReactionControl title="Some Video" value={null} onChange={(value) => chosen.push(value)} />,
    )
    expect(screen.queryByRole('button', { name: /Clear/ })).toBeNull()

    rerender(<ReactionControl title="Some Video" value="Shrug" onChange={(value) => chosen.push(value)} />)
    fireEvent.click(screen.getByRole('button', { name: 'Clear your reaction to Some Video' }))

    expect(chosen).toEqual([null])
  })

  it('captions itself only where there is room to', () => {
    const { rerender } = render(
      <ReactionControl title="Some Video" value={null} onChange={() => {}} />,
    )
    expect(screen.queryByText('Your reaction')).toBeNull()

    rerender(<ReactionControl title="Some Video" value={null} onChange={() => {}} size="large" />)
    expect(screen.getByText('Your reaction')).toBeInTheDocument()
  })

  it('cannot be operated while an action of its own is in flight', () => {
    render(<ReactionControl title="Some Video" value="Like" onChange={() => {}} disabled />)

    expect(screen.getByRole('radio', { name: 'Love' })).toBeDisabled()
    expect(screen.getByRole('button', { name: /Clear/ })).toBeDisabled()
  })
})
