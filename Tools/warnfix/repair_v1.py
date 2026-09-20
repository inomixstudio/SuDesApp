# -*- coding: utf-8 -*-
"""Perbaiki kerusakan autofix v1.

Pola kerusakan utama:
- `string x = ?EXPR;` → `string? x = EXPR;` atau `string x = EXPR!;`
- `var x = ?EXPR;` → `var x = EXPR;` (hapus ? saja, var inferensi)

Root cause: v1 autofix menambahkan ? di depan RHS expression alih-alih di tipe.
"""
import re
import os
import sys

def is_in_string_or_comment(line, pos):
    """Cek apakah posisi di dalam string atau komentar."""
    i = 0
    n = len(line)
    while i < pos and i < n:
        if line[i] == '/' and i + 1 < n:
            if line[i+1] == '/':
                return True
            if line[i+1] == '*':
                j = i + 2
                while j + 1 < n:
                    if line[j] == '*' and line[j+1] == '/':
                        i = j + 2
                        break
                    j += 1
                else:
                    return True
                continue
        if line[i] == '"':
            i += 1
            while i < n and line[i] != '"':
                if line[i] == '\\':
                    i += 1
                i += 1
            i += 1
            continue
        if line[i] == '@' and i + 1 < n and line[i+1] == '"':
            i += 2
            while i + 1 < n:
                if line[i] == '"' and line[i+1] == '"':
                    i += 2
                elif line[i] == '"':
                    i += 1
                    break
                else:
                    i += 1
            continue
        i += 1
    return False

def fix_v1_damage(line):
    """Perbaiki pola kerusakan v1: `TYPE x = ?EXPR;` → `TYPE? x = EXPR;`"""
    
    # Pola 1: deklarasi lokal dengan assignment
    # `TYPE NAME = ?EXPR;` → `TYPE? NAME = EXPR;`
    m = re.match(r'^(\s*)((?:string|int|long|short|byte|sbyte|uint|ulong|ushort|double|float|decimal|bool|char|object|var|DateTime|Guid|TimeSpan|DateOnly|TimeOnly|HashSet|List|Dictionary|Task|SqliteConnection|SqliteCommand|byte\[\]|byte\?|int\?|long\?|bool\?|string\?|double\?|decimal\?|float\?|char\?)\s+)([A-Za-z_]\w*)\s*=\s*\?([^;]+;)', line)
    if m and not is_in_string_or_comment(line, m.start()):
        indent = m.group(1)
        type_decl = m.group(2).rstrip()
        var_name = m.group(3)
        expr = m.group(4)
        
        # Jika tipe sudah nullable, jangan tambah ? lagi
        if not type_decl.endswith('?'):
            return f"{indent}{type_decl}? {var_name} = {expr}", True
        else:
            return f"{indent}{type_decl} {var_name} = {expr}", True
    
    # Pola 2: property/field initializer
    # `{ get; set; } = ?EXPR;` → `{ get; set; } = EXPR;` (perlu ? di tipe prop)
    # Ini lebih kompleks, skip dulu
    
    # Pola 3: assignment tanpa deklarasi
    # `NAME = ?EXPR;` → `NAME = EXPR;`
    m = re.match(r'^(\s*)([A-Za-z_]\w*)\s*=\s*\?([^;]+;)', line)
    if m and not is_in_string_or_comment(line, m.start()):
        indent = m.group(1)
        var_name = m.group(2)
        expr = m.group(3)
        
        # Skip jika ini bagian dari objek initializer atau ternary
        # Cek konteks sebelumnya
        before = line[:m.start(2)]
        if any(before.rstrip().endswith(x) for x in ['(', ',', '=', '!', '<', '>', '[', '{', '||', '&&']):
            return line, False
        
        return f"{indent}{var_name} = {expr}", True
    
    return line, False

def main():
    files_to_check = []
    for root, dirs, files in os.walk("."):
        dirs[:] = [d for d in dirs if not d.startswith('.') and d not in ('bin', 'obj', 'Tools', 'packages')]
        for f in files:
            if f.endswith('.cs'):
                files_to_check.append(os.path.join(root, f))
    
    total_fixed = 0
    fixed_files = []
    
    for fp in files_to_check:
        try:
            with open(fp, 'r', encoding='utf-8-sig') as f:
                content = f.read()
        except Exception:
            continue
        
        lines = content.split('\n')
        changed = False
        new_lines = []
        
        for line in lines:
            new_line, did_fix = fix_v1_damage(line)
            new_lines.append(new_line)
            if did_fix:
                changed = True
                total_fixed += 1
        
        if changed:
            with open(fp, 'w', encoding='utf-8') as f:
                f.write('\n'.join(new_lines))
            fixed_files.append(fp)
    
    print(f"Fixed {total_fixed} lines in {len(fixed_files)} files")
    for f in fixed_files:
        print(f"  - {f}")

if __name__ == "__main__":
    main()
