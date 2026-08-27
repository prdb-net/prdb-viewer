# Store recoverable credentials without application-level encryption

Recoverable installation credentials, including the prdb credential, are
stored in SQLite in the form required for unattended use. They are not
encrypted by the application at rest because this single-container deployment
has no independent place to keep a decryption key: a key file or Data
Protection key ring beside the database is exposed with it, an environment key
moves the same risk into Compose and process inspection, and an Account
password-derived key would make Background Work depend on a signed-in User and
turn password recovery into credential loss.

This applies only to secrets the application must recover. Account passwords
are one-way hashed, and session tokens, Bootstrap Authorizations, and Recovery
Codes are stored only in a form suitable for verification and expiry. A
replacement credential staged for verification receives the same protection
as the active credential.

## Consequences

- Read access to the application data volume includes access to recoverable
  installation credentials. The container creates application data with the
  configured process identity and restrictive permissions, and deployment
  documentation treats that volume as sensitive.
- Credential values are never readable through application interfaces: APIs
  and forms can report presence, masked identity, and verification state but
  never return a stored value.
- Credentials, password material, session material, complete URLs that may
  carry secrets, and authentication headers never appear in logs, diagnostics,
  Work Issues, Operator Handoffs, or command output.
- A Backup Archive is a different trust boundary. ADR 0007's application data
  controls do not travel with a portable file, so the complete archive remains
  encrypted and integrity-protected under its operator-supplied passphrase as
  required by the Backup and Restore contract.
- Adding a real external secret store may reopen this decision only when the
  supported deployment can require it without introducing another mandatory
  service or recovery dependency.
