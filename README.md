# Basis AccountServ

Anope-inspired account and operator services for [Basis Server](https://github.com/BasisVR/Basis), distributed as an independent Basis Server package and installed with `basispm`.

> **Development status: bootstrap scaffold.** The package entry point and package metadata are in place. NickServ/OperServ command handling, persistent account storage, password verification and permission synchronization are not yet enabled. They depend on the server-side chat interception API described below.

## Installation

From a Basis Server checkout:

```bash
basispm --project . server-install https://github.com/nodokaha/Basis-AccountServ.git
basispm --project . server-restore
```

The package is compiled into the `BasisNetworkServer` assembly using Basis' `basisServer.modules` package mechanism.

## Planned commands

| Command | Purpose |
|---|---|
| `!nick register <password>` | Register the authenticated Basis identity |
| `!nick identify <password>` | Authenticate an account session |
| `!nick info [name]` | Show account information |
| `!nick logout` | End the account session |
| `!oper help` | Show operator commands |
| `!oper add <uuid> admin` | Grant administrator role |
| `!oper add <uuid> moderator` | Grant moderator role |
| `!oper del <uuid>` | Revoke AccountServ role |
| `!oper list` | List registered operators |

## Security model

- Authorization is evaluated on the server against the authenticated Basis UUID, never a client-supplied display name or role flag.
- Passwords will be stored as salted, slow password hashes; plaintext passwords will never be persisted or logged.
- Operator bootstrap is performed from server configuration / existing Basis administrator permissions. Chat commands cannot grant their caller an initial administrator role.
- AccountServ roles are mapped to Basis permission nodes; they do not replace Basis' permission manager.
- Registration and login are rate-limited and account lockout is temporary.
- The package will not accept an arbitrary UUID as proof of identity: commands operate on the UUID associated with the sending authenticated peer.

## Required Basis integration

Basis' package system compiles package modules directly into `BasisNetworkServer`. The existing `NetworkServer.ServerStarted` and `ServerStopping` lifecycle events are sufficient for startup and shutdown, and the existing `PermissionIntegration` is the authority for server permissions.

However, the current `BasisNetworkChat.HandleChatMessage` path sanitizes and broadcasts messages without exposing a public pre-broadcast command event. A package lifecycle entry cannot consume chat messages on its own. A small upstream hook is therefore required, for example a server-side event invoked after chat deserialization/sanitization and before broadcast, with the authenticated `NetPeer` and sanitized message. AccountServ can then consume recognized service commands and suppress their public broadcast.

Until that hook exists, this scaffold deliberately does not pretend that chat commands work. No Harmony patching, packet interception from a second listener, or client-trusted authorization is used.

## Configuration

Copy `config/accountserv.example.json` to the server's config directory and review every setting before enabling the service. The sample is intentionally disabled by default.

## License

MIT
