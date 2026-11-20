"""Publication-check regressions using only temporary synthetic files."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / 'tools' / 'check_project.py'
spec = importlib.util.spec_from_file_location('check_project', SCRIPT)
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


class PublicationChecksTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name).resolve()
        self.original_root = checker.ROOT
        checker.ROOT = self.root

    def tearDown(self):
        checker.ROOT = self.original_root
        self.tmp.cleanup()

    def write(self, name, content='synthetic'):
        p = self.root / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(content)
        return Path(name)

    def errors(self, *paths):
        return checker.validate(paths)[0]

    def test_valid_local_and_remote_links(self):
        doc = self.write('docs/start.md', '[ok](next.md) [remote](https://example.invalid/path)')
        target = self.write('docs/next.md')
        self.assertEqual([], self.errors(doc, target))

    def test_broken_and_private_links_are_rejected(self):
        self.write('private/interview.md')
        doc = self.write('README.md', '[missing](absent.md) [private](private/interview.md)')
        self.assertEqual(2, len(self.errors(doc)))

    def test_tracked_personal_file_is_rejected_even_if_gitignore_would_hide_it(self):
        private = self.write('private/interview.md')
        key = self.write('credentials.key')
        self.assertEqual(2, len(self.errors(private, key)))

    def test_environment_template_allowed_real_environment_rejected(self):
        template = self.write('.env.example', 'TOKEN=')
        real = self.write('.env.production', 'TOKEN=synthetic')
        self.assertEqual([], self.errors(template))
        self.assertTrue(self.errors(real))

    def test_symlink_cannot_publish_external_content(self):
        (self.root / 'link.md').symlink_to(SCRIPT)
        self.assertTrue(self.errors(Path('link.md')))

    def test_diagram_requires_envelope_and_no_external_include(self):
        valid = self.write('valid.puml', '@startuml\nactor Owner\n@enduml\n')
        bad = self.write('bad.puml', '@startuml\n!include external.puml\n')
        self.assertEqual([], self.errors(valid))
        self.assertTrue(self.errors(bad))

    def test_personal_path_and_escaping_link_rejected(self):
        content = '/Users/' + 'synthetic/Documents/file\n[escape](../outside.md)'
        doc = self.write('README.md', content)
        self.assertEqual(2, len(self.errors(doc)))


if __name__ == '__main__':
    unittest.main()


class RepositoryLayoutTests(unittest.TestCase):
    def test_product_documentation_and_configuration_roots(self):
        self.assertEqual([], checker.validate_layout([Path('src/core/Example.cs'),
            Path('docs/design.md'), Path('configs/swift-format.json'), Path('tests/core/Contract.cs'),
            Path('tools/check_project.py'), Path('.github/workflows/check.yml')]))

    def test_tool_discovery_entrypoints_are_allowed(self):
        self.assertEqual([], checker.validate_layout([Path('global.json'), Path('.editorconfig'),
            Path('Directory.Build.props'), Path('Directory.Packages.props'), Path('README.md')]))

    def test_old_product_roots_and_arbitrary_root_files_are_rejected(self):
        for name in ['services/core/Example.cs', 'apps/macos/Package.swift', 'notes.md', 'private/data.json']:
            with self.subTest(name=name):
                self.assertEqual(1, len(checker.validate_layout([Path(name)])))
