#!/usr/bin/env python3
"""Exercise real containers using synthetic rows in an explicitly isolated check stack."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.error
import urllib.request
from ops import Stack

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--state', type=Path, required=True)
args = parser.parse_args()
os.umask(0o077)
stack = Stack(args.state.resolve())
assert stack.settings['project'] == 'uptime-urpo-m6-check' or stack.settings['project'].startswith('uptime-urpo-verify-'), 'Never seed a production/development database.'
base = f"http://127.0.0.1:{stack.settings['port']}"

def request(path):
    return urllib.request.urlopen(base + path, timeout=8)

with stack.lock():
    stack.ready()
    with request('/') as response:
        html = response.read().decode()
        assert 'Uptime-Urpo' in html
    asset = re.search(r'src="(/assets/[^\"]+\.js)"', html).group(1)
    with request(asset) as response: assert response.status == 200
    try: request('/api/not-a-route')
    except urllib.error.HTTPError as response:
        assert response.code == 404 and 'application/problem+json' in response.headers['content-type']
    else: raise AssertionError('API 404 became HTML')
    for service, expected_user in [('app', 'app'), ('db', 'postgres')]:
        identifier = stack.compose('ps', '-q', service, capture_output=True, text=True).stdout.strip()
        info = json.loads(subprocess.check_output(['docker', 'inspect', identifier]))[0]
        assert info['Config']['User'] == expected_user
        assert info['HostConfig']['ReadonlyRootfs']
        assert 'ALL' in info['HostConfig']['CapDrop']
        if service == 'db': assert not info['HostConfig']['PortBindings']
        else:
            assert info['HostConfig']['PortBindings']['8080/tcp'][0]['HostIp'] == '127.0.0.1'
            assert info['State']['Health']['Status'] == 'healthy'
    stack.compose('exec', '-T', 'app', 'test', '!', '-e', '/run/secrets/migration_config')
    stack.compose('exec', '-T', 'app', 'test', '!', '-e', '/run/secrets/admin_password')
    privileges = stack.sql("SELECT rolsuper, rolcreatedb, rolcreaterole, has_schema_privilege('uptime_urpo_runtime','public','CREATE'), has_table_privilege('uptime_urpo_runtime','public.\"__EFMigrationsHistory\"','INSERT') FROM pg_roles WHERE rolname='uptime_urpo_runtime'", capture=True).stdout.strip()
    assert privileges == 'f|f|f|f|f', privileges
    # Retired synthetic rows are never checked and do not alter private operator configuration.
    stack.sql('''INSERT INTO monitored_services ("Id","Name","Url","Enabled","Retired","IntervalSeconds","TimeoutSeconds","ExpectedStatusCode","FailureThreshold","SlowResponseThresholdMs","RegisteredAtUtc","ConsecutiveFailures") VALUES ('deployment-fixture','Deployment fixture','https://example.com/',false,true,60,10,200,3,2000,now(),0) ON CONFLICT DO NOTHING;
INSERT INTO monitoring_periods ("Id","ServiceId","StartedAtUtc","EndedAtUtc","Url","Enabled","IntervalSeconds","TimeoutSeconds","ExpectedStatusCode","FailureThreshold","SlowResponseThresholdMs") VALUES (900001,'deployment-fixture',now()-interval '5 minutes',now(),'https://example.com/',true,60,10,200,3,2000) ON CONFLICT DO NOTHING;
INSERT INTO check_results ("Id","ServiceId","MonitoringPeriodId","CheckedAtUtc","CompletedAtUtc","DurationMs","StatusCode","Success") VALUES (900001,'deployment-fixture',900001,now(),now(),123,200,true) ON CONFLICT DO NOTHING;
INSERT INTO incidents ("Id","ServiceId","FirstFailureAtUtc","ConfirmedAtUtc","EndedAtUtc","Resolution","HasMonitoringGaps") VALUES (900001,'deployment-fixture',now()-interval '3 minutes',now()-interval '1 minute',now(),'Recovered',false) ON CONFLICT DO NOTHING;''')
    before = stack.counts('uptime_urpo')
    stack.compose('stop', 'app')
    identifier = stack.compose('ps', '-aq', 'app', capture_output=True, text=True).stdout.strip()
    stopped = json.loads(subprocess.check_output(['docker', 'inspect', identifier]))[0]['State']
    assert stopped['ExitCode'] == 0 and not stopped['OOMKilled'], stopped
    backup = stack.backup(quiescent=True)
    stack.restore_check(backup)
    assert stack.counts('uptime_urpo') == before
    stack.compose('start', 'app')
    stack.ready()
    assert stack.counts('uptime_urpo') == before
    stack.compose('stop', 'db')
    with request('/health') as response: assert response.status == 200
    try: request('/health/ready')
    except urllib.error.HTTPError as response: assert response.code == 503
    else: raise AssertionError('Database outage did not fail readiness')
    stack.compose('start', 'db')
    stack.ready()
    assert stack.counts('uptime_urpo') == before
print('PASS: same-origin UI/API, non-root/read-only containers, private roles/ports, graceful shutdown, seeded history/incident restore, restart continuity, and database-outage readiness.')
