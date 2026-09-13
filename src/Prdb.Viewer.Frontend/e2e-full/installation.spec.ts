import { expect, test, type Locator, type Page } from '@playwright/test'

import { ADMINISTRATOR } from './installation'

/// The screens, against a real installation.
///
/// Everything here is answered by the product: a SQLite database the `seed` command filled, six
/// lanes that have run twice over five real video files, and a stand-in prdb that recognised three
/// of them by content, a fourth by name and had never heard of the fifth. Nothing is pinned by the
/// test, so a screen that reads
/// a field the API does not send, or sends a filter the API ignores, fails here — and only here.
///
/// A signed-in Administrator is the state every case starts from, because it is the only one that
/// can see all of these screens.
test.beforeEach(async ({ page }) => {
  await signIn(page)
})

test('the browsing screen shows what the lanes established', async ({ page }) => {
  // Three of the four files were matched on content, which is evidence enough to file a Work.
  // The fourth was matched by name, which is not, so it is still shown under the name it was
  // found under rather than under the title the catalogue offered.
  //
  // Sorted, because the order Videos are listed in is a separate question with its own rules,
  // and pinning it here would make this fail for a reason it is not about.
  const titles = await page.locator('.video-title').allTextContents()
  expect(titles.sort()).toEqual([
    'The First Film',
    'The Second Film',
    'The Third Film',
    'fourth-film',
  ])
})

test('a facet row is drawn from the library the server actually holds', async ({ page }) => {
  await openFilters(page)

  // Counts, not just names. A facet row that draws the right labels over the wrong numbers is a
  // screen that lies quietly, and no test that pins its own answers can see it.
  //
  // Second Example Studio holds two: the file whose Work is unestablished still has its Site
  // established. Recognising where something came from is a separate claim from deciding what it
  // is, and it is the one a path can carry on its own.
  await expect(facets(page, 'Sites')).toHaveText([
    'Example Pictures (2)',
    'Second Example Studio (2)',
  ])
  await expect(facets(page, 'Actors')).toHaveText([
    'Alex Doe (2)',
    'Jules Poe (1)',
    'Sam Roe (1)',
  ])

  // The bands the four files were encoded at, best first, as the server projected them. Nothing
  // here is pinned by the test: `ffmpeg` wrote the pictures, `ffprobe` read them back, and the
  // projection banded them.
  await expect(facets(page, 'Quality')).toHaveText([
    '1080p (1)',
    '720p (1)',
    'SD (2)',
  ])
})

test('what a card says a Video is worth watching at survives the round trip', async ({ page }) => {
  // The shape of every display defect this project has shipped: a screen reading a field the API
  // does not send. The badge is drawn from a band the Core derived and the contract carries, so a
  // field that stops arriving shows up here as an empty corner rather than as a passing test.
  await expect(page.locator('.quality-badge')).toHaveCount(4)
  expect((await page.locator('.quality-badge').allTextContents()).sort())
    .toEqual(['1080p', '720p', 'SD', 'SD'])
})

test('choosing a quality band narrows the list at the server', async ({ page }) => {
  await openFilters(page)
  await page.getByRole('button', { name: 'SD (2)' }).click()

  await expect(page.locator('.video-card')).toHaveCount(2)
  expect(new URL(page.url()).searchParams.get('quality')).toBe('StandardDefinition')
  // The toolbar's count. The open sheet carries the same number at its foot, which is the point
  // of it — the reader narrowing sees what it leaves without closing the sheet to look.
  await expect(page.locator('.library-toolbar .result-count')).toHaveText('2 matching')

  // And ordering by it is the server's answer too, not a sort the screen did to a page.
  await page.goto('/?sort=QualityDescending')
  await page.locator('.video-card').first().waitFor()
  expect((await page.locator('.quality-badge').allTextContents()))
    .toEqual(['1080p', '720p', 'SD', 'SD'])
})

test('choosing a Site narrows the list at the server', async ({ page }) => {
  await openFilters(page)
  await page.getByRole('button', { name: 'Example Pictures (2)' }).click()

  await expect(page.locator('.video-title')).toHaveText(['The Second Film', 'The First Film'])
  expect(new URL(page.url()).searchParams.get('sites')).toBe('Example Pictures')

  // The heading counts the whole match rather than the page, and it is the server's number.
  await expect(page.locator('.library-toolbar .result-count')).toHaveText('2 matching')
})

test('the address a choice wrote is enough to reproduce the screen', async ({ page }) => {
  // ADR 0004, end to end: the browser is given only the address, and the server has to narrow the
  // library the same way it did when the choice was made.
  await page.goto('/?actors=Alex+Doe')
  await page.locator('.video-card').first().waitFor()

  await expect(page.locator('.video-title')).toHaveText(['The Second Film', 'The First Film'])

  await openFilters(page)
  await expect(page.locator('button.facet[aria-pressed="true"]')).toHaveText(['Alex Doe (2)'])
})

test('a filter nothing matches says so rather than showing everything', async ({ page }) => {
  await page.goto('/?sites=No+Such+Site')
  await page.getByText('Nothing matches').waitFor()

  await expect(page.locator('.video-card')).toHaveCount(0)
})

/// The screen the `0/3` defect lived on, over lanes that have genuinely run.
///
/// The seed scans twice on purpose, so the lanes are seen in the state an installation sits in
/// almost all of the time: a run over a library that has not changed. What that leaves is not the
/// same for each of them, which is the interesting part and what the sentences below record.
test('every lane reads as finished rather than as a bare ratio', async ({ page }) => {
  await page.goto('/admin/work')
  await page.locator('.work-row').first().waitFor()

  const rows = page.locator('.work-row')
  await expect(rows).not.toHaveCount(0)

  for (const row of await rows.all()) {
    const text = (await row.textContent()) ?? ''
    expect(text).toContain('Completed')
    // `0/3` was what a finished Library Scan used to say. Any bare ratio here is the same defect
    // wearing different numbers.
    expect(text).not.toMatch(/\d+\s*\/\s*\d+/)
  }

  // What each lane settled on, named rather than counted, because the sentence is the thing the
  // `0/3` defect got wrong and because the six differ from each other for reasons worth pinning.
  const settled: Record<string, string> = {
    'Library Scan': '5 files found',
    // Inspection re-reads every file on a new scan: a file can change under a name that did not.
    'Technical Inspection': '5 files done',
    // These three had their answers already, and nothing about the files changed, so a second
    // scan gives them nothing to do.
    'Hashing': 'nothing to do',
    'Preview Generation': 'nothing to do',
    'Site Recognition': 'nothing to do',
    // A file is compared against the rest of the library once, for the hash value it carries, so
    // a second scan over an unchanged library finds nothing outstanding to compare.
    'Perceptual Neighbourhood': 'nothing to do',
    // One file's Work is still unestablished, and a new run asks prdb about those again because
    // the catalogue may have learned about them since. The other three are settled and are not
    // re-offered.
    'Identification': '1 file done',
  }

  for (const [lane, reads] of Object.entries(settled)) {
    await expect(page.locator('.work-row', { hasText: lane })).toContainText(reads)
  }
})

test('the file prdb matched by name is waiting in the review queue', async ({ page }) => {
  await page.goto('/admin/identification')
  await page.locator('.review-group').first().waitFor()

  // A name is not evidence enough to file a Work without a person agreeing to it, however sure
  // the catalogue sounded. It is one question, so the backlog holds one group.
  await expect(page.locator('.review-group')).toHaveCount(1)
  await expect(page.locator('.review-group')).toContainText('The Fourth Film')
  // And a group says what answering it would settle rather than leaving a count to stand for it.
  await expect(page.locator('.review-group')).toContainText('1 case')
})

/// The theme's whole point, against a real installation: prdb had never heard of the fifth file,
/// and the library placed it anyway because it looks like one prdb had already identified.
test('a second encode prdb never saw becomes part of the Video it looks like', async ({ page }) => {
  await page.goto('/?query=Third')
  await page.locator('.video-card').first().waitFor()
  await page.locator('.video-title', { hasText: 'The Third Film' }).click()

  // Two occurrences under one identity, one of which the catalogue answered nothing about: the
  // 720p H.264 prdb matched on content, and the VP8 re-encode it has never seen.
  await expect(page.locator('.variant-list li')).toHaveCount(2)
  await expect(page.locator('.variant-list')).toContainText('h264')
  await expect(page.locator('.variant-list')).toContainText('vp8')

  // And the library can account for it: an association names no work, so what it explains is the
  // merge rather than the identity.
  await expect(page.locator('.work-association')).toContainText('same content')
})

test('the Installation screen reports the connection it actually made', async ({ page }) => {
  await page.goto('/admin/setup')

  await expect(page.getByText('Verified', { exact: true })).toBeVisible()
  // The library the seed activated, mounted where the container was told to look.
  await expect(page.getByText('/libraries', { exact: true })).toBeVisible()
})

test('an Actor is opened from a Video, played from, kept, and found again', async ({ page }) => {
  // The whole contract this effort added, end to end against a real installation: the identity
  // the catalogue sent, the profile a lane fetched, the pictures another lane brought in, and the
  // Personal State a third screen wrote.
  await page.getByRole('link', { name: 'The First Film' }).click()
  const credit = page.locator('.actor-credits a').first()
  const name = (await credit.textContent())!.trim()
  await credit.click()

  await expect(page.getByRole('heading', { name })).toBeVisible()

  // Their Videos are the body of the page, and every picture on it is served from here.
  await expect(page.locator('.video-card').first()).toBeVisible()
  for (const source of await page.locator('.actor-detail img, .actor-gallery-grid img')
    .evaluateAll((images) => images.map((image) => image.getAttribute('src')))) {
    expect(source).toMatch(/^\/media\/actors\//)
  }

  // What prdb knows, held locally: the stand-in describes this Actor fully.
  await expect(page.locator('.actor-facts')).toContainText('Example City')

  await page.getByRole('button', { name: 'Make a Favourite' }).click()
  // The Actor's own control, not the heart on one of their Video cards.
  await expect(page.locator('.page-heading').getByRole('button', { name: 'Favourite' }))
    .toHaveAttribute('aria-pressed', 'true')

  // And the index knows it, from the server rather than from what the last screen remembered.
  await page.goto('/actors')
  const card = page.locator('.actor-card', { hasText: name })
  await expect(card).toBeVisible()
  await expect(card.locator('button.actor-favourite')).toHaveAttribute('aria-pressed', 'true')
  await expect(card).toContainText('Videos here')
})

test('a Video says what prdb knows about the work beyond its title', async ({ page }) => {
  await page.getByRole('link', { name: 'The First Film' }).click()
  const facts = page.locator('.work-facts')
  await expect(facts).toBeVisible()

  // The network, the release name a file is usually called after, and what prdb knows the work
  // in — all of it carried by the identification answer the lane already paid for.
  await expect(facts).toContainText('Network')
  await expect(facts).toContainText('The.First.Film.1080p.WEB-DL')
  await expect(facts).toContainText('3840×2160')

  // prdb's pictures of the work, held here rather than pointed at.
  for (const source of await facts.locator('img')
    .evaluateAll((images) => images.map((image) => image.getAttribute('src')))) {
    expect(source).toMatch(/^\/media\/works\//)
  }
})

test('the recommendations screen answers from what this Account actually did', async ({ page }) => {
  // Nothing has been watched, so the whole page is honest discovery and says so. This is the cold
  // start the rules ask for, against a real installation rather than a stub.
  await page.getByRole('link', { name: 'For you' }).click()
  await expect(page.getByRole('heading', { name: 'For you', exact: true })).toBeVisible()
  await expect(page.getByText(/rather than anything about your taste/)).toBeVisible()
  await expect(page.getByText(/Watch something for a minute or two/)).toBeVisible()

  const discovered = page.locator('.recommendation-section', { hasText: 'Not yet discovered' })
  await expect(discovered.locator('.video-card')).toHaveCount(4)
  // Every card says why it is there, and for a Video nobody has watched that is exactly one fact.
  await expect(discovered.locator('.card-line.why').first()).toContainText('You have not watched this')

  // A Dislike takes a Video out of every section, at the server: the page is asked again and the
  // card is gone rather than hidden by the browser.
  const first = discovered.locator('.video-card').first()
  const title = (await first.locator('.video-title').textContent())!.trim()
  const address = await idOf(page, title)
  await react(first, 'Dislike')
  await expect(page.locator('.video-card', { hasText: title })).toHaveCount(0)

  // Clearing it gives the Video back, which is what makes a Dislike a statement rather than a
  // deletion.
  await page.goto(`/videos/${address}`)
  await page.getByRole('button', { name: /^Clear your reaction/ }).click()
  await page.goto('/recommendations')
  await expect(page.locator('.video-card', { hasText: title })).toHaveCount(1)

  // Not today is a day rather than a dislike, and the undo is where the card was.
  await page.locator('.video-card', { hasText: title })
    .getByRole('button', { name: /^Not today/ })
    .click()
  await expect(page.getByText(/is put aside for 24 hours/)).toBeVisible()
  await expect(page.getByText(/It is not a/)).toBeVisible()
  await page.getByRole('button', { name: `Undo putting ${title} aside` }).click()
  await expect(page.locator('.video-card', { hasText: title })).toHaveCount(1)
})

test('loving a Video makes it something to watch again without having watched it', async ({ page }) => {
  // The rule that separates an explicit reaction from behaviour: a Love is offerable on its own,
  // where a Video nobody has watched and nobody has said anything about is not. The seeded clips
  // are two seconds long, so this is the half of the journey a real installation can prove — the
  // watching thresholds are held to their examples where the evidence can be made exactly.
  await page.getByRole('link', { name: 'For you' }).click()
  const again = page.locator('.recommendation-section', { hasText: 'For you to watch again' })
  await expect(again.locator('.video-card')).toHaveCount(0)

  const card = page.locator('.recommendation-section', { hasText: 'Not yet discovered' })
    .locator('.video-card', { hasText: 'The Second Film' })
  await react(card, 'Love')

  await expect(again.locator('.video-card', { hasText: 'The Second Film' })).toHaveCount(1)
  await expect(again.locator('.card-line.why')).toContainText('You loved this')
  // And it has left the section for Videos nobody has watched, because a page never repeats one.
  await expect(
    page.locator('.recommendation-section', { hasText: 'Not yet discovered' })
      .locator('.video-card', { hasText: 'The Second Film' }),
  ).toHaveCount(0)

  await page.getByRole('button', { name: /^Clear your reaction/ }).click()
  await expect(again.locator('.video-card')).toHaveCount(0)
})

/// Sets a reaction on one card. The radio behind each label is an off-screen pixel, so the label
/// is what a pointer and this test both press.
async function react(card: Locator, reaction: 'Dislike' | 'Shrug' | 'Like' | 'Love') {
  await card.locator('label.reaction', { hasText: reaction }).click()
}

/// The identity behind a title, taken from the link the card carries.
async function idOf(page: Page, title: string) {
  const href = await page.locator('.video-card', { hasText: title })
    .locator('a.video-link')
    .getAttribute('href')
  return /\/videos\/([^?#]+)/.exec(href!)![1]
}

function facets(page: Page, group: 'Sites' | 'Actors' | 'Quality') {
  return page.locator(`[aria-label="${group}"] button.facet`)
}

async function signIn(page: Page) {
  await page.goto('/')
  await page.getByLabel('Username').fill(ADMINISTRATOR.username)
  await page.getByLabel('Password').fill(ADMINISTRATOR.password)
  // Scoped to the form: the panel's own tab carries the same name, and a click on it would only
  // reselect the tab already showing.
  await page.locator('form').getByRole('button', { name: 'Sign in' }).click()
  await settled(page)
}

/// The facets, which wait behind one control rather than standing beside the Videos.
///
/// This suite used to sign in by waiting for a facet button to appear. Since the filter sheet
/// stopped covering what it narrows, that button exists from the first render and is hidden until
/// Filters is pressed — so every case here was waiting sixty seconds for something that was never
/// going to become visible on its own.
async function openFilters(page: Page) {
  await page.getByRole('button', { name: /^Filters/ }).click()
  await page.locator('button.facet').first().waitFor()
}

/// The library, whole.
///
/// Every case here starts from the same four Videos. Which of them a viewer is shown otherwise
/// depends on what their browser can play, and the setup turns that filter off for this Account
/// once — see `showEverythingTo` for why a test cannot sensibly wait for it instead.
async function settled(page: Page) {
  await expect(page.locator('.video-card')).toHaveCount(4)
}
