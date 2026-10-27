"""Portable image identity: SHA-256 of config bytes, which bind runtime settings and layer diff IDs.

Docker stores can report a config, manifest, or index as inspect.Id. Save/load may
change that wrapper; the config bytes and uncompressed layer identities survive.
"""
import hashlib
import json
from pathlib import Path
import subprocess
import tarfile
import tempfile


def archive_config_digest(path):
    with tarfile.open(path) as archive:
        def read_regular(name):
            member = archive.getmember(name)
            if not member.isfile() or member.size > 1024 * 1024:
                raise ValueError('Invalid image metadata member')
            return archive.extractfile(member).read()
        entries = json.loads(read_regular('manifest.json'))
        if len(entries) != 1:
            raise ValueError('Expected exactly one saved image')
        config = read_regular(entries[0]['Config'])
        data = json.loads(config)
        if not data.get('rootfs', {}).get('diff_ids'):
            raise ValueError('Image config has no layer identities')
        return 'sha256:' + hashlib.sha256(config).hexdigest()


def image_config_digest(image):
    with tempfile.TemporaryDirectory(prefix='uptime-image-') as directory:
        path = Path(directory) / 'image.tar'
        with path.open('wb') as output:
            subprocess.run(['docker', 'image', 'save', image], stdout=output, check=True)
        return archive_config_digest(path)


if __name__ == '__main__':
    import sys
    print(image_config_digest(sys.argv[1]))
