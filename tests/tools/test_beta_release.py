"""Release gate regressions: evidence cannot promote skipped/failed tests or branch builds."""
import importlib.util
import hashlib
import subprocess
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location('beta_release', Path(__file__).resolve().parents[2] / 'tools/beta_release.py')
BETA = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BETA)


class BetaEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.logs = self.root / 'logs'
        self.logs.mkdir()
        self.write('tooling', 'Ran 37 tests in 0.5s\n\nOK\n')
        self.write('dotnet', 'Test run summary: Passed!\n  total: 121\n  failed: 0\n  succeeded: 121\n')
        self.write('swift', '\x1b[32m✔\x1b[0m Test sample() passed after 0.1 seconds.\n➜ Test live() skipped.\n✔ Suite Example passed after 1s.\n✔ Test run with 2 tests passed after 1s.\n')
        self.integration = ''.join('✔ Test ' + name + '() passed after 1s.\n' for name in [
            'recordRetryHistoryCorrectionAgainstRealHost', 'managedHostStopAndRestartPreserveState',
            'killedCorePreservesPendingReceiptOnRestart', 'stoppingAttachedRuntimeLeavesExternalCoreRunning',
            'offlineMaintenanceRunnerRestoresProfileAndRefusesLiveWriter',
            'coordinatedOfflinePausePreservesWorkspacePendingAndRestartsOnFailure']) + '✔ Test run with 6 tests passed after 1s.\n'
        self.write('integration', self.integration)
        self.write('backup', 'Offline backup smoke passed: writer/desktop locks, snapshot/verify/restore, receipt replay and corruption refusal.\n')

    def write(self, name, content):
        (self.logs / (name + '.log')).write_text(content)

    def test_actual_passes_exclude_suites_and_report_intentional_unit_skip(self):
        result = BETA.test_evidence(self.logs)
        self.assertEqual(result['dotnet']['passed'], 121)
        self.assertEqual(result['swift'], {'passed': 1, 'skipped': 1})
        self.assertEqual(result['live_integration'], {'passed': 6, 'skipped': 0})

    def test_skipped_live_or_failed_core_cannot_be_release_evidence(self):
        self.write('integration', '➜ Test live() skipped.\n✔ Test run with 1 test passed after 1s.\n')
        with self.assertRaises(ValueError):
            BETA.test_evidence(self.logs)
        self.write('integration', self.integration)
        self.write('dotnet', 'total: 121\nfailed: 1\n')
        with self.assertRaises(ValueError):
            BETA.test_evidence(self.logs)

    def test_missing_backup_or_required_lifecycle_case_refuses_evidence(self):
        self.write('backup', 'process started but no successful roundtrip\n')
        with self.assertRaises(ValueError):
            BETA.test_evidence(self.logs)
        self.write('backup', 'Offline backup smoke passed: writer/desktop locks, snapshot/verify/restore, receipt replay and corruption refusal.\n')
        self.write('integration', self.integration.replace('✔ Test coordinatedOfflinePausePreservesWorkspacePendingAndRestartsOnFailure() passed after 1s.\n', ''))
        with self.assertRaises(ValueError):
            BETA.test_evidence(self.logs)

    def test_publish_refuses_non_main_without_calling_github(self):
        with patch.dict(os.environ, {'GITHUB_REF': 'refs/heads/develop', 'GITHUB_REPOSITORY': BETA.REPOSITORY,
                                     'GITHUB_SHA': 'a' * 40}), patch.object(BETA, 'run') as run:
            with self.assertRaises(ValueError):
                BETA.publish(self.root)
            run.assert_not_called()

    def test_publish_refuses_wrong_commit_before_upload(self):
        (self.root / 'evidence.json').write_text(json.dumps({'version': BETA.config()['version'], 'source_revision': 'b' * 40}))
        with patch.dict(os.environ, {'GITHUB_REF': 'refs/heads/main', 'GITHUB_REPOSITORY': BETA.REPOSITORY,
                                     'GITHUB_SHA': 'a' * 40}), patch.object(BETA, 'run') as run:
            with self.assertRaises(ValueError):
                BETA.publish(self.root)
            run.assert_not_called()

    def assets(self):
        self.output = self.root / 'assets'
        self.output.mkdir()
        version = BETA.config()['version']
        for name, content in {f'LifeTracker-{version}-macos-arm64.zip': b'synthetic archive',
                              'components.json': b'{}', 'TESTING.md': b'synthetic instructions',
                              'evidence.json': json.dumps({'format': 2, 'delivery': 'main prerelease', 'offline_backup': {'passed': True},
                                                         'version': version, 'source_revision': 'a' * 40}).encode()}.items():
            (self.output / name).write_bytes(content)
        (self.output / 'SHA256SUMS.txt').write_text(''.join(
            hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + p.name + '\n'
            for p in self.output.iterdir() if p.is_file()))

    def test_candidate_assets_cannot_be_published_even_if_source_matches_main(self):
        self.assets()
        evidence = json.loads((self.output / 'evidence.json').read_text())
        evidence['delivery'] = 'candidate artifact only'
        (self.output / 'evidence.json').write_text(json.dumps(evidence))
        with patch.dict(os.environ, {'GITHUB_REF': 'refs/heads/main', 'GITHUB_REPOSITORY': BETA.REPOSITORY,
                                     'GITHUB_SHA': 'a' * 40}), patch.object(BETA, 'run') as mutate, \
                patch.object(BETA.subprocess, 'run') as gh:
            with self.assertRaises(ValueError):
                BETA.publish(self.output)
            mutate.assert_not_called()
            gh.assert_not_called()

    def test_existing_tag_is_never_moved_to_new_source(self):
        self.assets()
        with patch.dict(os.environ, {'GITHUB_REF': 'refs/heads/main', 'GITHUB_REPOSITORY': BETA.REPOSITORY,
                                     'GITHUB_SHA': 'a' * 40}), patch.object(BETA.subprocess, 'run', return_value=
                subprocess.CompletedProcess([], 0, json.dumps({'object': {'type': 'commit', 'sha': 'b' * 40}}))) as gh:
            with self.assertRaises(ValueError):
                BETA.publish(self.output)
            self.assertEqual(gh.call_count, 1)

    def test_draft_with_foreign_asset_is_not_published(self):
        self.assets()
        replies = [subprocess.CompletedProcess([], 0, json.dumps({'object': {'type': 'commit', 'sha': 'a' * 40}})),
                   subprocess.CompletedProcess([], 0, json.dumps({'targetCommitish': 'a' * 40, 'isDraft': True,
                                                                  'assets': [{'name': 'foreign-file'}]}))]
        with patch.dict(os.environ, {'GITHUB_REF': 'refs/heads/main', 'GITHUB_REPOSITORY': BETA.REPOSITORY,
                                     'GITHUB_SHA': 'a' * 40}), patch.object(BETA.subprocess, 'run', side_effect=replies), \
                patch.object(BETA, 'run') as mutate:
            with self.assertRaises(ValueError):
                BETA.publish(self.output)
            mutate.assert_not_called()

    def test_unexpected_asset_is_not_uploaded(self):
        self.assets()
        (self.output / 'private-notes').write_text('synthetic')
        with patch.dict(os.environ, {'GITHUB_REF': 'refs/heads/main', 'GITHUB_REPOSITORY': BETA.REPOSITORY,
                                     'GITHUB_SHA': 'a' * 40}), patch.object(BETA, 'run') as run:
            with self.assertRaises(ValueError):
                BETA.publish(self.output)
            run.assert_not_called()

    def test_version_matches_the_application_and_has_notes(self):
        self.assertRegex(BETA.config()['version'], r'^\d+\.\d+\.\d+-beta\.[1-9]\d*$')


if __name__ == '__main__':
    unittest.main()
