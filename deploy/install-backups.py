#!/usr/bin/env python3
"""Install one project-scoped daily cron entry, preserving all unrelated entries."""
import argparse
from pathlib import Path
import shlex
import subprocess
from ops import Stack

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--state', type=Path, required=True)
parser.add_argument('--checkout', type=Path, required=True, help='Stable current symlink maintained by the publisher')
args = parser.parse_args()
stack = Stack(args.state.resolve())
marker = '# uptime-urpo-backup:' + stack.settings['project']
command = shlex.join(['/usr/bin/python3', str(args.checkout.absolute() / 'deploy/ops.py'), 'scheduled-backup', '--state', str(stack.state)])
entry = '17 * * * * ' + command + ' > ' + shlex.quote(str(stack.state / 'backup-schedule.log')) + ' 2>&1 ' + marker
if '%' in entry or '\n' in entry: raise SystemExit('Unsupported cron path')
previous = subprocess.run(['crontab', '-l'], capture_output=True, text=True)
if previous.returncode not in (0, 1): raise SystemExit('Cannot read existing crontab')
if previous.returncode == 1 and 'no crontab for' not in previous.stderr: raise SystemExit('Cannot read existing crontab')
lines = [line for line in previous.stdout.splitlines() if marker not in line]
subprocess.run(['crontab', '-'], input='\n'.join(lines + [entry]) + '\n', text=True, check=True)
print('Installed hourly check for one daily backup at/after 03:17 Europe/Helsinki. Other cron entries were preserved.')
