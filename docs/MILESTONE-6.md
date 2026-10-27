# Milestone 6 — Production packaging and private NUC operations

Status on 2026-10-27: production packaging and local verification complete; NUC deployment verification is in progress. M6 is not yet signed off. See [deployment commands and recovery boundaries](DEPLOYMENT.md).

## Inspected host

Authenticated using `roope@nuc`; the alias's default account was rejected. The NUC is x86-64 Debian 13.7, Docker 29.8.2, Compose 5.6.0. Inspection found about 165 GB free disk, 3.7 GB available memory, and an unused loopback port 8108. Existing applications, Nginx, Cloudflare, and host timezone are preserved. Application files use their own directory under the user's apps folder.

## Implemented

- Multi-stage build: React assets plus portable framework-dependent .NET output in one non-root application image. ARM64 and AMD64 images build. Locked dependencies remain enforced. No SDK, Node, private settings, or test fixtures are needed in the runtime image.
- Compose: separate internal database and outbound monitoring networks; unpublished PostgreSQL; loopback app port; non-root users, read-only roots, bounded temporary storage/resources/logs, health checks, restart policy, and graceful termination.
- Runtime and migration identities receive separate file-backed configuration secrets. The health-check CLI starts no worker and probes database-aware readiness.
- `ops.py`: guarded state initialization, operation locking, explicit backup-before-migration releases, readiness, restart/stop, validated backups, and restore into a separate generated database. No volume-deletion command exists.
- `publish`: clean commits by default, complete validation, AMD64 build, SSH source/image transfer, publisher lock, and `current` switch after readiness. Dirty releases require explicit opt-in and carry a dirty label. No Git push.
- Daily backup scheduling uses Europe/Helsinki and retries through hourly cron checks without changing the host timezone; repeated autumn hours do not create duplicate daily backups. Existing cron entries are preserved.
- Off-host recovery copy command includes archives, manifests, and credentials in a private local tar over SSH. Automatic off-host replication and backup pruning are not implemented.

## Local verification

- 144 backend unit and 55 integration cases pass; seven operations failure/schedule tests pass. Frontend checks are rerun by the publisher.
- Real ARM64 container checks passed: same-origin HTML/assets/API, API 404 Problem Details, non-root/read-only operation, healthy probe, loopback binding, no published database port, and no migration/admin secret in the app container.
- Runtime is neither superuser nor database/role creator, cannot create public schema objects, and cannot write EF migration history.
- Synthetic retired fixture rows were inserted only into `uptime-urpo-m6-check`, never into development or production. A stopped-app dump restored into a separate database with matching counts: one service, one period, one check, one incident, and two migrations. Restore uses migration ownership and verifies runtime reads. Only the disposable restore database was dropped.
- Graceful application stop exited zero without OOM; restart retained rows. Database interruption left liveness up and readiness at 503; readiness and data recovered after restart.
- Tests prove a failed backup prevents migrations/app startup, corrupt archives cause no restore database creation, existing state/volumes prevent credential regeneration, remote Python snippets compile, and scheduled-day state updates only after success.

The first draft checks found and fixed a portable lockfile/RID mismatch, Python 3.9's lack of `hashlib.file_digest`, an unquoted YAML tmpfs list, and per-database default grants needed by a restored runtime identity. Verification uses separate project volumes and preserves unrelated services/data.

## NUC evidence

First NUC release is healthy, with real HTTP 200 checks and the overview/details/history UI verified through SSH. Direct LAN access to port 8108 was refused/timed out. Pending: install the corrected restore helper release, verify native restart continuity and a production-backup restore, install the schedule, and make the off-host recovery copy. Physical-host reboot and public/Cloudflare access are outside the initial SSH-only verification.
