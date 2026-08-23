# The Home Assistant connection

How the app talks to your server, and what to do when it will not.

- [What it needs](#what-it-needs)
- [Getting a token](#getting-a-token)
- [The address](#the-address)
- [How the connection behaves](#how-the-connection-behaves)
- [What it reads](#what-it-reads)
- [What it sends](#what-it-sends)
- [Which entities appear](#which-entities-appear)
- [Troubleshooting](#troubleshooting)

---

## What it needs

Nothing installed on the server. No custom integration, no HACS component, no add-on.

Home Assistant's WebSocket API is a first-class, documented interface that already does everything
this app needs: authenticate, subscribe to state changes, read the area and device registries, and
call services. A custom integration would add a moving part on the server, another thing to keep
updated, and no capability.

You need two things: **the address of your instance** and **a Long-Lived Access Token**.

## Getting a token

1. In Home Assistant, click your user name at the bottom of the sidebar.
2. Go to the **Security** tab.
3. Scroll to **Long-Lived Access Tokens** and choose **Create Token**.
4. Name it something you will recognise later — "Fate Takes You Home" — and copy the value.

Home Assistant shows the token exactly once. If you lose it, delete that entry and make another.

The token inherits the permissions of the account that created it, so a token made by an
administrator can do administrator things. If that matters to you, make a dedicated non-admin
Home Assistant user and create the token as them.

Tokens do not expire on their own. A token that stops working was revoked, or its user was deleted.

## The address

Whatever you type into a browser to reach Home Assistant from the same PC. The app is forgiving
about the form:

| You type | It uses |
| --- | --- |
| `http://homeassistant.local:8123` | as given |
| `homeassistant.local:8123` | assumes `http` |
| `https://ha.example.com` | `wss` for the socket |
| `https://example.com/ha` | keeps the `/ha` prefix, for reverse-proxy subpaths |
| `ws://host:8123` | maps back to `http` for REST calls |

Trailing slashes are ignored.

**Self-signed certificates.** If you terminate TLS yourself with a certificate that does not chain
to a trusted root, tick *Accept self-signed certificates*. Understand what it does: it disables the
check that would otherwise catch someone intercepting the connection. Only use it on a network you
control, for a certificate you issued.

## How the connection behaves

The client is supervised. Once configured, it owns its own lifecycle and you should never need to
press anything:

1. Opens a WebSocket to `/api/websocket`.
2. Waits for `auth_required`, sends the token, expects `auth_ok`.
3. Subscribes to `state_changed`.
4. Reads the full state machine and the registries.

If the socket drops, it reconnects with **exponential backoff** — 2 seconds, then 4, 8, 16, up to a
two-minute ceiling — so a server that is rebooting is waited out rather than hammered.

An **application-level ping** runs every 30 seconds. A TCP connection can stay open long after the
thing at the other end is gone; a missed pong is what actually detects that, and it fails the
connection so the supervisor can rebuild it.

**A rejected token is terminal.** Retrying a bad credential forever is pointless and looks like a
network problem, so the client stops and says so. Fix the token in Settings and it starts again.

**After every reconnect the entity list is re-read in full.** Changes during an outage were never
delivered, and patching around that would leave tiles quietly lying about the state of your house.

## What it reads

| Command | Why |
| --- | --- |
| `get_states` | Every entity and its current state |
| `subscribe_events` (`state_changed`) | Live updates |
| `config/area_registry/list` | Room names |
| `config/floor_registry/list` | Floors, on core 2024.4 and later |
| `config/device_registry/list` | Which device an entity belongs to |
| `config/entity_registry/list` | Area overrides, hidden and disabled flags, entity category |
| `get_services` | The callable service list |
| `get_config` | Location name, version, unit system |
| `auth/current_user` | Confirms which account the token belongs to, for the connection test |

The registries are what turn a flat list of entity ids into rooms. If they cannot be read the app
carries on and groups by domain instead — that is a degraded experience, not a failure.

An entity's area is resolved the way Home Assistant's own UI resolves it: the entity's own area
assignment wins, and otherwise it inherits the area of its device.

## What it sends

Only `call_service`, and only when you press something. There is no polling and no background
activity beyond the keepalive ping.

Intent is mapped to the right service per domain, which is not always the obvious one:

| You press | It calls |
| --- | --- |
| A light, switch, fan | `homeassistant.toggle` |
| A scene | `scene.turn_on` |
| A script | `script.turn_on` |
| An automation | `automation.trigger` with `skip_condition: true` |
| A button | `button.press` |
| A cover | `cover.open_cover` / `cover.close_cover` |
| A lock | `lock.lock` / `lock.unlock` |

`automation.trigger` bypasses the automation's own conditions, which is Home Assistant's default
and almost always what somebody pressing a button by hand means. Enabling and disabling an
automation is separate, and does not fire it.

Slider changes are **debounced** — the call goes out once you stop dragging, not once per frame —
and incoming echoes are ignored briefly afterwards, so the thumb does not jump backwards under your
finger while a light ramps.

## Which entities appear

The browser shows entities that are:

- in a domain the app can do something with,
- not **disabled** in the entity registry,
- not **hidden** in the entity registry,
- not a **config or diagnostic** entity, unless *Show diagnostic entities* is on,
- not **unavailable**, unless *Show unavailable* is on.

Search matches the friendly name, the entity id, **and the area name** — so "kitchen" finds the
ceiling light even if nobody ever named it after the room it is in.

## Troubleshooting

**"That address answered, but not with a WebSocket."**
Something is at that address, but it is not Home Assistant, or a proxy is intercepting the upgrade.
Check the port. Open the address in a browser from this PC.

**"The server refused the WebSocket upgrade."**
A reverse proxy is stripping the upgrade headers. Both of these are required:

```nginx
proxy_set_header Upgrade    $http_upgrade;
proxy_set_header Connection "upgrade";
```

**"Home Assistant rejected the access token."**
Revoked, or its user was deleted. Create a new one. Also check you pasted the whole thing — tokens
are long and easy to truncate.

**The connection keeps dropping every minute or so.**
A proxy is timing out an idle WebSocket. Raise its read timeout above 60 seconds:

```nginx
proxy_read_timeout 3600s;
proxy_send_timeout 3600s;
```

**Nothing appears after connecting.**
Check the log — Help → Open the log. If it says how many entities it loaded, the connection is fine
and the filters are hiding things: turn on *Show unavailable* and *Show diagnostic entities*.

**It works in a browser but not from the app.**
The browser may be reaching a different address than you typed, via a saved redirect or a service
worker. Try the exact address in a private window.

**Turning verbose logging on** (Settings → Files and diagnostics) records every message exchanged
with Home Assistant. It is noisy, and it is the fastest way to see what the server actually said.
The access token is never written to the log.
