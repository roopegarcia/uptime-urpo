#!/usr/bin/env python3
"""Operate only this project's container stack; never deletes its data volume."""
import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import time
import urllib.error
import urllib.request
from zoneinfo import ZoneInfo

ROOT = Path(__file__).resolve().parent.parent
TABLES = ['monitored_services', 'monitoring_periods', 'check_results', 'incidents', '__EFMigrationsHistory']


def run(command, **kwargs):
    return subprocess.run(command, check=True, **kwargs)


def utc():
    return datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')


def checksum_file(path):
    digest = hashlib.sha256()
    with path.open('rb') as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def private_write(path, content, mode=0o600):
    with os.fdopen(os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, mode), 'w') as output:
        os.fchmod(output.fileno(), mode)
        output.write(content)


def initialize(state, project, port, image):
    if not re.fullmatch(r'uptime-urpo(?:-[a-z0-9]+)*', project or ''):
        raise SystemExit('Choose an uptime-urpo project name, such as uptime-urpo-production.')
    if not port or not 1024 <= port <= 65535 or not image:
        raise SystemExit('init requires --port (1024-65535) and --image.')
    if state.exists():
        raise SystemExit('State directory already exists. Preserve its credentials; do not reinitialize it.')
    existing = subprocess.run(['docker', 'volume', 'inspect', project + '_database'],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if existing.returncode == 0:
        raise SystemExit('An existing database volume has no matching state. Recover its original secrets.')
    run(['docker', 'info', '--format', '{{.OSType}}'], stdout=subprocess.DEVNULL)
    state.parent.mkdir(parents=True, exist_ok=True)
    temporary = state.with_name(state.name + '-init-' + secrets.token_hex(4))
    temporary.mkdir(mode=0o700)
    (temporary / 'secrets').mkdir(mode=0o700)
    passwords = {name: secrets.token_hex(32) for name in ['admin', 'migration', 'runtime']}
    # File-backed Compose secrets retain host modes. Parent directories are 0700;
    # mounted files must be readable by the non-root app/postgres users inside containers.
    for name, password in passwords.items():
        private_write(temporary / 'secrets' / (name + '_password'), password + '\n', 0o444)
    for role in ['migration', 'runtime']:
        username = 'uptime_urpo_' + ('migrator' if role == 'migration' else 'runtime')
        connection = (f'Host=db;Database=uptime_urpo;Username={username};Password={passwords[role]};'
                      'Timeout=3;Command Timeout=5;Maximum Pool Size=20;Include Error Detail=false')
        configuration = {'ConnectionStrings': {'Monitoring': connection}}
        if role == 'runtime':
            configuration.update({'AllowedHosts': 'localhost;127.0.0.1;[::1]',
                                  'Monitoring': {'MaxConcurrency': 3, 'Targets': []}})
        private_write(temporary / 'secrets' / (role + '.json'), json.dumps(configuration, indent=2) + '\n', 0o444)
    private_write(temporary / 'settings.json', json.dumps({'project': project, 'port': port, 'image': image}, indent=2) + '\n')
    temporary.rename(state)
    print(f'Initialized {state}. No targets are enabled; edit secrets/runtime.json privately before first production use.')


class Stack:
    def __init__(self, state, image=None):
        self.state = state
        if not (state / 'settings.json').is_file():
            raise SystemExit('Missing state/settings.json. Initialize a NEW stack, or recover existing state; never regenerate volume credentials.')
        self.settings = json.loads((state / 'settings.json').read_text())
        if not re.fullmatch(r'uptime-urpo(?:-[a-z0-9]+)*', self.settings['project']):
            raise SystemExit('Invalid project identity.')
        self.image = image or self.settings['image']
        for name in ['admin_password', 'migration_password', 'runtime_password', 'migration.json', 'runtime.json']:
            if not (state / 'secrets' / name).is_file():
                raise SystemExit('Missing secrets. Recover the original state directory; do not regenerate credentials.')
        self.environment = {**os.environ, 'UPTIME_STATE': str(state), 'UPTIME_PROJECT': self.settings['project'],
                            'UPTIME_HTTP_PORT': str(self.settings['port']), 'UPTIME_IMAGE': self.image}

    def compose(self, *arguments, **kwargs):
        return run(['docker', 'compose', '--file', str(ROOT / 'deploy' / 'compose.yaml'),
                    '--project-name', self.settings['project'], *arguments], env=self.environment, **kwargs)

    @contextmanager
    def lock(self):
        with (self.state / 'operation.lock').open('a') as lock:
            try:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                raise SystemExit('Another operation holds this stack lock.') from None
            yield

    def sql(self, sql, database='uptime_urpo', capture=False):
        return self.compose('exec', '-T', 'db', 'psql', '-U', 'postgres', '-d', database,
                            '--set', 'ON_ERROR_STOP=1', '-At', '-c', sql,
                            stdout=subprocess.PIPE if capture else None, text=True)

    def counts(self, database):
        result = {}
        for table in TABLES:
            exists = self.sql(f"SELECT to_regclass('public.\"{table}\"') IS NOT NULL", database, True).stdout.strip() == 't'
            result[table] = int(self.sql(f'SELECT count(*) FROM "{table}"', database, True).stdout.strip()) if exists else None
        return result

    def backup(self, quiescent=False):
        directory = self.state / 'backups'
        directory.mkdir(mode=0o700, exist_ok=True)
        path = directory / (utc() + '-' + secrets.token_hex(4) + '.dump')
        try:
            with os.fdopen(os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), 'wb') as output:
                self.compose('exec', '-T', 'db', 'pg_dump', '-U', 'postgres', '-d', 'uptime_urpo',
                             '--format=custom', '--no-password', stdout=output)
                output.flush()
                os.fsync(output.fileno())
            with path.open('rb') as source:
                self.compose('exec', '-T', 'db', 'pg_restore', '--list', stdin=source, stdout=subprocess.DEVNULL)
            # Before the first migration the database has no application tables.
            migrated = self.sql("SELECT to_regclass('public.monitored_services') IS NOT NULL", capture=True).stdout.strip() == 't'
            checksum = checksum_file(path)
            manifest = {'sha256': checksum, 'createdUtc': utc(),
                        'expectedCounts': self.counts('uptime_urpo') if quiescent and migrated else None}
            private_write(path.with_suffix('.json'), json.dumps(manifest, indent=2) + '\n')
        except BaseException:
            path.unlink(missing_ok=True)
            path.with_suffix('.json').unlink(missing_ok=True)
            raise
        print(f'Backup saved and archive validated: {path}', flush=True)
        return path

    def scheduled_backup(self, now=None):
        local = (now or datetime.now(timezone.utc)).astimezone(ZoneInfo('Europe/Helsinki'))
        date = local.strftime('%Y-%m-%d')
        marker = self.state / 'last-scheduled-backup'
        if local.hour < 3 or (marker.exists() and marker.read_text().strip() == date):
            print('Daily backup is not due.')
            return
        self.backup()
        temporary = self.state / ('scheduled-' + secrets.token_hex(4))
        private_write(temporary, date + '\n')
        temporary.replace(marker)

    def release(self):
        run(['docker', 'image', 'inspect', self.image], stdout=subprocess.DEVNULL)
        self.compose('stop', 'app')
        self.compose('up', '-d', '--wait', '--wait-timeout', '90', 'db')
        # Failure leaves the app stopped. Never migrate without a successful backup.
        self.backup(quiescent=True)
        self.compose('run', '--rm', '--no-deps', 'migrate')
        self.sql('REVOKE INSERT, UPDATE, DELETE ON "__EFMigrationsHistory" FROM uptime_urpo_runtime')
        self.compose('up', '-d', '--force-recreate', '--wait', '--wait-timeout', '90', 'app')
        self.ready()
        self.settings['image'] = self.image
        temporary = self.state / ('settings-' + secrets.token_hex(4) + '.json')
        private_write(temporary, json.dumps(self.settings, indent=2) + '\n')
        temporary.replace(self.state / 'settings.json')
        print('Release ready. Database migrations were explicit; no automatic schema rollback is implied.')

    def ready(self):
        url = f"http://127.0.0.1:{self.settings['port']}/health/ready"
        for _ in range(60):
            try:
                with urllib.request.urlopen(url, timeout=3) as response:
                    if response.status == 200 and json.load(response).get('status') == 'ready':
                        print('Loopback database-aware readiness passed.', flush=True)
                        return
            except (OSError, ValueError, urllib.error.URLError):
                pass
            time.sleep(1)
        raise SystemExit('Readiness did not pass. Inspect status/logs; do not roll back database state automatically.')

    def restore_check(self, path):
        manifest_path = path.with_suffix('.json')
        if not manifest_path.is_file():
            raise SystemExit('Missing backup manifest; preserve the archive and recover its matching manifest.')
        manifest = json.loads(manifest_path.read_text())
        checksum = checksum_file(path)
        if checksum != manifest['sha256']:
            raise SystemExit('Backup checksum mismatch. Restore has not started.')
        name = 'urpo_restore_' + secrets.token_hex(6)
        self.sql(f'CREATE DATABASE "{name}" OWNER uptime_urpo_migrator')
        try:
            # Default privileges are per database, not inherited from the source database.
            self.sql('''REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO uptime_urpo_migrator;
GRANT USAGE ON SCHEMA public TO uptime_urpo_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE uptime_urpo_migrator IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO uptime_urpo_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE uptime_urpo_migrator IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO uptime_urpo_runtime;''', name)

            with path.open('rb') as source:
                self.compose('exec', '-T', 'db', 'pg_restore', '-U', 'postgres', '-d', name,
                             '--role=uptime_urpo_migrator', '--no-owner', '--no-privileges', '--exit-on-error', stdin=source)
            counts = self.counts(name)
            if counts['__EFMigrationsHistory'] is not None:
                self.sql('REVOKE INSERT, UPDATE, DELETE ON "__EFMigrationsHistory" FROM uptime_urpo_runtime', name)
                self.sql('SET ROLE uptime_urpo_runtime; SELECT count(*) FROM monitored_services', name, True)
            if manifest.get('expectedCounts') is not None and counts != manifest['expectedCounts']:
                raise SystemExit('Restored row counts differ from the quiescent backup manifest.')
            print('Restore into a separate disposable database passed: ' + json.dumps(counts), flush=True)
        finally:
            # Generated database only. The main database and its volume are never dropped.
            self.sql(f'DROP DATABASE "{name}" WITH (FORCE)')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['init', 'up', 'status', 'backup', 'scheduled-backup', 'restore-check', 'restart', 'stop', 'down'])
    parser.add_argument('--state', type=Path, default=ROOT / '.local' / 'production')
    parser.add_argument('--project')
    parser.add_argument('--port', type=int)
    parser.add_argument('--image')
    parser.add_argument('--backup', type=Path)
    args = parser.parse_args()
    state = args.state.expanduser().resolve()
    os.umask(0o077)
    if args.command == 'init':
        initialize(state, args.project, args.port, args.image)
        return
    stack = Stack(state, args.image)
    with stack.lock():
        if args.command == 'up': stack.release()
        elif args.command == 'status': stack.compose('ps')
        elif args.command == 'backup': stack.backup()
        elif args.command == 'scheduled-backup': stack.scheduled_backup()
        elif args.command == 'restore-check':
            if not args.backup: parser.error('restore-check requires --backup PATH')
            stack.restore_check(args.backup.expanduser().resolve())
        elif args.command == 'restart':
            stack.compose('restart', 'app')
            stack.ready()
        elif args.command == 'stop': stack.compose('stop', 'app')
        elif args.command == 'down': stack.compose('down') # No -v: data and backups are preserved.


if __name__ == '__main__':
    try:
        main()
    except subprocess.CalledProcessError as exception:
        raise SystemExit(f'Operation stopped after a command failed (exit {exception.returncode}). Preserve state/backups and inspect the stack.') from None
