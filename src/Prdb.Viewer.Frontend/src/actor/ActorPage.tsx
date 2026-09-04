import { useQuery } from '@tanstack/react-query'
import { Link, useParams, useSearchParams } from 'react-router'

import { api, type Account, type ActorDetail } from '../api/client'
import { formatDay } from '../lib/format'
import { usePersonalActions } from '../personal/usePersonalActions'
import { queryKeys } from '../queryKeys'
import { HeartIcon, PageHeading, RequestError } from '../ui'
import { VideoGrid } from '../video/VideoCard'
import { returnTo } from '../lib/returnTo'
import { useFavouriteActor } from './useFavouriteActor'

/// One Actor, addressed rather than found.
///
/// Somebody opens an Actor to decide what to watch, so the Videos this library holds them in are
/// the body of the page and every one of them plays from here. What prdb knows sits beside them:
/// stated where it is known and left out where it is not, because an Actor prdb holds a name and
/// four fields for must not look like a page that failed to load.
export function ActorPage({ account }: { account: Account }) {
  const { actorId = '' } = useParams()
  const [parameters] = useSearchParams()
  const detail = useQuery({
    queryKey: queryKeys.actor(actorId),
    queryFn: () => api.actor(actorId),
    retry: false,
  })
  const personal = usePersonalActions(account)
  const favourite = useFavouriteActor(account)
  const back = returnTo(parameters) ?? { to: '/actors', label: 'the Actors' }
  const actor = detail.data

  if (detail.isPending) {
    return <p role="status">Opening this Actor…</p>
  }

  if (detail.isError || !actor) {
    return (
      <>
        <PageHeading title="This Actor is not here">
          No Video in this library credits them, or the link may be wrong.
        </PageHeading>
        <Link className="quiet-button" to={back.to}>Back to {back.label}</Link>
      </>
    )
  }

  const portrait = actor.images[0]
  const facts = factsOf(actor)
  const totalVideos = Number(actor.totalVideos)
  const offeredImages = Number(actor.offeredImageCount)
  // The Library's Actor facet is keyed by the name the Videos use, which is not always the one
  // prdb leads with, so a link into the Library carries the names this library credits.
  const inTheLibrary = `/?${new URLSearchParams(
    actor.creditedNames.map((name) => ['actors', name]),
  ).toString()}`

  return (
    <>
      <PageHeading
        eyebrow="Actor"
        title={actor.name}
        actions={
          <>
            <button
              className={actor.favourite ? 'quiet-button selected' : 'quiet-button'}
              aria-pressed={actor.favourite}
              onClick={() => favourite.act(actor.actorId, !actor.favourite)}
              disabled={favourite.pending(actor.actorId)}
            >
              <HeartIcon /> {actor.favourite ? 'Favourite' : 'Make a Favourite'}
            </button>
            <Link className="quiet-button" to={back.to}>Back to {back.label}</Link>
          </>
        }
      />

      <div className="actor-detail">
        <div className="actor-portrait">
          {portrait
            ? <img src={portrait.url} alt="" />
            : <div className="actor-placeholder" aria-hidden="true">☺</div>}
          {!portrait && (
            <small className="muted">
              {actor.profileState === 'Pending'
                ? 'Their pictures have not arrived yet.'
                : 'prdb offers no picture of them.'}
            </small>
          )}
        </div>

        <div className="actor-facts">
          {facts.length > 0 && (
            <dl className="fact-list">
              {facts.map((fact) => (
                <div key={fact.label}><dt>{fact.label}</dt><dd>{fact.value}</dd></div>
              ))}
            </dl>
          )}

          {facts.length === 0 && (
            <p className="muted">
              {actor.profileState === 'Pending'
                ? 'What prdb knows about them has not arrived yet. Their Videos are below.'
                : 'prdb holds nothing about them beyond their name. Their Videos are below.'}
            </p>
          )}

          {actor.aliases.length > 0 && (
            <p className="actor-aliases">
              <span className="muted">Also credited as</span> {actor.aliases.join(', ')}
            </p>
          )}

          {actor.links.length > 0 && (
            <ul className="actor-links">
              {actor.links.map((link) => (
                <li key={link.url}>
                  {/* The one place on this page that leaves it, and marked as leaving. */}
                  <a href={link.url} target="_blank" rel="noreferrer noopener">
                    {link.siteLabel ?? 'Link'} ↗
                  </a>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>

      {actor.bios.length > 0 && (
        <section className="actor-bios">
          {actor.bios.map((bio, index) => <p key={index}>{bio}</p>)}
        </section>
      )}

      {actor.images.length > 1 && (
        <section className="actor-gallery" aria-labelledby="actor-gallery-title">
          <div className="section-heading">
            <h3 id="actor-gallery-title">Pictures</h3>
            <span className="muted">
              {offeredImages > actor.images.length
                ? `${actor.images.length} of ${offeredImages} prdb offers`
                : actor.images.length}
            </span>
          </div>
          <div className="actor-gallery-grid">
            {actor.images.slice(1).map((image) => (
              <img key={image.url} src={image.url} alt="" loading="lazy" />
            ))}
          </div>
        </section>
      )}

      <section className="actor-videos" aria-labelledby="actor-videos-title">
        <div className="section-heading">
          <h3 id="actor-videos-title">Videos here</h3>
          <span className="muted">{actor.totalVideos}</span>
        </div>

        {actor.videos.length === 0
          ? (
            <p className="muted">
              Nothing in this library credits them any more.
            </p>
            )
          : (
            <VideoGrid
              videos={actor.videos}
              act={personal.act}
              pending={personal.pending}
              from={`/actors/${actor.actorId}`}
            />
            )}

        {totalVideos > actor.videos.length && (
          <p className="actor-more">
            <Link to={inTheLibrary}>
              All {totalVideos} in the Library, where they can be searched and narrowed
            </Link>
          </p>
        )}
      </section>

      {(personal.failed || favourite.failed) && (
        <RequestError error={personal.error ?? favourite.error} />
      )}
    </>
  )
}

/// What prdb says, in the order somebody reads a person: who they are, then what they look like,
/// then what their career has been. Nothing is stated where nothing is known — a page of "Unknown"
/// says less than a shorter page.
function factsOf(actor: ActorDetail) {
  const facts: { label: string; value: string }[] = []
  const add = (label: string, value: string | null | undefined) => {
    if (value) facts.push({ label, value })
  }

  add('Gender', actor.genderLabel)
  add('Born', born(actor))
  add('Died', actor.deathday ? formatDay(actor.deathday) : null)
  add('Birthplace', actor.birthplace)
  add('Nationality', actor.nationalityLabel)
  add('Ethnicity', actor.ethnicityLabel)
  add('Hair', actor.haircolourLabel)
  add('Eyes', actor.eyecolourLabel)
  add('Height', actor.heightCentimetres ? `${actor.heightCentimetres} cm` : null)
  add('Measurements', measurements(actor))
  add('Breasts', actor.breastTypeLabel)
  add('Career', career(actor))
  add('Tattoos', actor.tattoos)
  add('Piercings', actor.piercings)
  return facts
}

/// A birthday prdb knows only the year of is not a date, and printing it as one would invent a day
/// and a month. The precision it sends is what decides how much of it is said.
function born(actor: ActorDetail) {
  if (!actor.birthday) return null
  const exact = (actor.birthdayPrecisionLabel ?? '').toLowerCase().startsWith('exact')
  const day = formatDay(actor.birthday)
  if (exact || !actor.birthdayPrecisionLabel) return day
  return `${day} (${actor.birthdayPrecisionLabel.toLowerCase()})`
}

function measurements(actor: ActorDetail) {
  const parts = [
    actor.braSizeLabel,
    actor.waistCentimetres ? `${actor.waistCentimetres}` : null,
    actor.hipCentimetres ? `${actor.hipCentimetres}` : null,
  ]
  return parts.every((part) => !part) ? null : parts.map((part) => part ?? '—').join(' · ')
}

function career(actor: ActorDetail) {
  if (!actor.careerStart && !actor.careerEnd) return null
  if (actor.careerStart && actor.careerEnd) return `${actor.careerStart}–${actor.careerEnd}`
  return actor.careerStart ? `Since ${actor.careerStart}` : `Until ${actor.careerEnd}`
}
