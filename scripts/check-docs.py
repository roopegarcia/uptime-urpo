#!/usr/bin/env python3
"""Check repository-local Markdown links/anchors without making external requests."""
from pathlib import Path
import re
import subprocess
from urllib.parse import unquote

root=Path(__file__).resolve().parent.parent
errors=[]
files=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z','*.md'],cwd=root).decode().split('\0')
for filename in sorted(set(filter(None,files))):
    path=root/filename
    if not path.is_file():continue
    for link in re.findall(r'\]\(([^)]+)\)',path.read_text()):
        if re.match(r'^[a-zA-Z][a-zA-Z0-9+.-]*:',link):continue
        target,_,anchor=link.partition('#')
        destination=path.parent/unquote(target) if target else path
        if not destination.exists():errors.append(f'{filename}: missing {link}');continue
        if anchor and destination.suffix=='.md':
            headings=re.findall(r'^#+\s+(.+)',destination.read_text(),re.M)
            slugs=[re.sub(r'[^\w\- ]','',h.lower()).replace(' ','-') for h in headings]
            if anchor not in slugs:errors.append(f'{filename}: missing anchor {link}')
for error in errors:print(error)
if errors:raise SystemExit(1)
print('Repository documentation links and anchors passed.')
