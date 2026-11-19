"""Package the self-contained publish folder without losing Unix executable bits."""
from pathlib import Path
import os
import plistlib
import shutil
import subprocess
import sys
import tarfile

rid = sys.argv[1]
source = Path('artifacts/publish')
output = Path('artifacts')
if rid.startswith('win-'):
    shutil.make_archive(str(output / f'TMCL-{rid}'), 'zip', source)
elif rid.startswith('osx-'):
    app = output / 'bundle' / 'TMCL.app' / 'Contents'
    binary = app / 'MacOS'
    shutil.copytree(source, binary, dirs_exist_ok=True)
    executable = binary / 'TMCL'
    executable.chmod(executable.stat().st_mode | 0o111)
    with (app / 'Info.plist').open('wb') as stream:
        plistlib.dump({
            'CFBundleName': 'TMCL', 'CFBundleDisplayName': 'TMCL',
            'CFBundleIdentifier': 'com.tmcl.launcher', 'CFBundleVersion': '0.10.0',
            'CFBundleShortVersionString': '0.10.0', 'CFBundlePackageType': 'APPL',
            'CFBundleExecutable': 'TMCL', 'NSHighResolutionCapable': True,
            'LSMinimumSystemVersion': '12.0'
        }, stream)
    if sys.platform == 'darwin':
        subprocess.run(['codesign', '--force', '--deep', '--sign', '-', str(app.parent)], check=True)
    with tarfile.open(output / f'TMCL-{rid}.tar.gz', 'w:gz') as archive:
        archive.add(app.parent, arcname='TMCL.app')
else:
    (source / 'TMCL').chmod((source / 'TMCL').stat().st_mode | 0o111)
    with tarfile.open(output / f'TMCL-{rid}.tar.gz', 'w:gz') as archive:
        archive.add(source, arcname='TMCL')
