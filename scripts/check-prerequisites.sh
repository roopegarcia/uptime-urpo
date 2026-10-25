#!/bin/sh
# Read-only Milestone 0 checks. Does not install tools, start services, or scaffold code.
set -eu

project_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$project_root"
failed=0

printf 'Uptime-Urpo prerequisites\n'
printf 'Host architecture: %s\n' "$(uname -m)"

if command -v dotnet >/dev/null 2>&1; then
    dotnet_command=$(command -v dotnet)
elif [ -n "${DOTNET_ROOT:-}" ] && [ -x "$DOTNET_ROOT/dotnet" ]; then
    dotnet_command="$DOTNET_ROOT/dotnet"
elif [ -x "$HOME/.dotnet/dotnet" ]; then
    dotnet_command="$HOME/.dotnet/dotnet"
else
    dotnet_command=''
fi

if [ -n "$dotnet_command" ]; then
    if sdk_version=$("$dotnet_command" --version); then
        printf 'Selected .NET SDK: %s (global.json enforced)\n' "$sdk_version"
        "$dotnet_command" --list-runtimes
    else
        printf 'FAIL: .NET cannot resolve the SDK requested in global.json.\n' >&2
        failed=1
    fi
else
    printf 'FAIL: .NET SDK missing. Follow docs/LOCAL-DEVELOPMENT.md.\n' >&2
    failed=1
fi

if command -v docker >/dev/null 2>&1; then
    docker --version
    if ! docker compose version; then
        printf 'FAIL: Docker Compose unavailable.\n' >&2
        failed=1
    fi
    if ! docker info --format 'Docker engine: {{.OSType}} / {{.Architecture}}'; then
        printf 'FAIL: Docker engine unavailable. Start Docker Desktop and rerun.\n' >&2
        failed=1
    fi
else
    printf 'FAIL: Docker missing. Follow docs/LOCAL-DEVELOPMENT.md.\n' >&2
    failed=1
fi

if command -v node >/dev/null 2>&1 && command -v npm >/dev/null 2>&1; then
    printf 'Node: %s\n' "$(node --version)"
    printf 'npm: %s\n' "$(npm --version)"
else
    printf 'NOTE: Node/npm not found; required only from Milestone 5.\n'
fi

if [ "$failed" -eq 0 ]; then
    printf 'PASS: development prerequisites available. Application tests must be run separately.\n'
fi
exit "$failed"
