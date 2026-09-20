# -*- coding: utf-8 -*-
"""Hapus ,! yang salah (seharusnya hanya ,) di semua file .cs"""
import os

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
        if ",!" not in content:
            continue
        # Hanya hapus ,! yang diikuti whitespace atau akhir baris
        # (jangan hapus string literal seperti ",!" inside verbatim strings)
        new = content.replace(",!\n", ",\n").replace(",!\r\n", ",\r\n").replace(",! ", ", ").replace(",!;", ";")
        if new != content:
            nl = "\r\n" if "\r\n" in content[:4000] else "\n"
            with open(fp, "w", encoding="utf-8", newline=nl) as fh:
                fh.write(new)
            fixed_files.append(fp)

print("Fixed %d files" % len(fixed_files))
for f in fixed_files:
    print("  - %s" % f)
