# Local development prerequisites

This guide records Milestone 0 setup on an Apple Silicon Mac. For current build/test/run commands, see [the M4 guide](MILESTONE-4.md). Run commands from the repository root unless stated otherwise.

## .NET SDK

The repository now selects SDK **10.0.403** in `global.json`, with `latestPatch` roll-forward within its feature band and prereleases disabled. This SDK includes .NET / ASP.NET Core **10.0.14**. M3 applied this servicing update after verifying the official release feed; the original M0 record below remains historical. Pinning does not update an installation automatically.

The SDK is installed in `~/.dotnet`, outside Git. To use `dotnet` directly in the current terminal:

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet --info
dotnet --version
dotnet --list-runtimes
```

These exports affect only the current shell. No shell startup file was changed by M0. If you want persistent setup, add those two export lines to your own zsh profile, then open a new terminal. The repository's prerequisite script also recognizes `~/.dotnet/dotnet` without a PATH change.

For a fresh Apple Silicon Mac without .NET, Microsoft's installer can reproduce the selected SDK installation:

```sh
curl --fail --location --output /tmp/uptime-urpo-dotnet-install.sh https://dot.net/v1/dotnet-install.sh
# Inspect the downloaded official installer before executing it.
bash /tmp/uptime-urpo-dotnet-install.sh --version 10.0.403 --architecture arm64 --install-dir "$HOME/.dotnet"
```

M0 installation and the M3 update used official ARM64 SDK archives and verified SHA-512 against [Microsoft's release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) before extraction. The current artifact is:

```text
URL: https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.403/dotnet-sdk-10.0.403-osx-arm64.tar.gz
SHA-512: 5893c66b48ea3674c7da18501059d606e96f3672fabe4d5e41762e692c7c83df3ee0b2b9dc5a0786191da2dc413d7335158101c14c935ab217f31ac2529770f3
```

Install an SDK, not only the runtime: the SDK supplies the compiler, templates, build tooling, and test commands. C# 14 ships with the .NET 10 SDK; no separate C# compiler installation is needed. On another platform, choose its native architecture through the [official .NET downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), rather than reusing the macOS archive.

## Docker

Docker Desktop supplies the Linux container engine on this Mac. The CLI alone is not enough: the engine must be running.

```sh
open -a Docker
docker --version
docker compose version
docker info --format '{{.OSType}} {{.Architecture}}'
```

Wait for Docker Desktop to finish starting before retrying `docker info`. M0 starts Docker Desktop and checks engine availability; it does not start a project database, pull an image, run a container, or change existing Compose services. Database containers arrive in M3.

Disposable database integration tests will require a reachable Docker engine in M3. A macOS ARM64 engine is not evidence of Intel NUC compatibility; production image/runtime verification belongs to M6.

## Node, editor, Git

Node and npm are already available. No frontend installation is required until M5. At that point select and pin a supported Node release together with compatible React/Vite packages and a lockfile; do not assume today's Node version is the eventual project pin.

VS Code with Microsoft's C# tooling is optional. The CLI is the reproducible baseline. Git is already initialized; no remote, commit, or publication is created by M0.

## Local configuration and secrets

Future real targets belong in private local configuration, not committed settings. The ignore file excludes local environment files and local/development JSON settings. Sanitized examples may be committed later. An ignore rule is not a security boundary: inspect staged changes before every commit.

The application explicitly loads `appsettings.Local.json` and reapplies environment/CLI configuration afterward. .NET does not automatically load arbitrary `appsettings.Local.json` files. See [the current configuration and migration rules](MILESTONE-2.md#configuration). User Secrets are a possible alternative local source; this project currently uses the ignored local JSON file.

No credentials, private target list, connection strings, or NUC host details are required for M0. PostgreSQL and frontend tooling are intentionally deferred.

## Verification

```sh
sh -n scripts/check-prerequisites.sh
sh scripts/check-prerequisites.sh
git diff --check
```

The prerequisite script changes no services or project files. It exits nonzero if the selected SDK, Docker Compose, or Docker engine is unavailable. Node/npm are reported as optional until M5. SDK execution can create normal .NET user-level first-run/cache files.

The commands above check prerequisites and shell syntax only. Application tests were added in M1, expanded in M2, and now include real PostgreSQL containers in M3. Docker is required for the full current suite and local database. The `Category!=Database` filter runs fast unit/API/TLS tests without PostgreSQL. Do not substitute a prerequisite check for application, production, or device verification.

## Milestone 0 verification record — 2026-10-25

Initial inspection: empty Git repository on `main`, no remote, no applicable `AGENTS.md`, no available .NET SDK, installed Docker tooling with engine stopped, and installed Node/npm.

Verified after setup:

- SDK archive SHA-512 matched Microsoft's published metadata before extraction.
- `~/.dotnet/dotnet --info`: SDK 10.0.402, host 10.0.13, RID `osx-arm64`, ARM64 host architecture, repository `global.json` selected.
- Installed runtimes: `Microsoft.NETCore.App` 10.0.13 and `Microsoft.AspNetCore.App` 10.0.13.
- `sh scripts/check-prerequisites.sh`: PASS; Docker 29.9.2, Compose v5.6.1, reachable Linux/aarch64 engine, Node v26.9.0, npm 12.2.0.
- Shell syntax check, SDK JSON parsing, local Markdown links, and whitespace checks passed. No `.cs` or `.csproj` files exist.

At the end of M0, no application tests, production checks, or physical-device checks had been performed because those artifacts did not exist. No project containers were created. No commit, remote publication, or NUC operation occurred. The user-local SDK was installed and Docker Desktop was running at that point; shell profile files were not edited. These are historical observations, not a claim about the engine's current state.

Milestone 0 was completed before M1 approval. The current milestone status is in [the roadmap](ROADMAP.md); the verification record above remains historical.

## Frontend development (M5)

With the API on loopback port 5080, use a second terminal from the repository root:

```sh
npm --prefix frontend ci
npm --prefix frontend run dev
```

Open http://127.0.0.1:5173/. Node 26.9.0 and npm 12.2.0 were verified; `.nvmrc`, exact dependencies, and the npm lockfile live in `frontend/`. Vite proxies API calls locally. No CORS change is needed. `npm --prefix frontend test` runs frontend checks; `npm --prefix frontend run build` type-checks and builds assets. Restart the backend after building to serve the built dashboard directly at port 5080. See [M5](MILESTONE-5.md) for publishing, synthetic browser scenarios, and verification limits.

## Use the NUC deployment instead of local development

The deployed application needs neither Vite nor a local API. Use `ssh -N -L 127.0.0.1:5081:127.0.0.1:8108 roope@nuc`, then open http://127.0.0.1:5081/. Keep the tunnel open. The explicit SSH account is required with the existing alias. See [deployment/access/recovery](DEPLOYMENT.md). Local development commands remain independent of that deployment and use their own database.
