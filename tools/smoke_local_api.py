#!/usr/bin/env python3
"""Start the built host twice using an isolated synthetic profile; never print credentials or response bodies."""
import json
import os
from pathlib import Path
import selectors
import signal
import subprocess
import sys
import tempfile
from urllib.parse import urlparse
from urllib.request import Request, ProxyHandler, build_opener

OPENER = build_opener(ProxyHandler({}))

def start(dll, root):
    env = dict(os.environ, LIFETRACKER_RUNTIME_ROOT=str(root), LIFETRACKER_PORT='0')
    process = subprocess.Popen(['dotnet', str(dll)], env=env, stdout=subprocess.PIPE,
                               stderr=subprocess.DEVNULL, text=True)
    selector = selectors.DefaultSelector()
    selector.register(process.stdout, selectors.EVENT_READ)
    try:
        if not selector.select(30):
            raise RuntimeError('Host startup timed out')
        line = process.stdout.readline().strip()
        if not line.startswith('LifeTracker API: '):
            raise RuntimeError('Host did not publish a loopback address')
        url = line.removeprefix('LifeTracker API: ').rstrip('/')
        if urlparse(url).hostname != '127.0.0.1':
            raise RuntimeError('Host did not bind loopback')
        return process, url
    except Exception:
        process.kill()
        process.communicate()
        raise
    finally:
        selector.close()

def stop(process):
    if process.poll() is None:
        process.send_signal(signal.SIGINT)
        try:
            process.communicate(timeout=15)
        except subprocess.TimeoutExpired:
            process.kill()
            process.communicate()
            raise RuntimeError('Host shutdown timed out') from None
    if process.returncode != 0:
        raise RuntimeError('Host shutdown failed')

def request(url, token, path, data=None, method=None):
    headers = {'Authorization': 'Bearer ' + token}
    if data is not None:
        data = json.dumps(data).encode()
        headers.update({'Content-Type': 'application/json', 'Idempotency-Key': 'synthetic-process-smoke'})
    with OPENER.open(Request(url + '/api/v1/' + path, data=data, headers=headers, method=method), timeout=10) as result:
        if result.status != 200:
            raise RuntimeError('Unexpected smoke HTTP status')
        return json.load(result)

def main():
    dll = Path(sys.argv[1]).resolve(strict=True)
    with tempfile.TemporaryDirectory(prefix='lifetracker-api-synthetic-') as temporary:
        root = Path(temporary)
        process, url = start(dll, root)
        try:
            token = (root / 'owner.credential').read_text()
            body = {'observed_at': '2026-01-15T12:00:00Z', 'values': [{'metric': 'energy', 'score': 8}]}
            first = request(url, token, 'assessments', body)
        finally:
            stop(process)
        process, url = start(dll, root)
        try:
            if (root / 'owner.credential').read_text() != token:
                raise RuntimeError('Owner credential changed on restart')
            retry = request(url, token, 'assessments', body)
            if retry != first:
                raise RuntimeError('Receipt changed on restart')
            page = request(url, token, 'assessments')
            if len(page['items']) != 1 or page['items'][0]['score'] != 8:
                raise RuntimeError('Stored observation was not preserved')
            patched = request(url, token, 'assessments/' + first['resource_id'],
                              {'expected_revision': 1, 'observed_at': '2026-01-15T12:00:00Z', 'score': 6, 'reason': 'Synthetic correction'}, 'PATCH')
            if patched['revision'] != 2:
                raise RuntimeError('Correction was not applied')
        finally:
            stop(process)
    print('Process smoke passed: create, restart, read, retry and correction')

if __name__ == '__main__':
    main()
