#!/usr/bin/env python3
"""The same validation gates run locally and on GitHub-hosted runners."""
import argparse
from pathlib import Path
import shutil
import subprocess
import os

ROOT = Path(__file__).resolve().parent.parent

def run(arguments):
    subprocess.run(arguments, cwd=ROOT, check=True)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--part', choices=['all','backend','frontend','operations','image'], default='all')
    parser.add_argument('--image', default='uptime-urpo:validation')
    args = parser.parse_args()
    parts = ['backend','frontend','operations','image'] if args.part == 'all' else [args.part]
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    for part in parts:
        print('Validating '+part, flush=True)
        if part == 'backend':
            run([dotnet,'restore','Uptime-Urpo.slnx','--locked-mode'])
            run([dotnet,'build','Uptime-Urpo.slnx','-c','Release','--no-restore'])
            run([dotnet,'format','Uptime-Urpo.slnx','--verify-no-changes','--no-restore','--verbosity','quiet'])
            for category in ['Category!=Database','Category=Database']:
                run([dotnet,'test','tests/UptimeUrpo.IntegrationTests' if category=='Category=Database' else 'Uptime-Urpo.slnx','-c','Release','--no-build','--no-restore','--filter',category,
                     '--settings','tests/ci.runsettings','--logger','trx','--results-directory',str(ROOT/'artifacts/test-results'/('database' if category=='Category=Database' else 'fast'))])
        elif part == 'frontend':
            for command in [['ci'],['run','format:check'],['test'],['run','build']]:
                run(['npm','--prefix','frontend',*command])
        elif part == 'operations':
            run(['python3','-m','unittest','discover','-s','deploy','-p','test_*.py'])
            run(['python3','scripts/check-docs.py'])
            for path in ['deploy/init-roles.sh','database/init-roles.sh','scripts/check-prerequisites.sh']:
                run(['sh','-n',path])
            for path in [*ROOT.glob('deploy/*.py'), ROOT/'deploy/publish',ROOT/'deploy/fetch-backups',ROOT/'deploy/from-actions',ROOT/'deploy/from-artifact']:
                compile(path.read_text(),str(path),'exec')
            if shutil.which('actionlint'): run(['actionlint','-color'])
            else: run(['docker','run','--rm','--network','none','--cap-drop','ALL','--read-only','--volume',str(ROOT/'.github')+':/repo/.github:ro','--workdir','/repo','rhysd/actionlint:1.7.12@sha256:b1934ee5f1c509618f2508e6eb47ee0d3520686341fec936f3b79331f9315667','-color',*['.github/workflows/'+path.name for path in (ROOT/'.github/workflows').glob('*.yml')]])
        elif part == 'image':
            commit = subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
            run(['docker','buildx','build','--load','--platform','linux/amd64','--build-arg','REVISION='+commit,'--tag',args.image,'.'])

if __name__ == '__main__':
    try: main()
    except subprocess.CalledProcessError as error:
        raise SystemExit(error.returncode) from None
