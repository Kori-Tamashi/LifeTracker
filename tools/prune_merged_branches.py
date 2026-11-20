#!/usr/bin/env python3
"""Delete only merged PR branches whose exact heads remain reachable from develop."""
import base64
import json
import os
import re
import subprocess
from urllib.request import Request, urlopen


def candidates(pulls, refs, ancestor):
    opened = {p['head']['ref'] for p in pulls if p['state'] == 'open'}
    result = {}
    for pull in pulls:
        head = pull['head']
        branch, sha = head['ref'], head['sha']
        if (pull.get('merged_at') and branch not in opened
                and re.fullmatch(r'(feature|codex|release|hotfix)/[A-Za-z0-9._/-]+', branch)
                and '..' not in branch and refs.get(branch) == sha
                and head['repo'] is not None and head['repo']['full_name'] == os.environ['GITHUB_REPOSITORY']
                and ancestor(sha)):
            result[branch] = sha
    return result


def main():
    repository = os.environ['GITHUB_REPOSITORY']
    token = os.environ['GITHUB_TOKEN']
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository):
        raise RuntimeError('Invalid repository')
    pulls = []
    # Complete inventory: an open PR must never be hidden beyond the first page.
    for page in range(1, 101):
        request = Request(f'https://api.github.com/repos/{repository}/pulls?state=all&per_page=100&page={page}',
                          headers={'Authorization': 'Bearer ' + token, 'Accept': 'application/vnd.github+json'})
        with urlopen(request, timeout=30) as response:
            batch = json.load(response)
        pulls.extend(batch)
        if len(batch) < 100:
            break
    else:
        raise RuntimeError('Incomplete PR inventory; refusing cleanup')
    def git(*args, **kwargs):
        return subprocess.run(['git', *args], check=True, capture_output=True, text=True, **kwargs).stdout
    git('fetch', '--prune', 'origin')
    refs = dict(line.split(' ', 1) for line in git(
        'for-each-ref', '--format=%(refname:strip=3) %(objectname)', 'refs/remotes/origin').splitlines())
    def ancestor(sha):
        return subprocess.run(['git', 'merge-base', '--is-ancestor', sha, 'origin/develop'],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0
    selected = candidates(pulls, refs, ancestor)
    # Keep credentials out of command arguments, URLs and logs. Every deletion uses a SHA lease.
    environment = dict(os.environ, GIT_CONFIG_COUNT='1',
                       GIT_CONFIG_KEY_0='http.https://github.com/.extraheader',
                       GIT_CONFIG_VALUE_0='AUTHORIZATION: basic ' +
                       base64.b64encode(('x-access-token:' + token).encode()).decode())
    for branch, sha in sorted(selected.items()):
        try:
            git('push', f'--force-with-lease=refs/heads/{branch}:{sha}', 'origin',
                f':refs/heads/{branch}', env=environment)
        except subprocess.CalledProcessError:
            raise RuntimeError('Branch cleanup refused; inspect permissions or changed head') from None
        print('Deleted merged branch:', branch)
    print(f'Merged branch cleanup complete: {len(selected)} deleted.')


if __name__ == '__main__':
    main()
