#!/usr/bin/env python3
"""Build the route inventory used to distinguish unavailable capabilities from unknown paths."""
import argparse
import json
from pathlib import Path
import yaml

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    api = yaml.safe_load((ROOT / 'docs/architecture/contracts/openapi.yaml').read_text())
    rows = [{'path': path, 'method': method.upper()} for path, operations in api['paths'].items()
            for method in operations if method in {'get', 'post', 'patch', 'put', 'delete'}]
    content = json.dumps(rows, indent=2, ensure_ascii=False) + '\n'
    target = ROOT / 'configs/api/routes.json'
    if args.check:
        if not target.exists() or target.read_text() != content:
            raise SystemExit('API manifest differs from OpenAPI; run tools/build_api_manifest.py')
    else:
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content)
    print(f'API manifest: {len(rows)} routes consistent')

if __name__ == '__main__':
    main()
