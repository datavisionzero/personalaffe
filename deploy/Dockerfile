# One image, carrying the API and the built web application: the two toolchains
# meet here and nowhere else (docs/codebase.md). The runtime image carries
# neither of them.
#
# `pea` is not in here. It is a client of the public API and runs on a laptop, a
# CI runner or an agent's container, not beside the instance; it ships as its
# own binary.
#
# Build context is the repository root:
#   docker build -f deploy/Dockerfile -t personalaffe .

# ------------------------------------------------ the web application ----
FROM node:24-alpine AS web
WORKDIR /web

COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci

# The API layer is generated from the contract before the build runs, so the
# document has to be in this stage — at the path `npm run generate` reaches for,
# two levels above src/web/. Copied before the sources because it changes less
# often than they do.
COPY docs/api/openapi.json /docs/api/openapi.json

COPY src/web/ ./
RUN npm run build -- --outDir dist --emptyOutDir

# ------------------------------------------------------- the server ----
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# What this build calls itself. A release passes the tag; a build started by
# hand keeps the default the props file gives, which says plainly that it is not
# a release (Directory.Build.props).
ARG VERSION=0.0.0-dev

# Which commit it was built from, appended by the SDK to the informational
# version behind a `+`. CI passes it, because this build has no repository to
# read it out of — `Dockerfile.dockerignore` excludes `.git/`.
ARG REVISION=

# Restore against the manifests alone, so that a change to a source file does
# not invalidate the package layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Personalaffe.Domain/Personalaffe.Domain.csproj src/Personalaffe.Domain/
COPY src/Personalaffe.Application/Personalaffe.Application.csproj src/Personalaffe.Application/
COPY src/Personalaffe.Infrastructure/Personalaffe.Infrastructure.csproj src/Personalaffe.Infrastructure/
COPY src/Personalaffe.Api/Personalaffe.Api.csproj src/Personalaffe.Api/
RUN dotnet restore src/Personalaffe.Api/Personalaffe.Api.csproj

COPY src/Personalaffe.Domain/ src/Personalaffe.Domain/
COPY src/Personalaffe.Application/ src/Personalaffe.Application/
COPY src/Personalaffe.Infrastructure/ src/Personalaffe.Infrastructure/
COPY src/Personalaffe.Api/ src/Personalaffe.Api/
RUN dotnet publish src/Personalaffe.Api/Personalaffe.Api.csproj -c Release -o /app \
    -p:Version=$VERSION -p:SourceRevisionId=$REVISION

# A development connection string has no business in an image somebody runs in
# front of the internet, and `dotnet publish` copies every appsettings it finds.
# The dockerignore keeps it out of the context; this is the second lock, because
# a file that reappears in the context should not silently reappear in the image.
RUN rm -f /app/appsettings.Development.json

# ------------------------------------------------------ the runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Npgsql probes for GSSAPI when it opens a connection, whatever the connection
# string says about authentication. Without the library the probe writes two
# lines with the word `Error` straight to stderr on every start of a run that is
# going fine, and the next person reading the log during a real outage has to
# rule it out first. Three megabytes to keep the log honest; nothing here asks
# for Kerberos. Ahead of the COPY, so a source change does not re-run apk.
RUN apk add --no-cache krb5-libs

COPY --from=build /app ./
COPY --from=web /web/dist ./wwwroot

# Where the owner's files go. Inside the image it is an empty directory that a
# recreation throws away, which is exactly why Compose mounts a volume over it —
# and why the instance refuses to start if it cannot write here.
#
# Outside wwwroot on purpose: a file the owner stored is reached through the API,
# not by anyone who can guess its name.
RUN mkdir -p /var/lib/personalaffe/files

ENV PATH="/app:${PATH}" \
    ASPNETCORE_HTTP_PORTS=8080 \
    PERSONALAFFE_STORAGE_ROOT=/var/lib/personalaffe/files

EXPOSE 8080

# Reachable over the network, so it does not run as root. The storage root goes
# with it: a volume mounted over a directory this user cannot write would stop
# the start, which is the failure this ownership exists to avoid.
RUN chown -R $APP_UID:$APP_UID /app /var/lib/personalaffe
USER $APP_UID

# Where this image came from, said by the image itself. Two of them repeat the
# arguments the build stage consumed, so they are declared again here — an ARG
# belongs to one stage. Last, after the COPYs and the only RUN that costs
# anything, because `revision` changes on every commit and a label is the
# cheapest layer there is.
ARG VERSION=0.0.0-dev
ARG REVISION=
LABEL org.opencontainers.image.title="personalaffe" \
      org.opencontainers.image.description="A private workspace belonging to one person: scratchpad, knowledge, tasks and files." \
      org.opencontainers.image.source="https://github.com/datavisionzero/personalaffe" \
      org.opencontainers.image.documentation="https://github.com/datavisionzero/personalaffe/blob/main/docs/operations.md" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.vendor="datavisionzero" \
      org.opencontainers.image.version="$VERSION" \
      org.opencontainers.image.revision="$REVISION"

# It serves the instance and has no verbs: a word handed to it is answered with
# the sentence that says where to go instead.
ENTRYPOINT ["personalaffe"]
