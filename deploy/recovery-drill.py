#!/usr/bin/env python3
"""Demonstrate failed delivery, guarded image rollback and backup data loss in a NEW isolated stack."""
import argparse
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
from ops import ROOT,Stack,initialize
from verification_fixture import seed_fixture

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--image',required=True)
parser.add_argument('--port',type=int,default=18081)
args=parser.parse_args()
os.umask(0o077)
nonce=secrets.token_hex(5)
state=ROOT/'.local'/('recovery-'+nonce)
project='uptime-urpo-verify-'+nonce
initialize(state,project,args.port,args.image)
stack=Stack(state)
broken='uptime-urpo:broken-'+nonce
try:
    with stack.lock():
        stack.release()
        seed_fixture(stack)
        stack.compose('stop','app')
        recovery_point=stack.backup(quiescent=True)
        stack.sql('''INSERT INTO check_results ("Id","ServiceId","MonitoringPeriodId","CheckedAtUtc","CompletedAtUtc","DurationMs","StatusCode","Success") VALUES (900002,'deployment-fixture',900001,now(),now(),155,200,true)''')
        stack.compose('start','app');stack.ready()
        before=stack.counts('uptime_urpo')
        with tempfile.TemporaryDirectory() as context:
            Path(context,'Dockerfile').write_text('FROM '+args.image+'\nENTRYPOINT ["dotnet","UptimeUrpo.Api.dll","--Monitoring:MaxConcurrency=0"]\n')
            subprocess.run(['docker','build','-t',broken,context],check=True)
        candidate=Stack(state,broken)
        try:
            candidate.release()
        except subprocess.CalledProcessError:
            print('Expected broken application release was rejected.',flush=True)
            with (state/'failed-application.log').open('w') as output:
                candidate.compose('logs','--no-color','--tail','80','app',stdout=output)
        else:raise AssertionError('Broken release unexpectedly passed readiness')
        assert json.loads((state/'settings.json').read_text())['image']==args.image
        assert stack.counts('uptime_urpo')==before
        stack.rollback()
        assert stack.counts('uptime_urpo')==before
        print('Matching-schema image rollback preserved observations.',flush=True)
        stack.sql('''CREATE TABLE recovery_drill_schema_marker (id integer);
INSERT INTO "__EFMigrationsHistory" ("MigrationId","ProductVersion") VALUES ('20990101000000_RecoveryDrillOnly','10.0.14')''')
        try:
            try:stack.rollback()
            except SystemExit as error:
                assert 'cannot reverse migrations' in str(error)
                print('Unknown newer schema blocks image rollback before stopping the healthy app.',flush=True)
            else:raise AssertionError('Schema mismatch was accepted')
        finally:
            stack.sql('''DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId"='20990101000000_RecoveryDrillOnly'; DROP TABLE recovery_drill_schema_marker''')
        stack.ready()
        restored=stack.restore_check(recovery_point)
        assert restored['check_results']==1
        assert stack.counts('uptime_urpo')['check_results']==2
        print('Backup restored one check; the live isolated database kept two. Post-backup observations are absent from the recovered copy.',flush=True)
finally:
    # Keep private fixture state, backups and volume for inspection; stop only this unique stack.
    stack.compose('down')
print('PASS: failed release, compatible image rollback, schema mismatch refusal, and backup recovery limits. No production/development database was used.')
