import { execFileSync, spawn } from 'node:child_process'
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'

/// A seeded installation of the product as it ships, with a stand-in prdb behind it.
///
/// The other browser suite answers `/api/**` from the test. That is right for what it asks — what
/// a click does to the address — and it means the server is never involved, so nothing there can
/// notice a screen reading a field the API does not send, or sending a filter the API ignores. The
/// `0/3` a Background work screen once showed was exactly that kind of defect, and it was found by
/// a person looking at a deployed installation.
///
/// This runs the real thing instead: the published image, the real database, the real lanes, and a
/// library the `seed` command fills. It is not in CI — an image build and six lanes are too much to
/// ask of every push — so it is opt-in, and it says plainly when Docker is not there rather than
/// failing in some other way.

export const HOST_PORT = 8099
export const CATALOGUE_PORT = 5080
export const BASE_URL = `http://127.0.0.1:${HOST_PORT}`

/// The seed's own Administrator. Printed by the command, and not a secret.
export const ADMINISTRATOR = { username: 'admin', password: 'seed-password-2026' }

const IMAGE = 'prdb-viewer:e2e'
const CONTAINER = 'prdb-viewer-e2e'
const REPOSITORY = resolve(import.meta.dirname, '../../..')

/// Where the state file lives, so teardown can find what setup made. Playwright runs the two in
/// separate processes, so a module-level variable would be empty by the time it is read.
const STATE = join(tmpdir(), 'prdb-viewer-e2e-state.json')

export function dockerIsAvailable() {
  try {
    execFileSync('docker', ['info'], { stdio: 'ignore' })
    return true
  } catch {
    return false
  }
}

export async function start() {
  stop()

  const data = mkdtempSync(join(tmpdir(), 'prdb-viewer-e2e-data-'))
  const libraries = mkdtempSync(join(tmpdir(), 'prdb-viewer-e2e-libraries-'))
  // Written before anything is started, so a run interrupted halfway still leaves teardown
  // something to find.
  writeFileSync(STATE, JSON.stringify({ data, libraries }))

  say('Building the product image. The first run is slow; after that it is layers.')
  run('docker', ['build', '--tag', IMAGE, '.'], REPOSITORY)

  const catalogue = startCatalogue()
  writeFileSync(STATE, JSON.stringify({ data, libraries, catalogue }))
  await waitFor(`http://127.0.0.1:${CATALOGUE_PORT}/rate-limit`, 'the stand-in catalogue')

  say('Seeding an installation. This writes video files and runs every lane twice.')
  run('docker', [...containerArguments(data, libraries), '--rm', IMAGE,
    'dotnet', 'Prdb.Viewer.Host.dll', 'seed'])

  say('Starting the installation.')
  run('docker', [...containerArguments(data, libraries), '--detach', '--name', CONTAINER, IMAGE])
  await waitFor(`${BASE_URL}/api/health`, 'the installation')
  await showEverythingTo(ADMINISTRATOR)
}

/// Turns off the direct-play filter for one Account, once, before any test runs.
///
/// The library is not the same for every viewer (ADR 0015): three of the four seeded files are
/// H.264 in MP4, which is a question rather than an answer, and the browser driving these tests
/// answers no — headless Chromium ships without those codecs. So the browsing screen settles on
/// one Video and a line saying it is holding three back, and it takes a moment to get there,
/// because until this browser has answered the screen shows them all.
///
/// A test that waited for either state would be waiting on how fast a codec question was answered.
/// This asks for the state the Account can choose instead — show everything — which the assessment
/// then cannot change. It is the same preference the screen's own `Include them` offers, set
/// through the same route, and it is per Account rather than per browser, which is why it can be
/// set from here at all.
async function showEverythingTo(account: { username: string; password: string }) {
  const signIn = await fetch(`${BASE_URL}/api/access/sign-in`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(account),
  })
  const session = signIn.headers.getSetCookie().map((one) => one.split(';')[0]).join('; ')
  const { verdict, account: signedIn } = await signIn.json()

  if (!signedIn) {
    throw new Error(
      `The seeded Administrator could not sign in (${signIn.status}, ${verdict}). The seed prints ` +
      'the Account it created; if that name or password has changed, ADMINISTRATOR is stale.',
    )
  }

  const set = await fetch(`${BASE_URL}/api/library/preferences/include-not-ready`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      'X-CSRF-Token': signedIn.csrfToken,
      cookie: session,
    },
    body: JSON.stringify({ included: true }),
  })

  if (!set.ok) {
    throw new Error(`The direct-play filter could not be turned off: ${set.status}.`)
  }
}

export function stop() {
  try {
    execFileSync('docker', ['rm', '--force', CONTAINER], { stdio: 'ignore' })
  } catch {
    // There was no container to remove, which is the ordinary case on a first run.
  }

  if (!existsSync(STATE)) {
    return
  }

  const state = JSON.parse(readFileSync(STATE, 'utf8'))

  if (state.catalogue && isTheCatalogue(state.catalogue)) {
    try {
      process.kill(state.catalogue)
    } catch {
      // It had already exited.
    }
  }

  for (const directory of [state.data, state.libraries]) {
    if (directory) {
      rmSync(directory, { recursive: true, force: true })
    }
  }

  rmSync(STATE, { force: true })
}

/// The container shares the Host's network namespace, which is what lets it reach the stand-in
/// catalogue at `127.0.0.1` — and the SDK exempts exactly that address from its https requirement,
/// so no certificate is involved. A published port would not do: the address inside the container
/// would then be a bridge gateway, which is not loopback and would need https.
function containerArguments(data: string, libraries: string) {
  return [
    'run',
    '--network', 'host',
    '--volume', `${data}:/data`,
    '--volume', `${libraries}:/libraries`,
    '--env', `PUID=${process.getuid?.() ?? 1000}`,
    '--env', `PGID=${process.getgid?.() ?? 1000}`,
    '--env', `ASPNETCORE_HTTP_PORTS=${HOST_PORT}`,
    '--env', 'VIEWER_LIBRARY_MOUNT_ROOT=/libraries',
    '--env', `VIEWER_PRDB_BASE_URL=http://127.0.0.1:${CATALOGUE_PORT}`,
    // The stand-in accepts any credential: what it is for is the shape of an answer rather than
    // who is asking.
    '--env', 'VIEWER_SEED_PRDB_KEY=anything-the-stand-in-accepts',
  ]
}

/// The stand-in runs on the Host rather than in a container of its own, because the address it
/// answers on has to be loopback from inside the container and `--network host` already makes the
/// Host's loopback exactly that. The built assembly is run directly rather than through
/// `dotnet run`, so the process this records is the one that holds the port.
function startCatalogue() {
  run('dotnet', ['build', 'tools/Prdb.FakeCatalogue.Server', '--verbosity', 'quiet'], REPOSITORY)
  const assembly = join(
    REPOSITORY,
    'tools/Prdb.FakeCatalogue.Server/bin/Debug/net10.0/Prdb.FakeCatalogue.Server.dll',
  )
  const child = spawn('dotnet', [assembly], {
    cwd: REPOSITORY,
    detached: true,
    stdio: 'ignore',
    env: { ...process.env, FAKE_PRDB_URL: `http://127.0.0.1:${CATALOGUE_PORT}` },
  })
  child.unref()

  return child.pid
}

/// Whether that process id is still the stand-in and not something else that inherited the
/// number. A recorded pid outlives the process it named, and this runs on a machine with other
/// work on it — killing by a stale number is not a risk worth taking to save a few lines.
function isTheCatalogue(pid: number) {
  try {
    return readFileSync(`/proc/${pid}/cmdline`, 'utf8').includes('Prdb.FakeCatalogue.Server')
  } catch {
    return false
  }
}

async function waitFor(url: string, what: string) {
  for (let attempt = 0; attempt < 120; attempt++) {
    try {
      if ((await fetch(url)).ok) {
        return
      }
    } catch {
      // Not up yet.
    }

    await new Promise((wake) => setTimeout(wake, 500))
  }

  throw new Error(`${what} never answered at ${url}.`)
}

function run(command: string, argv: string[], cwd?: string) {
  execFileSync(command, argv, { cwd, stdio: ['ignore', 'inherit', 'inherit'] })
}

function say(message: string) {
  process.stdout.write(`\n  ${message}\n`)
}
