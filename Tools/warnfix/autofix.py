# -*- coding: utf-8 -*-
"""Auto-fixer nullability berbasis POSISI diagnostik compiler (baris+kolom).

Aturan behavior-preserving (hanya anotasi + asersi, tanpa ubah semantik runtime):
- CS8618/CS8600/CS8625(ref): tambah `?` pada token TIPE deklarasi (bukan sebelum identifier).
- CS8625(value): `= null` -> `= default`.
- CS8601/CS8603: tambah `!` sebelum `;` (akhir baris).
- CS8604: sisip `!` di akhir ekspresi argumen (sadar-paren/kutip/generik).

Per baris: bang-akhir dulu, lalu edit berbasis kolom dari KANAN ke KIRI agar
posisi kolom warning berikutnya tidak bergeser.
"""
import io
import re
import sys
from collections import defaultdict

FILES = sys.argv[1:] or ["SuDesApp.Core", "SuDesApp.Wpf"]
FIXED = {"int", "long", "short", "byte", "sbyte", "uint", "ulong", "ushort",
         "double", "float", "decimal", "bool", "char", "nint", "nuint",
         "DateTime", "Guid", "TimeSpan", "DateOnly", "TimeOnly"}
MODIFIERS = {"public", "private", "protected", "internal", "static", "readonly",
             "const", "volatile", "new", "required", "partial", "override",
             "virtual", "abstract", "sealed", "extern", "async", "unsafe", "fixed"}
TOKEN = r"[A-Za-z_][\w\.]*(?:<[^=]*?>)?\??"


def code_index(line):
    i_c = line.find("//")
    i_s = line.find('"')
    cands = [x for x in (i_c, i_s) if x >= 0]
    return min(cands) if cands else 10**9


def find_assign_eq(line, limit=None):
    """Posisi '=' assignment (bukan ==, !=, >=, <=, =>) sebelum `limit`."""
    seg = line[:limit] if limit is not None else line
    m = re.search(r"[^=!<>]=[^=]", seg)
    return (m.start() + 1) if m else -1


def type_name_before_eq(line, eq_pos):
    """Kembalikan (posisi_sisip, token_tipe) untuk pola `... TYPE NAME =` ; None bila bukan deklarasi."""
    seg = line[:eq_pos].rstrip()
    m = re.search(r"(" + TOKEN + r")\s+([A-Za-z_]\w*)\s*$", seg)
    if not m:
        return None
    typ = m.group(1)
    if typ in MODIFIERS or typ == "var" or typ.endswith("?"):
        return None
    p = seg.rfind(typ) + len(typ)
    return p, typ


def decl_add_nullable(line):
    eq = find_assign_eq(line)
    if eq == -1:
        return None
    r = type_name_before_eq(line, eq)
    if not r:
        return None
    p, typ = r
    return line[:p] + "?" + line[p:]


def fix_cs8618(line):
    """CS8618: kolom menunjuk identifier member tanpa initializer.
    Cari `TYPE NAME` yang berakhir tepat di kolom, sisip '?' setelah tipe."""
    return None  # ditangani handler khusus (butuh kolom)


def fix_cs8618_at(line, col):
    seg = line[:col].rstrip()
    m = re.search(r"(" + TOKEN + r")\s+([A-Za-z_]\w*)$", seg)
    if not m:
        return None
    typ = m.group(1)
    if typ in MODIFIERS or typ == "var" or typ.endswith("?"):
        return None
    p = seg.rfind(typ) + len(typ)
    return line[:p] + "?" + line[p:]


def fix_cs8625(line):
    m0 = re.search(r"=\s*null\b", line)
    if not m0:
        return None
    before = line[:m0.start()]
    if before.rstrip().endswith(("!", "=", "<", ">", "^")):
        return None
    typ = next((w.strip() for w in re.findall(TOKEN, before) if w.strip() in FIXED), None)
    if typ is not None:
        return line[:m0.start()] + "= default" + line[m0.end():]
    r = type_name_before_eq(line, m0.start())
    if not r:
        return None
    p, _ = r
    return line[:p] + "?" + line[p:]


def append_bang_semi(line):
    i = line.rfind(";")
    if i == -1:
        # Baris tanpa ; (misal baris dalam object initializer) — jangan tambah !
        return line
    if line[i-1:i] == "!":
        return line
    # Skip if content before ; ends with } or ) (dictionary/object initializer entry or method call)
    before_semi = line[:i].rstrip()
    if before_semi.endswith(("}", ")")):
        return line
    return line[:i] + "!" + line[i:]


def skip_block(line, i, op, cl):
    depth, n = 1, len(line)
    i += 1
    while i < n and depth:
        ch = line[i]
        if ch == op:
            depth += 1
        elif ch == cl:
            depth -= 1
        i += 1
    return i


def insert_bang_arg(line, col):
    """Sisip '!' di akhir ekspresi argumen yang DIMULAI di `col` (0-based)."""
    i, n = col, len(line)
    while i < n:
        ch = line[i]
        if ch in "\"'":
            q = ch
            i += 1
            while i < n and line[i] != q:
                if line[i] == "\\":
                    i += 1
                i += 1
            i += 1
        elif ch == "(":
            i = skip_block(line, i, "(", ")")
        elif ch == "[":
            i = skip_block(line, i, "[", "]")
        elif ch == "<" and i > col and (line[i-1].isalnum() or line[i-1] in "_>?)"):
            i = skip_block(line, i, "<", ">")
        elif ch in ",)":
            break
        else:
            i += 1
    end = min(i, n)
    seg = line[col:end].rstrip()
    if seg == "" or seg.endswith("!"):
        return line
    cut = col + len(seg)
    return line[:cut] + "!" + line[cut:]


def process_line(line, sites):
    """sites: [(col0based, code)] -> baris baru (atau None bila tak berubah)."""
    ci = code_index(line)
    out = line
    applied_cols = set()

    # 1) bang-akhir (8601/8603) — target ';' di ujung, sekali saja cukup
    for _, code in sites:
        if code in ("CS8601", "CS8603"):
            new = append_bang_semi(out)
            if new != out:
                out = new

    # 2) edit berbasis kolom, dari kanan ke kiri
    for c, code in sorted({(c, k) for c, k in sites}, key=lambda t: -t[0]):
        if c >= ci or c in applied_cols:
            continue
        new = None
        if code == "CS8618":
            new = fix_cs8618_at(out, c)
        elif code == "CS8625":
            new = fix_cs8625(out)  # regex-based; kolom hanya penjaga
        elif code == "CS8600":
            new = decl_add_nullable(out)
        elif code == "CS8604":
            new = insert_bang_arg(out, c)
        if new is not None and new != out:
            out = new
            applied_cols.add(c)
    return out if out != line else None


def main():
    warns = defaultdict(lambda: defaultdict(list))  # file -> lno -> [(col, code)]
    with io.open(".freebuff-warnings.txt", encoding="utf-8", errors="replace") as f:
        for ln in f:
            m = re.search(r"([^(]+)\((\d+),(\d+)\): warning (CS\d+)", ln)
            if not m:
                continue
            fp = m.group(1).replace("\\", "/")
            for t in FILES:
                i = fp.find(t + "/")
                if i > 0:
                    fp = fp[i:]
                    break
            if not any(t in fp for t in FILES):
                continue
            if m.group(4) not in {"CS8618", "CS8625", "CS8600", "CS8601", "CS8603", "CS8604"}:
                continue
            warns[fp][int(m.group(2))].append((int(m.group(3)) - 1, m.group(4)))

    total = touched = 0
    skipped = defaultdict(int)
    for fp, lines_map in sorted(warns.items()):
        try:
            with io.open(fp, encoding="utf-8-sig", newline="") as f:
                src = f.read()
        except OSError:
            skipped["no-file"] += sum(len(v) for v in lines_map.values())
            continue
        has_bom = src.startswith("\ufeff")
        nl = "\r\n" if "\r\n" in src[:4000] else "\n"
        lines = src.split(nl)
        changed = False
        for lno, sites in lines_map.items():
            idx = lno - 1
            if idx < 0 or idx >= len(lines):
                skipped["range"] += 1
                continue
            new = process_line(lines[idx], sites)
            if new is None:
                skipped["no-fix"] += 1
                continue
            lines[idx] = new
            total += len(sites)
            changed = True
        if changed:
            data = nl.join(lines)
            if has_bom:
                data = "\ufeff" + data
            with io.open(fp, "w", encoding="utf-8-sig" if has_bom else "utf-8", newline="") as f:
                f.write(data)
            touched += 1
    print("files_changed=%d sites=%d skipped=%s" % (touched, total, dict(skipped)))


main()
