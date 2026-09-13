# One owner with a browser, and agents with tokens

An instance has exactly one human account, and the schema is what says so: the
owner's table carries a column that is always true with a unique index on it, so
a second owner is impossible through an act that skipped its check, through two
callers setting up in the same millisecond, or through somebody at a `psql`
prompt. Setup works once and answers `conflict` afterwards.

The owner signs in with an email address and a password, in a browser, and gets
a server-side session in an `HttpOnly` cookie — server-side because "revoked"
has to mean revoked, which a self-contained token cannot without a list of the
ones that were. A session has an idle lifetime and an absolute one; being used
is written down at most every five minutes, so that reading the workspace is not
a write.

Everything else authenticates with `Authorization: Bearer`, and a token belongs
to **agent access**: a named, revocable authorization with one permission per
application. It is not a second human account and never becomes one. The
owner-only operations — issuing credentials, changing how the owner signs in,
listing sessions — are not permissions that could be granted; no permission for
them exists, because an agent that could issue a credential could issue itself a
better one.

**`pea` therefore holds an agent token, and there is no password in it.** A
console is an agent acting on the owner's behalf, which is what `CONTEXT.md`
already calls it. The consequence is deliberate and is written down in
`docs/cli.md`: `pea` cannot manage agents or change security settings. The
device-login flow of the sibling products is not taken.

The second factor is optional TOTP to RFC 6238 — SHA-1, six digits,
thirty-second steps — because that is what every authenticator app speaks and
several cannot be told otherwise. Enrolment is two operations and the first
changes nothing; a code that has been used cannot be used again. Turning it on
issues ten single-use recovery codes, shown once.

**There is no SMTP anywhere and there will not be.** The email address is a
login identifier. When the password, the authenticator and the recovery codes
are all gone, the way back is `personalaffe recover-owner` on the machine the
instance runs on: not an endpoint, no permission, no token — its authorization
is that somebody is standing at the host, which is the same authorization
`pg_dump` has.

What was adopted from the siblings: the Argon2id hasher, the token
authentication scheme, the browser cookie that follows the request's own scheme,
the failed-sign-in throttle, and the CSRF pair of a custom header and an
`Origin`. What was not: users, roles, invitations, device login, and mail.
