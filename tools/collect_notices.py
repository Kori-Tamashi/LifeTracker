#!/usr/bin/env python3
"""Collect declared NuGet licenses/copyrights without exporting local cache paths."""
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def collect(destination):
    assets = json.loads((ROOT / 'src/core/LifeTracker.Host/obj/project.assets.json').read_text())
    cache = Path(next(iter(assets['packageFolders'])))
    components = []
    destination.mkdir(parents=True)
    for identity, library in sorted(assets['libraries'].items()):
        if library['type'] != 'package':
            continue
        folder = cache / library['path']
        spec = ET.parse(next(folder.glob('*.nuspec'))).getroot()
        def field(name):
            return next((e for e in spec.iter() if e.tag.rsplit('}', 1)[-1] == name), None)
        license_node = field('license')
        if license_node is None:
            raise ValueError('Dependency lacks declared license')
        name, version = identity.rsplit('/', 1)
        declaration = license_node.text
        if license_node.attrib.get('type') == 'expression':
            if declaration not in {'MIT', 'Apache-2.0'}:
                raise ValueError('Unreviewed dependency license')
        elif name == 'SQLite' and declaration == 'LICENSE.txt':
            text = (folder / 'LICENSE.txt').read_text()
            if 'SQLite is Public Domain' not in text:
                raise ValueError('Unreviewed SQLite license')
            (destination / 'SQLite.txt').write_text(text)
        else:
            raise ValueError('Unreviewed file license')
        copyright_node = field('copyright')
        components.append({'name': name, 'version': version, 'license': declaration,
                           'copyright': copyright_node.text if copyright_node is not None else '',
                           'package_url': f'https://www.nuget.org/packages/{name}/{version}'})
    for name in ['MIT', 'Apache-2.0']:
        shutil.copy2(ROOT / f'configs/licenses/{name}.txt', destination / f'{name}.txt')
    inventory = {'format': 'lifetracker-components-v1',
                 'scope': 'Core NuGet dependency closure, including build-only packages; excludes OS frameworks and external .NET runtime',
                 'components': components}
    (destination / 'components.json').write_text(json.dumps(inventory, ensure_ascii=False, indent=2) + '\n')
    notices = ['LifeTracker third-party components', '', inventory['scope'], '']
    for component in components:
        notices.extend([component['name'] + ' ' + component['version'], component['copyright'],
                        'License: ' + component['license'], component['package_url'], ''])
    notices.append('License texts accompany this file. No license for LifeTracker itself is granted by these third-party notices.')
    (destination / 'NOTICES.txt').write_text('\n'.join(notices) + '\n')
