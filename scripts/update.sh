#!/usr/bin/env bash
#
# Manual WarTable update with a cold backup and automatic rollback.
#
# Usage:
#   ./scripts/update.sh <X.Y.Z> [--yes] [--dry-run] [--allow-latest]
#
#   <X.Y.Z>         Immutable release tag, with or without a leading "v".
#                   Pre-release suffixes (for example 0.3.0-rc.1) are allowed.
#                   "latest" is refused unless --allow-latest is given.
#   --yes           Skip the interactive confirmation.
#   --dry-run       Print the plan and exit without pulling or changing anything.
#   --allow-latest  Permit the mutable "latest" tag. Not recommended for
#                   production because it does not identify an immutable build.
#
# Privileges:
#   Run as a user that can talk to the Docker daemon and read the data
#   directory, or as root. On rollback, a root run restores the data directory
#   to UID/GID 10001:10001, which is the container user. If it runs
#   unprivileged and the data or database is unreadable, it stops with a sudo
#   and ownership hint instead of changing permissions on a live deployment.
#
# Defaults (all can be overridden with environment variables; relative paths
# are resolved from the repository root next to this script):
#   ENV_FILE=.env
#   COMPOSE_FILE=docker-compose.yml
#   DATA_DIR=./data
#   BACKUP_DIR=./backups
#   SERVICE_NAME=wartable
#   HEALTH_URL=http://127.0.0.1:5110/api/health
#   HEALTH_ATTEMPTS=60
#   HEALTH_INTERVAL=2
#   IMAGE_REPOSITORY=ghcr.io/alex98235/wartable
#
# Backups:
#   Every update writes a timestamped gzip archive of the whole data directory
#   to backups/wartable-<UTC timestamp>-from-<old>-to-<new>.tar.gz. The service
#   is stopped before the archive is made, so SQLite is copied cold.
#
# Rollback:
#   The target image is pulled before the service is touched. If the pull
#   fails, nothing changes. After a successful backup any later failure dumps
#   recent logs, stops the new container, preserves the failed data directory
#   as data.failed-<UTC timestamp>, restores the archive and the previous tag,
#   starts the previous image (pulling it if it is no longer local), and waits
#   for health. It exits 1 after a healthy rollback. If rollback cannot restore
#   health it exits 3 and prints manual recovery instructions.
#
# This is a manual procedure only. There is no timer, watcher, self-update, or
# GitHub polling here.

set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
ROOT_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"

ENV_FILE="${ENV_FILE:-.env}"
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.yml}"
DATA_DIR="${DATA_DIR:-./data}"
BACKUP_DIR="${BACKUP_DIR:-./backups}"
SERVICE_NAME="${SERVICE_NAME:-wartable}"
HEALTH_URL="${HEALTH_URL:-http://127.0.0.1:5110/api/health}"
HEALTH_ATTEMPTS="${HEALTH_ATTEMPTS:-60}"
HEALTH_INTERVAL="${HEALTH_INTERVAL:-2}"
IMAGE_REPOSITORY="${IMAGE_REPOSITORY:-ghcr.io/alex98235/wartable}"

resolve_path() {
    case "$1" in
        /*) printf '%s' "$1" ;;
        *) printf '%s/%s' "$ROOT_DIR" "${1#./}" ;;
    esac
}

ENV_FILE="$(resolve_path "$ENV_FILE")"
COMPOSE_FILE="$(resolve_path "$COMPOSE_FILE")"
DATA_DIR="$(resolve_path "$DATA_DIR")"
BACKUP_DIR="$(resolve_path "$BACKUP_DIR")"

# Trim trailing slashes so names such as "<data>.failed-<timestamp>" stay sane.
DATA_DIR="${DATA_DIR%/}"
BACKUP_DIR="${BACKUP_DIR%/}"

ENV_TMP=""
ROLLBACK_ARMED=0
ROLLBACK_RUNNING=0

log() {
    printf '%s\n' "$*"
}

warn() {
    printf 'warning: %s\n' "$*" >&2
}

die() {
    printf 'error: %s\n' "$*" >&2
    exit 1
}

usage() {
    cat <<'EOF'
Usage: ./scripts/update.sh <X.Y.Z> [--yes] [--dry-run] [--allow-latest]

  <X.Y.Z>         Immutable release tag, with or without a leading "v".
                  Pre-release suffixes such as 0.3.0-rc.1 are allowed.
  --yes           Skip the interactive confirmation.
  --dry-run       Print the plan and exit without pulling or changing anything.
  --allow-latest  Permit the mutable "latest" tag.

Environment overrides: ENV_FILE, COMPOSE_FILE, DATA_DIR, BACKUP_DIR,
SERVICE_NAME, HEALTH_URL, HEALTH_ATTEMPTS, HEALTH_INTERVAL, IMAGE_REPOSITORY.
EOF
}

utc_timestamp() {
    date -u +%Y%m%dT%H%M%SZ
}

sanitize_tag() {
    local safe
    safe="$(printf '%s' "$1" | sed 's/[^A-Za-z0-9._-]/_/g')"
    printf '%s' "${safe:-unknown}"
}

read_env_tag() {
    local line value

    line="$(grep -E '^[[:space:]]*(export[[:space:]]+)?WARTABLE_TAG[[:space:]]*=' "$ENV_FILE" 2>/dev/null | tail -n 1 || true)"
    [[ -n "$line" ]] || return 1

    line="${line%$'\r'}"
    value="${line#*=}"

    # Trim surrounding whitespace.
    value="${value#"${value%%[![:space:]]*}"}"
    value="${value%"${value##*[![:space:]]}"}"

    # Strip inline comments from unquoted values, then matching quotes.
    if [[ "$value" != \"* && "$value" != \'* ]]; then
        value="${value%%[[:space:]]#*}"
        value="${value%"${value##*[![:space:]]}"}"
    fi
    if [[ ${#value} -ge 2 ]]; then
        if [[ "$value" == \"*\" ]]; then
            value="${value:1:${#value}-2}"
        elif [[ "$value" == \'*\' ]]; then
            value="${value:1:${#value}-2}"
        fi
    fi

    value="${value%$'\r'}"
    [[ -n "$value" ]] || return 1
    printf '%s' "$value"
}

set_env_tag() {
    local tag="$1" tmp

    tmp="$(mktemp "${ENV_FILE}.tmp.XXXXXX")" || {
        warn "could not create a temporary file next to $ENV_FILE"
        return 1
    }
    ENV_TMP="$tmp"

    if ! awk -v tag="$tag" '
        BEGIN { replaced = 0 }
        /^[[:space:]]*(export[[:space:]]+)?WARTABLE_TAG[[:space:]]*=/ {
            print "WARTABLE_TAG=" tag
            replaced = 1
            next
        }
        { print }
        END {
            if (!replaced) {
                print "WARTABLE_TAG=" tag
            }
        }
    ' "$ENV_FILE" > "$tmp"; then
        rm -f "$tmp"
        ENV_TMP=""
        return 1
    fi

    # Keep the original mode and owner when the platform supports it.
    chmod --reference="$ENV_FILE" "$tmp" 2>/dev/null || true
    chown --reference="$ENV_FILE" "$tmp" 2>/dev/null || true

    if ! mv -f "$tmp" "$ENV_FILE"; then
        rm -f "$tmp"
        ENV_TMP=""
        return 1
    fi

    ENV_TMP=""
    return 0
}

compose() {
    docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" "$@"
}

image_ref() {
    printf '%s:%s' "$IMAGE_REPOSITORY" "$1"
}

pull_image() {
    local tag="$1"
    WARTABLE_TAG="$tag" docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" pull
}

image_digest() {
    local tag="$1"
    local ref digest

    ref="$(image_ref "$tag")"
    digest="$(docker image inspect --format '{{index .RepoDigests 0}}' "$ref" 2>/dev/null || true)"
    if [[ -z "$digest" || "$digest" == "<no value>" ]]; then
        digest="$(docker image inspect --format '{{.Id}}' "$ref" 2>/dev/null || true)"
    fi
    printf '%s' "${digest:-unknown}"
}

wait_for_health() {
    local attempt response

    for ((attempt = 1; attempt <= HEALTH_ATTEMPTS; attempt++)); do
        response=""
        if response="$(curl -fsS --max-time 5 "$HEALTH_URL" 2>/dev/null)" \
            && [[ "$response" =~ \"status\"[[:space:]]*:[[:space:]]*\"ok\" ]]; then
            return 0
        fi
        if ((attempt < HEALTH_ATTEMPTS)); then
            sleep "$HEALTH_INTERVAL"
        fi
    done
    return 1
}

start_previous_service() {
    if ! WARTABLE_TAG="$OLD_TAG" compose up -d --remove-orphans "$SERVICE_NAME"; then
        warn "could not start $SERVICE_NAME with $OLD_TAG"
        return 1
    fi
    if ! wait_for_health; then
        warn "$SERVICE_NAME did not become healthy at $HEALTH_URL"
        return 1
    fi
    log "Previous version is healthy."
    return 0
}

check_data_access() {
    if ((EUID == 0)); then
        return 0
    fi

    if [[ ! -d "$DATA_DIR" || ! -r "$DATA_DIR" || ! -x "$DATA_DIR" || ! -r "$DATA_DIR/wartable.db" ]]; then
        die "cannot read $DATA_DIR/wartable.db as $(id -un). The container writes as UID 10001. Re-run with sudo, or fix ownership first (for example: sudo chown -R 10001:10001 \"$DATA_DIR\"). This script will not chmod a live deployment."
    fi
}

restore_data_ownership() {
    if ((EUID == 0)); then
        if chown -R 10001:10001 "$DATA_DIR"; then
            log "Restored data ownership to 10001:10001."
        else
            warn "could not restore $DATA_DIR ownership to 10001:10001"
            return 1
        fi
    elif [[ ! -r "$DATA_DIR/wartable.db" ]]; then
        warn "restored data is not readable as $(id -un); run sudo chown -R 10001:10001 \"$DATA_DIR\""
        return 1
    fi
    return 0
}

print_plan() {
    local planned_backup
    planned_backup="$BACKUP_DIR/wartable-$(utc_timestamp)-from-$(sanitize_tag "$OLD_TAG")-to-$(sanitize_tag "$NEW_TAG").tar.gz"

    cat <<EOF
Update plan:
  Repository:   $ROOT_DIR
  Current tag:  $OLD_TAG
  Target tag:   $NEW_TAG
  .env file:    $ENV_FILE
  Compose file: $COMPOSE_FILE
  Data dir:     $DATA_DIR
  Backup dir:   $BACKUP_DIR
  Backup file:  $planned_backup
  Service:      $SERVICE_NAME
  Health check: $HEALTH_URL ($HEALTH_ATTEMPTS attempts, ${HEALTH_INTERVAL}s apart)
  Steps:        pull image; stop service; cold backup; update .env; recreate; wait for health
EOF
}

perform_rollback() {
    local reason="$1"
    local failed_dir health_ok=0 restore_ok=1

    ROLLBACK_ARMED=0
    ROLLBACK_RUNNING=1

    warn "Update failed: $reason"
    warn "Recent logs from $SERVICE_NAME:"
    compose logs --tail=100 "$SERVICE_NAME" >&2 || warn "could not read container logs"

    warn "Stopping the failed container..."
    compose stop "$SERVICE_NAME" >/dev/null 2>&1 || warn "could not stop $SERVICE_NAME"

    failed_dir="${DATA_DIR}.failed-$(utc_timestamp)"
    if [[ -d "$DATA_DIR" ]]; then
        if mv "$DATA_DIR" "$failed_dir"; then
            log "Failed data preserved at $failed_dir"
        else
            warn "could not preserve the failed data directory at $failed_dir"
            restore_ok=0
        fi
    fi

    if ((restore_ok)); then
        if ! mkdir -p "$DATA_DIR"; then
            warn "could not recreate $DATA_DIR"
            restore_ok=0
        fi
    fi

    if ((restore_ok)); then
        if ! tar -xzf "$BACKUP_FILE" -C "$DATA_DIR"; then
            warn "could not restore $BACKUP_FILE into $DATA_DIR"
            restore_ok=0
        fi
    fi

    if ((restore_ok)) && ! restore_data_ownership; then
        restore_ok=0
    fi

    if ! set_env_tag "$OLD_TAG"; then
        warn "could not restore WARTABLE_TAG=$OLD_TAG in $ENV_FILE"
        restore_ok=0
    fi

    if ! docker image inspect "$(image_ref "$OLD_TAG")" >/dev/null 2>&1; then
        warn "previous image $(image_ref "$OLD_TAG") is missing locally; pulling it..."
        if ! pull_image "$OLD_TAG"; then
            warn "could not pull the previous image"
            restore_ok=0
        fi
    fi

    if ((restore_ok)); then
        if ! WARTABLE_TAG="$OLD_TAG" compose up -d --remove-orphans "$SERVICE_NAME"; then
            warn "could not bring the previous image back up"
            restore_ok=0
        fi
    fi

    if ((restore_ok)); then
        log "Waiting for the previous version at $HEALTH_URL..."
        if wait_for_health; then
            health_ok=1
        else
            warn "the previous version did not become healthy"
        fi
    fi

    if ((health_ok)); then
        cat >&2 <<EOF
Rollback complete; the update failed.
  Restored tag:  $OLD_TAG
  Restored data: $DATA_DIR (from $BACKUP_FILE)
  Failed data:   $failed_dir
  Service:       $SERVICE_NAME

The service is healthy on the previous version. Review the logs above before
retrying. The failed update exited with status 1.
EOF
        exit 1
    fi

    cat >&2 <<EOF
MANUAL ROLLBACK REQUIRED.
The update failed and the automatic rollback could not restore a healthy
service. Nothing else will be changed automatically.

  Backup archive:   $BACKUP_FILE
  Previous tag:     $OLD_TAG
  Current .env:     $ENV_FILE
  Failed data dir:  $failed_dir
  Data dir:         $DATA_DIR

Recovery outline:
  1. Stop the container:
       cd "$ROOT_DIR"
       docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" stop "$SERVICE_NAME"
  2. Restore the cold backup (keep $failed_dir for inspection):
       rm -rf "$DATA_DIR"
       mkdir -p "$DATA_DIR"
       tar -xzf "$BACKUP_FILE" -C "$DATA_DIR"
       sudo chown -R 10001:10001 "$DATA_DIR"   # run as root (or with sudo)
  3. Set WARTABLE_TAG=$OLD_TAG in "$ENV_FILE".
  4. Start the previous image:
       WARTABLE_TAG=$OLD_TAG docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" up -d --remove-orphans "$SERVICE_NAME"
  5. Verify health:
       curl -fsS "$HEALTH_URL"

Exit status 3 means the update failed and automatic rollback did not restore
health. The failed data directory was preserved for inspection.
EOF
    exit 3
}

cleanup() {
    if [[ -n "$ENV_TMP" ]]; then
        rm -f "$ENV_TMP"
    fi
}

on_exit() {
    local rc=$?
    cleanup

    if ((ROLLBACK_ARMED == 1 && ROLLBACK_RUNNING == 0)); then
        perform_rollback "unexpected failure (exit $rc)"
    fi

    exit "$rc"
}

trap on_exit EXIT

main() {
    local target_input=""
    local allow_latest=0
    local assume_yes=0
    local dry_run=0
    local answer=""
    local normalized=""
    local old_tag_compare=""
    local listing=""
    local digest=""

    local SEMVER_RE='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$'

    local arg
    for arg in "$@"; do
        case "$arg" in
            --yes|-y)
                assume_yes=1
                ;;
            --dry-run)
                dry_run=1
                ;;
            --allow-latest)
                allow_latest=1
                ;;
            -h|--help)
                usage
                exit 0
                ;;
            --*)
                usage >&2
                exit 2
                ;;
            *)
                if [[ -n "$target_input" ]]; then
                    usage >&2
                    die "only one target tag may be given"
                fi
                target_input="$arg"
                ;;
        esac
    done

    if [[ -z "$target_input" ]]; then
        usage >&2
        exit 2
    fi

    normalized="${target_input#v}"
    if [[ "$normalized" == "latest" ]]; then
        if ((allow_latest == 0)); then
            die "refusing the mutable 'latest' tag; pass an immutable X.Y.Z tag or use --allow-latest explicitly"
        fi
        NEW_TAG="latest"
    elif [[ "$normalized" =~ $SEMVER_RE ]]; then
        NEW_TAG="$normalized"
    else
        die "invalid target '$target_input'; expected an immutable full semver X.Y.Z (pre-release suffix allowed), with or without a leading 'v'"
    fi

    [[ -d "$DATA_DIR" ]] || die "data directory not found: $DATA_DIR"
    [[ "$DATA_DIR" != "/" ]] || die "refusing to use / as DATA_DIR"
    [[ "$HEALTH_ATTEMPTS" =~ ^[1-9][0-9]*$ ]] || die "HEALTH_ATTEMPTS must be a positive integer"
    [[ "$HEALTH_INTERVAL" =~ ^[0-9]+$ ]] || die "HEALTH_INTERVAL must be a non-negative integer"

    local cmd
    for cmd in docker curl tar gzip; do
        command -v "$cmd" >/dev/null 2>&1 || die "required command not found: $cmd"
    done
    docker compose version >/dev/null 2>&1 || die "the 'docker compose' plugin is required"
    [[ -f "$ENV_FILE" ]] || die "env file not found: $ENV_FILE"
    [[ -f "$COMPOSE_FILE" ]] || die "compose file not found: $COMPOSE_FILE"
    [[ -f "$DATA_DIR/wartable.db" ]] || die "database not found: $DATA_DIR/wartable.db"

    if ! OLD_TAG="$(read_env_tag)"; then
        die "WARTABLE_TAG is missing or empty in $ENV_FILE; add WARTABLE_TAG=X.Y.Z before updating"
    fi

    old_tag_compare="${OLD_TAG#v}"
    if [[ "$NEW_TAG" == "$old_tag_compare" ]]; then
        die "already at $OLD_TAG; there is nothing to update"
    fi

    check_data_access

    print_plan

    if ((dry_run)); then
        log "Dry run: no image was pulled, no service was stopped, and no files were changed."
        exit 0
    fi

    if ((assume_yes == 0)); then
        printf 'Update %s -> %s? Type "yes" to continue: ' "$OLD_TAG" "$NEW_TAG"
        if ! IFS= read -r answer; then
            answer=""
        fi
        if [[ "$answer" != "yes" ]]; then
            die "update cancelled"
        fi
    fi

    log "Pulling $(image_ref "$NEW_TAG")..."
    if ! pull_image "$NEW_TAG"; then
        die "failed to pull $(image_ref "$NEW_TAG"); the running service and $ENV_FILE were left untouched"
    fi

    if ! mkdir -p "$BACKUP_DIR"; then
        die "could not create backup directory: $BACKUP_DIR"
    fi

    log "Stopping $SERVICE_NAME for a cold backup..."
    if ! compose stop "$SERVICE_NAME"; then
        warn "could not stop $SERVICE_NAME; attempting to leave the running version in place"
        start_previous_service || warn "the service may need manual attention"
        die "update aborted before the backup because the service could not be stopped"
    fi

    BACKUP_FILE="$BACKUP_DIR/wartable-$(utc_timestamp)-from-$(sanitize_tag "$OLD_TAG")-to-$(sanitize_tag "$NEW_TAG").tar.gz"
    log "Creating cold backup: $BACKUP_FILE"
    if ! tar -C "$DATA_DIR" -czf "$BACKUP_FILE" .; then
        rm -f "$BACKUP_FILE"
        warn "cold backup failed; restarting $OLD_TAG"
        start_previous_service || warn "the service may need manual attention"
        die "backup failed; $ENV_FILE was not changed"
    fi

    if ! listing="$(tar -tzf "$BACKUP_FILE")"; then
        rm -f "$BACKUP_FILE"
        warn "backup archive could not be read back; restarting $OLD_TAG"
        start_previous_service || warn "the service may need manual attention"
        die "backup verification failed; $ENV_FILE was not changed"
    fi
    if ! grep -Eq '(^|/)wartable\.db$' <<< "$listing"; then
        rm -f "$BACKUP_FILE"
        warn "backup archive does not contain wartable.db; restarting $OLD_TAG"
        start_previous_service || warn "the service may need manual attention"
        die "backup verification failed; $ENV_FILE was not changed"
    fi
    log "Backup verified: $BACKUP_FILE"

    ROLLBACK_ARMED=1

    log "Updating $ENV_FILE: WARTABLE_TAG=$OLD_TAG -> $NEW_TAG"
    if ! set_env_tag "$NEW_TAG"; then
        die "could not update $ENV_FILE atomically"
    fi
    if [[ "$(read_env_tag || true)" != "$NEW_TAG" ]]; then
        die "could not verify WARTABLE_TAG=$NEW_TAG in $ENV_FILE"
    fi

    log "Recreating $SERVICE_NAME with $(image_ref "$NEW_TAG")..."
    if ! compose up -d --remove-orphans "$SERVICE_NAME"; then
        die "docker compose up failed"
    fi

    log "Waiting for $HEALTH_URL..."
    if ! wait_for_health; then
        perform_rollback "health check did not report status ok after $HEALTH_ATTEMPTS attempts"
    fi

    ROLLBACK_ARMED=0
    digest="$(image_digest "$NEW_TAG")"

    cat <<EOF
Update complete.
  Old tag:       $OLD_TAG
  New tag:       $NEW_TAG
  Backup:        $BACKUP_FILE
  Image digest:  $digest

Keep the previous image ($(image_ref "$OLD_TAG")) locally until you have
verified the new version. It is the fastest rollback path; do not prune it
until you are satisfied.
EOF
}

main "$@"
