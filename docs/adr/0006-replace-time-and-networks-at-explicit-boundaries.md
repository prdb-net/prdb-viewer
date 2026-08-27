# Replace time and networks at explicit boundaries

Production code obtains wall-clock and elapsed time through an injected
`TimeProvider`; direct `DateTime.Now` and `DateTimeOffset.UtcNow` calls are
forbidden by an architecture test. Tests replace external networks at
`HttpMessageHandler`, below `Prdb.Sdk` and the application's transports, so the
real serialization, authentication headers, redirects, timeouts, and response
mapping remain exercised.

Infrastructure tests use the real selected database engine and real temporary
directories. They do not call prdb.net or depend on real mounted libraries, and
media-tool output is represented by recorded secret-free fixtures where parsing
behaviour needs coverage.

## Consequences

- Playback sessions, expiry, retries, Background Work scheduling, Recovery
  Codes, and operational timestamps can be tested without waiting for real
  time.
- Tests cannot replace the composition root merely to bypass the wiring they
  are intended to verify.
- Any recorded HTTP or tool fixture must exclude credentials, private paths,
  Personal State, and identifying local-library content.
