# -*- coding: utf-8 -*-
"""Hapus ! yang salah setelah method calls (OnPropertyChanged(...)! -> OnPropertyChanged(...))"""
import os
import re

fixed_files = []
for root, dirs, files in os.walk("."):
    dirs[:] = [d for d in dirs if not d.startswith(".") and d not in ("bin", "obj", "Tools", "packages", ".freebuff")]
    for f in files:
        if not f.endswith(".cs"):
            continue
        fp = os.path.join(root, f)
        try:
            with open(fp, encoding="utf-8-sig", newline="") as fh:
                content = fh.read()
        except OSError:
            continue
        # Pattern with nested parens: MethodName(...)!) -> MethodName(...))
        # Pattern with nested parens: MethodName(...)!; -> MethodName(...);
        new = re.sub(r'(\w+\((?:[^()]*|\([^()]*\))*\))!\s*;', r'\1;', content)
        new = re.sub(r'(\w+\((?:[^()]*|\([^()]*\))*\))!\s*\)', r'\1)', new)
        if new != content:
            with open(fp, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(new)
            fixed_files.append(fp)

print("Fixed %d files" % len(fixed_files))
for f in fixed_files:
    print("  - %s" % f)
