---
sidebar_position: 5
title: Updating
---

# Updating

:::warning
**Back up before you update.**

WarTable copies the database before it applies schema changes, so a failed
change is not the main risk. The manual copy protects you from everything
else: a bad image, a pull that half-succeeded, a container that no longer
starts, or a version you want to return to. It costs one command.
:::

WarTable ships as a container image and as a standalone binary. They update
differently. Find the one you run, then follow that section.

| You run | Section |
| --- | --- |
| The container image with `docker-compose.yml` | [Container](#container) |
| The standalone binary | [Standalone binary](#standalone-binary) |

## What the version tag means

Image tags are labels on a published image, not Git tags. Nothing resolves or
rewrites them.

| Tag | What it tracks |
| --- | --- |
| `3.0.0` | Exactly that release. Never changes. |
| `3.0` | The newest `3.0.x`, including patch fixes. |
| `3` | The newest non-breaking release in the major. |
| `latest` | The newest release of any kind, including a breaking major. |

The shipped `docker-compose.yml` defaults to `3`. That means
`docker compose pull` brings patch releases and new features within the major
without you editing anything, and a `4.0.0` never replaces your running server
on its own. Pin a full version only when you want to stop moving.

## Container

The container keeps its database in the named volume `wartable-data`. The steps
below assume the `docker-compose.yml` from this repository and a directory you
can run `docker compose` in.

### 1. Back up the volume

```sh
docker run --rm \
  -v wartable-data:/data \
  -v "$PWD":/backup \
  alpine tar czf "/backup/wartable-$(date -u +%Y%m%dT%H%M%SZ).tgz" -C /data .
```

Check that the archive contains `wartable.db` before going further:

```sh
tar tzf wartable-*.tgz | grep wartable.db
```

### 2. Choose the version

Leave the image line alone to take the newest release in the major you are on.
To move to a specific version, set it in `.env` next to the Compose file:

```sh
WARTABLE_TAG=3.0.0
```

### 3. Pull, then recreate

```sh
docker compose pull
docker compose up -d
```

`pull` is the step that matters. `docker compose up -d` on its own reuses the
image already on disk, so a deployment that only runs `up -d` stays on whatever
it fetched last. Pulling first also means a failed pull leaves the running
container untouched.

### 4. Check it

```sh
curl -fsS http://127.0.0.1:5110/api/health
```

A healthy server answers `{"status":"ok","service":"WarTable"}`. Compose runs the
same check every 30 seconds, so `docker compose ps` shows `healthy` once it
passes.

### 5. If it does not come up

Put the old tag back and recreate:

```sh
WARTABLE_TAG=<previous version> docker compose up -d
```

If the database itself is the problem, stop the container and restore the
archive over the volume:

```sh
docker compose down
docker run --rm -v wartable-data:/data -v "$PWD":/backup alpine \
  sh -c 'rm -rf /data/* && tar xzf /backup/wartable-<timestamp>.tgz -C /data'
docker compose up -d
```

### Coming from a bind-mounted `data` directory

:::danger
Earlier versions mounted `./data` from the host. The shipped Compose file now
uses the named volume `wartable-data`, so copying the file over an existing
install and running `up -d` starts an **empty** database: the volume and the
directory are different places.
:::

Copy the files once, before starting the new container:

```sh
docker compose down
docker run --rm \
  -v "$PWD/data:/from" \
  -v wartable-data:/to \
  alpine sh -c 'cp -a /from/. /to/'
docker compose up -d
```

Keep `./data` until you have confirmed the server serves your campaigns. It is a
copy, not a move.

## Standalone binary

The binary keeps the database in the file you chose with `--db`, by default
`~/.wartable/wartable.db`.

### 1. Back up the database

```sh
cp ~/.wartable/wartable.db ~/.wartable/wartable.db.$(date -u +%Y%m%dT%H%M%SZ)
```

Copy the whole database, not just the main file: if a `-wal` file sits beside
it, a copy of `wartable.db` alone can miss writes that have not been
checkpointed. Stop the service first if you want a cold copy.

### 2. Stop, replace, start

```sh
systemctl --user stop wartable      # or however you run it
# replace the binary with the new release
systemctl --user start wartable
```

If you have not registered it as a service yet, see
[Standalone binary](./standalone.md#run-it-as-a-user-service) for a complete
systemd unit, including the linger setting that keeps a user service alive after
you log out.

### 3. Check it

```sh
curl -fsS http://127.0.0.1:5110/api/health
```

## Rollback

:::note
Changing the tag back is enough only when the release did not change the
database schema.
:::

Schema changes are applied at startup. Before applying them, WarTable copies
the database next to itself and only proceeds when that copy exists. If a change
fails, the copy is restored and the process exits rather than serving a
half-changed database. Schema changes never run backwards, so rolling the image
back after a successful upgrade means restoring the archive you made in step 1,
not just changing the tag.

## What happens to your data

The database is the only persistent state. Container images and binaries are
replaceable. Schema changes carry existing campaigns, objectives, steps, and
notes forward, and they are applied at startup after the automatic copy.

The automatic copy is not a backup. It lives next to the database on the same
disk, and it is only as good as that disk. Keep at least the last two manual
backups, and test a restore at least once.
