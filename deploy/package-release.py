#!/usr/bin/env python3
"""Package an already validated GitHub image and clean source snapshot; no secrets."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
from release_artifact import digest
from image_identity import archive_config_digest

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--image',required=True)
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args()
commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
if subprocess.check_output(['git','status','--porcelain'],text=True):raise SystemExit('A clean source snapshot is required')
repository=os.environ.get('GITHUB_REPOSITORY','')
run_id=int(os.environ.get('GITHUB_RUN_ID','0'));attempt=int(os.environ.get('GITHUB_RUN_ATTEMPT','0'))
if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+',repository) or min(run_id,attempt)<=0:raise SystemExit('GitHub run metadata is required')
if args.image!='uptime-urpo:'+commit:raise SystemExit('Image tag must identify this exact commit')
info=json.loads(subprocess.check_output(['docker','image','inspect',args.image]))[0]
if info['Architecture']!='amd64' or info['Config'].get('Labels',{}).get('org.opencontainers.image.revision')!=commit:raise SystemExit('Image architecture/source label mismatch')
migrations=json.loads(subprocess.check_output(['docker','run','--rm','--network','none','--read-only','--cap-drop','ALL','--security-opt','no-new-privileges','--entrypoint','dotnet',args.image,'UptimeUrpo.Api.dll','--schema-migrations'],text=True))
args.output.mkdir(parents=True,exist_ok=True)
for name,command in [('image.tar',['docker','image','save',args.image]),('source.tar',['git','archive',commit])]:
    with (args.output/name).open('xb') as output:subprocess.run(command,stdout=output,check=True)
manifest={'schemaVersion':2,'repository':repository,'commit':commit,'runId':run_id,'runAttempt':attempt,'image':args.image,'imageConfigDigest':archive_config_digest(args.output/'image.tar'),'migrations':migrations,
          'sha256':{name:digest(args.output/name) for name in ['image.tar','source.tar']}}
(args.output/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('Packaged exact AMD64 image and clean source for '+commit)
