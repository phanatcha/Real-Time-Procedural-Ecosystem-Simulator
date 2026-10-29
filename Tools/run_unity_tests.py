#!/usr/bin/env python3
"""Run Unity tests in an isolated project copy (macOS, using the installed editor)."""
import argparse
from datetime import datetime, timezone
from pathlib import Path
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--editor', type=Path, help='Unity executable; defaults to the project version in Unity Hub')
parser.add_argument('--project-copy', type=Path, help='Reuse a dedicated test copy to avoid a full reimport')
parser.add_argument('--filter', help='Unity test name/fixture filter')
parser.add_argument('--license-channel', help='Existing local Unity licensing channel, if needed')
args = parser.parse_args()
version = re.search(r'm_EditorVersion: (\S+)', (root / 'ProjectSettings/ProjectVersion.txt').read_text())[1]
editor = args.editor or Path('/Applications/Unity/Hub/Editor') / version / 'Unity.app/Contents/MacOS/Unity'
if not editor.is_file():
    parser.error(f'Unity executable not found: {editor}; use --editor')
project = args.project_copy or Path(tempfile.mkdtemp(prefix='boreal-tests-'))
project = project.resolve()
if project == root or root in project.parents or project in root.parents:
    parser.error('The test copy must be separate from the source project')
marker = project / '.boreal-test-copy'
if project.exists() and any(project.iterdir()) and not marker.is_file():
    parser.error('Refusing to overwrite an existing directory without a .boreal-test-copy marker')
project.mkdir(parents=True, exist_ok=True)
marker.touch()
# These three folders are owned by the test copy; syncing deletions prevents stale tests from running.
for folder in ('Assets', 'Packages', 'ProjectSettings'):
    subprocess.run(['rsync', '-a', '--delete', str(root / folder) + '/', str(project / folder) + '/'], check=True)
channel = args.license_channel
editor_log = Path.home() / 'Library/Logs/Unity/Editor.log'
if not channel and editor_log.is_file():
    matches = re.findall(r'Successfully connected to: "(LicenseClient-[^"\r\n]+)"', editor_log.read_text(errors='replace'))
    channel = next((m for m in reversed(matches) if not m.endswith('-notifications')), None)
output = root / 'Logs/TestResults' / datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
output.mkdir(parents=True)
results = output / 'results.xml'
command = [str(editor), '-batchmode', '-projectPath', str(project), '-runTests',
           '-testPlatform', 'EditMode', '-testResults', str(results), '-logFile', str(output / 'editor.log')]
# Graphics stay enabled: OceanFFTTests exercises real compute shaders.
if channel:
    command += ['-licensingIpc', channel]
if args.filter:
    command += ['-testFilter', args.filter]
print(f'Test copy: {project}\nResults: {output}', flush=True)
run = subprocess.run(command)
if results.exists():
    report = ET.parse(results).getroot()
    print({key: report.get(key) for key in ('result', 'total', 'passed', 'failed', 'skipped')})
    for case in report.iter('test-case'):
        if case.get('result') == 'Failed':
            print(case.get('fullname'), case.findtext('failure/message'), sep='\n')
else:
    print('Unity did not produce test results; inspect editor.log.')
raise SystemExit(run.returncode or (0 if results.exists() else 1))
