# Private NUC deployment and recovery

M6 is in progress. Portable container and recovery checks have passed locally; the dated M6 record distinguishes later NUC evidence. These commands are specific to the inspected host, not permission to change unrelated applications.

## Inspected destination

The existing SSH alias resolves the NUC but selects a rejected account. Use **`roope@nuc`**, which authenticated successfully. The inspected host is x86-64 Debian 13, Docker 29.8.2, Compose 5.6.0; port 8108 was unused. Existing Nginx and Cloudflare services remain untouched. Application files belong under `/home/roope/apps/uptime-urpo`.

The first access path is an SSH tunnel. The application binds only NUC loopback and has no built-in authentication. There is no new public hostname or LAN listener. A future Cloudflare route requires inspecting its Access policy and verifying no origin bypass before use.

## Release

From a clean, verified repository:

```sh
./deploy/publish --host roope@nuc --directory /home/roope/apps/uptime-urpo --port 8108 --targets-file backend/UptimeUrpo.Api/appsettings.Local.json
```

The optional private JSON supplies **only** its `Monitoring` section. The publisher normalizes a legacy single `Target` to `Targets`. Do not pass another project's settings. Omit the flag to preserve existing production targets, or to start a new stack with no targets.

The publisher runs locked restore, backend/frontend/operations tests, frontend formatting/build, and an AMD64 image build before upload. Source is a Git archive; images transfer directly over SSH. Uncommitted changes are refused by default. It uses its own remote publisher lock and the stack's operation lock. Do not run competing manual deployment operations.

Host layout:

```text
uptime-urpo/
  releases/<commit>/     source snapshot including operations tools
  current -> releases/<commit>
  state/                private credentials, target config, settings and backups
```

The Compose project is `uptime-urpo-production`; its data volume is `uptime-urpo-production_database`. PostgreSQL has no host port and joins only an internal network. The app also joins an outbound network to check approved public HTTPS destinations. Both containers run as non-root users with dropped capabilities, read-only root filesystems, bounded writable temporary mounts, log rotation, CPU/memory/process limits, graceful stop time, and restart policies. A Docker health check reports readiness but does not itself restart an unhealthy live process; investigate a persistent unhealthy state.

Release order is stop this app → start/wait for this database → save and validate a logical backup → run migrations with the migration identity → revoke runtime writes to EF migration history → recreate/wait for the app → check loopback readiness → update `current`. A failed backup prevents migration. Failure leaves state/backups intact; there is no automatic schema rollback.

## Open the dashboard

On the Mac, keep this terminal open:

```sh
ssh -N -L 127.0.0.1:5081:127.0.0.1:8108 roope@nuc
```

Then open **http://127.0.0.1:5081/**. Backend and frontend share one origin. The forwarded port binds only Mac loopback. Vite, Node, and a native .NET process are not needed to use this deployed dashboard.

Verify the NUC origin separately:

```sh
ssh roope@nuc 'curl --fail --silent http://127.0.0.1:8108/health/ready'
```

Readiness must return `ready`; target failures do not fail `/health`. From a separate LAN machine, the NUC's port 8108 must refuse connections. Inspect Docker bindings and confirm there is no published database port. SSH authentication is the access boundary; this does not claim verification of a Cloudflare boundary.

## Backups and restore drill

Install the project-scoped daily schedule after the first successful release:

```sh
ssh roope@nuc 'python3 /home/roope/apps/uptime-urpo/current/deploy/install-backups.py --checkout /home/roope/apps/uptime-urpo/current --state /home/roope/apps/uptime-urpo/state'
```

Cron checks at 17 minutes past each hour. The helper uses **Europe/Helsinki** to make one daily backup at/after 03:17, retry after failures, and avoid duplicates during autumn's repeated hour. Missed checks result in one current backup at the next eligible tick, not a queue of old backups. The last check's output overwrites `state/backup-schedule.log`; `last-scheduled-backup` records the last successful scheduled day. Manual/release backups do not update that marker. Scheduled execution is separate evidence from installing the entry.

Create a manual backup and then select its exact printed path:

```sh
ssh roope@nuc 'python3 /home/roope/apps/uptime-urpo/current/deploy/ops.py backup --state /home/roope/apps/uptime-urpo/state'
ssh roope@nuc 'python3 /home/roope/apps/uptime-urpo/current/deploy/ops.py restore-check --state /home/roope/apps/uptime-urpo/state --backup /absolute/path/printed/by/backup.dump'
```

Backups are private custom-format `pg_dump` archives with a SHA-256 manifest. Archive inventory and checksum validation are not substitutes for restoration. `restore-check` creates a uniquely named disposable database, restores with the migration owner, probes tables/runtime reads, compares row counts when the archive came from a stopped app, and drops **only that disposable database**. The production database is not overwritten. Live manual/scheduled dumps have a consistent PostgreSQL snapshot, but do not promise a separately queried source row-count match.

Copy recovery data off the NUC after releases and regularly afterward:

```sh
./deploy/fetch-backups --host roope@nuc --state /home/roope/apps/uptime-urpo/state
```

The resulting `.local/nuc-backups/*.tar` contains credentials and database data: keep it private, encrypted at rest where appropriate, and outside Git/shared folders. Automatic off-host replication is not implemented. Backups on the same NUC alone do not protect against disk/host loss. Backup pruning is deliberately not automatic; monitor available disk and remove obsolete archives only under an agreed retention policy.

## Restart and recovery boundaries

```sh
ssh roope@nuc 'python3 /home/roope/apps/uptime-urpo/current/deploy/ops.py restart --state /home/roope/apps/uptime-urpo/state'
ssh roope@nuc 'python3 /home/roope/apps/uptime-urpo/current/deploy/ops.py status --state /home/roope/apps/uptime-urpo/state'
```

Restart policy brings containers back when Docker starts. Process/container restart checks do not prove a physical host reboot; do not reboot a shared NUC merely to test this application.

Keep prior releases/images. To use a previous image, first inspect migration compatibility. If compatible, use `ops.py up --image uptime-urpo:<previous-commit>` from the appropriate release; it still backs up and runs the explicit migration path. An image rollback does not undo database migrations. Never assume an older binary supports a newer schema.

For data recovery: stop the app, preserve the damaged/newer database and state, restore the chosen archive into a separate database, validate ownership/runtime grants and observations, then plan a reviewed connection switch or volume replacement. Do not drop the live database to make a restore succeed. Recovery can lose observations newer than the backup; main-database replacement is a separately reviewed operation, not part of the automated restore drill.

Preserve the original secrets with the volume. Recreating passwords does not reset an initialized PostgreSQL volume. Runtime credentials cannot perform schema migrations; migration credentials are mounted only into the short-lived migration container. Never weaken TLS/SSRF protections or an access policy to make a target green.

A monitor on this NUC cannot independently report the NUC's own power/network loss. Missing data remains a gap; an external observer is still a future complement.
