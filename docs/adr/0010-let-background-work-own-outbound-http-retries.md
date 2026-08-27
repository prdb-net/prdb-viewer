# Let Background Work own outbound HTTP retries

An outbound HTTP operation makes one transport attempt and reports its exact
outcome. Transport and SDK retry policies are disabled; Background Work owns
backoff, rate-limit waiting, exhaustion, and the transition of a Work Issue's
Remediation Owner. Interactive connection verification and `Retry now` also
make one visible attempt rather than hiding several requests inside one action.

The application uses separate `IHttpClientFactory` transports for credentialed
prdb API requests and credential-free artwork delivery. Timeouts belong to the
transport and operation class and remain shorter than the bounded work that
contains them. Credentialed transports do not follow redirects automatically;
a transport proven to carry no credential may follow redirects for artwork.

## Consequences

- Any retry policy supplied by `Prdb.Sdk` is explicitly disabled. Adding a
  general HTTP resilience pipeline would reopen this decision because nested
  retries would hide request consumption and multiply Background Work retries.
- `429` and retry guidance reach the prdb governor unchanged. A lane asks the
  governor before starting remote work and a bounded run ends when further
  requests are deferred; it never waits while occupying the lane.
- A timeout or connectivity failure establishes External Availability, not an
  authoritative rejection or an absent remote identity. Authentication and
  entitlement responses remain distinct External Authority outcomes.
- An honest product name and version are sent as the `User-Agent`. URLs,
  credentials, authentication headers, and unsanitised response bodies never
  enter logs, Work Issues, or Operator Handoffs.
- Ordinary browsing and playback read retained local state and never block on a
  live prdb request. HTTP response caching is not used as a substitute for the
  application's explicit metadata and artwork storage.
