# Milestone 7 — CI and delivery reliability

Status on 2026-10-27: implemented and locally verified; GitHub-hosted execution and CI-artifact delivery are pending the first push/authenticated run. M7 is not yet fully accepted. Production remains the verified M6 release `8b69d7e6e21c`.

## Chosen delivery boundary

The operator chose **CI on GitHub; deployment from the Mac over existing SSH**. GitHub prepares a candidate; it does not connect to the private LAN. No self-hosted runner, NUC credential, production secret, public hostname, or new network access is introduced.

Repository: [roopegarcia/uptime-urpo](https://github.com/roopegarcia/uptime-urpo). The new repository was empty when inspected, with `master` reported as the initial default. Push this checkout's `main` branch and verify it becomes the default before manually preparing a candidate. No existing remote branch was overwritten and no push was performed by the agent.

## Implemented

- [Validation](../.github/workflows/ci.yml): GitHub-hosted backend/real PostgreSQL, frontend, operations, and AMD64-image/recovery jobs. The image job requires every validation job to succeed. Actions are pinned to reviewed commit SHAs and token permissions are read-only.
- [Prepare private deployment](../.github/workflows/prepare-deployment.yml): `workflow_dispatch` on `main` only, reusing the same validation. It uploads a candidate only after image/recovery checks succeed. Push/PR validation never creates a deployment candidate.
- [Shared verification](../scripts/verify.py): the same commands run on the Mac and runners. VSTest settings make zero selected tests an error; database filtering targets only the integration project. Workflow linting uses native actionlint or its pinned, network-isolated container.
- Pinned Docker base/database digests and locked package restores. Release labels identify the commit. Independent fresh builds are not claimed byte-identical; delivery promotes the exact already-built image ID without rebuilding it on the Mac.
- [Packaging](../deploy/package-release.py): clean Git source archive, AMD64 image, source label/schema metadata, GitHub run/attempt identity, and SHA-256 checksums. Only image/source/manifest files are uploaded, not local state or backups.
- [Mac delivery](../deploy/from-actions): validate a successful manual main-branch run, download its artifact, verify GitHub's ZIP digest, reject unsafe ZIP/source paths, and use an isolated worktree at the candidate commit. The user's current branch remains intact.
- [Artifact promotion](../deploy/from-artifact): recheck run/ZIP/manifest/source/checksums, image ID/architecture/label/schema, then share the existing publisher's SSH locks, backup-before-migration, readiness, and current-link switch. The transferred image ID is checked on the NUC before migration.
- [Guarded rollback](../deploy/ops.py): inspect a retained local image's migration IDs without database credentials/network access; require exact equality with applied IDs before stopping the app. Back up and start the selected image without running migrations. Review application/data compatibility alongside this conservative check.

## Local evidence

- 199 backend tests (144 unit, 25 fast integration, 30 PostgreSQL), 25 frontend tests, and 21 operations/artifact/gate tests pass: **245 cases**.
- Production frontend build and backend Release build/format checks pass. Native and pinned-container actionlint pass; Python/shell syntax and repository documentation links pass.
- Artifact tests reject failed/pending/nonmanual/wrong-branch/wrong-repository runs, stale attempts, changed files, unsafe source paths/symlinks, wrong ZIP digests, ZIP traversal, and a changed extracted manifest. A failing frontend-test command stops validation before the build.
- Real ARM64 recovery drill in a NEW isolated `uptime-urpo-verify-*` project passed: a deliberately invalid app image failed readiness after the backup/migration boundary; recorded last-good image/data stayed intact; explicit same-schema image rollback restored readiness with both observations preserved.
- A synthetic newer schema marker caused rollback refusal before stopping the healthy app. The marker was removed only from the isolated fixture.
- Restoring the earlier backup into a separate database produced one check while the live isolated database kept two. The post-backup observation was absent from the restored copy; no live database was overwritten.
- The drill stopped only its own containers/networks and preserved its private test state/volume. Production, the development database, and unrelated services were not modified.

Clean-checkout validation gates passed. The first AMD64-on-ARM recovery rerun caught and fixed an implicit host-platform choice in the broken-image fixture; it now builds for the candidate image architecture. Pending evidence: the corrected recovery drill from its clean commit, an actual hosted Ubuntu Actions run, uploaded artifact download/verification, and an explicit operator-side promotion. GitHub rules requiring checks before merge are repository settings and have not been claimed configured. See [the CI guide](CI-AND-DELIVERY.md).

## Reproduce

```sh
python3 scripts/verify.py
python3 deploy/recovery-drill.py --image uptime-urpo:validation
```

Docker and a free test port 18081 are required. The first command includes an AMD64 build; Apple Silicon uses emulation when probing that runtime. The dedicated recovery script always creates a fresh, synthetic project and never accepts a production state path.

The new migration CLI and guarded rollback are in M7 images. M6 images do not implement that metadata command; the automated rollback tool refuses an image without usable metadata. Full database replacement, destructive migration reversal, semantic compatibility of arbitrary older code, and independent power/network observation remain separately reviewed concerns.
