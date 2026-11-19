#!/usr/bin/env python3
"""Validate publishable project files; never traverse ignored personal data."""
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[1]


def visible_files():
    result = subprocess.run(
        ['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'],
        cwd=ROOT, check=True, capture_output=True,
    )
    return sorted(set(Path(name.decode('utf-8')) for name in result.stdout.split(b'\0') if name))


def validate(paths):
    errors = []
    text_count = 0
    diagram_count = 0
    forbidden_roots = {'private', 'local', 'var'}
    secret_suffixes = {'.pem', '.key', '.p12', '.pfx', '.mobileprovision', '.sqlite', '.sqlite3', '.db', '.har'}
    for rel in paths:
        path = ROOT / rel
        if rel.parts[0] in forbidden_roots or rel.suffix.lower() in secret_suffixes:
            errors.append(f'{rel}: forbidden publication file')
            continue
        if (rel.name == '.env' or rel.name.startswith('.env.')) and rel.name != '.env.example':
            errors.append(f'{rel}: real environment configuration')
            continue
        if not path.is_file():
            errors.append(f'{rel}: tracked file missing from worktree')
            continue
        if path.is_symlink():
            errors.append(f'{rel}: review symlink before publication')
            continue
        if rel.suffix.lower() not in {'.md', '.puml', '.bpmn', '.svg', '.py', '.mjs', '.sql', '.yml', '.yaml', '.json', '.toml'}:
            continue
        try:
            content = path.read_text(encoding='utf-8')
        except UnicodeError:
            errors.append(f'{rel}: expected UTF-8 text')
            continue
        text_count += 1
        if re.search(r'/(?:Users|home)/[^/\s]+/', content):
            errors.append(f'{rel}: personal absolute filesystem path')
        if rel.suffix == '.md':
            for target in re.findall(r'\]\(([^\s]+)\)', content):
                target = target.strip('<>')
                if re.match(r'^[a-zA-Z][a-zA-Z0-9+.-]*:', target) or target.startswith('#'):
                    continue
                target = unquote(target.split('#', 1)[0])
                resolved = (path.parent / target).resolve()
                if not resolved.is_relative_to(ROOT):
                    errors.append(f'{rel}: local link escapes repository')
                elif set(resolved.relative_to(ROOT).parts[:1]) & forbidden_roots:
                    errors.append(f'{rel}: link to private runtime data')
                elif not resolved.exists():
                    errors.append(f'{rel}: broken local link {target}')
        if rel.suffix == '.puml':
            diagram_count += 1
            lines = content.strip().splitlines()
            if not lines or lines[0] != '@startuml' or lines[-1] != '@enduml':
                errors.append(f'{rel}: missing PlantUML envelope')
            if content.count('@startuml') != 1 or content.count('@enduml') != 1:
                errors.append(f'{rel}: expected exactly one diagram')
            if re.search(r'^\s*!include', content, flags=re.MULTILINE):
                errors.append(f'{rel}: external includes require explicit review')
    return errors, text_count, diagram_count


def main():
    errors, texts, diagrams = validate(visible_files())
    for error in errors:
        print(error, file=sys.stderr)
    if errors:
        print(f'Project checks FAILED: {len(errors)} issue(s).', file=sys.stderr)
        return 1
    print(f'Project checks passed: {texts} text files, {diagrams} PlantUML sources.')
    print('Structural checks only: no diagram rendering, product tests or complete secret scan.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
