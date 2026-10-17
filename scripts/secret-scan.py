#!/usr/bin/env python3
"""Narrow secret/runtime-data checks on this repository's staged files only."""
from pathlib import Path
import re
import subprocess
import sys

root=Path(__file__).resolve().parent.parent
names=subprocess.check_output(['git','-C',str(root),'ls-files','--cached','-z']).decode().split('\0')
patterns=[re.compile(x) for x in [rb'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',rb'gh[pousr]_[A-Za-z0-9]{30,}',rb'github_pat_[A-Za-z0-9_]{40,}',rb'AKIA[A-Z0-9]{16}',rb'sk-proj-[A-Za-z0-9_-]{30,}']]
problems=[]
for name in filter(None,names):
    p=root/name
    if not p.exists():continue
    if p.is_symlink():problems.append((name,'symlink'));continue
    if set(Path(name).parts)&{'.git','node_modules','bin','obj','data','.local'} or p.suffix in {'.db','.log'} or p.name.startswith('.env'):
        problems.append((name,'runtime/config data path'));continue
    if any(pattern.search(p.read_bytes()) for pattern in patterns):problems.append((name,'possible credential pattern'))
if problems:
    for name,reason in problems:print(f'FAIL {name}: {reason}')
    sys.exit(1)
print(f'PASS: {sum(bool(n) for n in names)} staged paths; known secret patterns/runtime paths. Manual review still required.')
