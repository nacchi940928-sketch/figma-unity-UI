#!/usr/bin/env python3
"""Offline verify: read prefab YAML patches and merge into Figma export template."""

from __future__ import annotations

import json
import re
import sys
from copy import deepcopy
from pathlib import Path


def parse_block_id(part: str) -> int:
    match = re.match(r"^\d+\s*&(\d+)", part.strip())
    return int(match.group(1)) if match else 0


def parse_file_id(part: str, pattern: str) -> int:
    match = re.search(pattern, part)
    return int(match.group(1)) if match else 0


def parse_vec2(part: str, field: str) -> tuple[float, float]:
    match = re.search(
        field + r"\s*:\s*\{x:\s*([-\d.]+),\s*y:\s*([-\d.]+)\}",
        part,
    )
    if not match:
        return 0.0, 0.0
    return float(match.group(1)), float(match.group(2))


def axis_size(rect: dict, horizontal: bool) -> float:
    index = 0 if horizontal else 1
    return abs(rect["size"][index])


def is_stretch(rect: dict, horizontal: bool) -> bool:
    index = 0 if horizontal else 1
    return abs(rect["amin"][index] - rect["amax"][index]) > 1e-5


def read_point(rect: dict, parent: dict, width: float, height: float) -> tuple[float, float]:
    anchor_x = (rect["amin"][0] + rect["amax"][0]) * 0.5
    anchor_y = (rect["amin"][1] + rect["amax"][1]) * 0.5
    parent_w = axis_size(parent, True)
    parent_h = axis_size(parent, False)
    pivot_world_x = rect["pos"][0] + parent_w * anchor_x
    pivot_from_parent_top_y = parent_h * (1.0 - anchor_y) - rect["pos"][1]
    x = pivot_world_x - width * rect["pivot"][0]
    y = pivot_from_parent_top_y - height * (1.0 - rect["pivot"][1])
    return x, y


def map_horizontal(anchor_x: float) -> str:
    if abs(anchor_x - 0.5) < 1e-5:
        return "CENTER"
    if abs(anchor_x - 1.0) < 1e-5:
        return "MAX"
    return "MIN"


def map_vertical(anchor_y: float) -> str:
    if abs(anchor_y - 0.5) < 1e-5:
        return "CENTER"
    if abs(anchor_y - 0.0) < 1e-5:
        return "MAX"
    return "MIN"


def read_constraints(rect: dict) -> tuple[str, str]:
    stretch_h = is_stretch(rect, True)
    stretch_v = is_stretch(rect, False)
    horizontal = "STRETCH" if stretch_h else map_horizontal((rect["amin"][0] + rect["amax"][0]) * 0.5)
    vertical = "STRETCH" if stretch_v else map_vertical((rect["amin"][1] + rect["amax"][1]) * 0.5)
    return horizontal, vertical


def export_patches_from_yaml(yaml: str) -> dict[str, dict]:
    rects: dict[int, dict] = {}
    for part in yaml.split("--- !u!"):
        if "RectTransform:" not in part:
            continue
        transform_id = parse_block_id(part)
        if transform_id == 0:
            continue
        rects[transform_id] = {
            "go": parse_file_id(part, r"m_GameObject:\s*\{fileID:\s*(-?\d+)"),
            "father": parse_file_id(part, r"m_Father:\s*\{fileID:\s*(-?\d+)"),
            "pos": parse_vec2(part, "m_AnchoredPosition"),
            "amin": parse_vec2(part, "m_AnchorMin"),
            "amax": parse_vec2(part, "m_AnchorMax"),
            "pivot": parse_vec2(part, "m_Pivot"),
            "size": parse_vec2(part, "m_SizeDelta"),
        }

    bindings: list[tuple[str, int, str | None]] = []
    for part in yaml.split("--- !u!"):
        if "irId:" not in part:
            continue
        ir_match = re.search(r"irId:\s*(\S+)", part)
        go_match = re.search(r"m_GameObject:\s*\{fileID:\s*(-?\d+)", part)
        figma_match = re.search(r"figmaNodeId:\s*(\S+)", part)
        if ir_match and go_match:
            bindings.append((ir_match.group(1), int(go_match.group(1)), figma_match.group(1) if figma_match else None))

    rect_by_go = {rect["go"]: rect for rect in rects.values()}
    root_go = next(rect["go"] for rect in rects.values() if rect["father"] == 0)
    parent_rect = rect_by_go[root_go]

    patches: dict[str, dict] = {}
    for ir_id, go_id, figma_node_id in bindings:
        rect = rect_by_go.get(go_id)
        if rect is None:
            continue
        parent = parent_rect if go_id != root_go else None
        width = axis_size(rect, True)
        height = axis_size(rect, False)
        if parent is None:
            x, y = 0.0, 0.0
        else:
            x, y = read_point(rect, parent, width, height)
        horizontal, vertical = read_constraints(rect)
        patches[ir_id] = {
            "irId": ir_id,
            "figmaNodeId": figma_node_id,
            "x": x,
            "y": y,
            "width": width,
            "height": height,
            "rotation": 0.0,
            "constraintHorizontal": horizontal,
            "constraintVertical": vertical,
        }

    return patches


def merge_node(node: dict, patches: dict[str, dict], matched: set[str], parent_root_x: float, parent_root_y: float) -> None:
    ir_id = node.get("irId")
    if ir_id and ir_id in patches:
        patch = patches[ir_id]
        node["x"] = patch["x"]
        node["y"] = patch["y"]
        node["width"] = patch["width"]
        node["height"] = patch["height"]
        node["rotation"] = patch["rotation"]
        node["constraints"] = {
            "horizontal": patch["constraintHorizontal"],
            "vertical": patch["constraintVertical"],
        }
        matched.add(ir_id)

    x = float(node.get("x", 0.0))
    y = float(node.get("y", 0.0))
    root_x = parent_root_x + x
    root_y = parent_root_y + y
    node["rootX"] = root_x
    node["rootY"] = root_y

    for child in node.get("children") or []:
        merge_node(child, patches, matched, root_x, root_y)


def main() -> int:
    repo = Path(__file__).resolve().parents[1]
    prefab = repo / "UITest/Assets/UI/Generated/main-screen-1080x2340.prefab"
    template = repo / "figmajson/main-screen-1080x2340-export/main-screen-1080x2340-full.json"
    output = repo / "figmajson/main-screen-1080x2340-export/main-screen-1080x2340-unity-export-3.json"
    sidecar = output.with_suffix(".patches.json")

    yaml = prefab.read_text(encoding="utf-8")
    patches = export_patches_from_yaml(yaml)
    bubble = patches.get("inspiration-bubble__6_121")
    print(f"bindings={len(patches)} inspiration-bubble={bubble}")

    doc = json.loads(template.read_text(encoding="utf-8"))
    matched: set[str] = set()
    merge_node(doc["node"], patches, matched, 0.0, 0.0)

    output.write_text(json.dumps(doc, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    sidecar.write_text(json.dumps(list(patches.values()), indent=2), encoding="utf-8")
    print(f"wrote {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
