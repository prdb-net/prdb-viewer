import { expect, test, type Page } from '@playwright/test'

/// Choosing facets faster than the screen can redraw.
///
/// 0.5.0 shipped multi-value facets, and clicking two Sites quickly kept only the second. 0.5.1
/// moved the computation and did not fix it. Both releases had a passing test, because a test that
/// clicks through `fireEvent` renders between the clicks and a person does not: React Router does
/// not sequence two navigations raised in the same tick, and the second updater still receives the
/// address the first one started from.
///
/// Every case here therefore raises its clicks inside one `evaluate`, which puts them in a single
/// task and a single React batch — the condition under which the defect existed and the only one
/// under which its absence means anything.
test.describe('Facets chosen within one batch', () => {
  test('two Sites clicked together both hold', async ({ page }) => {
    await open(page)

    await clickTogether(page, ['Alpha Site', 'Beta Site'])

    await chosenIn(page, 'sites').toEqual(['Alpha Site', 'Beta Site'])
    await expect(pressed(page)).toHaveText(['Alpha Site (4)', 'Beta Site (3)'])
  })

  test('two Actors clicked together both hold', async ({ page }) => {
    await open(page)

    await clickTogether(page, ['Alex Doe', 'Sam Roe'])

    await chosenIn(page, 'actors').toEqual(['Alex Doe', 'Sam Roe'])
    await expect(pressed(page)).toHaveText(['Alex Doe (2)', 'Sam Roe (1)'])
  })

  test('a Site and an Actor clicked together are written to their own keys', async ({ page }) => {
    await open(page)

    // Two different facets in one batch is the case where the second write has to carry the first
    // one's key across rather than start from an address that has neither.
    await clickTogether(page, ['Alpha Site', 'Alex Doe'])

    await chosenIn(page, 'sites').toEqual(['Alpha Site'])
    await chosenIn(page, 'actors').toEqual(['Alex Doe'])
  })

  test('three Sites clicked together all hold', async ({ page }) => {
    await open(page)

    // Two is the case that was reported; a third would still have been dropped by a fix that only
    // remembered one write.
    await clickTogether(page, ['Alpha Site', 'Beta Site', 'Gamma Site'])

    await chosenIn(page, 'sites').toEqual(['Alpha Site', 'Beta Site', 'Gamma Site'])
  })

  test('choosing and unchoosing one Site together leaves no trace', async ({ page }) => {
    await open(page)

    // The same button twice: the second click has to see the first one's selection to undo it. A
    // default is written as absence, so what is left is an address with no `sites` at all.
    await clickTogether(page, ['Alpha Site', 'Alpha Site'])

    await chosenIn(page, 'sites').toEqual([])
    await expect(pressed(page)).toHaveCount(0)
  })

  test('a Site chosen alongside one already in the address keeps both', async ({ page }) => {
    await open(page, '?sites=Alpha+Site')
    await expect(pressed(page)).toHaveText(['Alpha Site (4)'])

    await clickTogether(page, ['Beta Site'])

    await chosenIn(page, 'sites').toEqual(['Alpha Site', 'Beta Site'])
  })

  test('the address a choice wrote restores that choice when it is opened again', async ({ page }) => {
    await open(page)
    await clickTogether(page, ['Alpha Site', 'Beta Site'])

    // ADR 0004: the address has to reproduce what the User was looking at. Reloading it is the
    // only way to find out whether it really does.
    await page.reload()
    await page.locator('button.facet').first().waitFor()

    await expect(pressed(page)).toHaveText(['Alpha Site (4)', 'Beta Site (3)'])
  })
})

/// Raises every click inside one task, so React batches them the way it does for a person clicking
/// faster than a frame. Playwright's own `click` waits for the page to settle between calls, which
/// is exactly the courtesy that hid this defect.
async function clickTogether(page: Page, labels: string[]) {
  await page.evaluate((wanted) => {
    const buttons = [...document.querySelectorAll<HTMLButtonElement>('button.facet')]

    for (const label of wanted) {
      const button = buttons.find((candidate) => candidate.textContent?.startsWith(label))

      if (!button) {
        throw new Error(`No facet button for ${label}. Present: ${buttons.map((b) => b.textContent)}`)
      }

      button.click()
    }
  }, labels)
}

/// What one facet key holds, read back decoded, retried until the navigation those clicks raised
/// has landed. Asserting on the encoded address instead ties the test to how URLSearchParams
/// happens to spell a space, which is not what any of this is about.
///
/// Waiting for the address to *change* would not do: choosing a facet and unchoosing it in one
/// batch ends on the address it started from, and a test that waited for a change would sit there
/// until it timed out while the screen was already right.
function chosenIn(page: Page, key: 'sites' | 'actors') {
  return expect.poll(() => {
    const value = new URL(page.url()).searchParams.get(key)
    return value ? value.split(',') : []
  })
}

/// A Playwright locator rather than a resolved array, so the assertion retries while the screen
/// catches up instead of reading it once and believing whatever it saw.
function pressed(page: Page) {
  return page.locator('button.facet[aria-pressed="true"]')
}

async function open(page: Page, query = '') {
  await answerApi(page)
  await page.goto(`/${query}`)
  // The facets arrive on their own request, so nothing can be clicked until they are drawn.
  await page.locator('button.facet').first().waitFor()
}

/// The answers every screen needs before it renders. The library itself is empty on purpose: these
/// tests are about what a click does to the address, and a Video on the page is one more thing to
/// keep true for no gain.
async function answerApi(page: Page) {
  await page.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname

    const body =
      path === '/api/access/state' ? { claimed: true, signedIn: true } :
      path === '/api/access/me' ? {
        id: '01994dd4-2a0a-7000-8000-000000000001',
        username: 'viewer',
        email: null,
        authority: 'User',
        csrfToken: 'csrf-token',
      } :
      path === '/api/library/facets' ? {
        sites: [
          { value: 'Alpha Site', count: 4 },
          { value: 'Beta Site', count: 3 },
          { value: 'Gamma Site', count: 1 },
        ],
        actors: [
          { value: 'Alex Doe', count: 2 },
          { value: 'Sam Roe', count: 1 },
        ],
        quality: [],
      } :
      path === '/api/library/videos' ? {
        videos: [],
        totalMatches: 0,
        hiddenNotReadyForDirectPlay: 0,
        hiddenUnavailable: 0,
        hasMore: false,
        includesNotReadyForDirectPlay: false,
      } :
      []

    await route.fulfill({ json: body })
  })
}
