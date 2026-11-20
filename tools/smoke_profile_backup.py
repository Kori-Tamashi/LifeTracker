#!/usr/bin/env python3
"""Exercise offline maintenance with separate processes and disposable synthetic profiles."""
import base64
import fcntl
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import uuid
from urllib.request import Request

from smoke_local_api import OPENER, start, stop, request


def maintenance(dll, source, *arguments, success=True):
    result = subprocess.run(['dotnet', str(dll), *map(str, arguments)],
                            env=dict(os.environ, LIFETRACKER_RUNTIME_ROOT=str(source)),
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=60)
    if (result.returncode == 0) != success:
        raise RuntimeError('Unexpected offline maintenance result')


def send(url, token, body, command):
    data = json.dumps(body).encode()
    headers = {'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json',
               'Idempotency-Key': command, 'X-Command-ID': command}
    with OPENER.open(Request(url + '/api/v1/assessments', data=data, headers=headers), timeout=10) as response:
        return json.load(response)


def hashes(root):
    return {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in root.iterdir() if p.is_file()}


def main():
    dll = Path(sys.argv[1]).resolve(strict=True)
    with tempfile.TemporaryDirectory(prefix='lifetracker-backup-process-synthetic-') as temporary:
        root = Path(temporary)
        source, backup, restored = root / 'source', root / 'backup', root / 'restored'
        body = {'observed_at': '2026-01-15T12:00:00Z', 'values': [{'metric': 'energy', 'score': 8}], 'comment': ''}
        command = str(uuid.uuid4())
        process, url = start(dll, source)
        try:
            token = (source / 'owner.credential').read_text()
            first = send(url, token, body, command)
            maintenance(dll, source, 'backup', backup, success=False)
            if backup.exists():
                raise RuntimeError('Live writer backup unexpectedly published')
        finally:
            stop(process)
        # Same flock API as the Swift desktop journal; must interoperate with .NET FileShare.None.
        with (source / 'desktop-assessment.lock').open('a') as desktop:
            fcntl.flock(desktop, fcntl.LOCK_EX | fcntl.LOCK_NB)
            maintenance(dll, source, 'backup', backup, success=False)
        pending = {'id': command, 'path': 'assessments', 'method': 'POST',
                   'body': base64.b64encode(json.dumps(body).encode()).decode()}
        journal = json.dumps(pending).encode()
        (source / 'desktop-pending-assessment.json').write_bytes(journal)
        maintenance(dll, source, 'backup', backup)
        before = hashes(backup)
        maintenance(dll, source, 'verify-backup', backup)
        maintenance(dll, source, 'restore', backup, restored)
        if hashes(backup) != before or (restored / 'desktop-pending-assessment.json').read_bytes() != journal:
            raise RuntimeError('Backup or pending journal changed')
        if (restored / 'owner.credential').read_text() != token:
            raise RuntimeError('Credential changed during restore')
        process, url = start(dll, restored)
        try:
            if send(url, token, body, command) != first:
                raise RuntimeError('Restored receipt changed on replay')
            if len(request(url, token, 'assessments')['items']) != 1:
                raise RuntimeError('Restored retry duplicated an observation')
            maintenance(dll, source, 'restore', backup, restored, success=False)
        finally:
            stop(process)
        corrupt = root / 'corrupt'
        shutil.copytree(backup, corrupt)
        (corrupt / 'owner.credential').write_text('a' * 43)
        rejected = root / 'must-not-exist'
        maintenance(dll, source, 'restore', corrupt, rejected, success=False)
        if rejected.exists():
            raise RuntimeError('Corrupt restore published a destination')
    print('Offline backup smoke passed: writer/desktop locks, snapshot/verify/restore, receipt replay and corruption refusal.')


if __name__ == '__main__':
    main()
