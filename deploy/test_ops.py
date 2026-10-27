"""Failure boundaries in release/recovery orchestration; real containers are tested separately."""
import json
import ast
from datetime import datetime, timezone
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import ops

class OperationTests(unittest.TestCase):
    def test_daily_schedule_uses_helsinki_and_does_not_repeat_at_dst_fallback(self):
        with tempfile.TemporaryDirectory() as directory:
            stack = object.__new__(ops.Stack)
            stack.state = Path(directory)
            calls = []
            stack.backup = lambda: calls.append('backup')
            # Both UTC instants are 03:17 in Helsinki on autumn clock-change day.
            stack.scheduled_backup(datetime(2026, 10, 25, 0, 17, tzinfo=timezone.utc))
            stack.scheduled_backup(datetime(2026, 10, 25, 1, 17, tzinfo=timezone.utc))
            self.assertEqual(['backup'], calls)

    def test_failed_scheduled_backup_is_not_marked_successful(self):
        with tempfile.TemporaryDirectory() as directory:
            stack = object.__new__(ops.Stack)
            stack.state = Path(directory)
            def failed(): raise OSError('unavailable')
            stack.backup = failed
            with self.assertRaises(OSError):
                stack.scheduled_backup(datetime(2026, 10, 27, 2, 17, tzinfo=timezone.utc))
            self.assertFalse((stack.state / 'last-scheduled-backup').exists())

    def test_embedded_remote_programs_are_valid_python(self):
        tree = ast.parse((Path(__file__).parent / 'publish').read_text())
        for node in ast.walk(tree):
            if isinstance(node, ast.Constant) and isinstance(node.value, str) and node.value.startswith('import '):
                compile(node.value, '<remote publisher program>', 'exec')

    def test_failed_backup_prevents_migration_and_app_start(self):
        stack = object.__new__(ops.Stack)
        stack.image = 'uptime-urpo:test'
        calls = []
        stack.compose = lambda *args, **kwargs: calls.append(args)
        def failed(**kwargs): raise OSError('backup storage unavailable')
        stack.backup = failed
        with patch.object(ops, 'run'):
            with self.assertRaises(OSError): stack.release()
        self.assertIn(('stop', 'app'), calls)
        self.assertFalse(any('migrate' in call for call in calls))
        self.assertFalse(any(call[-1] == 'app' and call[0] == 'up' for call in calls))

    def test_corrupt_archive_never_creates_a_restore_database(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'backup.dump'
            path.write_bytes(b'corrupted')
            path.with_suffix('.json').write_text(json.dumps({'sha256': 'invalid'}))
            stack = object.__new__(ops.Stack)
            calls = []
            stack.sql = lambda *args, **kwargs: calls.append(args)
            with self.assertRaises(SystemExit): stack.restore_check(path)
            self.assertEqual([], calls)

    def test_existing_state_is_never_reinitialized(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / 'keep').write_text('existing data')
            with self.assertRaises(SystemExit): ops.initialize(path, 'uptime-urpo-production', 8108, 'uptime-urpo:test')
            self.assertEqual('existing data', (path / 'keep').read_text())

    def test_existing_volume_blocks_new_credentials(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'new-state'
            with patch.object(ops.subprocess, 'run') as run:
                run.return_value.returncode = 0
                with self.assertRaises(SystemExit): ops.initialize(path, 'uptime-urpo-production', 8108, 'uptime-urpo:test')
            self.assertFalse(path.exists())

if __name__ == '__main__': unittest.main()
