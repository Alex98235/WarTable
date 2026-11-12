---
sidebar_position: 3
title: Standalone binary
---

# Standalone binary

The standalone binary is WarTable without the container. It is the right form
for a machine that does not run Docker, typically your own workstation where an
agent runs beside the server. The same binary can also back a bare-metal Linux
server, but the container is the supported always-on deployment.

The binary contains the web UI, the HTTP API, and both MCP transports. It keeps
all state in one SQLite file.

## Install

Download the archive for your platform from the
[releases page](https://github.com/Alex98235/WarTable/releases):

| Platform | Asset |
| --- | --- |
| Linux `x64` | `wartable-<version>-linux-x64.tar.gz` |
| Windows `x64` | `wartable-<version>-win-x64.zip` |

Each archive has a `.sha256` sidecar. The Linux archive contains a single file
named `wartable`; the Windows archive contains `wartable.exe`.

On Linux:

```sh
version=<version>
base="https://github.com/Alex98235/WarTable/releases/download/v${version}"
curl -fsSLO "${base}/wartable-${version}-linux-x64.tar.gz"
curl -fsSLO "${base}/wartable-${version}-linux-x64.tar.gz.sha256"
sha256sum -c "wartable-${version}-linux-x64.tar.gz.sha256"
tar xzf "wartable-${version}-linux-x64.tar.gz"
mkdir -p ~/.local/bin
install -m 0755 wartable ~/.local/bin/wartable
```

Make sure `~/.local/bin` is on your `PATH`. On Windows, expand the zip and put
`wartable.exe` in a directory on `PATH`.

The archive is self-contained in the .NET sense: it needs no .NET runtime. On
Linux it is still linked against glibc and ICU. A minimal host without ICU exits
at startup with `Couldn't find a valid ICU package`; install the distribution's
ICU package first, for example `libicu72` on Debian 12 or `libicu74` on Ubuntu
24.04. The Windows binary needs no extra components.

## Run it once

With no flags, the binary runs in `serve` mode on the loopback interface:

```sh
wartable
```

That starts the web UI and HTTP API at
[http://localhost:5110](http://localhost:5110). The defaults are:

| Setting | Default |
| --- | --- |
| Mode | `serve` |
| Host | `localhost` |
| Port | `5110` |
| Database | `~/.wartable/wartable.db` |

The parent directory is created if it does not exist. On Windows the database
lands at `%USERPROFILE%\.wartable\wartable.db`.

Because the default host is `localhost`, only this machine can reach it. To
accept connections from the LAN, bind the LAN interface or `0.0.0.0`:

```sh
wartable --host 0.0.0.0 --port 5110 --db ~/.wartable/wartable.db
```

Check that it is up:

```sh
curl -fsS http://127.0.0.1:5110/api/health
```

A healthy server answers `{"status":"ok","service":"WarTable"}`.

## Run it as a user service

A user service starts with your session and restarts after a crash. It does not
need root, and it keeps the database in your home directory.

Create the unit at `~/.config/systemd/user/wartable.service`:

```ini
[Unit]
Description=WarTable
After=network.target

[Service]
Type=simple
ExecStart=%h/.local/bin/wartable --host 127.0.0.1 --port 5110 --db %h/.wartable/wartable.db
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
```

`%h` expands to your home directory. Adjust the binary path if you installed it
somewhere else.

Load and start it:

```sh
systemctl --user daemon-reload
systemctl --user enable --now wartable
```

:::warning
Enable lingering, or the service stops when your last session ends.

```sh
loginctl enable-linger $USER
```

Without lingering, systemd tears down your user manager at logout, and WarTable
goes with it. The unit looks enabled and healthy while you are logged in, which
is why this is easy to miss. Check it with:

```sh
loginctl show-user $USER -p Linger
```

`Linger=yes` means the service survives logout and starts at boot.
:::

Follow the logs with:

```sh
journalctl --user -u wartable -f
```

## Run it system-wide instead

Use a system unit when the machine is a shared or always-on server: it starts at
boot without anyone logging in, and it does not depend on one user's session.
The trade-off is that the database belongs to the service user, so backups and
manual inspection need that user or root.

Create a service account and install the binary:

```sh
sudo useradd --system --home-dir /var/lib/wartable --no-create-home \
  --shell /usr/sbin/nologin wartable
sudo install -m 0755 wartable /usr/local/bin/wartable
```

Create `/etc/systemd/system/wartable.service`:

```ini
[Unit]
Description=WarTable
After=network.target

[Service]
Type=simple
User=wartable
Group=wartable
StateDirectory=wartable
ExecStart=/usr/local/bin/wartable --host 0.0.0.0 --port 5110 --db /var/lib/wartable/wartable.db
Restart=on-failure
RestartSec=5
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/var/lib/wartable

[Install]
WantedBy=multi-user.target
```

`StateDirectory=wartable` creates `/var/lib/wartable` and gives it to
`wartable:wartable`. `ProtectSystem=strict` makes the rest of the filesystem
read-only, and `ReadWritePaths` keeps the database directory writable. If you
move the database, change both `--db` and `ReadWritePaths`.

If the host has more than one interface, bind the LAN address instead of
`0.0.0.0` so the port is not exposed on every network.

Load and start it:

```sh
sudo systemctl daemon-reload
sudo systemctl enable --now wartable
```

Logs go to the system journal:

```sh
journalctl -u wartable -f
```

## Updating it

Back up the database first; the rule and the commands are in
[Updating](./updating.md#standalone-binary).

Then replace the binary and restart the unit. On Linux, replacing a running
executable in place fails with `Text file busy`, so move the new file over the
old path and restart:

```sh
curl -fsSLO "https://github.com/Alex98235/WarTable/releases/download/v<version>/wartable-<version>-linux-x64.tar.gz"
tar xzf "wartable-<version>-linux-x64.tar.gz"
mv wartable ~/.local/bin/wartable
systemctl --user restart wartable
```

For a system-wide unit, use `sudo mv wartable /usr/local/bin/wartable` and
`sudo systemctl restart wartable`.

Check the new process:

```sh
curl -fsS http://127.0.0.1:5110/api/health
```

If it does not come up, put the previous binary back and restart. If the release
changed the schema, restoring the database backup is the rollback path;
migrations do not run backwards.

## One database, one server

A co-located agent should use the stdio MCP mode against the same database file
the service uses:

```sh
wartable --mode mcp --db ~/.wartable/wartable.db
```

In stdio mode the process does not listen on a port. stdout carries MCP
JSON-RPC, logs go to stderr, and the process exits when stdin closes. Point the
agent's `command` at the same binary and the same `--db` path as the service.

:::warning
Run one HTTP server per database. Do not start a second `wartable` in `serve`
mode against the same file.

SQLite serializes the writes, but that does not make the topology supported.
The deployment contract assumes one HTTP server owns the database, and the
live-update path is built around that. A second server is not a replica; it is a
second owner. Multiple stdio MCP processes against the same file are fine, as
long as they are on the same local filesystem.
:::
