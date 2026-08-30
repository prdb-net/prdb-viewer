import { fireEvent, render, screen } from '@testing-library/react'

import { StarRating } from './StarRating'

/// A Personal Rating is a choice out of a fixed scale, so the control is asked the things a
/// choice out of a scale is asked: what it currently says, what a click changes it to, and how
/// the absence of a rating is reached. The stars themselves are decoration over that.

describe('StarRating', () => {
  it('states the score it holds without being opened', () => {
    render(<StarRating title="Some Video" value={3} onChange={() => {}} />)

    expect(screen.getByRole('radio', { name: '3 of 5' })).toBeChecked()
    expect(screen.getByRole('radio', { name: '4 of 5' })).not.toBeChecked()
  })

  it('reads a wide integer from the wire as the score it is', () => {
    render(<StarRating title="Some Video" value="5" onChange={() => {}} />)

    expect(screen.getByRole('radio', { name: '5 of 5' })).toBeChecked()
  })

  it('sets the score that was chosen', () => {
    const chosen: (number | null)[] = []
    render(<StarRating title="Some Video" value={null} onChange={(score) => chosen.push(score)} />)

    fireEvent.click(screen.getByRole('radio', { name: '4 of 5' }))

    expect(chosen).toEqual([4])
  })

  it('offers clearing only where there is a rating to clear', () => {
    const chosen: (number | null)[] = []
    const { rerender } = render(
      <StarRating title="Some Video" value={null} onChange={(score) => chosen.push(score)} />,
    )
    expect(screen.queryByRole('button')).toBeNull()

    rerender(<StarRating title="Some Video" value={2} onChange={(score) => chosen.push(score)} />)
    fireEvent.click(screen.getByRole('button'))

    expect(chosen).toEqual([null])
  })

  it('says a Video is unrated rather than showing a score of nothing', () => {
    const { rerender } = render(
      <StarRating title="Some Video" value={null} onChange={() => {}} size="large" />,
    )
    expect(screen.getByText('Not rated')).toBeInTheDocument()

    rerender(<StarRating title="Some Video" value={2} onChange={() => {}} size="large" />)
    expect(screen.queryByText('Not rated')).toBeNull()
  })
})
