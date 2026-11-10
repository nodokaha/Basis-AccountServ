# Central authentication service

## Deployment boundary

The central API is an independently deployable ASP.NET Core service in
Api~/BasisAccountServ.Api. It owns the shared PostgreSQL identity database.
Basis game servers and web applications are intended to become clients of this
service; they must not share the database directly.

The first implementation milestone provides an independently buildable .NET 10
API container, a PostgreSQL-backed identity/server-role schema, liveness and
database-readiness endpoints, and a Compose deployment with private persistent
database storage.

This milestone deliberately does NOT expose registration, password login,
token issuance, or administrative endpoints. Those endpoints must be added
together with the complete OIDC Authorization Code + PKCE flow, strict redirect
URI validation, one-time authorization-code consumption, token hashing,
signing-key management, and client authentication. Do not expose this service
as a public authentication provider until that work is complete.

## Identity model

- accounts represents a global AccountServ identity.
- basis_servers represents an independently registered Basis server.
- Basis identities are `did:key:z...` identifiers, not server-issued UUIDs.
  Basis currently resolves `did:key` using the Ed25519 public key encoded with
  multicodec + base58-btc. The DID is therefore a public-key identifier; it is
  not proof that the connecting client controls the corresponding private key.
- `account_did_keys` records a DID only after proof of possession. A link flow
  must issue a short-lived, single-use random challenge and verify an Ed25519
  signature against the public key extracted from the DID. The signed payload
  must bind the challenge to this service, the intended account, the DID, the
  operation, and the expiry to prevent replay and cross-service relay.
- `account_basis_links` maps a verified account/DID pair to a registered Basis
  server. The DID must be obtained from the authenticated Basis peer or verified
  by the central service; never accept a DID supplied only as an arbitrary
  request field. A Basis server may attest the peer identity over an authenticated
  server-to-server channel after its native Basis DID challenge succeeds.
- The current Basis implementation supports `did:key` with Ed25519. Do not
  assume arbitrary DID methods or other key algorithms are valid. DID parsing,
  multicodec validation, key extraction, and signature verification must match
  Basis semantics; string-prefix checks alone are not authentication.
- server_roles scopes moderator/admin privileges to one server. A role on one
  Basis server does not grant privileges on another.
- oauth_clients, oauth_authorization_codes, and oauth_refresh_tokens establish
  storage boundaries for the upcoming OIDC implementation. Raw authorization
  codes and refresh tokens are not stored.

The initial schema is applied idempotently at API startup. Back up the
accountserv-postgres volume before upgrades and production migration work.

## Health endpoints

- GET /health/live — process liveness; does not depend on PostgreSQL.
- GET /health/ready — returns 200 only when PostgreSQL is reachable.

## Local deployment

    export POSTGRES_PASSWORD='replace-with-a-long-random-secret'
    docker compose -f compose.central-auth.yaml up -d --build

The API listens on port 8080 inside the Compose network. Do not publish the
PostgreSQL port. In Coolify, expose only the API service through HTTPS after
OIDC and API authentication are implemented; keep the database private.

## Compatibility constraints

## Basis DID proof-of-possession

Basis's native DID authentication uses a random challenge (256-bit nonce) and
verifies the response signature against the public key resolved from the
challenge identity. The implementation also supports a context-bound signed
payload overload; central linking must use that form rather than signing a bare
nonce, binding at least the verifier/service, account, DID, purpose, and
challenge identifier. Challenges are stored hashed, expire quickly, and are
consumed atomically exactly once. Never store private keys or ask clients to
upload them. A successful OIDC login alone does not prove control of a Basis
DID; account linking requires this separate signature proof.

The existing Basis plugin-message authentication flow remains separate during
the migration. It continues to derive the caller UUID from the authenticated
Basis peer. Ordinary Basis chat and its ChatChannel are not modified. The
central service must not become a dependency for ordinary chat delivery; a
central-auth outage must not suppress or intercept chat traffic.
