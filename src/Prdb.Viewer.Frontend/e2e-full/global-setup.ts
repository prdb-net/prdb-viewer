import { dockerIsAvailable, start } from './installation'

export default async function () {
  if (!dockerIsAvailable()) {
    throw new Error(
      'This suite runs the product image, so it needs Docker. Start Docker and try again, or ' +
      'run `npm run test:e2e` for the suite that answers the API from the test instead.',
    )
  }

  await start()
}
