# Milestone 7 — CI and delivery reliability

Status on 2026-10-27: delivery verified; a test-harness follow-up is locally verified and awaits hosted validation. Hosted validation, manual preparation, verified artifact promotion, NUC readiness/history/access checks, and separate-database recovery passed. Production now runs `7ba440656009de8616b2e85d351d4b46531c42f9`. M7 final acceptance awaits that follow-up hosted run. Optional M8 has not started.

## Chosen delivery boundary

The operator chose **CI on GitHub; deployment from the Mac over existing SSH**. GitHub prepares a candidate; it does not connect to the private LAN. No self-hosted runner, NUC credential, production secret, public hostname, or new network access is introduced.

Repository: [roopegarcia/uptime-urpo](https://github.com/roopegarcia/uptime-urpo). The new repository was initially empty with `master` reported as default. The operator pushed main; GitHub now reports main as default. No existing remote branch was overwritten and no push was performed by the agent.

## Implemented

- [Validation](../.github/workflows/ci.yml): GitHub-hosted backend/real PostgreSQL, frontend, operations, and AMD64-image/recovery jobs. The image job requires every validation job to succeed. Actions are pinned to reviewed commit SHAs and token permissions are read-only.
- [Prepare private deployment](../.github/workflows/prepare-deployment.yml): `workflow_dispatch` on `main` only, reusing the same validation. It uploads a candidate only after image/recovery checks succeed. Push/PR validation never creates a deployment candidate.
- [Shared verification](../scripts/verify.py): the same commands run on the Mac and runners. VSTest settings make zero selected tests an error; database filtering targets only the integration project. Workflow linting uses native actionlint or its pinned, network-isolated container.
- Pinned Docker base/database digests and locked package restores. Release labels identify the commit. Independent fresh builds are not claimed byte-identical; delivery promotes the exact already-built image config digest without rebuilding it on the Mac.
- [Packaging](../deploy/package-release.py): clean Git source archive, AMD64 image, source label/schema metadata, GitHub run/attempt identity, and SHA-256 checksums. Only image/source/manifest files are uploaded, not local state or backups.
- [Mac delivery](../deploy/from-actions): validate a successful manual main-branch run, download its artifact, verify GitHub's ZIP digest, reject unsafe ZIP/source paths, and use an isolated worktree at the candidate commit. The user's current branch remains intact.
- [Artifact promotion](../deploy/from-artifact): recheck run/ZIP/manifest/source/checksums, image config digest/architecture/label/schema, then share the existing publisher's SSH locks, backup-before-migration, readiness, and current-link switch. The transferred image config digest is checked on the NUC before migration.
- [Guarded rollback](../deploy/ops.py): inspect a retained local image's migration IDs without database credentials/network access; require exact equality with applied IDs before stopping the app. Back up and start the selected image without running migrations. Review application/data compatibility alongside this conservative check.

## Local evidence

- 199 backend tests (144 unit, 25 fast integration, 30 PostgreSQL), 25 frontend tests, and 24 operations/artifact/gate tests pass: **248 cases**.
- Production frontend build and backend Release build/format checks pass. Native and pinned-container actionlint pass; Python/shell syntax and repository documentation links pass.
- Artifact tests reject failed/pending/nonmanual/wrong-branch/wrong-repository runs, stale attempts, changed files, unsafe source paths/symlinks, wrong ZIP digests, ZIP traversal, and a changed extracted manifest. A failing frontend-test command stops validation before the build.
- Real ARM64 recovery drill in a NEW isolated `uptime-urpo-verify-*` project passed: a deliberately invalid app image failed readiness after the backup/migration boundary; recorded last-good image/data stayed intact; explicit same-schema image rollback restored readiness with both observations preserved.
- A synthetic newer schema marker caused rollback refusal before stopping the healthy app. The marker was removed only from the isolated fixture.
- Restoring the earlier backup into a separate database produced one check while the live isolated database kept two. The post-backup observation was absent from the restored copy; no live database was overwritten.
- The drill stopped only its own containers/networks and preserved its private test state/volume. Production, the development database, and unrelated services were not modified.

Clean-checkout validation gates passed. The first AMD64-on-ARM recovery rerun caught and fixed an implicit host-platform choice in the broken-image fixture; it now builds for the candidate image architecture. The corrected clean-commit recovery drill also passed using an AMD64 image on the ARM64 Mac. A local real-image packaging/ZIP/source/checksum smoke test passed with explicitly synthetic provider metadata; it is not a GitHub-approved deployment candidate.

Hosted [Validation run 40514986868](https://github.com/roopegarcia/uptime-urpo/actions/runs/40514986868) passed all four Ubuntu jobs at `0ecd46e39e99c07aaa6ffd833b829e5a2bed8bb6`, including image/recovery. GitHub initially did not list the dispatch-only workflow after the empty repository's default changed to main, despite its file existing there; a follow-up touches the explicit manual-event declaration on established main.

Manual [preparation run 40516554183](https://github.com/roopegarcia/uptime-urpo/actions/runs/40516554183) passed all four jobs at `29cb5b51c12fd14a07935ec9a83487539da3a416`. Artifact `12740386136` downloaded successfully; its ZIP SHA-256 matched GitHub: `6d9b4c675170b110046d27f45dc2f9d7190d6581f436e588ec981eb38d44e34a`.

The first promotion stopped on the Mac before SSH/release operations: the runner recorded a config digest as Docker's `.Id`, while Docker Desktop reported a different manifest digest after loading the same image. The saved config bytes were unchanged. Delivery now uses manifest schema 2 and an explicit `imageConfigDigest`, computed from saved config bytes on the runner, Mac, and NUC. The config binds runtime settings and uncompressed layer digests. Existing ZIP/source/image archive checks remain required; old schema-1 candidates are refused rather than reinterpreted. See [Docker's image-store documentation](https://docs.docker.com/engine/storage/containerd/) for the different storage backends.

Three regression tests cover classic/OCI archive wrappers, changed runtime/layer identities, and unsafe metadata members. The exact hosted archive and its loaded Mac image both produced config digest `sha256:0f2eabaa9fce55b633fbdb6d2bf912d5c3d5a9dfe201c2b590d4d355650987b0`. The staged NUC image produced that same config digest after SSH transfer. Staging did not start it or change the live release: `current` still points to `8b69d7e6e21c`, and origin readiness returned `200 {"status":"ready"}`.

## Hosted delivery and NUC verification — 2026-10-27

Corrected manual [preparation run 40523094085](https://github.com/roopegarcia/uptime-urpo/actions/runs/40523094085), attempt 1, passed all four hosted jobs at `7ba440656009de8616b2e85d351d4b46531c42f9`: backend/PostgreSQL, frontend, release/recovery tools, and AMD64 image/recovery. The same 248 test cases passed; the runner also exercised the real failed-release, compatible rollback, schema-mismatch refusal, and earlier-backup recovery scenarios.

Artifact `12743370532` has ZIP SHA-256 `b71d30ae50cff9f72d01bf5dcc9ce0c061f5129f364a3d76e932f45ca5df5c32`. Its schema-2 manifest records image config digest `sha256:798bd909d97a95bee19d55f5789ebce1838d50d4ce28ddc55f01467534397f15`. Authenticated CLI download and the complete `from-actions` → isolated candidate worktree → `from-artifact` → publisher path succeeded without rebuilding. The received NUC digest matched before backup/migration. The operator performed Git pushes; the agent dispatched preparation and performed the already-authorized SSH promotion.

```sh
./deploy/from-actions --repository roopegarcia/uptime-urpo --run-id 40523094085 --host roope@nuc --directory /home/roope/apps/uptime-urpo --port 8108
```

- Current source link, recorded settings, and running app identify the candidate commit. Both containers are healthy; database-aware readiness returns HTTP 200.
- The app runs as `app` with a read-only filesystem; port 8108 binds only to loopback. PostgreSQL runs as `postgres`, has a read-only root filesystem and no published port. A direct LAN connection to the known origin port remained blocked. No access-layer policy was changed.
- The private runtime configuration checksum was unchanged. Before promotion, 209 checks were present; afterward, 213 were present, including the original check ID 1. The one service, one monitoring period, two migrations, and zero incidents were preserved. The first post-release check returned HTTP 200 and UP.
- The locked release validated backup `20261027T144300Z-0bd9d872.dump` before migration, containing 212 checks. A separate disposable restore recovered exactly those 212 checks while the live database kept 213. Only the generated restore database was dropped; the live database was not replaced.
- A new owner-only off-host recovery archive was copied to ignored `.local/nuc-backups/`. Its five backup checksums and recovery configuration were verified without printing credentials. Off-host replication remains manual.
- Chrome loaded the deployed overview and service detail through the existing SSH tunnel, including history and the empty incident panel. The UI showed 100% observed availability and one missing monitoring opportunity, reporting the gap separately through coverage. No synthetic success was inserted for deployment downtime. Physical-device testing was not performed.

Repository rules requiring checks before merge are not configured or claimed. Artifact promotion independently requires a successful manual run. Full host reboot, unattended backup execution, full main-database replacement, and long-term operational reliability remain outside this evidence. See [the CI guide](CI-AND-DELIVERY.md) and [M6 limits](MILESTONE-6.md).

## Reproduce

```sh
python3 scripts/verify.py
python3 deploy/recovery-drill.py --image uptime-urpo:validation
```

Docker and a free test port 18081 are required. The first command includes an AMD64 build; Apple Silicon uses emulation when probing that runtime. The dedicated recovery script always creates a fresh, synthetic project and never accepts a production state path.

The new migration CLI and guarded rollback are in M7 images. M6 images do not implement that metadata command; the automated rollback tool refuses an image without usable metadata. Full database replacement, destructive migration reversal, semantic compatibility of arbitrary older code, and independent power/network observation remain separately reviewed concerns.

## Push-run test-host follow-up

The separate [push Validation run 40523056923](https://github.com/roopegarcia/uptime-urpo/actions/runs/40523056923) at the same deployed commit failed two invalid-configuration test rows; its dependent image job was skipped. The successful manual run above independently ran all validation gates before packaging. The failure was a deferred `WebApplicationFactory` startup race: invalid options failed startup, but host disposal sometimes masked the original validation exception with `ObjectDisposedException`. The [upstream ASP.NET Core report/fix](https://github.com/dotnet/aspnetcore/pull/69685) describes that ordering; no preview package or upstream patch is introduced here.

The test now captures the actual host-logged `OptionsValidationException` and asserts its options type is `MonitoringOptions`, asserts startup fails with either the original exception or the known disposal wrapper, and verifies zero outbound checks. It cannot pass on a disposal exception alone. This changes only test observation, not runtime validation. After the fix, the full local backend gate passed: Release build, formatting, 144 unit tests, 25 fast integration tests, and 30 disposable PostgreSQL tests. All 24 operations checks passed. Twenty consecutive runs of the eleven invalid-configuration rows passed (220 repeated cases). These repetitions are stress evidence, not additional unique tests. The follow-up still requires a push/hosted result before final M7 acceptance.
