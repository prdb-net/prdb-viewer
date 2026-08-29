# Derive the CSRF token from the Session Token

The Cross-Site Request Forgery token a client presents with a state-changing
request is derived from that client's Session Token —
`HMAC-SHA256(key: Session Token, message: "prdb-viewer:csrf")` — rather than
generated separately and stored beside the Session.

A stored token has to be answered to a client that reloads, and there is only
one place to answer it from: `GET /api/access/me`. That endpoint therefore
issued a fresh token on every call, and because the token belongs to the
Session rather than to the client asking, every other client of the same
Session was invalidated by the asking. Two tabs of one installation were enough:
opening the second refused every state-changing request from the first until it
was reloaded, and the refusal reached the frontend as a request that failed for
no reason a reader could act on.

A derived token has no such problem. It is a property of the Session, not state
of its own, so it needs neither an answer to keep consistent nor a row to keep
current. Every client holding the Session computes the same value, asking who
you are changes nothing, and a Session that ends takes its token with it,
because the Session Token it came from is gone.

The protection is the one a random per-Session token gave. The Session cookie
is HttpOnly, SameSite Strict and, behind TLS, Secure, so a cross-site caller can
neither read the key nor set the header. The derivation is one-way, so handing
the token to the page's own script — which is what it is for — reveals nothing
about the Session Token. The comparison is fixed-time.

## Consequences

- The token is stable for the lifetime of a Session. A client that holds one
  may use it until it signs out or the Session expires, which is what a browser
  application with more than one tab needs and what rotation prevented.
- `GET /api/access/me` is a question again. It reads the cookie and reports; it
  writes nothing and touches no row.
- Verification costs no database round trip. The filter derives the expected
  value from the cookie the request already carries.
- `session.CsrfTokenHash` is gone, and with it the only column the Session table
  held that was not about the Session itself.
- Rotating the CSRF token independently of the Session is no longer possible.
  Should a reason to rotate it appear, the Session is what rotates: issuing a
  new Session Token issues a new CSRF token with it.
