#!/usr/bin/env python3
"""Exercise the real Swift client/presentation model against an isolated .NET host."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile

from smoke_local_api import start, stop


def main():
    dll = Path(sys.argv[1]).resolve(strict=True)
    with tempfile.TemporaryDirectory(prefix='lifetracker-swift-synthetic-') as temporary:
        root = Path(temporary)
        process, url = start(dll, root)
        try:
            environment = dict(os.environ, LIFETRACKER_INTEGRATION_URL=url + '/api/v1',
                               LIFETRACKER_INTEGRATION_ROOT=str(root))
            subprocess.run(['sh', 'tools/test_macos.sh', '--filter', 'LiveAssessmentTests'],
                           env=environment, check=True, timeout=120)
        finally:
            stop(process)
    print('Swift client/host smoke passed: record/retry/history/correction/conflict.')


if __name__ == '__main__':
    main()
