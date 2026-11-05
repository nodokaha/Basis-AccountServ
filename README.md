# Basis AccountServ

Anope-inspired account and operator services for [Basis Server](https://github.com/BasisVR/Basis), distributed as an independent Basis Server package and installed with `basispm`.

> **Development status: minimal account API implemented.** Persistent account registration, PBKDF2 password verification, UUID-bound sessions, lockout, and guarded operator-role assignment are implemented. Chat commands and a console command adapter are not implemented.

## Installation

From a Basis Server checkout:

```bash
basispm --project . server-install https://github.com/nodokaha/Basis-AccountServ.git
basispm --project . server-restore
```

Copy `config/accountserv.example.json` to the server's `config/accountserv.json`, then set `enabled` to `true`.

The package is compiled into the `BasisNetworkServer` assembly using Basis' `basisServer.modules` package mechanism.

## Implemented API

The service is available to trusted server-side code through `BasisAccountServ.AccountServService`:

- `Register(authenticatedUuid, accountName, password, out error)`
- `Identify(authenticatedUuid, accountName, password, out error)`
- `IsIdentified(authenticatedUuid)`
- `GetIdentifiedAccount(authenticatedUuid)`
- `Logout(authenticatedUuid)`
- `ListAccounts()`
- `SetOperatorRole(actorUuid, targetUuid, role, out error)`

Example (the UUID must come from Basis' authenticated peer identity, not from client input):

```csharp
if (NetworkServer.AuthIdentity.NetIDToUUID(peer, out string uuid))
{
    if (AccountServService.Identify(uuid, accountName, password, out string error))
    {
        // This Basis identity now has an AccountServ session.
    }
}
```

### Current behavior

- Accounts are stored in `config/accountserv.db` as JSON. Passwords are stored only as salted PBKDF2-HMAC-SHA256 hashes.
- An account is bound to the Basis UUID that registered it. Another UUID cannot identify into that account.
- Sessions are in-memory and expire after the configured lifetime; server restart logs all sessions out.
- Login failures are rate-limited per UUID with a temporary lockout.
- Operator roles use Basis' existing permission groups. Only a UUID already in Basis' `admin` group can call `SetOperatorRole`; supported roles are `admin`, `moderator`, and `none`.
- The configured minimum password length, failed-login limit, lockout duration, and session lifetime are applied at startup.

## Not implemented yet

- `!nick register / identify / info / logout` chat commands
- `!oper` chat commands and server-console command registration
- Automatic bootstrap from `bootstrap.operatorUuids`
- Account linking for multiple Basis UUIDs

The current `BasisNetworkChat.HandleChatMessage` path does not expose a public pre-broadcast message event. This implementation intentionally does not intercept or suppress ordinary chat. It exposes the account API so other trusted server modules can use it now; a future command adapter can call the same API if Basis later exposes a suitable input hook.

## Security notes

- Pass only UUIDs resolved from the authenticated Basis peer via `NetworkServer.AuthIdentity`.
- Never accept a UUID, role, or permission flag supplied by a client as proof of identity.
- The account database contains password hashes and should be backed up and access-restricted like other server secrets.
- The service does not replace Basis' permission manager.

## CI validation

GitHub Actions runs on pushes to `main`, pull requests, and manual dispatch.

- Validates package metadata and the example configuration.
- Checks out the `developer` branch of the official `BasisVR/Basis` repository.
- Injects the package source into `BasisNetworkServer` and builds the project with .NET 10.

The CI build is the compatibility check against the upstream server. It does not yet provide automated behavioral tests for account registration and authentication.

## License

MIT
