#!/usr/bin/env python3
"""Compare figmajson/main/full vs unity-export."""
import json
from pathlib import Path
from collections import Counter, defaultdict

base = Path(r"e:/figma-unity-UI/figmajson/main")
full = json.loads((base / "main-screen-1080x2340-full.json").read_text(encoding="utf-8"))
exp = json.loads((base / "main-screen-1080x2340-unity-export.json").read_text(encoding="utf-8"))


def walk(node, out):
    ir = node.get("irId")
    if ir:
        out[ir] = node
    for c in node.get("children") or []:
        walk(c, out)


ff, ee = {}, {}
walk(full["node"], ff)
walk(exp["node"], ee)

print("=" * 60)
print("FILES: figmajson/main/")
print("  FULL  : main-screen-1080x2340-full.json")
print("  EXPORT: main-screen-1080x2340-unity-export.json")
print("=" * 60)

print("\n## 1. METADATA")
for k in sorted(set(full["metadata"]) | set(exp["metadata"])):
    fv = full["metadata"].get(k, "—")
    ev = exp["metadata"].get(k, "—")
    if fv != ev:
        print(f"  {k}:")
        print(f"    FULL  : {fv}")
        print(f"    EXPORT: {ev}")

print("\n## 2. ROOT")
for label, doc in [("FULL", full), ("EXPORT", exp)]:
    n = doc["node"]
    print(
        f"  {label}: w={n['width']} h={n['height']} fill={(n.get('fills') or [{}])[0].get('color')} "
        f"constraints={n.get('constraints')}"
    )

only_full = set(ff) - set(ee)
only_exp = set(ee) - set(ff)
print(f"\n## 3. NODE COUNT: full={len(ff)} export={len(ee)}")
print(f"  only in full: {len(only_full)}  only in export: {len(only_exp)}")
if only_full:
    print("  missing in export:", list(only_full)[:5])
if only_exp:
    print("  extra in export:", list(only_exp)[:5])

# layoutMode
layout_diff = []
for ir in set(ff) & set(ee):
    fl = (ff[ir].get("layout") or {}).get("layoutMode")
    el = (ee[ir].get("layout") or {}).get("layoutMode")
    if fl != el:
        layout_diff.append((ir, ff[ir].get("name"), fl, el))

print(f"\n## 4. layoutMode 变化 ({len(layout_diff)})")
for ir, name, fl, el in layout_diff[:15]:
    print(f"  {ir:42} {fl} -> {el}")
if len(layout_diff) > 15:
    print(f"  ... 还有 {len(layout_diff)-15} 个")

# TEXT nodes - fonts & alignment
text_diffs = defaultdict(list)
for ir in set(ff) & set(ee):
    f, e = ff[ir], ee[ir]
    if f.get("type") != "TEXT":
        continue
    checks = [
        ("fontFamily", f.get("fontFamily"), e.get("fontFamily")),
        ("fontSize", f.get("fontSize"), e.get("fontSize")),
        ("fontWeight", f.get("fontWeight"), e.get("fontWeight")),
        ("textAlignHorizontal", f.get("textAlignHorizontal"), e.get("textAlignHorizontal")),
        ("textAlignVertical", f.get("textAlignVertical"), e.get("textAlignVertical")),
        ("layoutSizingH", f.get("layoutSizingHorizontal"), e.get("layoutSizingHorizontal")),
        ("layoutSizingV", f.get("layoutSizingVertical"), e.get("layoutSizingVertical")),
        ("layoutPositioning", f.get("layoutPositioning"), e.get("layoutPositioning")),
        ("x", f.get("x"), e.get("x")),
        ("y", f.get("y"), e.get("y")),
        ("width", f.get("width"), e.get("width")),
        ("height", f.get("height"), e.get("height")),
    ]
    for field, fv, ev in checks:
        if fv != ev and not (isinstance(fv, (int, float)) and isinstance(ev, (int, float)) and abs(float(fv)-float(ev)) < 0.01):
            text_diffs[field].append((ir, f.get("name"), fv, ev))

print(f"\n## 5. TEXT 差异 (共 {len([ir for ir in set(ff)&set(ee) if ff[ir].get('type')=='TEXT'])} 个 TEXT 节点)")
for field, rows in sorted(text_diffs.items(), key=lambda x: -len(x[1])):
    print(f"\n  ### {field} ({len(rows)} 处)")
    for ir, name, fv, ev in rows[:8]:
        print(f"    {ir} {name!r}: {fv!r} -> {ev!r}")
    if len(rows) > 8:
        print(f"    ... 还有 {len(rows)-8} 处")

# constraints on all nodes
con_diff = [(ir, ff[ir].get("name"), ff[ir]["constraints"], ee[ir]["constraints"])
            for ir in set(ff) & set(ee) if ff[ir].get("constraints") != ee[ir].get("constraints")]
print(f"\n## 6. constraints 变化 ({len(con_diff)})")
for ir, name, a, b in con_diff[:12]:
    print(f"  {ir}: {a} -> {b}")

# layout object changes (auto layout settings)
layout_obj_diff = []
for ir in set(ff) & set(ee):
    fl, el = ff[ir].get("layout") or {}, ee[ir].get("layout") or {}
    if fl != el:
        changed_keys = [k for k in set(fl)|set(el) if fl.get(k) != el.get(k)]
        if changed_keys:
            layout_obj_diff.append((ir, ff[ir].get("name"), changed_keys, fl, el))
print(f"\n## 7. layout 对象字段变化 ({len(layout_obj_diff)})")
for ir, name, keys, fl, el in layout_obj_diff[:10]:
    print(f"  {ir} {name!r} keys={keys}")
    for k in keys[:4]:
        print(f"    {k}: {fl.get(k)!r} -> {el.get(k)!r}")

# position diffs summary
pos = [(ir, ff[ir].get("name"), ff[ir].get("y"), ee[ir].get("y"))
       for ir in set(ff) & set(ee)
       if abs(float(ff[ir].get("y") or 0) - float(ee[ir].get("y") or 0)) > 0.5]
pos.sort(key=lambda r: abs(float(r[2] or 0) - float(r[3] or 0)), reverse=True)
print(f"\n## 8. y 坐标差异 >0.5 ({len(pos)})")
for ir, name, fy, ey in pos[:15]:
    print(f"  {ir:42} y {fy} -> {ey}")

# fill diffs
fills = [(ir, (ff[ir].get("fills") or [{}])[0].get("color") if ff[ir].get("fills") else None,
              (ee[ir].get("fills") or [{}])[0].get("color") if ee[ir].get("fills") else None)
         for ir in set(ff) & set(ee)]
fills = [(ir, a, b) for ir, a, b in fills if a != b]
print(f"\n## 9. fills 颜色变化 ({len(fills)})")
for ir, a, b in fills[:15]:
    print(f"  {ir}: {a} -> {b}")

# segments diff for text
seg_diff = []
for ir in set(ff) & set(ee):
    if ff[ir].get("type") != "TEXT":
        continue
    fs, es = ff[ir].get("segments"), ee[ir].get("segments")
    if fs != es:
        seg_diff.append((ir, ff[ir].get("name"), fs, es))
print(f"\n## 10. TEXT segments 变化 ({len(seg_diff)})")
for ir, name, fs, es in seg_diff[:5]:
    print(f"  {ir} {name!r}")
    if fs and es:
        print(f"    FULL seg[0]: {fs[0]}")
        print(f"    EXP  seg[0]: {es[0]}")

print("\n## 11. 关键节点 side-by-side")
for ir in ["node__6_124", "inspiration-bubble__6_121", "top-status-bar__6_5", "bottom-nav__6_138"]:
    if ir not in ff or ir not in ee:
        continue
    print(f"\n--- {ir} ---")
    for label, doc in [("FULL", ff[ir]), ("EXPORT", ee[ir])]:
        keys = ["x","y","width","height","fontFamily","fontSize","fontWeight",
                "textAlignHorizontal","textAlignVertical","layoutSizingHorizontal",
                "layoutSizingVertical","layoutPositioning","constraints"]
        layout = (doc.get("layout") or {}).get("layoutMode")
        vals = {k: doc.get(k) for k in keys if doc.get(k) is not None}
        vals["layoutMode"] = layout
        print(f"  {label}: {json.dumps(vals, ensure_ascii=False)}")
