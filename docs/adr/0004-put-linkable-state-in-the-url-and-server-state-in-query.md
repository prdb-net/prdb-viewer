# Put linkable state in the URL and server state in Query

The React frontend uses React Router in library mode and TanStack Query. Routes,
searches, filters, sorting, paging, selected Videos, and administrative work
items live in the URL whenever another person or a later browser session could
meaningfully return to them; data obtained from the backend lives in Query's
cache, while transient component-only interaction stays local component state.

The Viewer has many parameterised Video, discovery, review, Account, and
operations surfaces, so the small hand-written routing and request-state
patterns used by simpler siblings no longer fit. Framework mode is excluded
because it would introduce a Node runtime and conflict with ADR 0001.

## Consequences

- Navigation and route matching use one route definition rather than parallel
  lists that can drift.
- Mutations invalidate server-state keys instead of teaching one screen how to
  refresh every other screen.
- A linkable filter kept only in component state is a defect because the URL no
  longer reproduces what the User was looking at.
