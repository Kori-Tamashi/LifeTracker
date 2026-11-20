#!/usr/bin/env python3
"""Exercise the real Swift client/presentation model against an isolated .NET host."""
import os
from pathlib import Path
import subprocess
import sys
import shutil
import socket
import tempfile

from smoke_local_api import start, stop


def main():
    dll = Path(sys.argv[1]).resolve(strict=True)
    with tempfile.TemporaryDirectory(prefix='lifetracker-swift-synthetic-') as temporary:
        root = Path(temporary)
        process, url = start(dll, root)
        try:
            with socket.socket() as available:
                available.bind(('127.0.0.1', 0))
                port = available.getsockname()[1]
            environment = dict(os.environ, LIFETRACKER_INTEGRATION_URL=url + '/api/v1',
                               LIFETRACKER_INTEGRATION_ROOT=str(root), LIFETRACKER_INTEGRATION_DLL=str(dll),
                               LIFETRACKER_INTEGRATION_DOTNET=shutil.which('dotnet'),
                               LIFETRACKER_MANAGED_TEST_PORT=str(port))
            subprocess.run(['sh', 'tools/test_macos.sh', '--filter', 'Live'],
                           env=environment, check=True, timeout=120)
        finally:
            stop(process)
    print('Swift client/host smoke passed: record/retry/history/correction/conflict owned stop/restart, Core crash/replay and external host preservation.')


if __name__ == '__main__':
    main()
