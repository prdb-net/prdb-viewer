import { useId, useState } from 'react'

/// The scores a Personal Rating can take. The domain fixes the scale at one to five, and the
/// control shows the whole of it at once so the scale is read rather than remembered.
const scores = [1, 2, 3, 4, 5]

/// A Personal Rating, read and set through the same picture.
///
/// A dropdown made reading a rating cost a click: a shelf of sixty cards showed sixty controls
/// that look like something to operate, and what each of them said had to be opened to be seen.
/// Five stars are read across a grid at a glance, and the same five are what a rating is set
/// with, so there is no separate reading form and setting form of a rating to keep in agreement.
///
/// Underneath it is a radio group rather than five buttons, because that is what a rating is: one
/// choice out of a fixed scale. The browser then supplies the keyboard for nothing — arrow keys
/// walk the scale, and the group is one stop rather than five — and clearing gets an action that
/// says so, since "not rated" is an absence rather than a sixth score.
export function StarRating({ title, value, onChange, disabled = false, size = 'compact' }: {
  /// What is being rated, so the group says which Video it belongs to when it is read out.
  title: string
  /// The Personal Rating as the catalogue states it, which is a wide integer on the wire.
  value: number | string | null | undefined
  onChange: (score: number | null) => void
  disabled?: boolean
  size?: 'compact' | 'large'
}) {
  // What the pointer is about to choose. Showing it before the click lands is what makes the
  // scale legible without operating it; the rating itself is untouched until the click lands.
  const [preview, setPreview] = useState<number>()
  const group = useId()
  const score = value === null || value === undefined ? null : Number(value)
  const filled = preview ?? score ?? 0

  return (
    <div
      className={`star-rating ${size}${disabled ? ' saving' : ''}`}
      onMouseLeave={() => setPreview(undefined)}
    >
      {size === 'large' && <span className="rating-caption" aria-hidden="true">Personal Rating</span>}
      <div className="rating-row">
        <fieldset className="stars" disabled={disabled}>
          <legend className="visually-hidden">Personal Rating for {title}</legend>
          {scores.map((rating) => (
            <label
              key={rating}
              className={rating <= filled ? 'star on' : 'star'}
              onMouseEnter={() => { if (!disabled) setPreview(rating) }}
            >
              <input
                type="radio"
                name={group}
                value={rating}
                checked={score === rating}
                onChange={() => onChange(rating)}
              />
              <span className="glyph" aria-hidden="true">★</span>
              <span className="visually-hidden">{rating} of 5</span>
            </label>
          ))}
        </fieldset>
        {size === 'large' && (
          <span className="rating-readout" aria-hidden="true">
            {filled > 0 ? `${filled} of 5` : 'Not rated'}
          </span>
        )}
        {score !== null && (
          <button
            type="button"
            className="clear-rating"
            onClick={() => onChange(null)}
            disabled={disabled}
          >
            Clear<span className="visually-hidden"> the Personal Rating of {title}</span>
          </button>
        )}
      </div>
    </div>
  )
}
