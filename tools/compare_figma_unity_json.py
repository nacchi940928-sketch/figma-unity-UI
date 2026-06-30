#!/usr/bin/env python3
import json
from pathlib import Path
from collections import Counter

base = Path(__file__).resolve().parents[1] / "figmajson/main-screen-1080x2340-export"
full = json.loads((base / "main-screen-1080x2340-full.json").read_text(encoding="utf-8"))
exp = json.loads((base / "main-screen-1080x2340-unity-export-6.json").read_text(encoding="utf-8"))


def walk(node, out):
    ir = node.get("irId")
    if ir:
        out[ir] = {
            "name": node.get("name"),
            "type": node.get("type"),
            "x": node.get("x"),
            "y": node.get("y"),
            "width": node.get("width"),
            "height": node.get("height"),
            "layoutMode": (node.get("layout") or {}).get("layoutMode"),
            "layoutSizingV": node.get("layoutSizingVertical"),
            "layoutSizingH": node.get("layoutSizingHorizontal"),
            "layoutPositioning": node.get("layoutPositioning"),
            "constraints": node.get("constraints"),
            "fills0": (node.get("fills") or [{}])[0].get("color") if node.get("fills") else None,
            "characters": node.get("characters"),
        }
    for c in node.get("children") or []:
        walk(c, out)


ff, ee = {}, {}
walk(full["node"], ff)
walk(exp["node"], ee)

print("=== METADATA ===")
print("FULL:", json.dumps(full["metadata"], ensure_ascii=False))
print("EXPORT:", json.dumps(exp["metadata"], ensure_ascii=False))

print("\n=== ROOT NODE ===")
for label, doc in [("FULL", full), ("EXPORT", exp)]:
    n = doc["node"]
    print(
        label,
        f"x={n['x']} y={n['y']} w={n['width']} h={n['height']}",
        f"fill={(n.get('fills') or [{}])[0].get('color')}",
        f"constraints={n.get('constraints')}",
    )

layout_break = Counter()
for ir in set(ff) & set(ee):
    f, e = ff[ir], ee[ir]
    if f["layoutMode"] != e["layoutMode"]:
        layout_break[f"{f['layoutMode']} -> {e['layoutMode']}"] += 1

print("\n=== layoutMode CHANGES (count", sum(layout_break.values()), ") ===")
for k, v in layout_break.most_common():
    print(f"  {v:3}  {k}")

pos_diff = []
for ir in set(ff) & set(ee):
    f, e = ff[ir], ee[ir]
    if any(abs(float(f[k] or 0) - float(e[k] or 0)) > 0.01 for k in ["x", "y", "width", "height"]):
        pos_diff.append(
            (ir, f["name"], f["x"], e["x"], f["y"], e["y"], f["width"], e["width"], f["height"], e["height"])
        )

print("\n=== ALL POSITION/SIZE DIFFS (", len(pos_diff), "nodes ) ===")
for row in sorted(pos_diff, key=lambda r: abs(float(r[4] or 0) - float(r[5] or 0)), reverse=True):
    ir, name, fx, ex, fy, ey, fw, ew, fh, eh = row
    parts = []
    if abs(float(fx or 0) - float(ex or 0)) > 0.01:
        parts.append(f"x {fx}->{ex}")
    if abs(float(fy or 0) - float(ey or 0)) > 0.01:
        parts.append(f"y {fy}->{ey}")
    if abs(float(fw or 0) - float(ew or 0)) > 0.01:
        parts.append(f"w {fw}->{ew}")
    if abs(float(fh or 0) - float(eh or 0)) > 0.01:
        parts.append(f"h {fh}->{eh}")
    print(f"  {ir:42} {name!r:20} {' | '.join(parts)}")

fill_diff = [(ir, ff[ir]["fills0"], ee[ir]["fills0"]) for ir in set(ff) & set(ee) if ff[ir]["fills0"] != ee[ir]["fills0"]]
print("\n=== FILL COLOR DIFFS (", len(fill_diff), ") ===")
for ir, a, b in fill_diff:
    print(f"  {ir}: {a} -> {b}")

constraint_diff = [
    (ir, ff[ir]["constraints"], ee[ir]["constraints"])
    for ir in set(ff) & set(ee)
    if ff[ir]["constraints"] != ee[ir]["constraints"]
]
print("\n=== CONSTRAINTS DIFFS (", len(constraint_diff), ") ===")
for ir, a, b in constraint_diff[:20]:
    print(f"  {ir}: {a} -> {b}")

text_layout = []
for ir in set(ff) & set(ee):
    f, e = ff[ir], ee[ir]
    if f["type"] != "TEXT":
        continue
    changes = []
    if f["layoutSizingV"] != e["layoutSizingV"]:
        changes.append(f"sizingV {f['layoutSizingV']}->{e['layoutSizingV']}")
    if f["layoutPositioning"] != e["layoutPositioning"]:
        changes.append(f"pos {f['layoutPositioning']}->{e['layoutPositioning']}")
    if changes:
        text_layout.append((ir, f["name"], f["y"], e["y"], changes))

print("\n=== TEXT LAYOUT FIELD CHANGES (", len(text_layout), ") ===")
for ir, name, fy, ey, changes in text_layout:
    print(f"  {ir} {name!r} y {fy}->{ey}  {' | '.join(changes)}")

print("\n=== KEY NODES DETAIL ===")
for ir in [
    "main-screen-1080x2340__6_4",
    "inspiration-bubble__6_121",
    "frame__6_122",
    "node__6_124",
    "quick-actions__6_125",
]:
    print(f"\n--- {ir} ---")
    if ir in ff:
        print("FULL  ", json.dumps(ff[ir], ensure_ascii=False))
    if ir in ee:
        print("EXPORT", json.dumps(ee[ir], ensure_ascii=False))
