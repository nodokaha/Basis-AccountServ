# Basis AccountServ

Anope-inspired account and operator services for [Basis](https://github.com/BasisVR/Basis). AccountServ adds account registration, authentication, session management, and administrator-controlled role assignment.

The package consists of two parts:

- **Basis Server module** — installed with `basispm`.
- **Unity client module** — installed separately through Unity Package Manager (UPM).

AccountServ uses a dedicated private client-to-server plugin-message route. It does **not** replace, intercept, inspect, or suppress Basis' ordinary `ChatChannel`. AccountServ requests are handled by the server and replies are sent only to the requesting peer.

## Features

- Register an AccountServ account bound to the authenticated Basis UUID
- Identify, logout, and inspect the current AccountServ session
- PBKDF2-HMAC-SHA256 credential derivation
- One-time challenge-response authentication; the password itself is never sent over the network
- Failed-login lockout and expiring sessions
- Assign `admin`, `moderator`, or `none` roles through Basis' existing permission groups
- In-game Unity management panel opened from the **Basis main-menu Account button** or with **F8**
- Ordinary public chat remains unchanged

## Requirements

- A Basis Server checkout with `basispm` available
- A Unity client project using the compatible Basis Framework and BasisNetworkCore assemblies
- The AccountServ server module and Unity client package must both be installed

## Installation

### 1. Install the server module

From the Basis Server checkout, install the package from the repository's default branch and restore the server project:

```bash
basispm --project . server-install https://github.com/nodokaha/Basis-AccountServ.git
basispm --project . server-restore
```

### 2. Configure the server

The Basis Server module reads `config/accountserv.json` relative to the Basis Server process working directory when `NetworkServer.ServerStarted` fires. The repository contains only the template `config/accountserv.example.json`; copy it to the live filename and edit it:

```bash
mkdir -p config
cp config/accountserv.example.json config/accountserv.json
```

Example:

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

The example intentionally defaults to `"enabled": false`. Set it to `true` in the live file only when you are ready to enable AccountServ.

#### Environment variables

The server module can also be configured entirely through environment variables. Environment variables override JSON values, and JSON values override built-in defaults. If the JSON file does not exist, the module can still start when `ACCOUNTSERV_ENABLED=true` is supplied.

| Environment variable | Default | Description |
|---|---:|---|
| `ACCOUNTSERV_CONFIG_FILE` | `config/accountserv.json` | JSON config file path |
| `ACCOUNTSERV_ENABLED` | `false` | Enables AccountServ |
| `ACCOUNTSERV_DATABASE` | `config/accountserv.db` | Account database path |
| `ACCOUNTSERV_SECURITY_MINIMUM_PASSWORD_LENGTH` | `12` | Minimum reported password length |
| `ACCOUNTSERV_SECURITY_MAXIMUM_FAILED_LOGINS` | `5` | Failed Identify attempts before lockout |
| `ACCOUNTSERV_SECURITY_LOCKOUT_SECONDS` | `300` | Lockout duration |
| `ACCOUNTSERV_SECURITY_SESSION_LIFETIME_MINUTES` | `1440` | AccountServ session lifetime |

For example, set these on the **Basis Server container/service** in Docker Compose or Coolify:

```yaml
environment:
  ACCOUNTSERV_ENABLED: "true"
  ACCOUNTSERV_DATABASE: "/data/accountserv.db"
  ACCOUNTSERV_SECURITY_MINIMUM_PASSWORD_LENGTH: "12"
  ACCOUNTSERV_SECURITY_MAXIMUM_FAILED_LOGINS: "5"
  ACCOUNTSERV_SECURITY_LOCKOUT_SECONDS: "300"
  ACCOUNTSERV_SECURITY_SESSION_LIFETIME_MINUTES: "1440"
```

You can omit the JSON file when all required values are provided through environment variables. The database path should point to persistent storage; back it up with the rest of your server data. Do not commit live configuration or account database files to a public repository. Restart the Basis Server after changing environment variables or the JSON file; configuration is read at server startup.

### 3. Install the Unity client package

Add the package Git URL to the Unity project's `Packages/manifest.json` under `dependencies`:

```json
{
  "dependencies": {
    "com.nodokaha.accountserv": "https://github.com/nodokaha/Basis-AccountServ.git"
  }
}
```

You can also use Unity Package Manager's **Add package from Git URL** and enter:

```
https://github.com/nodokaha/Basis-AccountServ.git
```

The Unity integration is provided as the separate `BasisAccountServ.Unity` assembly. It references Basis Framework and BasisNetworkCore and registers its client plugin-message handler during Unity startup. It is not part of the server module installed by basispm.

### 4. Open the panel

Start the server with AccountServ enabled, launch the Unity client with the package installed, and connect to that server. Open the Basis main menu and select **Account**, or press **F8** as a shortcut, to open the AccountServ panel.

## Using the panel

| Panel action | Description |
|---|---|
| Register | Create an account for the authenticated Basis UUID and identify |
| Identify | Authenticate an existing account bound to this Basis UUID |
| Status | Check the current AccountServ session |
| Logout | End the current AccountServ session |
| Operator | Assign `admin`, `moderator`, or `none` to a target Basis UUID |

The Operator action is accepted only when the requesting authenticated UUID is already in Basis' `admin` permission group. AccountServ does not bootstrap the first administrator. Configure the initial administrator through Basis' existing administrator/permission setup before using role management.

## Password and authentication security

- The client generates a random 16-byte salt and derives credential material with PBKDF2-HMAC-SHA256 using 210,000 iterations.
- Registration sends the salt and a SCRAM-style stored key, not the password.
- During Identify, the server issues a random 32-byte one-time challenge. The challenge expires after one minute.
- The client returns a proof derived from the password. The server verifies that proof against the stored key.
- Failed Identify attempts are counted per authenticated Basis UUID and can trigger the configured temporary lockout.
- Accounts created by the earlier PBKDF2-only package version are upgraded to the stored-key format on their first challenge-based Identify attempt.

The private plugin channel provides message routing and peer scoping; it is **not** a claim that every Basis transport encrypts every packet. Use a trusted network or a secure transport when confidentiality of all traffic is required.

The client currently enforces a 12-character minimum locally. The server checks the configured minimum against the reported password length, but the password is intentionally never transmitted. A modified client can therefore falsify this reported length. Password length validation must not be treated as an authorization or anti-compromise boundary.

## Server configuration reference

| Key | Default | Meaning |
|---|---:|---|
| `enabled` | `false` | Enables or disables AccountServ |
| `database` | `config/accountserv.db` | Account database path |
| `security.minimumPasswordLength` | `12` | Client-side minimum; see the security note above |
| `security.maximumFailedLogins` | `5` | Failed Identify attempts before lockout |
| `security.lockoutSeconds` | `300` | Lockout duration |
| `security.sessionLifetimeMinutes` | `1440` | AccountServ session lifetime |

Accounts are stored in the configured database. Active sessions and pending authentication challenges are held in memory; sessions are cleared when the server restarts.

## Server-side API

Trusted server-side modules can use `BasisAccountServ.AccountServService`:

- `Register(authenticatedUuid, accountName, password, out error)`
- `Identify(authenticatedUuid, accountName, password, out error)`
- `IsIdentified(authenticatedUuid)`
- `GetIdentifiedAccount(authenticatedUuid)`
- `Logout(authenticatedUuid)`
- `ListAccounts()`
- `SetOperatorRole(actorUuid, targetUuid, role, out error)`

These APIs are for trusted server-side code. Network clients must use the private message endpoint. Do not expose an endpoint that trusts a client-provided UUID or accepts a plaintext password from the network.

## Architecture and boundaries

- The server registers `com.nodokaha.accountserv.dm` as a reliable-ordered plugin message.
- The Unity client registers the matching handler with `BasisClientMessageRegistry`.
- The server derives the caller's UUID from the authenticated peer using Basis' network identity mapping; it does not trust a UUID supplied in the request.
- Replies are sent only to the requesting peer and are not broadcast to other players.
- Role changes are authorized server-side against Basis' existing `admin` permission group.
- The Basis main-menu **Account** button and F8 shortcut open the same private client-to-server AccountServ interface, not player-to-player direct messaging.
- Ordinary `ChatChannel` handling is not modified, intercepted, observed, or suppressed.
- The server and Unity client components are installed independently.

## Testing

The current GitHub Actions workflow does **not** compile the Unity client assembly.
Its checks are split as follows:

- **Package metadata and configuration:** parses package/config JSON, checks required source files exist, and checks expected strings in the Unity asmdef references. This is not a C# compilation.
- **Compile AccountServ server module:** injects the server source into the official Basis `developer` checkout and runs `dotnet build` on `BasisNetworkServer`.
- **Build central authentication API:** builds the ASP.NET Core API with .NET 10.
- **Test Basis DID proof verification:** runs the central API's .NET unit tests.

Therefore, a green CI run does not establish that `BasisAccountServ.Unity` compiles. The Unity client assembly depends on Unity and Basis assemblies and must be compiled in a compatible Unity project/editor. Until a Unity Editor build job is added, this remains a manual validation step.

Before production use, open a test project using the same Basis Framework and BasisNetworkCore version targeted by this package, add this repository through UPM, let Unity import and compile the package, and confirm the Console contains no C# compilation errors. Then validate the following in a compatible Unity Editor and a test Basis server:

1. The Unity project imports `com.nodokaha.accountserv` without assembly or compilation errors.
2. With AccountServ enabled on the server, the client can open the panel from the Basis main-menu Account button and with F8.
3. Register creates an account associated with the connected Basis UUID.
4. Identify succeeds for the matching account and Status reports the session.
5. Logout clears the session; a subsequent Status reflects that state.
6. An existing Basis `admin` can assign `admin`, `moderator`, and `none`; a non-admin cannot change roles.
7. Replies are visible only to the requesting client.
8. Ordinary public chat continues to work normally and is not intercepted by AccountServ.

Use test accounts and a non-production server for this validation. Do not treat a successful CI run alone as proof of live network or Unity runtime compatibility.

## License

MIT


## Central authentication service

The repository now includes the first deployment foundation for a shared AccountServ
identity service. See [Central authentication architecture](docs/central-auth.md)
for the API service, PostgreSQL identity schema, server-scoped role model, and
deployment boundary.

The central API foundation includes an Ed25519 DID proof-of-possession linking flow, but its authenticated-account middleware and OIDC endpoints are not yet enabled. Account registration/login and dashboard visitor sessions remain unavailable until the complete Authorization Code + PKCE flow is implemented.
The existing Basis plugin-message flow remains operational and ordinary chat
handling is unchanged.
