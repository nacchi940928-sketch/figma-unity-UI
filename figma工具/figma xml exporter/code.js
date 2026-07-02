"use strict";
var _a;
const TOOL_ID = "3bc4284c-7b77-4024-a251-a0d8c39c3249";
const DISPLAY_NAME = "Full design exporter v2";
const ATTACH_KEY = TOOL_ID + ':state';
const DEFAULTS = { includeAssets: true };
let latestParams = DEFAULTS;
let isExecuting = false;
function normalizeParams(input) {
    const includeAssets = typeof (input === null || input === void 0 ? void 0 : input.includeAssets) === 'boolean' ? input.includeAssets : true;
    return { includeAssets };
}
function uniqueSceneNodes(nodes) {
    return [...new Set(nodes)].filter((n) => !n.removed);
}
function attachRelaunch(nodes) {
    const unique = uniqueSceneNodes(nodes);
    if (unique.length > 0) {
        for (const n of unique)
            n.setRelaunchData({ [TOOL_ID]: DISPLAY_NAME });
    }
    else {
        figma.root.setRelaunchData({ [TOOL_ID]: DISPLAY_NAME });
    }
}
function singleSelectedTarget() {
    var _a;
    const sel = figma.currentPage.selection;
    return sel.length === 1 ? ((_a = sel[0]) !== null && _a !== void 0 ? _a : null) : null;
}
function readAttachment(node) {
    var _a;
    try {
        const parsed = JSON.parse(node.getPluginData(ATTACH_KEY));
        if ((parsed === null || parsed === void 0 ? void 0 : parsed.version) !== 1)
            return null;
        return { version: 1, params: normalizeParams(parsed.params), state: ((_a = parsed.state) !== null && _a !== void 0 ? _a : null) };
    }
    catch (_b) {
        return null;
    }
}
function writeAttachment(node, params, state) {
    node.setPluginData(ATTACH_KEY, JSON.stringify({ version: 1, params, state }));
}
function slugify(name) {
    return name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'node';
}
function makeIrId(name, id) {
    return slugify(name) + '__' + id.replace(/[^a-z0-9]/gi, '_');
}
function absolutePosition(node) {
    const t = node.absoluteTransform;
    return { x: t[0][2], y: t[1][2] };
}
function toHex2(v) {
    return Math.round(Math.max(0, Math.min(1, v)) * 255).toString(16).padStart(2, '0');
}
function exportColor(color) {
    const base = '#' + toHex2(color.r) + toHex2(color.g) + toHex2(color.b);
    return 'a' in color && color.a < 1 ? base + toHex2(color.a) : base;
}
function mixedVal(value) {
    return typeof value === 'symbol' ? 'mixed' : value;
}
function generateImageFilename(nodeSlug, fillIdx, hash, usedFilenames) {
    const base = nodeSlug + (fillIdx > 0 ? '-' + String(fillIdx) : '');
    let filename = base + '.png';
    if (usedFilenames.has(filename)) {
        filename = base + '-' + hash.slice(0, 6) + '.png';
    }
    if (usedFilenames.has(filename)) {
        filename = 'img-' + hash.slice(0, 12) + '.png';
    }
    return filename;
}
function exportPaint(paint, imageMap, includeAssets, nodeSlug, fillIdx, usedFilenames, imageHashMeta) {
    var _a, _b, _c, _d, _e;
    if (!paint.visible)
        return { type: paint.type, visible: false };
    if (paint.type === 'SOLID') {
        return { type: 'SOLID', color: exportColor(paint.color), opacity: (_a = paint.opacity) !== null && _a !== void 0 ? _a : 1, blendMode: paint.blendMode };
    }
    if (paint.type === 'GRADIENT_LINEAR' || paint.type === 'GRADIENT_RADIAL' ||
        paint.type === 'GRADIENT_ANGULAR' || paint.type === 'GRADIENT_DIAMOND') {
        const t = paint.gradientTransform;
        return {
            type: paint.type,
            transform: { a: t[0][0], b: t[0][1], tx: t[0][2], c: t[1][0], d: t[1][1], ty: t[1][2] },
            stops: paint.gradientStops.map((s) => ({ position: s.position, color: exportColor(s.color) })),
            opacity: (_b = paint.opacity) !== null && _b !== void 0 ? _b : 1,
            blendMode: paint.blendMode,
        };
    }
    if (paint.type === 'IMAGE') {
        let imageFile = null;
        if (paint.imageHash) {
            if (imageMap[paint.imageHash]) {
                imageFile = imageMap[paint.imageHash].filename;
            }
            else {
                const meta = imageHashMeta[paint.imageHash];
                const filename = meta
                    ? meta.filename
                    : generateImageFilename(nodeSlug, fillIdx, paint.imageHash, usedFilenames);
                usedFilenames.add(filename);
                imageMap[paint.imageHash] = { filename, bytes: null };
                imageFile = filename;
            }
        }
        return {
            type: 'IMAGE',
            imageHash: (_c = paint.imageHash) !== null && _c !== void 0 ? _c : null,
            imageFile,
            scaleMode: paint.scaleMode,
            opacity: (_d = paint.opacity) !== null && _d !== void 0 ? _d : 1,
            blendMode: paint.blendMode,
        };
    }
    return { type: paint.type, opacity: (_e = paint.opacity) !== null && _e !== void 0 ? _e : 1 };
}
function exportStrokeProps(node) {
    var _a, _b, _c, _d, _e;
    const empty = {
        strokeWeight: null, strokeAlign: null, dashPattern: [],
        strokeCap: null, strokeJoin: null, strokeMiterLimit: null,
        strokeTopWeight: null, strokeRightWeight: null, strokeBottomWeight: null, strokeLeftWeight: null,
    };
    if (!('strokeWeight' in node))
        return empty;
    const strokeCap = 'strokeCap' in node
        ? (typeof node.strokeCap === 'symbol'
            ? 'mixed'
            : String(node.strokeCap))
        : null;
    const strokeJoin = 'strokeJoin' in node
        ? (typeof node.strokeJoin === 'symbol'
            ? 'mixed'
            : String(node.strokeJoin))
        : null;
    const strokeMiterLimit = 'strokeMiterLimit' in node
        ? ((_a = node.strokeMiterLimit) !== null && _a !== void 0 ? _a : null)
        : null;
    const sw = node;
    const strokeTopWeight = 'strokeTopWeight' in node ? ((_b = sw.strokeTopWeight) !== null && _b !== void 0 ? _b : null) : null;
    const strokeRightWeight = 'strokeRightWeight' in node ? ((_c = sw.strokeRightWeight) !== null && _c !== void 0 ? _c : null) : null;
    const strokeBottomWeight = 'strokeBottomWeight' in node ? ((_d = sw.strokeBottomWeight) !== null && _d !== void 0 ? _d : null) : null;
    const strokeLeftWeight = 'strokeLeftWeight' in node ? ((_e = sw.strokeLeftWeight) !== null && _e !== void 0 ? _e : null) : null;
    return {
        strokeWeight: typeof node.strokeWeight === 'number'
            ? node.strokeWeight
            : null,
        strokeAlign: 'strokeAlign' in node ? String(node.strokeAlign) : null,
        dashPattern: 'dashPattern' in node ? node.dashPattern : [],
        strokeCap,
        strokeJoin,
        strokeMiterLimit,
        strokeTopWeight,
        strokeRightWeight,
        strokeBottomWeight,
        strokeLeftWeight,
    };
}
function exportLayoutGrids(grids) {
    return grids.map((g) => {
        const base = {
            pattern: g.pattern,
            visible: g.visible,
            color: g.color ? exportColor(g.color) : null,
            sectionSize: g.sectionSize,
        };
        if (g.pattern === 'COLUMNS' || g.pattern === 'ROWS') {
            base.count = g.count;
            base.gutterSize = g.gutterSize;
            base.alignment = g.alignment;
            base.offset = g.offset;
        }
        return base;
    });
}
function exportTextSegments(node) {
    try {
        const segments = node.getStyledTextSegments([
            'fontName', 'fontSize', 'fontWeight', 'fills', 'letterSpacing',
            'lineHeight', 'textDecoration', 'textCase', 'hyperlink', 'indentation', 'listSpacing',
        ]);
        return segments.map((seg) => {
            var _a;
            const solidFill = seg.fills.find((p) => p.type === 'SOLID');
            return {
                text: seg.characters, start: seg.start, end: seg.end,
                fontFamily: seg.fontName.family, fontStyle: seg.fontName.style,
                fontSize: seg.fontSize, fontWeight: seg.fontWeight,
                color: solidFill ? exportColor(solidFill.color) : null,
                fills: seg.fills.map((p) => {
                    var _a;
                    if (p.type === 'SOLID') return { type: 'SOLID', color: exportColor(p.color), opacity: (_a = p.opacity) !== null && _a !== void 0 ? _a : 1 };
                    return { type: p.type };
                }),
                letterSpacing: seg.letterSpacing, lineHeight: seg.lineHeight,
                textDecoration: seg.textDecoration, textCase: seg.textCase,
                hyperlink: (_a = seg.hyperlink) !== null && _a !== void 0 ? _a : null,
                indentation: seg.indentation, listSpacing: seg.listSpacing,
            };
        });
    }
    catch (_a) {
        return [{ text: node.characters }];
    }
}
function detectNineSlice(node) {
    if (!('children' in node)) return null;
    const frame = node;
    if (frame.children.length !== 9) return null;
    const hashes = [];
    for (const child of frame.children) {
        if (!('fills' in child)) return null;
        const fills = child.fills;
        if (!Array.isArray(fills)) return null;
        const imgFill = fills.find((f) => f.type === 'IMAGE');
        if (!imgFill) return null;
        if (imgFill.imageHash) hashes.push(imgFill.imageHash);
    }
    const children = frame.children;
    const xs = [...new Set(children.map((c) => Math.round(c.x)))].sort((a, b) => a - b);
    const ys = [...new Set(children.map((c) => Math.round(c.y)))].sort((a, b) => a - b);
    if (xs.length !== 3 || ys.length !== 3) return null;
    for (const x of xs) {
        for (const y of ys) {
            if (!children.some((c) => Math.round(c.x) === x && Math.round(c.y) === y)) return null;
        }
    }
    const borderLeft = xs[1];
    const borderTop = ys[1];
    const borderRight = Math.round(node.width) - xs[2];
    const borderBottom = Math.round(node.height) - ys[2];
    if (borderLeft <= 0 || borderTop <= 0 || borderRight <= 0 || borderBottom <= 0) return null;
    const sharedImageHash = hashes.length === 9 && new Set(hashes).size === 1 ? hashes[0] : null;
    return { borderLeft, borderRight, borderTop, borderBottom, sharedImageHash };
}
function preScanPage(page) {
    const imageHashMeta = {};
    const hashToNineSlice = {};
    const nameToNineSlice = {};
    function collectFills(node, nameNode) {
        if (!('fills' in node) || !Array.isArray(node.fills)) return;
        for (const f of node.fills) {
            if (f.type !== 'IMAGE') continue;
            const ip = f;
            if (!ip.imageHash || ip.scaleMode === 'CROP') continue;
            if (!imageHashMeta[ip.imageHash]) {
                imageHashMeta[ip.imageHash] = {
                    filename: slugify(nameNode.name) + '.png',
                    width: Math.round(nameNode.width),
                    height: Math.round(nameNode.height),
                };
            }
        }
    }
    function visitPass1(node) {
        if (node.type === 'COMPONENT' && detectNineSlice(node)) return;
        const isContainer = node.type === 'FRAME' || node.type === 'COMPONENT' || node.type === 'COMPONENT_SET';
        if (isContainer) collectFills(node, node);
        if ('children' in node) for (const child of node.children) visitPass1(child);
    }
    function visitPass2(node) {
        if (node.type === 'COMPONENT') {
            const compNode = node;
            const ns = detectNineSlice(compNode);
            if (ns && Array.isArray(compNode.fills)) {
                const imageFills = compNode.fills.filter((f) => f.type === 'IMAGE' && !!f.imageHash);
                if (imageFills.length > 0) {
                    const primaryHash = imageFills[0].imageHash;
                    const meta = imageHashMeta[primaryHash];
                    const info = {
                        primaryHash,
                        primaryFilename: meta ? meta.filename : slugify(node.name) + '.png',
                        refWidth: meta ? meta.width : Math.round(node.width),
                        refHeight: meta ? meta.height : Math.round(node.height),
                        borders: { borderLeft: ns.borderLeft, borderRight: ns.borderRight, borderTop: ns.borderTop, borderBottom: ns.borderBottom },
                        componentName: node.name,
                    };
                    nameToNineSlice[node.name] = info;
                    for (const f of imageFills) {
                        if (f.imageHash && !hashToNineSlice[f.imageHash]) hashToNineSlice[f.imageHash] = info;
                    }
                }
            }
        }
        if ('children' in node) for (const child of node.children) visitPass2(child);
    }
    for (const top of page.children) visitPass1(top);
    for (const top of page.children) visitPass2(top);
    return { imageHashMeta, hashToNineSlice, nameToNineSlice };
}
async function registerNineSliceImage(ns, componentId, componentName, fallbackNode, ctx) {
    const filename = slugify(componentName) + '.png';
    if (ns.sharedImageHash) {
        const hash = ns.sharedImageHash;
        if (!ctx.maps.images[hash]) { ctx.usedFilenames.add(filename); ctx.maps.images[hash] = { filename, bytes: null }; }
        return ctx.maps.images[hash].filename;
    }
    const mapKey = '__nineSlice__' + componentId;
    if (!ctx.maps.images[mapKey]) {
        ctx.usedFilenames.add(filename);
        ctx.maps.images[mapKey] = { filename, bytes: null };
        if (ctx.includeAssets) {
            try {
                const bytes = await fallbackNode.exportAsync({ format: 'PNG', constraint: { type: 'SCALE', value: 1 } });
                ctx.maps.images[mapKey].bytes = bytes;
            } catch (_a) {}
        }
    }
    return ctx.maps.images[mapKey].filename;
}
function applyNineSliceInfo(result, info, ctx) {
    result.nineSlice = true;
    result.nineSliceImageFile = info.primaryFilename;
    result.nineSliceBorderLeft = info.borders.borderLeft;
    result.nineSliceBorderRight = info.borders.borderRight;
    result.nineSliceBorderTop = info.borders.borderTop;
    result.nineSliceBorderBottom = info.borders.borderBottom;
    result.nineSliceReferenceWidth = info.refWidth;
    result.nineSliceReferenceHeight = info.refHeight;
    if (!ctx.maps.images[info.primaryHash]) {
        ctx.usedFilenames.add(info.primaryFilename);
        ctx.maps.images[info.primaryHash] = { filename: info.primaryFilename, bytes: null };
    }
    result.fills = [{ type: 'IMAGE', imageHash: info.primaryHash, imageFile: info.primaryFilename, scaleMode: 'FILL', opacity: 1, blendMode: 'NORMAL' }];
}
async function exportNode(node, ctx, isRoot) {
    var _a, _b, _c, _d, _e, _f, _g;
    const abs = absolutePosition(node);
    const x = isRoot ? 0 : node.x;
    const y = isRoot ? 0 : node.y;
    const rootX = abs.x - ctx.rootAbsX;
    const rootY = abs.y - ctx.rootAbsY;
    const nodeSlug = slugify(node.name);
    const fills = 'fills' in node && Array.isArray(node.fills)
        ? node.fills.map((p, i) => exportPaint(p, ctx.maps.images, ctx.includeAssets, nodeSlug, i, ctx.usedFilenames, ctx.imageHashMeta))
        : [];
    const strokes = 'strokes' in node
        ? node.strokes.map((p, i) => exportPaint(p, ctx.maps.images, ctx.includeAssets, nodeSlug + '-stroke', i, ctx.usedFilenames, ctx.imageHashMeta))
        : [];
    const sp = exportStrokeProps(node);
    const effects = 'effects' in node
        ? node.effects.map((e) => {
            var _a;
            if (e.type === 'DROP_SHADOW' || e.type === 'INNER_SHADOW') {
                return { type: e.type, visible: e.visible, color: e.color ? exportColor(e.color) : null, offset: e.offset, radius: e.radius, spread: (_a = e.spread) !== null && _a !== void 0 ? _a : 0, blendMode: e.blendMode };
            }
            if (e.type === 'LAYER_BLUR' || e.type === 'BACKGROUND_BLUR') return { type: e.type, visible: e.visible, radius: e.radius };
            return null;
        }).filter((e) => e !== null)
        : [];
    let cornerRadius = 0;
    let rectangleCornerRadii;
    if ('cornerRadius' in node) {
        const cr = node.cornerRadius;
        if (typeof cr === 'symbol') {
            const rect = node;
            const tl = rect.topLeftRadius, tr = rect.topRightRadius, bl = rect.bottomLeftRadius, br = rect.bottomRightRadius;
            cornerRadius = Math.max(tl, tr, bl, br);
            rectangleCornerRadii = [tl, tr, br, bl];
        } else {
            cornerRadius = cr !== null && cr !== void 0 ? cr : 0;
        }
    }
    let layout = { layoutMode: 'NONE' };
    let layoutGrids;
    if (node.type === 'FRAME' || node.type === 'COMPONENT' || node.type === 'INSTANCE') {
        const f = node;
        const layoutObj = {
            layoutMode: f.layoutMode, primaryAxisAlignItems: f.primaryAxisAlignItems,
            counterAxisAlignItems: f.counterAxisAlignItems, primaryAxisSizingMode: f.primaryAxisSizingMode,
            counterAxisSizingMode: f.counterAxisSizingMode, paddingTop: f.paddingTop,
            paddingRight: f.paddingRight, paddingBottom: f.paddingBottom, paddingLeft: f.paddingLeft,
            itemSpacing: f.itemSpacing, clipsContent: f.clipsContent,
        };
        if (f.layoutMode !== 'NONE') {
            layoutObj.layoutWrap = f.layoutWrap;
            if (f.layoutWrap === 'WRAP') { layoutObj.counterAxisAlignContent = f.counterAxisAlignContent; layoutObj.counterAxisSpacing = f.counterAxisSpacing; }
            if ('strokesIncludedInLayout' in f) layoutObj.strokesIncludedInLayout = f.strokesIncludedInLayout;
        }
        layout = layoutObj;
        if (f.layoutGrids && f.layoutGrids.length > 0) layoutGrids = exportLayoutGrids(f.layoutGrids);
    }
    const blendMode = 'blendMode' in node ? String(node.blendMode) : 'NORMAL';
    const isMask = 'isMask' in node ? node.isMask : false;
    const maskType = 'maskType' in node ? String(node.maskType) : 'ALPHA';
    const locked = 'locked' in node ? node.locked : false;
    let absoluteRenderBounds = null;
    if ('absoluteRenderBounds' in node) {
        const b = node.absoluteRenderBounds;
        absoluteRenderBounds = b ? { x: b.x, y: b.y, width: b.width, height: b.height } : null;
    }
    const result = {
        id: node.id, irId: makeIrId(node.name, node.id), name: node.name, type: node.type,
        visible: node.visible, locked, x, y, rootX, rootY, width: node.width, height: node.height,
        rotation: 'rotation' in node ? ((_a = node.rotation) !== null && _a !== void 0 ? _a : 0) : 0,
        opacity: 'opacity' in node ? ((_b = node.opacity) !== null && _b !== void 0 ? _b : 1) : 1,
        blendMode, isMask, maskType, absoluteRenderBounds, fills, strokes,
        strokeWeight: sp.strokeWeight, strokeAlign: sp.strokeAlign, dashPattern: sp.dashPattern,
        strokeCap: sp.strokeCap, strokeJoin: sp.strokeJoin, strokeMiterLimit: sp.strokeMiterLimit,
        strokeTopWeight: sp.strokeTopWeight, strokeRightWeight: sp.strokeRightWeight,
        strokeBottomWeight: sp.strokeBottomWeight, strokeLeftWeight: sp.strokeLeftWeight,
        effects, cornerRadius, layout, children: [],
    };
    if (rectangleCornerRadii) result.rectangleCornerRadii = rectangleCornerRadii;
    if (layoutGrids) result.layoutGrids = layoutGrids;
    if ('constraints' in node) result.constraints = { horizontal: node.constraints.horizontal, vertical: node.constraints.vertical };
    if ('layoutSizingHorizontal' in node) result.layoutSizingHorizontal = node.layoutSizingHorizontal;
    if ('layoutSizingVertical' in node) result.layoutSizingVertical = node.layoutSizingVertical;
    if ('layoutPositioning' in node) result.layoutPositioning = node.layoutPositioning;
    if (node.type === 'TEXT') {
        const t = node;
        result.characters = t.characters;
        result.fontFamily = t.fontName !== figma.mixed ? t.fontName.family : 'mixed';
        result.fontStyle = t.fontName !== figma.mixed ? t.fontName.style : 'mixed';
        result.fontSize = t.fontSize !== figma.mixed ? t.fontSize : 'mixed';
        result.fontWeight = t.fontWeight !== figma.mixed ? t.fontWeight : 'mixed';
        result.letterSpacing = mixedVal(t.letterSpacing);
        result.lineHeight = mixedVal(t.lineHeight);
        result.paragraphSpacing = mixedVal(t.paragraphSpacing);
        if ('paragraphIndent' in t) result.paragraphIndent = mixedVal(t.paragraphIndent);
        result.textDecoration = String(mixedVal(t.textDecoration));
        result.textCase = String(mixedVal(t.textCase));
        result.textAutoResize = t.textAutoResize;
        result.maxLines = t.maxLines;
        if ('listSpacing' in t) result.listSpacing = mixedVal(t.listSpacing);
        result.textAlignHorizontal = t.textAlignHorizontal;
        result.textAlignVertical = t.textAlignVertical;
        result.segments = exportTextSegments(t);
    }
    if (node.type === 'COMPONENT') {
        const comp = node;
        result.componentPropertyDefinitions = comp.componentPropertyDefinitions;
        if ('variantProperties' in comp) result.variantProperties = comp.variantProperties;
    }
    if (node.type === 'COMPONENT_SET') result.componentPropertyDefinitions = node.componentPropertyDefinitions;
    if ('children' in node) result.children = await Promise.all(node.children.map((child) => exportNode(child, ctx, false)));
    const ns = detectNineSlice(node);
    if (node.type === 'INSTANCE') {
        const inst = node;
        result.componentProperties = inst.componentProperties;
        const mc = await inst.getMainComponentAsync();
        result.mainComponentId = (_c = mc === null || mc === void 0 ? void 0 : mc.id) !== null && _c !== void 0 ? _c : null;
        const mcNs = mc ? detectNineSlice(mc) : null;
        const effectiveNs = ns !== null && ns !== void 0 ? ns : mcNs;
        if (effectiveNs) {
            for (const child of result.children) child.nineSlicePart = true;
            const mcName = (_d = mc === null || mc === void 0 ? void 0 : mc.name) !== null && _d !== void 0 ? _d : node.name;
            const info = ctx.nameToNineSlice[mcName];
            if (info) {
                applyNineSliceInfo(result, info, ctx);
            } else {
                const srcNs = mc ? ((_e = detectNineSlice(mc)) !== null && _e !== void 0 ? _e : effectiveNs) : effectiveNs;
                const srcId = (_f = mc === null || mc === void 0 ? void 0 : mc.id) !== null && _f !== void 0 ? _f : node.id;
                const srcName = (_g = mc === null || mc === void 0 ? void 0 : mc.name) !== null && _g !== void 0 ? _g : node.name;
                const srcNode = (mc !== null && mc !== void 0 ? mc : node);
                result.nineSlice = true;
                result.nineSliceBorderLeft = effectiveNs.borderLeft;
                result.nineSliceBorderRight = effectiveNs.borderRight;
                result.nineSliceBorderTop = effectiveNs.borderTop;
                result.nineSliceBorderBottom = effectiveNs.borderBottom;
                result.nineSliceReferenceWidth = mc ? Math.round(mc.width) : Math.round(node.width);
                result.nineSliceReferenceHeight = mc ? Math.round(mc.height) : Math.round(node.height);
                const imageFile = await registerNineSliceImage(srcNs, srcId, srcName, srcNode, ctx);
                result.nineSliceImageFile = imageFile;
                result.fills = [{ type: 'IMAGE', imageHash: null, imageFile, scaleMode: 'FILL', opacity: 1, blendMode: 'NORMAL' }];
            }
        }
    } else if (ns) {
        for (const child of result.children) child.nineSlicePart = true;
        const info = ctx.nameToNineSlice[node.name];
        if (info) {
            applyNineSliceInfo(result, info, ctx);
        } else {
            result.nineSlice = true;
            result.nineSliceBorderLeft = ns.borderLeft;
            result.nineSliceBorderRight = ns.borderRight;
            result.nineSliceBorderTop = ns.borderTop;
            result.nineSliceBorderBottom = ns.borderBottom;
            result.nineSliceReferenceWidth = Math.round(node.width);
            result.nineSliceReferenceHeight = Math.round(node.height);
            const imageFile = await registerNineSliceImage(ns, node.id, node.name, node, ctx);
            result.nineSliceImageFile = imageFile;
            result.fills = [{ type: 'IMAGE', imageHash: null, imageFile, scaleMode: 'FILL', opacity: 1, blendMode: 'NORMAL' }];
        }
    } else {
        let inheritedInfo = null;
        if ('fills' in node && Array.isArray(node.fills)) {
            for (const f of node.fills) {
                if (f.type === 'IMAGE') {
                    const hash = f.imageHash;
                    if (hash && ctx.hashToNineSlice[hash]) { inheritedInfo = ctx.hashToNineSlice[hash]; break; }
                }
            }
        }
        if (!inheritedInfo && ctx.nameToNineSlice[node.name]) inheritedInfo = ctx.nameToNineSlice[node.name];
        if (inheritedInfo) applyNineSliceInfo(result, inheritedInfo, ctx);
    }
    return result;
}
function countNodes(node) {
    let count = 1;
    if ('children' in node) for (const child of node.children) count += countNodes(child);
    return count;
}
function serializeReplacer(_key, value) {
    if (typeof value === 'symbol') return 'mixed';
    return value;
}
function bytesToBase64(bytes) {
    let bin = '';
    const len = bytes.length;
    for (let i = 0; i < len; i++) bin += String.fromCharCode(bytes[i]);
    return btoa(bin);
}
async function collectImageBytes(imageMap) {
    for (const [hash, entry] of Object.entries(imageMap)) {
        if (hash.startsWith('__nineSlice__') || entry.bytes !== null) continue;
        try {
            const img = figma.getImageByHash(hash);
            if (img) entry.bytes = await img.getBytesAsync();
        } catch (_a) {}
    }
}
function escapeXml(s) {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&apos;');
}
function xmlTag(key) {
    const safe = key.replace(/[^a-zA-Z0-9_\-.]/g, '_');
    return /^[0-9\-.]/.test(safe) ? '_' + safe : safe;
}
function singularize(key) {
    if (key === 'children') return 'child';
    if (key.endsWith('ies') && key.length > 3) return key.slice(0, -3) + 'y';
    if (key.endsWith('s') && key.length > 1) return key.slice(0, -1);
    return 'item';
}
function valToXml(key, value, indent) {
    const tag = xmlTag(key);
    if (value === null || value === undefined) return indent + '<' + tag + ' xsi:nil="true"/>\n';
    if (typeof value === 'boolean' || typeof value === 'number') return indent + '<' + tag + '>' + String(value) + '</' + tag + '>\n';
    if (typeof value === 'string') return indent + '<' + tag + '>' + escapeXml(value) + '</' + tag + '>\n';
    if (Array.isArray(value)) {
        if (value.length === 0) return indent + '<' + tag + '/>\n';
        const allPrimitive = value.every((v) => v === null || typeof v !== 'object');
        if (allPrimitive) {
            const joined = value.map((v) => (v === null ? 'null' : String(v))).join(', ');
            return indent + '<' + tag + '>' + escapeXml(joined) + '</' + tag + '>\n';
        }
        const childTag = singularize(tag);
        let out = indent + '<' + tag + '>\n';
        for (const item of value) out += valToXml(childTag, item, indent + '  ');
        out += indent + '</' + tag + '>\n';
        return out;
    }
    if (typeof value === 'object') {
        let out = indent + '<' + tag + '>\n';
        for (const [k, v] of Object.entries(value)) out += valToXml(k, v, indent + '  ');
        out += indent + '</' + tag + '>\n';
        return out;
    }
    return indent + '<' + tag + '>' + escapeXml(String(value)) + '</' + tag + '>\n';
}
function objectToXml(obj) {
    let xml = '<?xml version="1.0" encoding="UTF-8"?>\n';
    xml += '<design-export xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">\n';
    for (const [k, v] of Object.entries(obj)) xml += valToXml(k, v, '  ');
    xml += '</design-export>\n';
    return xml;
}
async function buildExport(params, target, format) {
    const maps = { images: {}, vectors: {} };
    const rootAbs = absolutePosition(target);
    const { imageHashMeta, hashToNineSlice, nameToNineSlice } = preScanPage(figma.currentPage);
    const ctx = {
        maps, rootAbsX: rootAbs.x, rootAbsY: rootAbs.y,
        includeAssets: params.includeAssets, usedFilenames: new Set(),
        imageHashMeta, hashToNineSlice, nameToNineSlice,
    };
    const exported = await exportNode(target, ctx, true);
    const totalElements = countNodes(target);
    const slug = slugify(target.name);
    const output = {
        metadata: {
            exportedAt: new Date().toISOString(), componentName: target.name,
            rootIrId: exported.irId, totalElements, rootNormalized: true,
            plugin: 'Full design exporter v2 v4 (nineSlice:shared-source)',
            artAssetConvention: { lookup: 'filename' },
        },
        node: exported,
    };
    let content, filename;
    if (format === 'xml') {
        const sanitized = JSON.parse(JSON.stringify(output, serializeReplacer));
        content = objectToXml(sanitized);
        filename = slug + '-full.xml';
    } else {
        content = JSON.stringify(output, serializeReplacer, 2);
        filename = slug + '-full.json';
    }
    return { content, filename, maps, totalElements, slug };
}
async function sendExport(result, params, format) {
    const imageEntries = Object.values(result.maps.images);
    const hasImages = params.includeAssets && imageEntries.length > 0;
    if (hasImages) {
        await collectImageBytes(result.maps.images);
        const imageFiles = imageEntries.filter((e) => e.bytes !== null).map((e) => ({ name: e.filename, data: bytesToBase64(e.bytes) }));
        const zipName = result.slug + '-export.zip';
        figma.ui.postMessage({ type: 'download-zip', zipName, jsonFile: { name: result.filename, content: result.content }, imageFiles });
        const label = format === 'xml' ? 'XML' : 'JSON';
        figma.notify('Exported ' + result.totalElements + ' nodes + ' + imageFiles.length + ' image(s) → ZIP (' + label + ')');
    } else {
        figma.ui.postMessage({ type: 'download', filename: result.filename, content: result.content });
        const label = format === 'xml' ? 'XML' : 'JSON';
        figma.notify('Exported ' + result.totalElements + ' nodes → ' + label);
    }
}
function evaluateEnabled_exportJson(selection) { return selection.length === 1; }
function actionTarget_exportJson() {
    const target = singleSelectedTarget();
    if (target == null) return null;
    return evaluateEnabled_exportJson([target]) ? target : null;
}
async function action_exportJson(params, target) {
    figma.notify('Collecting data…', { timeout: 60000 });
    const result = await buildExport(params, target, 'json');
    await sendExport(result, params, 'json');
    return { affectedNodes: [target], state: null };
}
async function runAction_exportJson(target) {
    if (target == null) return;
    isExecuting = true;
    try {
        const result = await action_exportJson(latestParams, target);
        writeAttachment(target, latestParams, result.state);
        attachRelaunch(result.affectedNodes);
        pushActionStates();
    } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        figma.notify(message, { error: true });
        throw error;
    } finally { isExecuting = false; }
}
function evaluateEnabled_exportXml(selection) { return selection.length === 1; }
function actionTarget_exportXml() {
    const target = singleSelectedTarget();
    if (target == null) return null;
    return evaluateEnabled_exportXml([target]) ? target : null;
}
async function action_exportXml(params, target) {
    figma.notify('Collecting data…', { timeout: 60000 });
    const result = await buildExport(params, target, 'xml');
    await sendExport(result, params, 'xml');
    return { affectedNodes: [target], state: null };
}
async function runAction_exportXml(target) {
    if (target == null) return;
    isExecuting = true;
    try {
        const result = await action_exportXml(latestParams, target);
        writeAttachment(target, latestParams, result.state);
        attachRelaunch(result.affectedNodes);
        pushActionStates();
    } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        figma.notify(message, { error: true });
        throw error;
    } finally { isExecuting = false; }
}
function pushActionStates() {
    const enabled = actionTarget_exportJson() != null;
    figma.ui.postMessage({ type: 'action-state', actions: { exportJson: { enabled, label: 'Export JSON', status: undefined }, exportXml: { enabled, label: 'Export XML', status: undefined } } });
}
function refreshSelection() {
    var _a;
    if (isExecuting) return;
    const target = singleSelectedTarget();
    const attachment = target != null ? readAttachment(target) : null;
    latestParams = (_a = attachment === null || attachment === void 0 ? void 0 : attachment.params) !== null && _a !== void 0 ? _a : DEFAULTS;
    figma.ui.postMessage({ type: 'params-change', params: latestParams });
    pushActionStates();
}
const initialTarget = singleSelectedTarget();
const initialAttachment = initialTarget != null ? readAttachment(initialTarget) : null;
const initialParams = (_a = initialAttachment === null || initialAttachment === void 0 ? void 0 : initialAttachment.params) !== null && _a !== void 0 ? _a : DEFAULTS;
latestParams = initialParams;
const html = __html__;
figma.root.setRelaunchData({ [TOOL_ID]: DISPLAY_NAME });
figma.showUI(html, { width: 280, height: 320 });
pushActionStates();
figma.on('selectionchange', refreshSelection);
figma.ui.onmessage = (msg) => {
    if (msg.type === 'resize') { figma.ui.resize(280, Math.max(120, Math.min(900, Math.round(msg.height)))); return; }
    if (msg.type === 'action' && msg.id === 'exportJson') {
        const target = actionTarget_exportJson();
        if (target == null) return;
        latestParams = normalizeParams(msg.params);
        void runAction_exportJson(target);
    }
    if (msg.type === 'action' && msg.id === 'exportXml') {
        const target = actionTarget_exportXml();
        if (target == null) return;
        latestParams = normalizeParams(msg.params);
        void runAction_exportXml(target);
    }
};
