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

The API now contains the Basis DID proof-of-possession endpoints described
below. They accept only an already-authenticated account principal with a GUID
`sub` (or NameIdentifier) claim. This API milestone does not yet configure the
account login/OIDC authentication handler, so the endpoints fail closed with
401 until that authentication layer is integrated. Registration, password
login, token issuance, and public administrative endpoints are not enabled.
Do not expose this service as a public authentication provider until the full
OIDC Authorization Code + PKCE flow, strict redirect URI validation, one-time
authorization-code consumption, token hashing, signing-key management, and
client authentication are complete.

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
    export DID_PROOF_ISSUER='https://auth.example.com'
    docker compose -f compose.central-auth.yaml up -d --build

The API listens on port 8080 inside the Compose network. Do not publish the
PostgreSQL port. In Coolify, expose only the API service through HTTPS after
OIDC and API authentication are implemented; keep the database private.

## Compatibility constraints

## Basis DID proof-of-possession

Basis's native DID authentication uses a random challenge (256-bit nonce) and
verifies the response signature against the public key resolved from the
challenge identity. The API uses NSec/libsodium for Ed25519 verification and
decodes the same `did:key` Ed25519 multicodec + base58-btc representation as
Basis. Unsupported key types, malformed encodings, and signatures not exactly
64 bytes are rejected.

The link payload is a binary, length-prefixed sequence (each length is a signed 32-bit little-endian integer) containing the purpose
(`accountserv:did-link:v1`), configured issuer, account ID, DID, challenge ID,
expiry (Unix seconds encoded as invariant UTF-8 decimal text), and random nonce (32 raw bytes preceded by its 32-bit little-endian length). Text fields are UTF-8 with 32-bit little-endian byte lengths. Clients must sign the exact decoded payload bytes
returned by the challenge endpoint; they must not reconstruct a JSON string or
sign only the nonce. This binds the proof to the verifier, account, identity,
operation, and expiry and prevents cross-service replay.

### DID linking API

Both endpoints require an authenticated principal whose `sub` (or
NameIdentifier) is the AccountServ account UUID. The login/OIDC authentication
handler is not yet wired in this milestone, so requests currently fail closed
with 401 until that layer is connected.

The current Basis client helper `BasisDIDAuthIdentityClient.RespondToChallenge`
signs the Basis network-login transcript, which is bound to a live connection.
It does not currently expose a generic arbitrary-payload signing method for
AccountServ's web linking flow. The API verifier is implemented and tested, but
end-to-end in-game linking still needs either a narrowly scoped public
`SignPayload` helper in the Basis client (using the existing local DID key),
or a server-attestation flow that forwards the DID of an already-authenticated
Basis peer over an authenticated server-to-server channel. Do not try to reuse
the network-login transcript as an HTTP proof without the corresponding
connection context.

1. `POST /api/did/link/challenges` with JSON `{"did":"did:key:z..."}`.
   The response contains `challengeId`, `did`, a Base64URL `payload`, and
   `expiresAt`. Challenges expire after five minutes.
2. Decode `payload` from Base64URL and sign those exact bytes with the
   matching Basis Ed25519 private key. Submit
   `POST /api/did/link/challenges/{challengeId}/complete` with JSON
   `{"signature":"<base64url-signature>"}`.
3. On success the DID is inserted into `account_did_keys`. A DID can belong
   to only one global account. Failed signature attempts consume the challenge;
   challenge completion is serialized in a database transaction to prevent
   concurrent replay.

Set `DID_PROOF_ISSUER` to the canonical public HTTPS issuer URL before enabling
the account authentication layer. The issuer is included in the signed payload.
Never store private keys or ask clients to upload them. A successful OIDC login
alone does not prove control of a Basis DID; account linking requires this
separate signature proof.

The existing Basis plugin-message authentication flow remains separate during
the migration. It continues to derive the caller UUID from the authenticated
Basis peer. Ordinary Basis chat and its ChatChannel are not modified. The
central service must not become a dependency for ordinary chat delivery; a
central-auth outage must not suppress or intercept chat traffic.
