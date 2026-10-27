# Uptime-Urpo release tools

Read [the deployment guide](../docs/DEPLOYMENT.md) before using these commands. They operate only this application's Compose project. `ops.py down` preserves its database volume and backups. No command offers a volume-deletion shortcut.

- `publish --help`: clean-checkout release, verification, AMD64 image build, source/image transfer, remote release lock, backup before migration, and readiness before changing `current`. `--allow-dirty` requires explicit authorization and produces a dirty release label. No Git push occurs.
- `ops.py --help`: initialize new private state; start/release, inspect, back up, restore-check into a separate database, restart, stop, or down. Use an absolute state path on the host. `--image` chooses a candidate release only for `up`.
- `install-backups.py`: preserve existing crontab entries and install one project-scoped hourly check for a daily backup at/after 03:17 Europe/Helsinki. Failure does not mark that day successful; the next hourly check retries. The host timezone is not changed.
- `fetch-backups`: copy archives, manifests, configuration, and recovery passwords to an owner-only local tar file over SSH. Keep it outside Git/shared folders. This is an operator command, not automatic off-host replication.
- `verify-local.py`: destructive synthetic-row insertion is permitted only in a project explicitly named `uptime-urpo-m6-check` or `uptime-urpo-verify-*`. Never use it on production. Its real-container checks preserve the test volume afterward.

Basic isolated container check (port 18080 must be free):

```sh
docker buildx build --load --platform linux/arm64 -t uptime-urpo:m6-arm64 .
python3 deploy/ops.py init --state .local/m6-check --project uptime-urpo-m6-check --port 18080 --image uptime-urpo:m6-arm64
python3 deploy/ops.py up --state .local/m6-check
python3 deploy/verify-local.py --state .local/m6-check
python3 deploy/ops.py down --state .local/m6-check
```

Initialize only once. If state already exists, reuse it. If a database volume exists without its original state, recover the matching credentials instead of reinitializing. The app starts with no targets; private production target configuration is supplied deliberately.

Secrets are file-backed Compose mounts. Their host parent directories are mode 0700. Mounted files are readable by the non-root container users; these permissions do not protect against a host administrator or someone with Docker access. Never print `docker compose config` or full container inspect data into public logs. Build context excludes local settings and backups.
