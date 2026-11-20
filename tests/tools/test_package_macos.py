"""Installer safety on synthetic app-shaped fixtures; real codesign runs separately on macOS."""
import importlib.util
import hashlib
import json
from pathlib import Path
import plistlib
import subprocess
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location('package_macos', Path(__file__).resolve().parents[2] / 'tools/package_macos.py')
PACKAGE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PACKAGE)


def fixture(path, content=b'synthetic executable'):
    for name in PACKAGE.FILES:
        file = path / name
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_bytes(b'synthetic')
    (path / 'Contents/Info.plist').write_bytes(plistlib.dumps({
        'CFBundleIdentifier': PACKAGE.IDENTIFIER, 'CFBundleExecutable': 'LifeTrackerDesktop'}))
    (path / PACKAGE.MANIFEST).write_text(json.dumps({
        'format': 1, 'source_revision': 'a' * 40, 'working_tree_dirty': False, 'executable': PACKAGE.EXECUTABLE}))
    (path / PACKAGE.EXECUTABLE).write_bytes(content)
    (path / PACKAGE.EXECUTABLE).chmod(0o755)


class PackageSafetyTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / 'Built.app'
        self.target = self.root / 'Installed.app'
        fixture(self.source)
        self.signing = patch.object(PACKAGE, 'run')
        self.signing.start()
        self.addCleanup(self.signing.stop)
        self.process = patch.object(PACKAGE.subprocess, 'run', return_value=subprocess.CompletedProcess([], 1))
        self.process.start()
        self.addCleanup(self.process.stop)

    def test_core_manifest_rejects_tampering_and_escaping_paths(self):
        core = self.source / 'Contents/Resources/Core'
        core.mkdir()
        for name in ['LifeTracker.Host.dll', 'LifeTracker.Host.runtimeconfig.json', 'LifeTracker.Host.deps.json']:
            (core / name).write_bytes(b'synthetic core')
        manifest_file = self.source / PACKAGE.MANIFEST
        manifest = json.loads(manifest_file.read_text())
        manifest['format'] = 2
        manifest['core_files'] = {p.relative_to(self.source).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                                  for p in core.iterdir()}
        manifest_file.write_text(json.dumps(manifest))
        PACKAGE.verify(self.source)
        (core / 'LifeTracker.Host.dll').write_bytes(b'tampered synthetic core')
        with self.assertRaises(ValueError):
            PACKAGE.verify(self.source)
        manifest['core_files']['Contents/Resources/Core/../../outside.dll'] = 'a' * 64
        manifest_file.write_text(json.dumps(manifest))
        with self.assertRaises(ValueError):
            PACKAGE.verify(self.source)

    def test_update_preserves_external_profile_and_replaces_only_verified_app(self):
        fixture(self.target, b'previous executable')
        profile = self.root / 'profile'
        profile.mkdir()
        sentinel = profile / 'sentinel'
        sentinel.write_text('synthetic profile remains')
        PACKAGE.replace_bundle(self.source, self.target)
        self.assertEqual((self.target / PACKAGE.EXECUTABLE).read_bytes(), b'synthetic executable')
        self.assertEqual(sentinel.read_text(), 'synthetic profile remains')

    def test_running_core_also_prevents_replacement(self):
        fixture(self.target, b'previous executable')
        with patch.object(PACKAGE.subprocess, 'run', side_effect=[subprocess.CompletedProcess([], 1),
                                                                subprocess.CompletedProcess([], 0)]):
            with self.assertRaises(ValueError):
                PACKAGE.replace_bundle(self.source, self.target)
        self.assertEqual((self.target / PACKAGE.EXECUTABLE).read_bytes(), b'previous executable')

    def test_failed_replacement_restores_previous_app(self):
        fixture(self.target, b'previous executable')
        rename = Path.rename
        def fail_stage(path, destination):
            if path.name == 'LifeTracker.app':
                raise OSError('synthetic rename failure')
            return rename(path, destination)
        with patch.object(Path, 'rename', fail_stage), self.assertRaises(OSError):
            PACKAGE.replace_bundle(self.source, self.target)
        self.assertEqual((self.target / PACKAGE.EXECUTABLE).read_bytes(), b'previous executable')

    def test_failed_rollback_keeps_previous_app_outside_temporary_cleanup(self):
        fixture(self.target, b'previous executable')
        rename = Path.rename
        def fail_stage_and_rollback(path, destination):
            if path.name == 'LifeTracker.app' or path.name.startswith('.lifetracker-previous-'):
                raise OSError('synthetic rename failure')
            return rename(path, destination)
        with patch.object(Path, 'rename', fail_stage_and_rollback), self.assertRaises(RuntimeError):
            PACKAGE.replace_bundle(self.source, self.target)
        previous = list(self.root.glob('.lifetracker-previous-*.app'))
        self.assertEqual(len(previous), 1)
        self.assertEqual((previous[0] / PACKAGE.EXECUTABLE).read_bytes(), b'previous executable')

    def test_runtime_file_or_symlink_in_package_is_rejected(self):
        extra = self.source / 'Contents/profile.sqlite'
        extra.write_text('synthetic runtime')
        with self.assertRaises(ValueError):
            PACKAGE.replace_bundle(self.source, self.target)
        extra.unlink()
        extra.symlink_to(self.source / PACKAGE.EXECUTABLE)
        with self.assertRaises(ValueError):
            PACKAGE.verify(self.source)
        self.assertFalse(self.target.exists())

    def test_unknown_destination_running_app_and_nested_paths_are_rejected(self):
        self.target.mkdir()
        (self.target / 'personal-note').write_text('synthetic sentinel')
        with self.assertRaises(ValueError):
            PACKAGE.replace_bundle(self.source, self.target)
        self.assertEqual((self.target / 'personal-note').read_text(), 'synthetic sentinel')
        with self.assertRaises(ValueError):
            PACKAGE.replace_bundle(self.source, self.source / 'nested.app')
        with patch.object(PACKAGE.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0)):
            with self.assertRaises(ValueError):
                PACKAGE.replace_bundle(self.source, self.root / 'Other.app')


if __name__ == '__main__':
    unittest.main()
