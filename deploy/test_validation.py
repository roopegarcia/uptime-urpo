"""A failed validation command must stop before production build/delivery."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

class ValidationGateTests(unittest.TestCase):
    def test_failed_frontend_tests_prevent_build(self):
        root=Path(__file__).resolve().parent.parent
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory);log=path/'commands'
            executable=path/'npm'
            executable.write_text('#!/bin/sh\nprintf "%s\\n" "$*" >> "$VALIDATION_LOG"\ncase "$*" in *" test") exit 13;; esac\nexit 0\n')
            executable.chmod(0o755)
            result=subprocess.run([sys.executable,str(root/'scripts/verify.py'),'--part','frontend'],env={**os.environ,'PATH':str(path)+os.pathsep+os.environ['PATH'],'VALIDATION_LOG':str(log)},capture_output=True,text=True)
            self.assertEqual(13,result.returncode)
            commands=log.read_text()
            self.assertIn(' test',commands)
            self.assertNotIn('run build',commands)

if __name__=='__main__':unittest.main()
