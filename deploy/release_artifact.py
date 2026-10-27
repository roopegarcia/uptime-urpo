"""Validate the GitHub run and its immutable deployment archive before any SSH action."""
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import shutil
import urllib.request
import urllib.error
import tarfile
import zipfile


def digest(path):
    value=hashlib.sha256()
    with Path(path).open('rb') as source:
        for chunk in iter(lambda: source.read(1024*1024),b''):value.update(chunk)
    return value.hexdigest()


def github(path):
    # Public metadata can be inspected without granting a CLI OAuth token.
    if shutil.which('gh'):
        result=subprocess.run(['gh','api',path],capture_output=True,text=True)
        if result.returncode==0:return json.loads(result.stdout)
    try:
        request=urllib.request.Request('https://api.github.com/'+path,headers={'Accept':'application/vnd.github+json','User-Agent':'Uptime-Urpo-release'})
        with urllib.request.urlopen(request,timeout=20) as response:return json.load(response)
    except (OSError,urllib.error.HTTPError):
        raise SystemExit('GitHub metadata is inaccessible. Check repository/run access; authenticate gh for private repositories. No deployment started.') from None


def validate_run(run, repository, run_id):
    if run.get('repository',{}).get('full_name','').lower()!=repository.lower():raise ValueError('Wrong repository')
    if run.get('id')!=run_id or run.get('event')!='workflow_dispatch':raise ValueError('Not the requested manual workflow run')
    if run.get('path')!='.github/workflows/prepare-deployment.yml' or run.get('head_branch')!='main':raise ValueError('Not a main-branch deployment preparation')
    if run.get('status')!='completed' or run.get('conclusion')!='success':raise ValueError('Validation did not finish successfully; delivery is blocked')
    if not re.fullmatch('[0-9a-f]{40}',run.get('head_sha','')):raise ValueError('Invalid commit')


def validate_bundle(directory, run, repository, run_id):
    validate_run(run,repository,run_id)
    directory=Path(directory)
    manifest=json.loads((directory/'manifest.json').read_text())
    if manifest.get('schemaVersion')!=1:raise ValueError('Unsupported manifest')
    if manifest.get('repository','').lower()!=repository.lower() or manifest.get('commit')!=run['head_sha']:raise ValueError('Artifact source does not match the GitHub run')
    if manifest.get('runId')!=run_id or manifest.get('runAttempt')!=run['run_attempt']:raise ValueError('Artifact belongs to another run/attempt')
    if manifest.get('image')!='uptime-urpo:'+run['head_sha'] or not re.fullmatch('sha256:[0-9a-f]{64}',manifest.get('imageId','')):raise ValueError('Invalid image identity')
    if not manifest.get('migrations') or not all(re.fullmatch(r'[0-9]{14}_[A-Za-z0-9_]+',item) for item in manifest['migrations']):raise ValueError('Invalid schema metadata')
    for name in ['image.tar','source.tar']:
        expected=manifest.get('sha256',{}).get(name,'')
        if not re.fullmatch('[0-9a-f]{64}',expected) or digest(directory/name)!=expected:raise ValueError('Artifact checksum mismatch: '+name)
    with tarfile.open(directory/'source.tar') as archive:
        for member in archive.getmembers():
            path=PurePosixPath(member.name)
            if path.is_absolute() or '..' in path.parts or not (member.isfile() or member.isdir()):raise ValueError('Unsafe source archive')
    with (directory/'source.tar').open('rb') as source:
        commit=subprocess.check_output(['git','get-tar-commit-id'],stdin=source,text=True).strip()
    if commit!=manifest['commit']:raise ValueError('Source archive commit mismatch')
    return manifest


def unpack_download(archive, directory, expected_digest):
    if expected_digest!='sha256:'+digest(archive):raise ValueError('GitHub artifact digest mismatch')
    directory=Path(directory)
    allowed={'manifest.json':65536,'source.tar':32*1024*1024,'image.tar':2*1024*1024*1024}
    with zipfile.ZipFile(archive) as source:
        members=source.infolist()
        if len(members)!=3 or {item.filename for item in members}!=set(allowed):raise ValueError('Unexpected artifact files')
        for item in members:
            if item.file_size>allowed[item.filename] or (item.external_attr>>16)&0o170000==0o120000:raise ValueError('Unsafe artifact member')
            with source.open(item) as incoming, (directory/item.filename).open('xb') as output:
                while True:
                    chunk=incoming.read(1024*1024)
                    if not chunk:break
                    output.write(chunk)


def validate_download(directory, run, repository, run_id):
    validate_run(run,repository,run_id)
    listing=github(f'repos/{repository}/actions/runs/{run_id}/artifacts?per_page=100')['artifacts']
    candidates=[item for item in listing if item['name']=='deployment-'+run['head_sha'] and not item['expired']]
    if not candidates:raise ValueError('Deployment artifact is absent or expired')
    artifact=max(candidates,key=lambda item:item['id'])
    directory=Path(directory)
    if artifact.get('digest')!='sha256:'+digest(directory/'candidate.zip'):raise ValueError('Downloaded ZIP does not match GitHub')
    with zipfile.ZipFile(directory/'candidate.zip') as archive:
        if archive.namelist().count('manifest.json')!=1:raise ValueError('Invalid manifest entry')
        if hashlib.sha256(archive.read('manifest.json')).hexdigest()!=digest(directory/'manifest.json'):raise ValueError('Extracted manifest changed after download')
    return validate_bundle(directory,run,repository,run_id)
