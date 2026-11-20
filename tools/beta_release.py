#!/usr/bin/env python3
"""Validate, assemble and publish an exact-commit early beta after successful checks."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import plistlib
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = 'Kori-Tamashi/LifeTracker'


def run(*args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)


def config():
    value = json.loads((ROOT / 'configs/releases/beta.json').read_text())
    if not re.fullmatch(r'\d+\.\d+\.\d+-beta\.[1-9]\d*', value['version']) or value['channel'] != 'beta':
        raise ValueError('Expected a numbered beta version')
    info = plistlib.loads((ROOT / 'configs/macos/Info.plist').read_bytes())
    expected = {'CFBundleShortVersionString': value['app_version'], 'CFBundleVersion': value['build_number'],
                'LSMinimumSystemVersion': value['minimum_macos'], 'LifeTrackerReleaseVersion': value['version'],
                'LifeTrackerReleaseChannel': 'beta'}
    if any(info.get(key) != item for key, item in expected.items()):
        raise ValueError('Release configuration and Info.plist differ')
    if value['architecture'] != 'arm64' or value['notes'] != f"docs/releases/v{value['version']}.md":
        raise ValueError('Unexpected beta architecture or release notes path')
    if not (ROOT / value['notes']).is_file():
        raise ValueError('Release notes missing')
    return value


def test_evidence(logs):
    contents = {name: (logs / f'{name}.log').read_text() for name in ['tooling', 'dotnet', 'swift', 'integration', 'backup']}
    original = contents.copy()
    contents = {name: re.sub(r'\x1b\[[0-?]*[ -/]*[@-~]', '', text) for name, text in contents.items()}
    tooling = re.search(r'Ran (\d+) tests?', contents['tooling'])
    dotnet = re.search(r'(?:total|итог):\s*(\d+)', contents['dotnet'], re.I)
    if not tooling or not re.search(r'^OK\s*$', contents['tooling'], re.M):
        raise ValueError('Tooling test result missing')
    if not dotnet or not re.search(r'(?:failed|сбой):\s*0\s*$', contents['dotnet'], re.I | re.M):
        raise ValueError('Successful .NET test summary missing')
    def swift_tests(name):
        text = contents[name]
        passed = len(re.findall(r'^.*✔ Test .+\(\) passed after', text, re.M))
        skipped = len(re.findall(r'^.*Test .+\(\) skipped\.', text, re.M))
        if not passed or '✘' in text or 'Test run with ' not in text:
            raise ValueError('Successful Swift test summary missing')
        return {'passed': passed, 'skipped': skipped}
    result = {'tooling': {'passed': int(tooling[1])}, 'dotnet': {'passed': int(dotnet[1])},
              'swift': swift_tests('swift'), 'live_integration': swift_tests('integration')}
    if result['live_integration']['skipped']:
        raise ValueError('Live integration must not be skipped')
    required = {
        'recordRetryHistoryCorrectionAgainstRealHost',
        'managedHostStopAndRestartPreserveState',
        'killedCorePreservesPendingReceiptOnRestart',
        'stoppingAttachedRuntimeLeavesExternalCoreRunning',
        'offlineMaintenanceRunnerRestoresProfileAndRefusesLiveWriter',
        'coordinatedOfflinePausePreservesWorkspacePendingAndRestartsOnFailure',
    }
    live_passed = set(re.findall(r'^.*✔ Test ([A-Za-z0-9_]+)\(\) passed after', contents['integration'], re.M))
    if not required.issubset(live_passed):
        raise ValueError('Required lifecycle/backup integration evidence missing')
    backup_marker = 'Offline backup smoke passed: writer/desktop locks, snapshot/verify/restore, receipt replay and corruption refusal.'
    if backup_marker not in contents['backup'].splitlines():
        raise ValueError('Offline backup smoke evidence missing')
    result['required_live_cases'] = sorted(required)
    result['offline_backup'] = {'passed': True, 'scope': 'synthetic process roundtrip; not GUI/RPO qualification'}
    result['log_sha256'] = {name: hashlib.sha256(text.encode()).hexdigest() for name, text in original.items()}
    return result


def assemble(app, logs, output):
    from package_macos import verify
    value = config()
    verify(app)
    manifest = json.loads((app / 'Contents/Resources/package.json').read_text())
    revision = run('git', '-C', str(ROOT), 'rev-parse', 'HEAD', capture_output=True, text=True).stdout.strip()
    if (manifest.get('format') != 3 or manifest.get('working_tree_dirty') is not False
            or manifest.get('source_revision') != revision or manifest.get('release_version') != value['version']
            or manifest.get('architecture') != value['architecture']):
        raise ValueError('Beta requires a clean exact-commit ARM64 package with notices')
    if output.exists() and any(output.iterdir()):
        raise ValueError('Release output directory must be empty')
    evidence = test_evidence(logs)
    output.mkdir(parents=True, exist_ok=True)
    stem = f"LifeTracker-{value['version']}-macos-arm64"
    archive = output / f'{stem}.zip'
    run('ditto', '-c', '-k', '--sequesterRsrc', '--keepParent', str(app), str(archive))
    with tempfile.TemporaryDirectory(prefix='lifetracker-archive-check-') as temporary:
        run('ditto', '-x', '-k', str(archive), temporary)
        verify(Path(temporary) / 'LifeTracker.app')
    shutil.copy2(app / 'Contents/Resources/ThirdParty/components.json', output / 'components.json')
    shutil.copy2(ROOT / value['notes'], output / 'TESTING.md')
    evidence.update({'format': 2, 'version': value['version'], 'source_revision': revision,
                     'scope': 'B1 State/lifecycle/backup manual-test candidate; not full R1 acceptance',
                     'delivery': 'main prerelease' if os.environ.get('GITHUB_REF') == 'refs/heads/main' else 'candidate artifact only',
                     'rpo_rto': 'not target-qualified', 'total_disk_budget': 'not qualified',
                     'ui_acceptance': 'pending manual tester', 'login_items': 'not OS-qualified',
                     'runtime': manifest['requires_runtime'], 'architecture': value['architecture'],
                     'minimum_macos': value['minimum_macos'],
                     'swift_compiler': run('swift', '--version', capture_output=True, text=True).stdout.strip(),
                     'dotnet_sdk': run('sh', str(ROOT / 'tools/dotnet.sh'), '--version', capture_output=True, text=True).stdout.strip(),
                     'actions_run_url': f"https://github.com/{REPOSITORY}/actions/runs/{os.environ.get('GITHUB_RUN_ID', '')}",
                     'installed_bytes': sum(p.stat().st_size for p in app.rglob('*') if p.is_file())})
    (output / 'evidence.json').write_text(json.dumps(evidence, indent=2) + '\n')
    checksums = [f'{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.name}' for p in sorted(output.iterdir()) if p.is_file()]
    (output / 'SHA256SUMS.txt').write_text('\n'.join(checksums) + '\n')
    print('Beta archive/evidence/checksums prepared.')


def publish(output):
    value = config()
    sha = os.environ.get('GITHUB_SHA', '')
    if (os.environ.get('GITHUB_REF') != 'refs/heads/main' or os.environ.get('GITHUB_REPOSITORY') != REPOSITORY
            or not re.fullmatch(r'[0-9a-f]{40}', sha)):
        raise ValueError('Publishing is restricted to an exact main commit in the configured repository')
    evidence = json.loads((output / 'evidence.json').read_text())
    if evidence['source_revision'] != sha or evidence['version'] != value['version']:
        raise ValueError('Release evidence does not match main commit')
    if (evidence.get('format') != 2 or evidence.get('delivery') != 'main prerelease'
            or evidence.get('offline_backup', {}).get('passed') is not True):
        raise ValueError('Candidate assets cannot be published; exact-main backup evidence required')
    expected = {f"LifeTracker-{value['version']}-macos-arm64.zip", 'components.json', 'TESTING.md', 'evidence.json', 'SHA256SUMS.txt'}
    if {p.name for p in output.iterdir()} != expected or any(p.is_symlink() or not p.is_file() for p in output.iterdir()):
        raise ValueError('Unexpected release assets')
    seen = set()
    for line in (output / 'SHA256SUMS.txt').read_text().splitlines():
        digest, name = line.split('  ', 1)
        if name in seen or not re.fullmatch(r'[0-9a-f]{64}', digest) or Path(name).name != name or hashlib.sha256((output / name).read_bytes()).hexdigest() != digest:
            raise ValueError('Release asset checksum mismatch')
        seen.add(name)
    if seen != expected - {'SHA256SUMS.txt'}:
        raise ValueError('Incomplete asset checksums')
    tag = 'v' + value['version']
    tag_ref = subprocess.run(['gh', 'api', f'repos/{REPOSITORY}/git/ref/tags/{tag}'], capture_output=True, text=True)
    if tag_ref.returncode == 0:
        target = json.loads(tag_ref.stdout)['object']
        if target.get('type') != 'commit' or target.get('sha') != sha:
            raise ValueError('Existing tag points elsewhere; tags are never moved')
    existing = subprocess.run(['gh', 'release', 'view', tag, '--repo', REPOSITORY,
                               '--json', 'isDraft,isPrerelease,targetCommitish,assets'], capture_output=True, text=True)
    if existing.returncode == 0:
        release = json.loads(existing.stdout)
        if release['targetCommitish'] != sha:
            raise ValueError('Version already exists for another commit; increment the version')
        if release['isDraft'] and {a['name'] for a in release['assets']} - expected:
            raise ValueError('Draft contains unexpected assets; refuse to publish')
        if not release['isDraft']:
            if not release['isPrerelease'] or {a['name'] for a in release['assets']} != expected:
                raise ValueError('Existing release channel/assets differ')
            with tempfile.TemporaryDirectory(prefix='lifetracker-release-verify-') as temporary:
                run('gh', 'release', 'download', tag, '--repo', REPOSITORY, '--pattern', 'evidence.json', '--dir', temporary)
                previous = json.loads((Path(temporary) / 'evidence.json').read_text())
                if previous['source_revision'] != sha or previous['version'] != value['version']:
                    raise ValueError('Existing release evidence differs')
            print(f'https://github.com/{REPOSITORY}/releases/tag/{tag}')
            return
    else:
        run('gh', 'release', 'create', tag, '--repo', REPOSITORY, '--target', sha, '--draft', '--prerelease',
            '--latest=false', '--title', f"LifeTracker {value['version']}", '--notes-file', str(ROOT / value['notes']))
    assets = [str(p) for p in sorted(output.iterdir()) if p.is_file()]
    run('gh', 'release', 'upload', tag, '--repo', REPOSITORY, '--clobber', *assets)
    run('gh', 'release', 'edit', tag, '--repo', REPOSITORY, '--draft=false', '--prerelease', '--latest=false')
    actual = run('gh', 'api', f'repos/{REPOSITORY}/git/ref/tags/{tag}', '--jq', '.object.sha', capture_output=True, text=True).stdout.strip()
    if actual != sha:
        raise ValueError('Published tag does not match tested main commit')
    print(f'https://github.com/{REPOSITORY}/releases/tag/{tag}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest='command', required=True)
    actions.add_parser('check')
    bundle = actions.add_parser('bundle')
    bundle.add_argument('--app', type=Path, required=True)
    bundle.add_argument('--logs', type=Path, required=True)
    bundle.add_argument('--output', type=Path, required=True)
    actions.add_parser('publish').add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.command == 'check':
        config()
        print('Beta version and metadata are consistent.')
    elif args.command == 'bundle':
        assemble(args.app, args.logs, args.output)
    else:
        publish(args.output)


if __name__ == '__main__':
    main()
