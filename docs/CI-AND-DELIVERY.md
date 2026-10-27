# CI, manual deployment candidates, and recovery

This project keeps the NUC private. GitHub-hosted runners validate/build/package; the Mac performs the authorized SSH deployment. No production credentials are stored in GitHub Actions. Read [the M7 evidence](MILESTONE-7.md) for implemented versus actually executed work.

## One validation path

```sh
python3 scripts/verify.py
```

Prerequisites: the SDK selected by global.json, the Node version in frontend/.nvmrc, npm, Python 3.9+, and a working Docker engine. actionlint runs locally if installed, otherwise in a pinned container with only workflow files mounted. GitHub CLI is needed for convenient artifact download, not for validation.

`--part backend`, `frontend`, `operations`, or `image` runs one gate. Backend performs locked restore, Release build, formatting verification, fast tests, and real disposable PostgreSQL tests; zero selected tests fail via tests/ci.runsettings. Frontend performs npm ci, formatting, tests, type checking, and build. Operations tests failure boundaries/artifact validation and checks docs/syntax/workflows. Image builds AMD64 from pinned base digests. Release inputs are repeatable; independent rebuilds are not promised identical image digests.

GitHub runs these gates on push to main and pull requests, with the image/recovery job depending on all three validation jobs. Test reports retain seven days. Validation uses hosted ephemeral runners, read-only repository permissions, no production secrets, and no pull_request_target event. Action versions are pinned to immutable SHAs; review upstream versions/digests deliberately when updating them.

Recommended repository rule: require the four validation jobs before merging into main. Repository rules are separate from committed workflow YAML and must be verified in GitHub settings; the artifact-deployment gate also independently rejects unsuccessful runs. Do not claim branch protection exists merely because tests run.

## First GitHub setup

The operator owns pushes. This checkout's origin points to `https://github.com/roopegarcia/uptime-urpo.git`.

```sh
git push -u origin main
```

Verify main is the repository's default branch. The first push to an empty repository should establish it; if GitHub still selects another default, update that setting before using workflow_dispatch. Preserve other branches; do not force-push unrelated history.

For CLI access, the operator can run:

```sh
gh auth login --web
gh auth status
```

Public run metadata can be read anonymously, but artifact downloads need authenticated GitHub access. A ZIP downloaded in an authenticated browser can be supplied through `--archive` instead of CLI download. The tool still compares its bytes with GitHub's recorded digest. Private repository metadata also requires authorized access. Tokens never belong in chat, workflow source, artifacts, or NUC configuration.

## Prepare a candidate explicitly

After pushing and observing validation:

```sh
gh workflow run prepare-deployment.yml --repo roopegarcia/uptime-urpo --ref main
gh run list --repo roopegarcia/uptime-urpo --workflow prepare-deployment.yml --limit 5
```

Alternatively use Actions → Prepare private deployment → Run workflow → main. The workflow reuses validation and exercises recovery before uploading a deployment artifact. It does not deploy to the NUC. A push, PR, failed validation, wrong branch, canceled run, or expired artifact cannot be promoted through the artifact command.

The artifact contains source.tar, image.tar, and manifest.json. Source is a clean git archive; the manifest records repository, commit, run/attempt, image config digest, migrations, and checksums. Manifest schema 2 uses `imageConfigDigest`: the SHA-256 of the saved config bytes, which bind runtime settings and layer diff IDs. Docker `.Id` can identify different wrapper objects across image stores, so it is not used for cross-engine comparison. Schema-1 candidates must be rebuilt through a new preparation run. The image has the commit label. GitHub's artifact digest protects the downloaded ZIP; internal hashes and source/image identities protect the following steps. [GitHub documents artifact digests](https://github.blog/changelog/2025-03-18-github-actions-now-supports-a-digest-for-validating-your-artifacts-at-runtime/). The tool fails on mismatch rather than treating it as a warning.

## Promote the same image from the Mac

Replace RUN_ID with the successful preparation run's numeric ID:

```sh
./deploy/from-actions --repository roopegarcia/uptime-urpo --run-id RUN_ID --host roope@nuc --directory /home/roope/apps/uptime-urpo --port 8108
```

The candidate commit must exist in the local repository. Fetch origin if necessary; the helper creates a detached worktree at that commit without changing your current branch. It removes the worktree without force afterward; unexpected edits are preserved. Candidate downloads remain private under ignored .local/ci-downloads.

For a browser-downloaded ZIP, add `--archive /absolute/path/to/deployment.zip`. The repository/run/attempt/ZIP digest checks remain mandatory. The helper imports the verified image locally and checks its ID, AMD64 architecture, source label and schema metadata. It then uses the existing publisher's remote preflight/lock, source/image transfer, received-image identity check, backup before migration, readiness, and current-link switch. Existing production target/secrets configuration is preserved. No build runs on the Mac or NUC during this promotion.

Keep prior image/source releases and backups. Seven-day GitHub artifact retention is not a long-term recovery store. Promotion requires the live GitHub run/artifact records to remain available; expired candidates require a fresh preparation. The retained local-image rollback path is independent of artifact download.

Open the deployed UI with the existing SSH tunnel described in [the deployment guide](DEPLOYMENT.md). Verify origin readiness, private port bindings, target observations, and browser behavior separately after promotion. An image config digest transferred intact does not by itself prove the running app is ready.

## Failed release and explicit image rollback

The isolated recovery exercise is safe to run locally:

```sh
python3 deploy/recovery-drill.py --image uptime-urpo:validation
```

It creates a new test project with synthetic rows and no enabled targets; it never loads production configuration. A broken app image passes the unchanged migration stage but fails readiness. The helper leaves the last-good image recorded and does not automatically reverse schema changes. An explicit rollback to a retained matching-schema image restores service/data.

For a reviewed real rollback, use the M7 tools from a known-good release and an explicit retained image:

```sh
python3 deploy/ops.py rollback --state /absolute/private/state --image uptime-urpo:KNOWN_GOOD_COMMIT
```

The tool checks image migration metadata against applied database IDs before stopping the app, then backs up and switches the image without migrations. Review application/data compatibility as well. It refuses missing metadata, including older M6 images, and any migration mismatch. It does not infer that an older binary supports a newer schema.

If schemas differ, preserve current state and stop for a reviewed recovery plan. A backup restored into a separate database can recover its earlier observations while omitting every observation saved afterward. The M7 drill demonstrates one restored check versus two remaining in the live isolated database. Restoring a copy does not change the live database; a main-database switch/replacement is a separate operation with explicit data-loss consequences.

## Verification limits

Local tests, syntax checks, a clean-checkout reproduction, an actual GitHub run, artifact promotion, application rollback, and full database replacement are separate evidence. Record run URLs and the running image/source commit when performed. Do not label file creation or a local script as a successful hosted CI/deployment run. See [M7's dated record](MILESTONE-7.md) and [M6's deployed baseline](MILESTONE-6.md).
