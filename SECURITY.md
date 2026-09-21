# Security

personalaffe is a private workspace belonging to one person, reachable from the
open internet. Everything in it is behind one door, and what that door is made
of is written down here.

## Reporting something

**Report it privately first**, at
[github.com/datavisionzero/personalaffe/security/advisories/new](https://github.com/datavisionzero/personalaffe/security/advisories/new)
— GitHub's private advisory form, which nobody but the maintainers can read. If
that is not available to you, open an issue saying only that you have something
to report and how to reach you, and it will be taken off the issue tracker
before any detail is asked for.

What helps: the version the instance reports at `GET /api/version`, how it is
reached (behind which reverse proxy, over which scheme), what you did, what you
expected and what happened instead. A request and its answer, with any
credential removed, is worth more than a description of either.

What to expect: an acknowledgement within a week, and an answer about whether it
is in scope with it. There is no bounty and no commercial support behind this
project; what there is, is a fix, a release, and your name in it if you want it
there.

**Please do not** test against anybody else's instance. Install one — it is
`docker compose up` and a proxy ([`docs/operations.md`](docs/operations.md)) —
and break your own.

## What is in scope

The product, as it is installed by its own documentation:

- The HTTP API, the web application and `pea`, at the version `main` is at.
- Anything that gets past the door: reaching content without a credential, with
  a revoked one, or with one the owner granted less than that.
- Anything that gets past an application's switch, or out of the storage root,
  or into somebody else's session.
- The backup and the restore: an archive that carries a secret it should not, or
  a restore that puts an instance into a state its owner did not ask for.
- The documented deployment itself — the Compose file, the image, the variables
  and the reverse-proxy guidance — where following it produces something less
  safe than it says it is.

## What is deliberately not promised

VISION §8 says this out loud, and it is repeated here so that nobody reports it
as a finding:

- **No end-to-end or zero-knowledge encryption against the operator.** The owner
  and the operator are the same person, and the instance reads its own database.
- **No protection against a compromised host.** Anybody with the machine has the
  database, the volume and the ability to hand themselves the owner's account
  (`personalaffe recover-owner`, which exists on purpose).
- **No formal certification**, and no promise about a deployment that is not the
  documented one.
- **No public content and no sharing links.** Everything is the owner's; a link
  that works without a credential would be the bug.
- **Nothing looks inside a stored file.** No virus scanning, no content
  inspection, no preview — a file goes down as bytes and comes back as bytes,
  as an attachment, with `X-Content-Type-Options: nosniff`.
- **A session over plain HTTP travels in the clear.** The instance still works
  there, because a first installation is reached at `http://127.0.0.1:8080/`
  before anything is in front of it; putting TLS in front of anything that is
  not a trial is the operator's, and it is the first section of
  [`docs/operations.md`](docs/operations.md).

## What the door is made of

| What holds | Where it is proved |
| --- | --- |
| Everything but five operations needs a credential, and the sixth — sign-in — refuses anything but the right password | `TheDoorHoldsTests` walks every operation the contract names; `TheSecurityPassTests` walks the instance's own route table, so an endpoint that never reached the contract cannot be the hole nobody looked in |
| A password is Argon2id, a token is stored as a hash, and neither is ever logged | `Argon2idPasswordHasherTests`, `TheDoorHoldsTests.No_secret_anybody_holds_is_written_down_anywhere`, `TheLogTests` |
| An optional second factor, TOTP to RFC 6238, with recovery codes; a used code cannot be used again | `SecurityTests`, `TotpTests` |
| An optional inactivity lock closes a browser session server-side, survives reloads and restarts, delays PIN and password guesses independently, and never applies to bearer agents | `InactivityLockTests`, `InactivityLockConfigurationTests`, `BrowserSessionTests`, and `src/web/browser/inactivity-lock.spec.ts` |
| Failed sign-ins are throttled per account and per caller, and the caller is the forwarded address where a proxy is trusted to say so | `SignInTests`, `TrustedProxiesTests` |
| An agent reaches only the applications the owner named, only as far as the owner said, and stops at its next request when it is revoked — a download it already holds included | `AgentAccessTests`, `TheSecurityPassTests` |
| A read-only credential is refused every write the contract names in all four applications, their Trash and the home page's preferences | `TheSecurityPassTests.A_credential_that_may_only_read_changes_nothing_anywhere` |
| A browser write proves it came from this application (`X-Personalaffe-CSRF` and an `Origin`), and a bearer token never sees that check | `DoorTests`, `CsrfProtection` |
| The session cookie is `HttpOnly`, `SameSite=Lax`, and over HTTPS is `__Host-`-prefixed and `secure` | `DoorTests`, `TheSecurityPassTests` |
| Every answer carries a content security policy, `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy: no-referrer`, a same-origin opener policy and a permissions policy — and, over HTTPS only, `Strict-Transport-Security` | `SecurityHeaders`, `TheSecurityPassTests`, and `scripts/smoke.sh` from outside, where a proxy stripping them is what is being looked for |
| No name in a request reaches out of the storage root, and neither upload limit leaves bytes behind when it refuses | `FilesTests`, `TheFilesHoldTests` |
| A stored file is served as an attachment and never as a document of this instance's own origin | `TheFilesHoldTests.A_stored_file_is_never_a_document_of_this_instances_own_origin` |
| Markdown is parsed to a component tree with raw HTML skipped — never interpreted, never `innerHTML` — and only `http`, `https` and `mailto` survive as links | `src/web/src/shared/Markdown.tsx`, `Markdown.test.tsx` |
| A switched-off application refuses everywhere, the search and the Trash included | `ApplicationSwitchTests`, `SearchTests`, `FilesTests` |
| A write says which version it replaces, and a stale one changes nothing | `GuardedWriteTests`, `TheSafeguardsHoldTests`, `TheSecurityPassTests` |
| The request log says what the caller was told and never what the owner wrote | `TheLogTests` |
| Nothing a caller types is ever an operator in a query | `SearchTests.Nothing_a_caller_types_can_be_an_operator` |

## The policy, and the one thing it has to allow

```
default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none';
form-action 'self'; script-src 'self'; style-src 'self' 'unsafe-inline';
img-src 'self' data: https:; font-src 'self'; connect-src 'self'
```

Script from this instance and nowhere else; nothing framed; and `connect-src
'self'`, which is the line that matters most — a script that somehow ran could
still not send what it read anywhere.

`style-src` carries `'unsafe-inline'` because the Markdown editor
([ADR 0001](docs/adr/0001-adopt-the-existing-affe-stack-and-components.md),
CodeMirror) writes its own stylesheets into the document at runtime. `img-src`
admits `https:` because a knowledge page is the owner's Markdown and may point
at a picture anywhere; `Referrer-Policy: no-referrer` is what keeps that request
from telling the other end which page it was on.

## Keeping an instance safe

The short version of [`docs/operations.md`](docs/operations.md):

- Put TLS in front of it, and let the proxy hold the certificate.
- Set `PERSONALAFFE_TRUSTED_PROXY` when there is one, so the throttle counts the
  caller rather than the proxy; leave it unset when there is not.
- Set `PERSONALAFFE_PUBLIC_URL`, which makes the check on a browser write
  compare the whole origin rather than the host alone.
- Take backups, and prove one by restoring it —
  `scripts/rehearse-a-restore.sh` is that rehearsal, and it destroys the volumes
  it uses.
- Upgrade forward; the way back is the backup taken before the upgrade
  (`scripts/rehearse-an-upgrade.sh`).
