import { expect, test, type Page } from '@playwright/test'

import { ADMINISTRATOR } from './installation'

/// The screens, against a real installation.
///
/// Everything here is answered by the product: a SQLite database the `seed` command filled, six
/// lanes that have run twice over four real video files, and a stand-in prdb that recognised three
/// of them by content and a fourth by name. Nothing is pinned by the test, so a screen that reads
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
  await page.getByRole('button', { name: 'SD (2)' }).click()

  await expect(page.locator('.video-card')).toHaveCount(2)
  expect(new URL(page.url()).searchParams.get('quality')).toBe('StandardDefinition')
  await expect(page.getByText('2 matching')).toBeVisible()

  // And ordering by it is the server's answer too, not a sort the screen did to a page.
  await page.goto('/?sort=QualityDescending')
  await page.locator('.video-card').first().waitFor()
  expect((await page.locator('.quality-badge').allTextContents()))
    .toEqual(['1080p', '720p', 'SD', 'SD'])
})

test('choosing a Site narrows the list at the server', async ({ page }) => {
  await page.getByRole('button', { name: 'Example Pictures (2)' }).click()

  await expect(page.locator('.video-title')).toHaveText(['The Second Film', 'The First Film'])
  expect(new URL(page.url()).searchParams.get('sites')).toBe('Example Pictures')

  // The heading counts the whole match rather than the page, and it is the server's number.
  await expect(page.getByText('2 matching')).toBeVisible()
})

test('the address a choice wrote is enough to reproduce the screen', async ({ page }) => {
  // ADR 0004, end to end: the browser is given only the address, and the server has to narrow the
  // library the same way it did when the choice was made.
  await page.goto('/?actors=Alex+Doe')
  await page.locator('.video-card').first().waitFor()

  await expect(page.locator('.video-title')).toHaveText(['The Second Film', 'The First Film'])
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
    'Library Scan': '4 files found',
    // Inspection re-reads every file on a new scan: a file can change under a name that did not.
    'Technical Inspection': '4 files done',
    // These three had their answers already, and nothing about the files changed, so a second
    // scan gives them nothing to do.
    'Hashing': 'nothing to do',
    'Preview Generation': 'nothing to do',
    'Site Recognition': 'nothing to do',
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
  await page.locator('.review-item').first().waitFor()

  // A name is not evidence enough to file a Work without a person agreeing to it, however sure
  // the catalogue sounded.
  await expect(page.locator('.review-item')).toHaveCount(1)
  await expect(page.locator('.review-item')).toContainText('The Fourth Film')
})

test('the Installation screen reports the connection it actually made', async ({ page }) => {
  await page.goto('/admin/setup')

  await expect(page.getByText('Verified', { exact: true })).toBeVisible()
  // The library the seed activated, mounted where the container was told to look.
  await expect(page.getByText('/libraries', { exact: true })).toBeVisible()
})

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
  await page.locator('button.facet').first().waitFor()
  await settled(page)
}

/// The library, whole.
///
/// Every case here starts from the same four Videos. Which of them a viewer is shown otherwise
/// depends on what their browser can play, and the setup turns that filter off for this Account
/// once — see `showEverythingTo` for why a test cannot sensibly wait for it instead.
async function settled(page: Page) {
  await expect(page.locator('.video-card')).toHaveCount(4)
}
