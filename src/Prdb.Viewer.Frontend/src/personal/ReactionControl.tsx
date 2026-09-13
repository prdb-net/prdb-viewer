import { useId } from 'react'

import type { PersonalReaction } from '../api/client'

/// The four things a User can say about a Video, in the order of increasing interest, each with
/// what it means said in the words the reader gets rather than in the domain's.
export const reactions: { value: PersonalReaction, label: string, meaning: string }[] = [
  { value: 'Dislike', label: 'Dislike', meaning: 'Keeps it out of your recommendations' },
  { value: 'Shrug', label: 'Shrug', meaning: 'Seen it, no opinion' },
  { value: 'Like', label: 'Like', meaning: 'Show me more like this' },
  { value: 'Love', label: 'Love', meaning: 'A favourite to come back to' },
]

/// A Personal Reaction, read and set through the same four words.
///
/// They are words rather than glyphs. A thumb and a heart are read quickly by people who already
/// know what they mean here, and a shrug has no glyph anybody agrees on; four short labels are
/// read by everybody the first time, and by a screen reader without a second, hidden name that
/// has to be kept in agreement with the picture.
///
/// Underneath it is a radio group, because a reaction is one choice out of a fixed set. The
/// browser then supplies the keyboard for nothing — the arrow keys walk the four, and the group is
/// one tab stop rather than four — and clearing gets an action of its own, since having said
/// nothing is an absence rather than a fifth value and a Shrug is not it.
export function ReactionControl({ title, value, onChange, disabled = false, size = 'compact' }: {
  /// What is being reacted to, so the group says which Video it belongs to when it is read out.
  title: string
  value: PersonalReaction | null | undefined
  onChange: (reaction: PersonalReaction | null) => void
  disabled?: boolean
  size?: 'compact' | 'large'
}) {
  const group = useId()
  const chosen = value ?? null

  return (
    <div className={`reaction-control ${size}${disabled ? ' saving' : ''}`}>
      {size === 'large' && <span className="reaction-caption" aria-hidden="true">Your reaction</span>}
      <div className="reaction-row">
        <fieldset className="reaction-choices" disabled={disabled}>
          <legend className="visually-hidden">Your reaction to {title}</legend>
          {reactions.map((reaction) => (
            <label
              key={reaction.value}
              className={chosen === reaction.value ? `reaction on ${reaction.value.toLowerCase()}` : 'reaction'}
              title={reaction.meaning}
            >
              <input
                type="radio"
                name={group}
                value={reaction.value}
                checked={chosen === reaction.value}
                onChange={() => onChange(reaction.value)}
              />
              <span className="reaction-label">{reaction.label}</span>
            </label>
          ))}
        </fieldset>
        {chosen !== null && (
          <button
            type="button"
            className="clear-reaction"
            aria-label={`Clear your reaction to ${title}`}
            onClick={() => onChange(null)}
            disabled={disabled}
          >
            Clear
          </button>
        )}
      </div>
    </div>
  )
}
