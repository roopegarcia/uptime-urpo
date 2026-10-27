#!/usr/bin/env python3
"""Local-only database commands. Never deletes volumes or changes another Compose project."""
import argparse
from datetime import datetime, timezone
import os
from pathlib import Path
import secrets
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent.parent
ENV_FILE = ROOT / '.local' / 'database.env'


def compose_command():
    return ['docker', 'compose', '--project-name', 'uptime-urpo-dev', '--env-file', str(ENV_FILE),
            '--file', str(ROOT / 'compose.dev.yaml')]


def backup():
    directory = ROOT / '.local' / 'backups'
    directory.mkdir(mode=0o700, parents=True, exist_ok=True)
    filename = directory / (datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + secrets.token_hex(4) + '.dump')
    try:
        with os.fdopen(os.open(filename, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), 'wb') as output:
            subprocess.run(compose_command() + ['exec', '-T', 'db', 'pg_dump', '-U', 'postgres',
                           '-d', 'uptime_urpo', '--format=custom'], cwd=ROOT, stdout=output, check=True)
    except BaseException:
        filename.unlink(missing_ok=True)
        raise
    print(f'Local database backup saved: {filename.relative_to(ROOT)}', flush=True)


def credentials(create=False):
    if not ENV_FILE.exists():
        if not create:
            raise SystemExit('Run python3 scripts/dev-db.py up first.')
        subprocess.run(['docker', 'info', '--format', '{{.OSType}}'], check=True,
                       stdout=subprocess.DEVNULL)
        existing = subprocess.run(['docker', 'volume', 'inspect', 'uptime-urpo-dev_database'],
                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if existing.returncode == 0:
            raise SystemExit('Credentials are missing for an existing database volume. Recover database.env; do not regenerate or delete the volume.')
        ENV_FILE.parent.mkdir(mode=0o700, exist_ok=True)
        values = {name: secrets.token_hex(24) for name in
                  ['POSTGRES_PASSWORD', 'MIGRATION_PASSWORD', 'RUNTIME_PASSWORD']}
        with os.fdopen(os.open(ENV_FILE, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), 'w') as output:
            output.write(''.join(f'{key}={value}\n' for key, value in values.items()))
    values = dict(line.split('=', 1) for line in ENV_FILE.read_text().splitlines() if line and not line.startswith('#'))
    for key in ['POSTGRES_PASSWORD', 'MIGRATION_PASSWORD', 'RUNTIME_PASSWORD']:
        if not values.get(key) or not all(character in '0123456789abcdef' for character in values[key]):
            raise SystemExit('Invalid generated database credentials; preserve the existing file and volume.')
    return values


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['up', 'down', 'status', 'backup', 'migrate', 'run'])
    args = parser.parse_args()
    values = credentials(create=args.command == 'up')
    if args.command in ['up', 'down', 'status']:
        compose = compose_command()
        operation = {'up': ['up', '-d', '--wait', 'db'], 'down': ['down'], 'status': ['ps']}[args.command]
        subprocess.run(compose + operation, cwd=ROOT, check=True)
        return
    if args.command == 'backup':
        backup()
        return

    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet' / 'dotnet')
    migration = args.command == 'migrate'
    role = 'uptime_urpo_migrator' if migration else 'uptime_urpo_runtime'
    password = values['MIGRATION_PASSWORD' if migration else 'RUNTIME_PASSWORD']
    port = int(os.environ.get('UPTIME_DB_PORT', '54330'))
    environment = os.environ.copy()
    environment['ConnectionStrings__Monitoring'] = (
        f'Host=127.0.0.1;Port={port};Database=uptime_urpo;Username={role};Password={password};'
        'Timeout=3;Command Timeout=5;Maximum Pool Size=20;Include Error Detail=false')
    command = [dotnet, 'run', '--project', str(ROOT / 'backend/UptimeUrpo.Api'), '--no-launch-profile']
    if migration:
        backup()
        command += ['--', '--migrate=true']
        subprocess.run(command, cwd=ROOT, env=environment, check=True)
        subprocess.run(['docker', 'compose', '--project-name', 'uptime-urpo-dev', '--env-file', str(ENV_FILE),
                        '--file', str(ROOT / 'compose.dev.yaml'), 'exec', '-T', 'db', 'psql', '-U', 'postgres',
                        '-d', 'uptime_urpo', '--set', 'ON_ERROR_STOP=1', '-c',
                        'REVOKE INSERT, UPDATE, DELETE ON "__EFMigrationsHistory" FROM uptime_urpo_runtime'],
                       cwd=ROOT, check=True)
        return
    # Replace this helper with dotnet, preserving ordinary Ctrl+C/SIGTERM behavior.
    os.chdir(ROOT)
    os.execve(dotnet, command, environment)


if __name__ == '__main__':
    main()
