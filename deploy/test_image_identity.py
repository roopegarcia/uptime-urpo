"""The image config identity survives classic/OCI archive wrappers and detects changes."""
import hashlib
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
from image_identity import archive_config_digest


class ImageIdentityTests(unittest.TestCase):
    def saved(self, path, config, oci):
        payload = json.dumps(config).encode()
        digest = hashlib.sha256(payload).hexdigest()
        name = 'blobs/sha256/' + digest if oci else digest + '.json'
        with tarfile.open(path, 'w') as archive:
            for key, value in [('manifest.json', json.dumps([{'Config': name}]).encode()), (name, payload)]:
                member = tarfile.TarInfo(key); member.size = len(value)
                archive.addfile(member, io.BytesIO(value))
        return 'sha256:' + digest

    def test_same_config_survives_classic_and_oci_wrappers(self):
        with tempfile.TemporaryDirectory() as directory:
            config = {'config': {'User': 'app'}, 'rootfs': {'diff_ids': ['sha256:' + '1' * 64]}}
            classic = Path(directory) / 'classic.tar'; oci = Path(directory) / 'oci.tar'
            expected = self.saved(classic, config, False); self.saved(oci, config, True)
            self.assertEqual(expected, archive_config_digest(classic))
            self.assertEqual(expected, archive_config_digest(oci))

    def test_changed_layer_or_runtime_setting_changes_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'image.tar'
            original = {'config': {'User': 'app'}, 'rootfs': {'diff_ids': ['sha256:' + '1' * 64]}}
            expected = self.saved(path, original, False)
            for changed in [dict(original, config={'User': 'root'}), dict(original, rootfs={'diff_ids': ['sha256:' + '2' * 64]})]:
                self.saved(path, changed, True)
                self.assertNotEqual(expected, archive_config_digest(path))

    def test_metadata_symlink_is_rejected_without_extraction(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'image.tar'
            with tarfile.open(path, 'w') as archive:
                member = tarfile.TarInfo('manifest.json'); member.type = tarfile.SYMTYPE; member.linkname = '/etc/passwd'
                archive.addfile(member)
            with self.assertRaisesRegex(ValueError, 'metadata'): archive_config_digest(path)
