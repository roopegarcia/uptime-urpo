# Milestone 6 — Production packaging and private NUC operations

Status on 2026-10-27: complete and privately deployed to the inspected NUC through SSH-only access. M7 remains behind the next approval gate. See [deployment commands and recovery boundaries](DEPLOYMENT.md).

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

- 144 backend unit, 55 integration, 25 frontend, and seven operations cases pass (231 total). The publisher reran these gates from clean commits.
- Real ARM64 container checks passed: same-origin HTML/assets/API, API 404 Problem Details, non-root/read-only operation, healthy probe, loopback binding, no published database port, and no migration/admin secret in the app container.
- Runtime is neither superuser nor database/role creator, cannot create public schema objects, and cannot write EF migration history.
- Synthetic retired fixture rows were inserted only into `uptime-urpo-m6-check`, never into development or production. A stopped-app dump restored into a separate database with matching counts: one service, one period, one check, one incident, and two migrations. Restore uses migration ownership and verifies runtime reads. Only the disposable restore database was dropped.
- Graceful application stop exited zero without OOM; restart retained rows. Database interruption left liveness up and readiness at 503; readiness and data recovered after restart.
- Tests prove a failed backup prevents migrations/app startup, corrupt archives cause no restore database creation, existing state/volumes prevent credential regeneration, remote Python snippets compile, and scheduled-day state updates only after success.

The first draft checks found and fixed a portable lockfile/RID mismatch, Python 3.9's lack of `hashlib.file_digest`, an unquoted YAML tmpfs list, and per-database default grants needed by a restored runtime identity. Verification uses separate project volumes and preserves unrelated services/data.

## NUC evidence

Deployed application/tooling release: **`8b69d7e6e21c`**, after initial release `cdb674ff3ff8`. Later documentation commits do not imply a different running image.

- Native AMD64 application/database containers are healthy. The approved configured target returned HTTP 200 and was UP; reads showed persisted history.
- Both containers' non-root users and read-only roots were inspected. Application port binds only to loopback 8108; PostgreSQL has no published port. Direct Mac-to-NUC LAN connection to 8108 was blocked/refused.
- Authenticated SSH forwarding to Mac loopback 5081 serves the actual overview, detail, chart, and API. Chrome showed the deployed data. There is no new public hostname or changed Cloudflare/Nginx rule.
- Graceful app stop exited zero. A database-container restart preserved all rows. A quiescent production backup restored into a separate database: one service, one period, five checks, zero incidents, two migrations. Runtime reads succeeded in the restored copy; it was then dropped. The live database was never overwritten.
- Application restart resumed monitoring: checks increased from five to six while the service/period/migrations stayed intact. Production incidents were not manufactured for testing; non-empty incident restoration was covered in the isolated local fixture.
- A project-scoped cron entry was installed. The scheduled helper was invoked successfully and made a backup. Its unattended execution at the next eligible hourly tick remains future operational evidence.
- Four database archives plus configuration/recovery passwords were copied off-host to an ignored owner-only local tar. All four archive checksums matched their manifests. This copy is manual; automatic off-host replication is not implemented.

No physical-host reboot, disk-failure recovery, public/Cloudflare access, full main-database replacement, load benchmark, or physical-device UI test was performed. Existing host services and the development database were preserved. An SSH tunnel is required to access the deployed dashboard; the app itself has no built-in authentication.

## Reproduce verification

```sh
python3 -m unittest discover -s deploy -p 'test_*.py'
python3 deploy/verify-local.py --state .local/m6-check
```

The isolated check stack must already exist and be running; see [the tools guide](../deploy/README.md). The publisher runs locked restore, backend/frontend tests, type-check/build, formatting, and the AMD64 image build before transfer. Root `git diff --check`, .NET formatting, Python/shell syntax, documentation links, and secret/build-artifact exclusions were also checked.

The restore permission fix matters: PostgreSQL default privileges belong to each database. Owning a restored schema is insufficient to let the restricted runtime read it. The drill now establishes the migration role's default grants in the disposable database before restoration and verifies runtime access afterward.
