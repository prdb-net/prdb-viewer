# Generate frontend types from the OpenAPI contract

ASP.NET Core minimal API endpoints are grouped by capability and publish an
OpenAPI document that is committed and checked for drift in CI. The frontend
generates TypeScript types from that document and uses the platform `fetch`
API rather than maintaining a second handwritten contract or shipping a
generated runtime client.

Domain actions receive named endpoints, and an expected product outcome is a
successful response with a typed verdict. HTTP error status codes and
`ProblemDetails` retain transport meanings such as malformed input, missing
authentication, forbidden authority, a missing resource, or an unexpected
server failure.

## Consequences

- Backend response models that cross the contract boundary are named and
  stable enough to generate useful frontend types.
- Personal State endpoints derive the acting Account from authentication and
  never expose another Account identifier as a way to select Personal State.
- CI regenerates both the OpenAPI document and TypeScript declarations and
  fails when committed output differs.
