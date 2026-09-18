# Installing personalaffe

**This page assumes nothing about the machine you are reading it on**: no
checkout, no toolchain, no account with anybody. What it asks for is a host with
Docker, a name in DNS pointing at it, and a reverse proxy you are willing to put
in front of it. Everything else is downloaded from the release.

If you have a checkout and want to run it from source instead, that is the
[README](../README.md); this page is for somebody installing what was published.

## What a release is

| | |
| --- | --- |
| **The image** | `ghcr.io/datavisionzero/personalaffe:<version>`, for `linux/amd64` and `linux/arm64`. It carries the API and the web application; one container serves both. |
| **`pea`** | The console client, for macOS and Linux on both architectures, as `pea_<version>_<os>_<arch>.tar.gz` with the licence beside the binary. |
| **`SHA256SUMS`** | One checksum per archive. |
| **`docker-compose.yml`** and **`env.example`** | What an installation is made of, attached so that installing needs no checkout. |
| **`restore.sh`** | The way back: it puts a backup into the installation it is run beside. |
| **`LICENSE`** | MIT. |

Releases are at
[github.com/datavisionzero/personalaffe/releases](https://github.com/datavisionzero/personalaffe/releases).
A version with a hyphen in it — `0.1.0-rc.1` — is a pre-release: published on
purpose, and not what `:latest` points at.

> **0.1.0 is the first release.** `:latest` points at the newest one that is not
> a candidate; naming the version instead is what makes an upgrade something you
> decided rather than something that happened.

## The instance

### 1. The two files

```sh
version=0.1.0                     # the release you are installing

curl -LO "https://github.com/datavisionzero/personalaffe/releases/download/v$version/docker-compose.yml"
curl -LO "https://github.com/datavisionzero/personalaffe/releases/download/v$version/env.example"

cp env.example .env
```

`.env` is the whole of what an operator sets, and it lists every variable with
its default and what it is for. Two of them matter now:

```sh
POSTGRES_PASSWORD=…               # openssl rand -base64 33
PERSONALAFFE_IMAGE=ghcr.io/datavisionzero/personalaffe:0.1.0
```

**No secret in this product has a default.** An instance whose database password
is missing does not start with a weak one: it stops before it opens a socket,
with a line naming the variable.

**Name the proxy before you claim the instance**, if there will be one:

```sh
PERSONALAFFE_TRUSTED_PROXY=all    # or its address, or a CIDR network
PERSONALAFFE_PUBLIC_URL=https://workspace.example.com
```

The first is what makes the throttle on failed sign-ins count the caller rather
than the proxy — which is why it is set now and not after somebody has been
locked out. The second is optional and buys a stricter check on browser writes.

### 2. Up

```sh
docker compose up -d --wait
```

`--wait` and not `-d` alone: without it the command returns before the
migrations have run, and you would be curling a port that answers nothing.

```sh
curl http://127.0.0.1:8080/api/health/ready    # {"status":"ready"}
curl http://127.0.0.1:8080/api/version         # what this build calls itself
```

The instance publishes on loopback only. It terminates no TLS and asks for none.

### 3. The proxy

Put yours in front of it, terminating TLS and forwarding to `127.0.0.1:8080`.
[`docs/operations.md`](operations.md#behind-a-reverse-proxy) has a Caddyfile end
to end, what nginx has to be told instead, and what a proxy has to forward for
the instance to see the caller.

### 4. Claim it

Open `https://workspace.example.com/` in a browser. An instance with no owner
offers to be claimed; the first email address and password given to it are the
owner's, and the offer is gone afterwards. Then, if you want one, turn on the
second factor and **write the recovery codes down** — and read
[When the owner is locked out](operations.md#when-the-owner-is-locked-out)
before you need it.

## `pea`, on the machine you work from

`pea` is a client of the public API and nothing else. It does not belong beside
the instance; it belongs where you are.

```sh
version=0.1.0
platform=darwin_arm64             # or darwin_amd64, linux_amd64, linux_arm64
base="https://github.com/datavisionzero/personalaffe/releases/download/v$version"

curl -LO "$base/pea_${version}_${platform}.tar.gz"
curl -LO "$base/SHA256SUMS"

shasum -a 256 --check --ignore-missing SHA256SUMS    # sha256sum on Linux
tar -xzf "pea_${version}_${platform}.tar.gz"
sudo mv pea /usr/local/bin/
```

Then tell it where the instance is, and let an agent in to carry a token
([`docs/operations.md`](operations.md), step 7):

```sh
export PERSONALAFFE_URL=https://workspace.example.com
export PERSONALAFFE_TOKEN=pea_…

pea version          # what this pea is, and what the instance is
pea dashboard        # what is open, what was written, what arrived
```

`pea` talks to an instance of its own minor version and older. When it will not,
it says which side moves and stops with exit 9 rather than guessing —
[`docs/cli.md`](cli.md) has the configuration ladders, the input rules and the
exit codes.

## Backing it up

The backup is one command, run against the container, and it holds the instance
still for the moment it takes — reads keep working, writes are told to come back
in a few seconds:

```sh
docker compose exec -T personalaffe personalaffe backup --to - > "personalaffe-$(date +%F).tar"
```

One archive, carrying the database, the owner's files and a manifest of both.
Anything carrying only one of the two is not a backup of this product. Where the
archive then goes, and how long it is kept, is yours — and
**a backup nobody has restored is not yet known to be a backup**.

## Upgrading

```sh
docker compose exec -T personalaffe personalaffe backup --to - > "personalaffe-$(date +%F).tar"
# then set PERSONALAFFE_IMAGE to the new version in .env
docker compose pull
docker compose up -d --wait
```

Migrations apply themselves on start and **only ever forward**. There is no
downgrade: the way back from an upgrade is the backup taken before it, put back
with `restore.sh` — which is attached to the release for this — and the earlier
image named:

```sh
curl -LO "https://github.com/datavisionzero/personalaffe/releases/download/v$version/restore.sh"
chmod +x restore.sh

# beside your docker-compose.yml and .env, with the earlier version in .env
./restore.sh personalaffe-2026-09-17.tar --over-a-populated-instance
```

It stops the instance, puts the archive back in a one-off container beside it,
and starts it again — and it changes nothing at all unless the archive is
complete, every checksum matches and its schema is one that build knows.
**A restore signs every browser out**, including yours.
[`docs/operations.md`](operations.md#backing-it-up) has the backup, what is in
it, what the pause costs, and the restore in full.

An instance started against a schema a newer build wrote refuses to serve and
says what it does not know, rather than guessing at it.

## What it is, and what it is not

MIT licensed. The weather tile, where it is switched on, is served by
[Open-Meteo](https://open-meteo.com) — *Weather data by Open-Meteo.com*,
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) — and the API says so
with every reading. `PERSONALAFFE_WEATHER=off` and nothing in this product opens
a socket to anywhere.

[`SECURITY.md`](../SECURITY.md) says what the door is made of, what is in scope,
and how to report something you find in it.
