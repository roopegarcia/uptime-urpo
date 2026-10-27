"""Delivery rejects failed runs, changed bytes and unsafe archives before Docker/SSH."""
import copy
import hashlib
import io
import json
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import zipfile
from release_artifact import digest,unpack_download,validate_bundle,validate_run,validate_download

ROOT=Path(__file__).resolve().parent.parent
class ArtifactTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.directory=Path(self.temp.name)
        self.commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
        self.run={'id':42,'event':'workflow_dispatch','path':'.github/workflows/prepare-deployment.yml','head_branch':'main','head_sha':self.commit,'status':'completed','conclusion':'success','run_attempt':1,'repository':{'full_name':'owner/repo'}}
        with (self.directory/'source.tar').open('wb') as output:subprocess.run(['git','archive','HEAD'],cwd=ROOT,stdout=output,check=True)
        (self.directory/'image.tar').write_bytes(b'test image bytes')
        self.manifest={'schemaVersion':1,'repository':'owner/repo','commit':self.commit,'runId':42,'runAttempt':1,'image':'uptime-urpo:'+self.commit,'imageId':'sha256:'+'1'*64,'migrations':['20261026133756_InitialMonitoring'],
            'sha256':{name:digest(self.directory/name) for name in ['source.tar','image.tar']}}
        self.write_manifest()

    def write_manifest(self):
        (self.directory/'manifest.json').write_text(json.dumps(self.manifest))

    def test_accepts_matching_source_and_run(self):
        self.assertEqual(self.commit,validate_bundle(self.directory,self.run,'owner/repo',42)['commit'])

    def test_failed_pending_wrong_branch_and_nonmanual_runs_block_delivery(self):
        for field,value in [('conclusion','failure'),('status','in_progress'),('event','push'),('head_branch','topic'),('path','.github/workflows/ci.yml')]:
            with self.subTest(field=field):
                run={**self.run,field:value}
                with self.assertRaises(ValueError):validate_run(run,'owner/repo',42)

    def test_rejects_another_repository(self):
        with self.assertRaises(ValueError):validate_run(self.run,'someone/else',42)

    def test_rejects_artifact_from_an_earlier_attempt(self):
        with self.assertRaises(ValueError):validate_bundle(self.directory,{**self.run,'run_attempt':2},'owner/repo',42)

    def test_changed_image_fails_checksum(self):
        (self.directory/'image.tar').write_bytes(b'changed')
        with self.assertRaisesRegex(ValueError,'checksum'):validate_bundle(self.directory,self.run,'owner/repo',42)

    def test_source_paths_cannot_escape_release_directory(self):
        with tarfile.open(self.directory/'source.tar','w') as archive:
            member=tarfile.TarInfo('../outside');member.size=1;archive.addfile(member,io.BytesIO(b'x'))
        self.manifest['sha256']['source.tar']=digest(self.directory/'source.tar');self.write_manifest()
        with self.assertRaisesRegex(ValueError,'Unsafe source'):validate_bundle(self.directory,self.run,'owner/repo',42)

    def test_source_symlinks_are_rejected(self):
        with tarfile.open(self.directory/'source.tar','w') as archive:
            member=tarfile.TarInfo('link');member.type=tarfile.SYMTYPE;member.linkname='/private';archive.addfile(member)
        self.manifest['sha256']['source.tar']=digest(self.directory/'source.tar');self.write_manifest()
        with self.assertRaisesRegex(ValueError,'Unsafe source'):validate_bundle(self.directory,self.run,'owner/repo',42)

    def test_zip_must_match_github_digest(self):
        path=self.directory/'candidate.zip';path.write_bytes(b'not the downloaded artifact')
        with self.assertRaisesRegex(ValueError,'digest'):unpack_download(path,self.directory,'sha256:'+'0'*64)

    def test_zip_traversal_cannot_write_outside_download_directory(self):
        path=self.directory/'candidate.zip'
        with zipfile.ZipFile(path,'w') as archive:archive.writestr('../escape','bad')
        with self.assertRaisesRegex(ValueError,'Unexpected'):unpack_download(path,self.directory,'sha256:'+digest(path))
        self.assertFalse((self.directory.parent/'escape').exists())

    def test_changed_extracted_manifest_is_not_trusted_even_with_new_checksums(self):
        path=self.directory/'candidate.zip'
        with zipfile.ZipFile(path,'w') as archive:
            for name in ['manifest.json','image.tar','source.tar']:archive.write(self.directory/name,arcname=name)
        listing={'artifacts':[{'id':1,'name':'deployment-'+self.commit,'expired':False,'digest':'sha256:'+digest(path)}]}
        self.manifest['imageId']='sha256:'+'2'*64;self.write_manifest()
        with patch('release_artifact.github',return_value=listing):
            with self.assertRaisesRegex(ValueError,'manifest changed'):validate_download(self.directory,self.run,'owner/repo',42)

    def test_valid_zip_extracts_only_expected_members(self):
        path=self.directory/'candidate.zip'
        with zipfile.ZipFile(path,'w') as archive:
            for name in ['manifest.json','image.tar','source.tar']:archive.write(self.directory/name,arcname=name)
        extracted=self.directory/'extracted';extracted.mkdir()
        unpack_download(path,extracted,'sha256:'+digest(path))
        self.assertEqual({'manifest.json','image.tar','source.tar'},{p.name for p in extracted.iterdir()})

if __name__=='__main__':unittest.main()
