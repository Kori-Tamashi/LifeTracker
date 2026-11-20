#!/usr/bin/env python3
"""Validate synthetic, actually captured HTTP responses against the normative OpenAPI schemas."""
import json
import sys
from pathlib import Path
import yaml
from jsonschema import Draft4Validator, FormatChecker

ROOT = Path(__file__).resolve().parents[1]

def convert(value):
    if isinstance(value, list):
        return [convert(item) for item in value]
    if not isinstance(value, dict):
        return value
    result = {key: (item.replace('#/components/schemas/', '#/definitions/') if key == '$ref' else convert(item))
              for key, item in value.items() if key != 'nullable'}
    if value.get('nullable'):
        return {'anyOf': [result, {'type': 'null'}]}
    return result

def main():
    api = yaml.safe_load((ROOT / 'docs/architecture/contracts/openapi.yaml').read_text())
    definitions = convert(api['components']['schemas'])
    evidence = json.loads(Path(sys.argv[1]).read_text())
    required = {'CurrentState', 'Capabilities', 'Receipt', 'AssessmentRecordPage', 'Error'}
    checker = FormatChecker()
    if "date-time" not in checker.checkers:
        raise SystemExit("Install tools/requirements-design.txt: date-time format validation is required")
    seen = set()
    for record in evidence:
        name = record['schema']
        validator = Draft4Validator({'$ref': '#/definitions/' + name, 'definitions': definitions}, format_checker=checker)
        # Never echo response bodies into shared logs, including on failure.
        errors = list(validator.iter_errors(record['body']))
        if errors:
            raise SystemExit(f'Runtime contract failed: schema={name}, rule={errors[0].validator}')
        seen.add(name)
    if not required <= seen:
        raise SystemExit('Runtime contract evidence is incomplete')
    print(f'Runtime contract: {len(evidence)} actual responses match {len(seen)} OpenAPI schemas')

if __name__ == '__main__':
    main()
