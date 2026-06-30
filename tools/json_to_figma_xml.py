#!/usr/bin/env python3
"""Convert Figma *-full.json <-> XML (same schema as Unity FigmaDocumentXmlSerializer)."""

from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

ROOT = "figma-export"
NODE = "node"
CHILDREN = "children"
ITEM = "item"
METADATA_NESTED = {"coordinateConvention", "exportProfile", "layoutAdjustments"}
ARRAY_KEYS = {"fills", "strokes", "effects", "dashPattern", "segments", "layoutGrids", "prunedIrIds"}


def _scalar_str(value: Any) -> str:
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (int, float)):
        return repr(value)
    return str(value)


def _parse_scalar(raw: str) -> Any:
    if raw.lower() == "true":
        return True
    if raw.lower() == "false":
        return False
    try:
        if "." in raw:
            return float(raw)
        return int(raw)
    except ValueError:
        return raw


def _write_object_elem(name: str, obj: dict[str, Any]) -> ET.Element:
    el = ET.Element(name)
    for key, value in obj.items():
        if isinstance(value, (str, int, float, bool)) or value is None:
            if value is not None:
                el.set(key, _scalar_str(value))
        elif isinstance(value, dict):
            el.append(_write_object_elem(key, value))
        elif isinstance(value, list):
            el.append(_write_array_elem(key, value))
    return el


def _write_array_elem(name: str, arr: list[Any]) -> ET.Element:
    el = ET.Element(name)
    if not arr:
        el.set("empty", "true")
        return el
    for value in arr:
        if isinstance(value, dict):
            el.append(_write_object_elem(ITEM, value))
        else:
            item = ET.SubElement(el, ITEM)
            item.text = _scalar_str(value)
    return el


def _write_node(obj: dict[str, Any]) -> ET.Element:
    el = ET.Element(NODE)
    for key, value in obj.items():
        if key == "children":
            continue
        if isinstance(value, (str, int, float, bool)) or value is None:
            if value is not None:
                el.set(key, _scalar_str(value))
        elif isinstance(value, dict):
            el.append(_write_object_elem(key, value))
        elif isinstance(value, list) and key != "children":
            el.append(_write_array_elem(key, value))

    children = obj.get("children") or []
    if children:
        container = ET.SubElement(el, CHILDREN)
        for child in children:
            container.append(_write_node(child))
    return el


def json_to_xml(data: dict[str, Any]) -> str:
    root = ET.Element(ROOT, {"format": "v1"})
    metadata = data.get("metadata")
    if isinstance(metadata, dict):
        meta_el = ET.SubElement(root, "metadata")
        for key, value in metadata.items():
            if key in METADATA_NESTED and isinstance(value, dict):
                meta_el.append(_write_object_elem(key, value))
            elif key == "prunedIrIds" and isinstance(value, list):
                meta_el.append(_write_array_elem(key, value))
            elif isinstance(value, (str, int, float, bool)):
                meta_el.set(key, _scalar_str(value))
    node = data.get("node")
    if isinstance(node, dict):
        root.append(_write_node(node))
    ET.indent(root, space="  ")
    return ET.tostring(root, encoding="unicode", xml_declaration=False)


def _is_empty_array(el: ET.Element) -> bool:
    return el.attrib.get("empty") == "true" or (not el.attrib and not len(el))


def _read_attrs(el: ET.Element) -> dict[str, Any]:
    return {k: _parse_scalar(v) for k, v in el.attrib.items()}


def _read_object(el: ET.Element) -> dict[str, Any]:
    obj = _read_attrs(el)
    for child in el:
        if len(child):
            if all(grand.tag == ITEM for grand in child):
                obj[child.tag] = _read_array(child)
            else:
                obj[child.tag] = _read_object(child)
        else:
            obj[child.tag] = _parse_scalar(child.text or "")
    return obj


def _read_array(el: ET.Element) -> list[Any]:
    arr: list[Any] = []
    for item in el:
        if item.attrib or len(item):
            arr.append(_read_object(item))
        else:
            arr.append(_parse_scalar(item.text or ""))
    return arr


def _read_node(el: ET.Element) -> dict[str, Any]:
    node = _read_attrs(el)
    for child in el:
        if child.tag == CHILDREN:
            node["children"] = [_read_node(n) for n in child if n.tag == NODE]
        elif child.tag in ARRAY_KEYS:
            node[child.tag] = [] if _is_empty_array(child) else _read_array(child)
        elif len(child) and all(grand.tag == ITEM for grand in child):
            node[child.tag] = _read_array(child)
        else:
            node[child.tag] = _read_object(child)
    return node


def xml_to_json(xml_text: str) -> dict[str, Any]:
    root = ET.fromstring(xml_text)
    if root.tag != ROOT:
        raise ValueError(f"expected <{ROOT}>")
    result: dict[str, Any] = {}
    for child in root:
        if child.tag == "metadata":
            result["metadata"] = _read_object(child)
        elif child.tag == NODE:
            result["node"] = _read_node(child)
    return result


def main() -> int:
    if len(sys.argv) < 3:
        print("Usage: json_to_figma_xml.py <input.json|input.xml> <output.xml|output.json>")
        return 1

    src = Path(sys.argv[1])
    dst = Path(sys.argv[2])
    text = src.read_text(encoding="utf-8")
    if src.suffix.lower() == ".xml":
        data = xml_to_json(text)
        dst.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    else:
        data = json.loads(text)
        xml = '<?xml version="1.0" encoding="utf-8"?>\n' + json_to_xml(data)
        dst.write_text(xml, encoding="utf-8")
    print(f"Wrote {dst}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
