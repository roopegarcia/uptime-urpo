"""Synthetic persistence fixture; never used by the application or production deployment."""
def seed_fixture(stack):
    project=stack.settings['project']
    if project!='uptime-urpo-m6-check' and not project.startswith('uptime-urpo-verify-'):
        raise ValueError('Synthetic rows are forbidden in this project')
    stack.sql('''INSERT INTO monitored_services ("Id","Name","Url","Enabled","Retired","IntervalSeconds","TimeoutSeconds","ExpectedStatusCode","FailureThreshold","SlowResponseThresholdMs","RegisteredAtUtc","ConsecutiveFailures") VALUES ('deployment-fixture','Deployment fixture','https://example.com/',false,true,60,10,200,3,2000,now(),0) ON CONFLICT DO NOTHING;
    INSERT INTO monitoring_periods ("Id","ServiceId","StartedAtUtc","EndedAtUtc","Url","Enabled","IntervalSeconds","TimeoutSeconds","ExpectedStatusCode","FailureThreshold","SlowResponseThresholdMs") VALUES (900001,'deployment-fixture',now()-interval '5 minutes',now(),'https://example.com/',true,60,10,200,3,2000) ON CONFLICT DO NOTHING;
    INSERT INTO check_results ("Id","ServiceId","MonitoringPeriodId","CheckedAtUtc","CompletedAtUtc","DurationMs","StatusCode","Success") VALUES (900001,'deployment-fixture',900001,now(),now(),123,200,true) ON CONFLICT DO NOTHING;
    INSERT INTO incidents ("Id","ServiceId","FirstFailureAtUtc","ConfirmedAtUtc","EndedAtUtc","Resolution","HasMonitoringGaps") VALUES (900001,'deployment-fixture',now()-interval '3 minutes',now()-interval '1 minute',now(),'Recovered',false) ON CONFLICT DO NOTHING;''')
