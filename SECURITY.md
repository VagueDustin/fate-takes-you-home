# Security

## Reporting a vulnerability

Open a [security advisory](https://github.com/VagueDustin/fate-takes-you-home/security/advisories/new)
rather than a public issue. Please include what you did, what happened, and what you expected. A
proof of concept helps but is not required.

## What this application is

A desktop client. It runs as a normal user, talks to one Home Assistant instance that you name, and
does nothing else. It opens no listening ports, contacts no other host, and has no telemetry,
analytics or update check.

## The access token

A Home Assistant Long-Lived Access Token is a **bearer credential with no expiry** that carries the
permissions of the account that created it. It is the one genuinely sensitive thing this
application holds, and it is treated accordingly.

**At rest** it is encrypted with the Windows Data Protection API at `CurrentUser` scope, with
application-specific entropy, and stored as a Base64 blob in `settings.json`. So:

- Another account on the same machine cannot read it.
- Copying `settings.json` to another machine yields nothing usable.
- A settings file pasted into a bug report, or swept up in a backup or a synced roaming profile,
  does not leak the token.

If Windows data protection is unavailable — which happens in some sandboxed contexts — the token is
**not stored at all**. Holding nothing is better than holding a credential in plain text.

**In memory** it is held as a `string`, which is honest about its limits: .NET strings are immutable
and unpinned, so the value may persist in the managed heap until collection. `SecureString` would be
theatre here — it has to be decrypted to be sent over the socket, and Microsoft's own guidance is
not to use it for new work.

**In the UI** it is never a bindable property. The password box pushes the value straight to the
view model, which encrypts it before anything persistent sees it. Displayed, it is reduced to its
last four characters, so somebody can confirm which token is stored during a screen share without
putting it on screen.

**In the log** it never appears. Verbose logging records the messages exchanged with Home Assistant;
the authentication frame is not one of them.

### What this does not protect against

Malware already running as your Windows account can ask DPAPI to decrypt the blob, exactly as the
application does. Nothing client-side can prevent that, and any product claiming otherwise is
selling you something. What the encryption buys is protection against the realistic problems: other
accounts, copied files, synced profiles and backups.

If a token is exposed, revoke it in Home Assistant — profile → Security → Long-Lived Access Tokens —
and create a new one. Revocation is immediate.

## Transport

TLS is used whenever the address you give is `https` or `wss`, with the system certificate store and
normal validation.

**Accept self-signed certificates** disables certificate validation for the connection. It exists
because self-signed certificates on a home LAN are common, and it is off by default. Turning it on
means an attacker positioned between this PC and your server can present any certificate and be
believed. Only use it on a network you control, for a certificate you issued yourself.

## What it writes

| Path | Contents |
| --- | --- |
| `%APPDATA%\VagueDustin Enterprises\Fate Takes You Home\` | `settings.json`, your themes |
| `%LOCALAPPDATA%\VagueDustin Enterprises\Fate Takes You Home\Logs\` | Rolling log, 2 MB × 4 files |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | The autostart entry, only when you enable it |

Nothing is written to the installation directory, so Program Files can stay read-only for standard
users. The application manifest requests `asInvoker` and the app never elevates — which is also why
the autostart setting can be toggled from inside it without an administrator prompt.

## Third-party code

| | |
| --- | --- |
| `CommunityToolkit.Mvvm` | MIT |
| `Microsoft.Extensions.DependencyInjection` | MIT |
| `System.Security.Cryptography.ProtectedData` | MIT |
| Inter, Cinzel, Crimson Pro | SIL Open Font License 1.1 |

The dependency list is deliberately short. The tray icon, the WebSocket client, the logger, the
theme engine and the easing solver are all in this repository, where they can be read.

## Supported versions

Pre-1.0. Fixes go to the latest release only.
