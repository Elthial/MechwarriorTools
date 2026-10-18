"""Validate current reader guides; historical/native evidence is intentionally separate."""
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
documents = [root / 'README.md']
if (root / 'PLAN.md').exists(): documents.append(root / 'PLAN.md')
documents += sorted(path for path in (root / 'docs').rglob('*.md') if 'history' not in path.parts)
errors = []
if (root / 'docs/history').exists():
    errors.append('Documentation history belongs in the reverse repository, not this public repository.')
for filename in ('GAME_DATA.md', 'GAMEPLAY_METHOD_INDEX.json'):
    if (root / 'docs' / filename).exists():
        errors.append(f'{filename}: C17 game documentation belongs in the SDL-port repository.')
for path in documents:
    text = path.read_text(encoding='utf-8-sig')
    relative = path.relative_to(root).as_posix()
    for match in re.finditer(r'\b(?:Mw|Btech|Ega)[A-Za-z0-9_]*_[0-9A-F]{4}\b', text):
        errors.append(f'{relative}: stale runtime suffix: {match[0]}')
    for name in re.findall(r'\b((?:Mw|Btech|Ega)[A-Z]\w*)\(\)', text):
        errors.append(f'{relative}: game method {name} belongs in the SDL-port documentation.')
    if path.parent.name in ('gameplay', 'reference'):
        for match in re.finditer(r'\b(?:AL|AH|AX|BX|CX|DX|SI|DI|DS|SS|DIV|JAE)\b|\b[0-9A-F]{4}:[0-9A-F]{4}\b', text):
            errors.append(f'{relative}: low-level research in gameplay guide: {match[0]}')
    for target in re.findall(r'\]\(([^\n)]+)\)', text):
        target = target.strip('<>')
        if re.match(r'(?:\w+://|mailto:|#)', target): continue
        target = target.split('#', 1)[0]
        target = re.sub(r':\d+$', '', target)
        if target and not (path.parent / target).exists(): errors.append(f'{relative}: broken local link: {target}')
if errors:
    print('\n'.join(sorted(set(errors)))); sys.exit(1)
print(f'PASS: {len(documents)} tooling documents; no C17 game-method guides, stale suffixes or broken local links.')
