#!/usr/bin/env python3
"""Build/verify/install a local ad-hoc signed desktop bundle; never copy runtime data."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import plistlib
import platform
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
    if not FILES <= {p.relative_to(bundle).as_posix() for p in entries if p.is_file()}:
        raise ValueError('Missing application files')
    info = plistlib.loads((bundle / 'Contents/Info.plist').read_bytes())
    if info.get('CFBundleIdentifier') != IDENTIFIER or info.get('CFBundleExecutable') != 'LifeTrackerDesktop':
        raise ValueError('Unexpected application identity')
    manifest = json.loads((bundle / MANIFEST).read_text())
    if (manifest.get('format') not in (1, 2) or manifest.get('executable') != EXECUTABLE
            or not isinstance(manifest.get('working_tree_dirty', False), bool)
            or not re.fullmatch(r'[0-9a-f]{40}', manifest.get('source_revision', ''))):
        raise ValueError('Unexpected application manifest')
    core_files = manifest.get('core_files', {})
    if not isinstance(core_files, dict) or (manifest['format'] == 1 and core_files):
        raise ValueError('Unexpected core manifest')
    if manifest['format'] == 2 and not {
            'Contents/Resources/Core/LifeTracker.Host.dll',
            'Contents/Resources/Core/LifeTracker.Host.runtimeconfig.json',
            'Contents/Resources/Core/LifeTracker.Host.deps.json'} <= core_files.keys():
        raise ValueError('Missing core host files')
    for name, digest in core_files.items():
        parts = PurePosixPath(name).parts
        if (len(parts) < 4 or parts[:3] != ('Contents', 'Resources', 'Core')
                or '..' in parts or '\\' in name or str(PurePosixPath(name)) != name
                or Path(name).suffix not in {'.dll', '.dylib', '.json'}
                or not re.fullmatch(r'[0-9a-f]{64}', digest)):
            raise ValueError('Invalid core file manifest')
    if {p.relative_to(bundle).as_posix() for p in entries if p.is_file()} != FILES | core_files.keys():
        raise ValueError('Unexpected or missing application files')
    for name, digest in core_files.items():
        if hashlib.sha256((bundle / name).read_bytes()).hexdigest() != digest:
            raise ValueError('Core file checksum mismatch')
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
    host = str((destination / 'Contents/Resources/Core/LifeTracker.Host.dll').resolve())
    host_pattern = r'(^|[[:space:]])' + re.escape(host) + r'([[:space:]]|$)'
    core = subprocess.run(['pgrep', '-f', host_pattern], capture_output=True, check=False)
    if core.returncode != 1:
        raise ValueError('Stop the core using the destination bundle before updating it')
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
    run('sh', str(ROOT / 'tools/dotnet.sh'), 'restore', '--locked-mode', '--disable-parallel')
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
        core = bundle / 'Contents/Resources/Core'
        with tempfile.TemporaryDirectory(prefix='lifetracker-publish-') as published:
            run('sh', str(ROOT / 'tools/dotnet.sh'), 'publish-host', '--configuration', 'Release',
                '--no-restore', '--self-contained', 'false', '-p:UseAppHost=false', '-p:DebugType=None',
                '-p:DebugSymbols=false', '-p:UseSharedCompilation=false', '-m:1', '-nr:false', '--output', published)
            core.mkdir()
            published = Path(published)
            selected = list(published.glob('*.dll')) + [published / 'LifeTracker.Host.deps.json',
                                                        published / 'LifeTracker.Host.runtimeconfig.json']
            rid = 'osx-arm64' if platform.machine() == 'arm64' else 'osx-x64'
            selected += list((published / 'runtimes' / rid).rglob('*.dylib'))
            for source in selected:
                target = core / source.relative_to(published)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, target)
                if target.suffix == '.dylib':
                    run('codesign', '--force', '--sign', '-', '--timestamp=none', str(target))
        core_files = {p.relative_to(bundle).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                      for p in sorted(core.rglob('*')) if p.is_file()}
        manifest = {'format': 2, 'source_revision': revision, 'working_tree_dirty': dirty,
                    'executable': EXECUTABLE, 'core_files': core_files,
                    'requires_runtime': 'Microsoft.AspNetCore.App 10.0', 'architecture': platform.machine()}
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
