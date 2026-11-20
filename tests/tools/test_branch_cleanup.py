import importlib.util
import os
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('prune', Path(__file__).resolve().parents[2] / 'tools/prune_merged_branches.py')
prune = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prune)


def pull(branch='feature/done', sha='abc', merged=True, state='closed', repo='owner/repo'):
    return {'state': state, 'merged_at': 'date' if merged else None,
            'head': {'ref': branch, 'sha': sha, 'repo': {'full_name': repo}}}


class BranchCleanupTests(unittest.TestCase):
    @patch.dict(os.environ, GITHUB_REPOSITORY='owner/repo')
    def test_only_exact_merged_reachable_heads(self):
        self.assertEqual(prune.candidates([pull()], {'feature/done': 'abc'}, lambda _: True), {'feature/done': 'abc'})
        self.assertEqual(prune.candidates([pull()], {'feature/done': 'new'}, lambda _: True), {})
        self.assertEqual(prune.candidates([pull()], {'feature/done': 'abc'}, lambda _: False), {})

    @patch.dict(os.environ, GITHUB_REPOSITORY='owner/repo')
    def test_open_pr_or_fork_blocks_deletion(self):
        self.assertEqual(prune.candidates([pull(), pull(merged=False, state='open')],
                                         {'feature/done': 'abc'}, lambda _: True), {})
        self.assertEqual(prune.candidates([pull(repo='fork/repo')], {'feature/done': 'abc'}, lambda _: True), {})

    @patch.dict(os.environ, GITHUB_REPOSITORY='owner/repo')
    def test_permanent_or_unknown_names_are_preserved(self):
        for name in ['main', 'develop', 'archive/work', 'feature/a..b']:
            with self.subTest(name=name):
                self.assertEqual(prune.candidates([pull(name)], {name: 'abc'}, lambda _: True), {})

    @patch.dict(os.environ, GITHUB_REPOSITORY='owner/repo')
    def test_closed_unmerged_is_preserved(self):
        self.assertEqual(prune.candidates([pull(merged=False)], {'feature/done': 'abc'}, lambda _: True), {})
