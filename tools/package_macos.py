#!/usr/bin/env python3
"""Build/verify/install a local ad-hoc signed desktop bundle; never copy runtime data."""
import argparse
import json
import os
from pathlib import Path
import plistlib
import re
import shutil
import subprocess
import tempfile
import uuid

ROOT = Path(__file__).resolve().parents[1]
IDENTIFIER = 'io.github.kori-tamashi.lifetracker'
EXECUTABLE = 'Contents/MacOS/LifeTrackerDesktop'
MANIFEST = 'Contents/Resources/package.json'
FILES = {'Contents/Info.plist', EXECUTABLE, MANIFEST, 'Contents/_CodeSignature/CodeResources'}


def run(*args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)


def verify(bundle):
    if bundle.is_symlink() or not bundle.is_dir():
        raise ValueError('Expected a real application directory')
    entries = list(bundle.rglob('*'))
    if any(p.is_symlink() for p in entries):
        raise ValueError('Application links are not supported')
    if {p.relative_to(bundle).as_posix() for p in entries if p.is_file()} != FILES:
        raise ValueError('Unexpected or missing application files')
    info = plistlib.loads((bundle / 'Contents/Info.plist').read_bytes())
    if info.get('CFBundleIdentifier') != IDENTIFIER or info.get('CFBundleExecutable') != 'LifeTrackerDesktop':
        raise ValueError('Unexpected application identity')
    manifest = json.loads((bundle / MANIFEST).read_text())
    if (manifest.get('format') != 1 or manifest.get('executable') != EXECUTABLE
            or not isinstance(manifest.get('working_tree_dirty', False), bool)
            or not re.fullmatch(r'[0-9a-f]{40}', manifest.get('source_revision', ''))):
        raise ValueError('Unexpected application manifest')
    if not os.access(bundle / EXECUTABLE, os.X_OK):
        raise ValueError('Application is not executable')
    run('codesign', '--verify', '--deep', '--strict', str(bundle))


def replace_bundle(source, destination):
    """Only replace a verified generated bundle; restore the old bundle if rename fails."""
    verify(source)
    if destination.suffix != '.app' or destination.is_symlink():
        raise ValueError('Destination must be a real .app path')
    if source.resolve().is_relative_to(destination.resolve()) or destination.resolve().is_relative_to(source.resolve()):
        raise ValueError('Source and destination must be separate directories')
    if destination.exists():
        verify(destination)
    running = subprocess.run(['pgrep', '-f', r'(^|/)LifeTrackerDesktop([[:space:]]|$)'], capture_output=True, check=False)
    if running.returncode != 1:
        raise ValueError('Quit LifeTracker before building or installing an update')
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.lifetracker-install-', dir=destination.parent) as temporary:
        stage = Path(temporary) / 'LifeTracker.app'
        previous = destination.parent / ('.lifetracker-previous-' + uuid.uuid4().hex + '.app')
        shutil.copytree(source, stage)
        verify(stage)
        if destination.exists():
            destination.rename(previous)
        try:
            stage.rename(destination)
        except BaseException:
            if previous.exists():
                try:
                    previous.rename(destination)
                except OSError as recovery_error:
                    raise RuntimeError(f'Previous application preserved at {previous}') from recovery_error
            raise
        if previous.exists():
            shutil.rmtree(previous)


def build(destination):
    package = str(ROOT / 'src/macos')
    run('swift', 'build', '--package-path', package, '--configuration', 'release', '--product', 'LifeTrackerDesktop')
    output = run('swift', 'build', '--package-path', package, '--configuration', 'release', '--show-bin-path',
                 capture_output=True, text=True).stdout.strip()
    revision = run('git', '-C', str(ROOT), 'rev-parse', 'HEAD', capture_output=True, text=True).stdout.strip()
    dirty = bool(run('git', '-C', str(ROOT), 'status', '--porcelain', capture_output=True, text=True).stdout)
    with tempfile.TemporaryDirectory(prefix='lifetracker-package-') as temporary:
        bundle = Path(temporary) / 'LifeTracker.app'
        (bundle / 'Contents/MacOS').mkdir(parents=True)
        (bundle / 'Contents/Resources').mkdir()
        shutil.copy2(Path(output) / 'LifeTrackerDesktop', bundle / EXECUTABLE)
        shutil.copy2(ROOT / 'configs/macos/Info.plist', bundle / 'Contents/Info.plist')
        manifest = {'format': 1, 'source_revision': revision, 'working_tree_dirty': dirty, 'executable': EXECUTABLE}
        (bundle / MANIFEST).write_text(json.dumps(manifest, indent=2) + '\n')
        run('codesign', '--force', '--sign', '-', '--timestamp=none', str(bundle))
        replace_bundle(bundle, destination)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    commands.add_parser('build').add_argument('--destination', type=Path, default=ROOT / 'var/build/LifeTracker.app')
    commands.add_parser('verify').add_argument('bundle', type=Path)
    install = commands.add_parser('install')
    install.add_argument('bundle', type=Path)
    install.add_argument('--destination', type=Path, required=True)
    args = parser.parse_args()
    if args.command == 'build':
        build(args.destination.absolute())
    elif args.command == 'verify':
        verify(args.bundle)
    else:
        replace_bundle(args.bundle, args.destination.absolute())
    print('Local application package check completed.')


if __name__ == '__main__':
    main()
