# Basis AccountServ

Anope-inspired account and operator services for [Basis](https://github.com/BasisVR/Basis). The package provides a private client-to-server AccountServ message channel and a small in-game management panel. It does not replace or intercept Basis' ordinary chat.

## Features

- Account registration bound to the authenticated Basis UUID
- Identify / logout / session status
- PBKDF2-HMAC-SHA256 password derivation
- One-time challenge-response authentication; the password itself is never sent over the network
- Failed-login lockout and expiring sessions
- Admin/moderator role assignment through Basis' existing permission groups
- Private F8 panel in the Unity client
- Ordinary public chat continues to work unchanged

## Installation

AccountServ has a server half and a Unity client half. Install both.

### Basis Server

From a Basis Server checkout:

```bash
basispm --project . server-install https://github.com/nodokaha/Basis-AccountServ.git
basispm --project . server-restore
```

Copy `config/accountserv.example.json` to `config/accountserv.json`, then set `enabled` to `true`.

### Unity client

Add the Git package to the Unity project's `Packages/manifest.json` dependencies:

```json
{
  "dependencies": {
    "com.nodokaha.accountserv": "https://github.com/nodokaha/Basis-AccountServ.git"
  }
}
```

Alternatively, add the repository URL through Unity Package Manager's **Add package from Git URL**.

After the package is imported and the client connects to a server with AccountServ enabled, press **F8** to open the AccountServ panel.

The Unity client integration is compiled into its own assembly referencing Basis Framework and BasisNetworkCore. The server half is installed separately through basispm.

The panel communicates over a dedicated reliable plugin message registered through Basis' dynamic message registry. The server replies only to the requesting authenticated peer. The message does not use `ChatChannel` and is not broadcast to other players.

## Using the panel

| Panel | Action |
|---|---|
| Register | Create an account and automatically identify |
| Identify | Sign in to an account already bound to this Basis UUID |
| Status | Check the current AccountServ session |
| Logout | End the AccountServ session |
| Operator | Assign `admin`, `moderator`, or `none` to a target Basis UUID |

The Operator page is accepted only when the requesting UUID is already in Basis' `admin` group. AccountServ does not provide a way to bootstrap the first administrator; use Basis' existing administrator configuration.

## Password and message security

- The client generates a random 16-byte salt and derives credential keys with PBKDF2-HMAC-SHA256 (210,000 iterations).
- Registration sends the salt and a SCRAM-style stored key, not the password.
- Identification uses a random 32-byte, one-use challenge that expires after one minute. The client sends a proof derived from the password; the server verifies it against the stored key.
- Failed identify attempts are counted per authenticated Basis UUID and trigger the configured temporary lockout.
- The private plugin channel is a routing boundary, not a claim that every Basis transport encrypts all packets. Use a trusted network/secure transport when confidentiality of all traffic is required.

The client currently enforces a 12-character minimum locally. The server also checks the configured minimum against the reported password length; because the password is intentionally never transmitted, a modified client can bypass this client-side length policy. AccountServ must never treat password length as an authorization boundary.

## Server configuration

`config/accountserv.example.json`:

```json
{
  "enabled": true,
  "database": "config/accountserv.db",
  "security": {
    "minimumPasswordLength": 12,
    "maximumFailedLogins": 5,
    "lockoutSeconds": 300,
    "sessionLifetimeMinutes": 1440
  }
}
```

Accounts are stored in `config/accountserv.db`. Sessions and pending challenges are held in memory; sessions are cleared when the server restarts.

## Server-side API

Trusted server-side modules can call `BasisAccountServ.AccountServService`:

- `Register(authenticatedUuid, accountName, password, out error)`
- `Identify(authenticatedUuid, accountName, password, out error)`
- `IsIdentified(authenticatedUuid)`
- `GetIdentifiedAccount(authenticatedUuid)`
- `Logout(authenticatedUuid)`
- `ListAccounts()`
- `SetOperatorRole(actorUuid, targetUuid, role, out error)`

For network clients, use the private message endpoint; do not add a handler that accepts a client-provided UUID or a plaintext password.

## Architecture and limitations

- The server registers `com.nodokaha.accountserv.dm` as a reliable-ordered plugin message.
- The Unity client registers a matching handler with `BasisClientMessageRegistry`.
- The server resolves the caller UUID from `NetworkServer.AuthIdentity.NetIDToUUID(peer, ...)`.
- All role changes are authorized server-side against Basis' existing `admin` group.
- The AccountServ panel is a private client-to-server interface, not player-to-player direct messaging.
- Ordinary `ChatChannel` handling is not modified, suppressed, or observed.
- The Unity client package must be installed separately from the server's basispm package.

## CI

GitHub Actions validates package metadata and compiles the server module against the `developer` branch of the official Basis repository. The Unity-only client assembly is checked for required source files and assembly references; it requires the Unity editor and is not compiled by the .NET server CI.

## License

MIT
