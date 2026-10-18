"""Fail closed on game assets/binaries in the publishable source inventory.

Checks tracked plus untracked/nonignored files, never scans ignored user data.
Documentation PNGs must be explicitly inventoried. Does not assess ownership
of reconstructed source expressions; see NOTICE and LICENSES/README.md.
"""
import json
import hashlib
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
files = subprocess.check_output(['git', '-C', str(root), 'ls-files', '-z', '--cached', '--others', '--exclude-standard']).decode().split('\0')
manifest = root / 'docs/images/PROVENANCE.json'
images = {row['file'] for row in json.loads(manifest.read_text())['images']} if manifest.exists() else set()
forbidden = {'.exe', '.dll', '.tbl', '.gi', '.grd', '.wld', '.pal', '.snd', '.mus', '.bin', '.bmp', '.scr', '.fnt', '.itm', '.gam', '.wav', '.mid', '.mwpcm', '.asm', '.indices', '.palette', '.f32', '.zip', '.7z', '.dat', '.com', '.ovl', '.raw', '.pcm'}
errors = []
for name in sorted(set(filter(None, files))):
    path = root / name
    if name.startswith('docs/history/') or path.name in ('DOCUMENTATION_REVIEW.md', 'DOCUMENTATION_REVIEW.json', 'TRANSFER.md', 'TRANSFER_VALIDATION.md', 'TRANSFER_MANIFEST.json'):
        errors.append(name + ': internal documentation history belongs in the reverse repository')
    if path.suffix.lower() in forbidden: errors.append(name)
    if path.suffix.lower() in ('.png', '.jpg', '.jpeg') and name not in images: errors.append(name + ': image not inventoried')
    if path.is_file() and path.read_bytes()[:2] == b'MZ': errors.append(name + ': MZ executable signature')
for name in images:
    if not (root / name).is_file(): errors.append(name + ': missing documentation image')
if manifest.exists():
    for row in json.loads(manifest.read_text())['images']:
        image = root / row['file']
        if image.is_file() and hashlib.sha256(image.read_bytes()).hexdigest() != row['sha256']:
            errors.append(row['file'] + ': provenance hash mismatch')
if errors:
    print('\n'.join(errors)); sys.exit(1)
print(f'PASS: {len(set(filter(None, files)))} publishable files; no original binaries/raw assets; {len(images)} documented PNGs.')
