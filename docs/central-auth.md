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
- account_basis_links maps one global account to a Basis UUID separately for
  each server. An untrusted client UUID must never establish this link; the
  game server must derive it from the authenticated Basis peer.
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

The existing Basis plugin-message authentication flow remains separate during
the migration. It continues to derive the caller UUID from the authenticated
Basis peer. Ordinary Basis chat and its ChatChannel are not modified. The
central service must not become a dependency for ordinary chat delivery; a
central-auth outage must not suppress or intercept chat traffic.
