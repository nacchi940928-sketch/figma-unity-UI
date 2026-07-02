"use strict";
const TOOL_ID = "3dbe66a5-8717-4dc6-9f83-5b27f3a4889a";
const DISPLAY_NAME = "Unity JSON/XML importer  v2";
const UNITY_PATH_KEY = TOOL_ID + ':unity-path';
const NINE_SLICE_KEY = TOOL_ID + ':nine-slice';
const DEFAULTS = { jsonFile: null, xmlFile: null, syncPosition: true, syncVisibility: true, syncText: true, syncFills: true, syncImages: true, syncNineSlice: true, imageAssets: [], exportFormat: 'json' };
let latestParams = DEFAULTS;
let isExecuting = false;
// Image asset maps: exact filename and lowercase for case-insensitive fallback
let _currentImageMap = new Map();
let _currentImageMapLower = new Map();
function resolveImageBytes(imageFile) {
    var _a;
    const name = imageFile.trim();
    if (!name)
        return null;
    const exact = _currentImageMap.get(name);
    if (exact)
        return exact;
    return (_a = _currentImageMapLower.get(name.toLowerCase())) !== null && _a !== void 0 ? _a : null;
}
function normalizeParams(input) {
    var _a, _b;
    const value = input !== null && input !== void 0 ? input : {};
    return {
        jsonFile: (_a = value.jsonFile) !== null && _a !== void 0 ? _a : DEFAULTS.jsonFile,
        xmlFile: (_b = value.xmlFile) !== null && _b !== void 0 ? _b : DEFAULTS.xmlFile,
        syncPosition: typeof value.syncPosition === 'boolean' ? value.syncPosition : DEFAULTS.syncPosition,
        syncVisibility: typeof value.syncVisibility === 'boolean' ? value.syncVisibility : DEFAULTS.syncVisibility,
        syncText: typeof value.syncText === 'boolean' ? value.syncText : DEFAULTS.syncText,
        syncFills: typeof value.syncFills === 'boolean' ? value.syncFills : DEFAULTS.syncFills,
        syncImages: typeof value.syncImages === 'boolean' ? value.syncImages : DEFAULTS.syncImages,
        syncNineSlice: typeof value.syncNineSlice === 'boolean' ? value.syncNineSlice : DEFAULTS.syncNineSlice,
        imageAssets: Array.isArray(value.imageAssets) ? value.imageAssets : DEFAULTS.imageAssets,
        exportFormat: value.exportFormat === 'xml' ? 'xml' : 'json',
    };
}
function setBooleanControl(html, id, value) {
    const escaped = id.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const checked = new RegExp('(<fig-switch[^>]*id="' + escaped + '"[^>]*)\\schecked(=[^\\s>]*)?', 'g');
    let next = html.replace(checked, '$1');
    if (value) {
        const target = new RegExp('(<fig-switch[^>]*id="' + escaped + '"[^>]*)(>)', 'g');
        next = next.replace(target, '$1 checked$2');
    }
    return next;
}
// ─── Font helpers ─────────────────────────────────────────────────────────────
let _fontCache = new Map();
function _clearFontCache() { _fontCache = new Map(); }
async function loadFontWithFallback(family, style) {
    const cacheKey = family + '|' + style;
    if (_fontCache.has(cacheKey))
        return _fontCache.get(cacheKey);
    const stylesToTry = [style, 'Regular'].filter((s, i, a) => a.indexOf(s) === i);
    const familiesToTry = [family];
    const stripped = family
        .replace(/ SDF$/i, '').replace(/ \(TMP_Font Asset\)$/i, '').replace(/ TMP$/i, '').replace(/ Atlas$/i, '').trim();
    if (stripped !== family)
        familiesToTry.push(stripped);
    for (const fam of familiesToTry) {
        for (const sty of stylesToTry) {
            try {
                await figma.loadFontAsync({ family: fam, style: sty });
                const result = { family: fam, style: sty };
                _fontCache.set(cacheKey, result);
                return result;
            }
            catch (_a) { }
        }
    }
    _fontCache.set(cacheKey, null);
    return null;
}
function normalizeHAlign(raw) {
    const up = raw.toUpperCase();
    if (up === 'LEFT') return 'LEFT';
    if (up === 'RIGHT') return 'RIGHT';
    if (up === 'CENTER' || up === 'CENTRE') return 'CENTER';
    if (up === 'JUSTIFIED' || up === 'JUSTIFY') return 'JUSTIFIED';
    if (/CENTER|CENTRE/.test(up)) return 'CENTER';
    if (/JUSTIFY|FLUSH/.test(up)) return 'JUSTIFIED';
    if (/RIGHT/.test(up)) return 'RIGHT';
    if (/LEFT/.test(up)) return 'LEFT';
    return null;
}
function normalizeVAlign(raw) {
    const up = raw.toUpperCase();
    if (up === 'TOP') return 'TOP';
    if (up === 'CENTER' || up === 'CENTRE' || up === 'MIDDLE' || up === 'MID') return 'CENTER';
    if (up === 'BOTTOM') return 'BOTTOM';
    if (/TOP/.test(up)) return 'TOP';
    if (/BOTTOM/.test(up)) return 'BOTTOM';
    if (/MID|CENTER|CENTRE/.test(up)) return 'CENTER';
    return null;
}
// ─── Color helpers ────────────────────────────────────────────────────────────
function hexToRgb(hex) {
    const h = hex.replace('#', '');
    if (h.length < 6) return null;
    const r = parseInt(h.slice(0, 2), 16), g = parseInt(h.slice(2, 4), 16), b = parseInt(h.slice(4, 6), 16);
    if (isNaN(r) || isNaN(g) || isNaN(b)) return null;
    return { r: r / 255, g: g / 255, b: b / 255 };
}
function hexToRgba(hex) {
    const h = hex.replace('#', '');
    if (h.length < 6) return null;
    const r = parseInt(h.slice(0, 2), 16) / 255, g = parseInt(h.slice(2, 4), 16) / 255;
    const b = parseInt(h.slice(4, 6), 16) / 255, a = h.length >= 8 ? parseInt(h.slice(6, 8), 16) / 255 : 1;
    if (isNaN(r) || isNaN(g) || isNaN(b) || isNaN(a)) return null;
    return { r, g, b, a };
}
function rgbToHex(r, g, b) {
    const toHex = (v) => Math.round(Math.max(0, Math.min(255, v * 255))).toString(16).padStart(2, '0');
    return '#' + toHex(r) + toHex(g) + toHex(b);
}
function rgbaToHex(r, g, b, a) {
    const toHex = (v) => Math.round(Math.max(0, Math.min(255, v * 255))).toString(16).padStart(2, '0');
    return '#' + toHex(r) + toHex(g) + toHex(b) + toHex(a);
}
// ─── Paint / fill helpers ─────────────────────────────────────────────────────
function buildPaintFromJson(jf) {
    var _a, _b, _c, _d, _e, _f, _g, _h;
    const type = String((_a = jf.type) !== null && _a !== void 0 ? _a : '');
    const visible = jf.visible !== false;
    if (type === 'SOLID') {
        const rgb = hexToRgb(String((_b = jf.color) !== null && _b !== void 0 ? _b : ''));
        if (!rgb) return null;
        const opacity = typeof jf.opacity === 'number' ? Math.max(0, Math.min(1, jf.opacity)) : 1;
        return { type: 'SOLID', color: rgb, opacity, visible, blendMode: 'NORMAL' };
    }
    const gradTypes = ['GRADIENT_LINEAR', 'GRADIENT_RADIAL', 'GRADIENT_ANGULAR', 'GRADIENT_DIAMOND'];
    if (gradTypes.includes(type)) {
        if (!jf.transform || typeof jf.transform !== 'object') return null;
        const t = jf.transform;
        const gradientTransform = [[(_c = t.a) !== null && _c !== void 0 ? _c : 1, (_d = t.b) !== null && _d !== void 0 ? _d : 0, (_e = t.tx) !== null && _e !== void 0 ? _e : 0], [(_f = t.c) !== null && _f !== void 0 ? _f : 0, (_g = t.d) !== null && _g !== void 0 ? _g : 1, (_h = t.ty) !== null && _h !== void 0 ? _h : 0]];
        if (!Array.isArray(jf.stops)) return null;
        const gradientStops = jf.stops.map((s) => {
            var _a, _b;
            const rgba = (_b = hexToRgba(String((_a = s.color) !== null && _a !== void 0 ? _a : ''))) !== null && _b !== void 0 ? _b : { r: 0, g: 0, b: 0, a: 1 };
            return { position: typeof s.position === 'number' ? s.position : 0, color: rgba };
        });
        const opacity = typeof jf.opacity === 'number' ? Math.max(0, Math.min(1, jf.opacity)) : 1;
        return { type: type, gradientTransform, gradientStops, opacity, visible, blendMode: 'NORMAL' };
    }
    if (type === 'IMAGE') {
        const hash = typeof jf.imageHash === 'string' && jf.imageHash ? jf.imageHash : null;
        if (!hash) return null;
        const opacity = typeof jf.opacity === 'number' ? Math.max(0, Math.min(1, jf.opacity)) : 1;
        const scaleMode = (typeof jf.scaleMode === 'string' ? jf.scaleMode : 'FILL');
        return { type: 'IMAGE', imageHash: hash, scaleMode, opacity, visible, blendMode: 'NORMAL' };
    }
    return null;
}
function applyFillsFromJson(scene, jFills, syncImages, stats) {
    if (!('fills' in scene)) return false;
    const rawFills = scene.fills;
    if (rawFills === figma.mixed) return false;
    const newFills = [];
    let hasImageWithNoHash = false;
    for (const jf of jFills) {
        if (jf.type === 'IMAGE') {
            const imageFile = typeof jf.imageFile === 'string' && jf.imageFile ? jf.imageFile : null;
            const hash = typeof jf.imageHash === 'string' && jf.imageHash ? jf.imageHash : null;
            if (syncImages && imageFile) {
                const bytes = resolveImageBytes(imageFile);
                if (bytes) {
                    try {
                        const imgRef = figma.createImage(bytes);
                        const scaleMode = (typeof jf.scaleMode === 'string' ? jf.scaleMode : 'FILL');
                        const opacity = typeof jf.opacity === 'number' ? Math.max(0, Math.min(1, jf.opacity)) : 1;
                        const visible = jf.visible !== false;
                        newFills.push({ type: 'IMAGE', imageHash: imgRef.hash, scaleMode, opacity, visible, blendMode: 'NORMAL' });
                        stats.imagesUpdated++;
                    }
                    catch (_a) {
                        stats.missingFileNames.push(imageFile);
                        if (hash) { const p = buildPaintFromJson(jf); if (p) newFills.push(p); }
                    }
                } else {
                    stats.missingFileNames.push(imageFile);
                    if (hash) { const p = buildPaintFromJson(jf); if (p) newFills.push(p); }
                }
                continue;
            }
            if (!hash) { hasImageWithNoHash = true; continue; }
            const p = buildPaintFromJson(jf);
            if (p) newFills.push(p);
            continue;
        }
        const p = buildPaintFromJson(jf);
        if (p) newFills.push(p);
    }
    if (newFills.length === 0 && hasImageWithNoHash) return false;
    if (newFills.length === 0) return false;
    scene.fills = newFills;
    return true;
}
// ─── Property application ─────────────────────────────────────────────────────
async function applyNodeProperties(scene, jn, p, applyPosition, isNew, stats) {
    var _a, _b;
    let changed = false;
    const doLayout = isNew || p.syncPosition;
    const doVisibility = isNew || p.syncVisibility;
    const doText = isNew || p.syncText;
    const doFills = isNew || p.syncFills;
    if (typeof jn.name === 'string' && scene.name !== jn.name) { scene.name = jn.name; changed = true; }
    if (doLayout && typeof jn.layoutPositioning === 'string' && 'layoutPositioning' in scene) {
        try { scene.layoutPositioning = jn.layoutPositioning; changed = true; } catch (_c) { }
    }
    if (applyPosition) {
        const sx = typeof jn.scaleX === 'number' && jn.scaleX > 0 ? jn.scaleX : 1;
        const sy = typeof jn.scaleY === 'number' && jn.scaleY > 0 ? jn.scaleY : 1;
        if (typeof jn.x === 'number' && 'x' in scene && Math.abs(scene.x - jn.x) > 0.5) { scene.x = jn.x; changed = true; }
        if (typeof jn.y === 'number' && 'y' in scene && Math.abs(scene.y - jn.y) > 0.5) { scene.y = jn.y; changed = true; }
        if ('resize' in scene) {
            const jw = typeof jn.width === 'number' ? jn.width : -1;
            const jh = typeof jn.height === 'number' ? jn.height : -1;
            const w = jw > 0 ? Math.round(jw * sx * 100) / 100 : scene.width;
            const h = jh > 0 ? Math.round(jh * sy * 100) / 100 : scene.height;
            if (Math.abs(w - scene.width) > 0.5 || Math.abs(h - scene.height) > 0.5) { scene.resize(Math.max(1, w), Math.max(1, h)); changed = true; }
        }
        if (typeof jn.rotation === 'number' && 'rotation' in scene) {
            if (Math.abs(scene.rotation - jn.rotation) > 0.01) {
                try { scene.rotation = jn.rotation; changed = true; } catch (_d) { }
            }
        }
    }
    if (doLayout && 'cornerRadius' in scene) {
        if (Array.isArray(jn.rectangleCornerRadii) && jn.rectangleCornerRadii.length === 4) {
            const [tl, tr, br, bl] = jn.rectangleCornerRadii;
            try { scene.topLeftRadius = tl; scene.topRightRadius = tr; scene.bottomRightRadius = br; scene.bottomLeftRadius = bl; changed = true; } catch (_e) { }
        } else if (typeof jn.cornerRadius === 'number' && jn.cornerRadius >= 0) {
            try { scene.cornerRadius = jn.cornerRadius; changed = true; } catch (_f) { }
        }
    }
    if (doLayout && 'constraints' in scene && jn.constraints && typeof jn.constraints === 'object') {
        const jc = jn.constraints;
        if (typeof jc.horizontal === 'string' && typeof jc.vertical === 'string') {
            try { scene.constraints = { horizontal: jc.horizontal, vertical: jc.vertical }; changed = true; } catch (_g) { }
        }
    }
    if (doLayout && jn.layout && typeof jn.layout === 'object') {
        const jl = jn.layout;
        if (scene.type === 'FRAME' || scene.type === 'COMPONENT' || scene.type === 'INSTANCE') {
            const f = scene;
            if (typeof jl.layoutMode === 'string') {
                if (jl.layoutMode !== 'NONE') {
                    try {
                        f.layoutMode = jl.layoutMode;
                        if (typeof jl.paddingTop === 'number') f.paddingTop = jl.paddingTop;
                        if (typeof jl.paddingRight === 'number') f.paddingRight = jl.paddingRight;
                        if (typeof jl.paddingBottom === 'number') f.paddingBottom = jl.paddingBottom;
                        if (typeof jl.paddingLeft === 'number') f.paddingLeft = jl.paddingLeft;
                        if (typeof jl.itemSpacing === 'number') f.itemSpacing = jl.itemSpacing;
                        if (typeof jl.primaryAxisAlignItems === 'string') f.primaryAxisAlignItems = jl.primaryAxisAlignItems;
                        if (typeof jl.counterAxisAlignItems === 'string') f.counterAxisAlignItems = jl.counterAxisAlignItems;
                        changed = true;
                    } catch (_h) { }
                } else if (f.layoutMode !== 'NONE') {
                    try { f.layoutMode = 'NONE'; changed = true; } catch (_j) { }
                }
            }
            if (typeof jl.clipsContent === 'boolean') { try { f.clipsContent = jl.clipsContent; changed = true; } catch (_k) { } }
        }
    }
    if (doLayout && typeof jn.layoutSizingHorizontal === 'string' && 'layoutSizingHorizontal' in scene) {
        try { scene.layoutSizingHorizontal = jn.layoutSizingHorizontal; changed = true; } catch (_l) { }
    }
    if (doLayout && typeof jn.layoutSizingVertical === 'string' && 'layoutSizingVertical' in scene) {
        try { scene.layoutSizingVertical = jn.layoutSizingVertical; changed = true; } catch (_m) { }
    }
    if (doVisibility) {
        if (typeof jn.visible === 'boolean' && scene.visible !== jn.visible) { scene.visible = jn.visible; changed = true; }
        if (typeof jn.opacity === 'number' && 'opacity' in scene) {
            const op = Math.max(0, Math.min(1, jn.opacity));
            if (Math.abs(scene.opacity - op) > 0.001) { scene.opacity = op; changed = true; }
        }
    }
    if (doText && scene.type === 'TEXT') {
        const txt = scene;
        const segments = jn.segments;
        if (Array.isArray(segments) && segments.length > 0) {
            const loadedFonts = new Map();
            const segFonts = [];
            if (txt.fontName !== figma.mixed) { const fn = txt.fontName; segFonts.push({ family: fn.family, style: fn.style }); }
            for (const seg of segments) {
                if (typeof seg.fontFamily === 'string') segFonts.push({ family: seg.fontFamily, style: typeof seg.fontStyle === 'string' ? seg.fontStyle : 'Regular' });
            }
            for (const { family, style } of segFonts) {
                const key = family + '|' + style;
                if (!loadedFonts.has(key)) {
                    const loaded = await loadFontWithFallback(family, style);
                    if (loaded) loadedFonts.set(key, loaded);
                    else stats.errors.add('Font not found: ' + family);
                }
            }
            const fullText = segments.map((s) => { var _a; return String((_a = s.text) !== null && _a !== void 0 ? _a : ''); }).join('');
            if (txt.characters !== fullText) { txt.characters = fullText; changed = true; }
            let offset = 0;
            for (const seg of segments) {
                const segText = String((_a = seg.text) !== null && _a !== void 0 ? _a : '');
                const len = segText.length;
                if (len > 0) {
                    try {
                        if (typeof seg.fontFamily === 'string') {
                            const style = typeof seg.fontStyle === 'string' ? seg.fontStyle : 'Regular';
                            const resolved = loadedFonts.get(seg.fontFamily + '|' + style);
                            if (resolved) txt.setRangeFontName(offset, offset + len, resolved);
                        }
                        if (typeof seg.fontSize === 'number') txt.setRangeFontSize(offset, offset + len, seg.fontSize);
                        if (typeof seg.color === 'string') {
                            const rgba = hexToRgba(seg.color);
                            if (rgba) txt.setRangeFills(offset, offset + len, [{ type: 'SOLID', color: { r: rgba.r, g: rgba.g, b: rgba.b }, opacity: rgba.a, visible: true, blendMode: 'NORMAL' }]);
                        }
                        changed = true;
                    } catch (_o) { }
                }
                offset += len;
            }
        } else if (typeof jn.characters === 'string' && txt.characters !== jn.characters) {
            const currentFont = txt.fontName !== figma.mixed ? txt.fontName : { family: 'Inter', style: 'Regular' };
            const loaded = await loadFontWithFallback(currentFont.family, currentFont.style);
            if (loaded) { try { txt.characters = jn.characters; changed = true; } catch (_p) { } }
            else stats.errors.add('Font not found: ' + currentFont.family);
        }
        if (typeof jn.fontFamily === 'string') {
            const style = typeof jn.fontStyle === 'string' ? jn.fontStyle : 'Regular';
            const resolved = await loadFontWithFallback(jn.fontFamily, style);
            if (resolved) {
                try {
                    const fn = txt.fontName !== figma.mixed ? txt.fontName : { family: '', style: '' };
                    if (fn.family !== resolved.family || fn.style !== resolved.style) { txt.fontName = resolved; changed = true; }
                } catch (_q) { }
                if (resolved.family !== jn.fontFamily) stats.errors.add('Font substituted: ' + jn.fontFamily + ' → ' + resolved.family);
            } else stats.errors.add('Font not found: ' + jn.fontFamily);
        }
        if (typeof jn.fontSize === 'number' && txt.fontSize !== figma.mixed && txt.fontSize !== jn.fontSize) {
            try { txt.fontSize = jn.fontSize; changed = true; } catch (_r) { }
        }
        const rawHAlign = typeof jn.textAlignHorizontal === 'string' ? jn.textAlignHorizontal
            : typeof jn.textAlign === 'string' ? jn.textAlign
            : typeof jn.alignment === 'string' ? jn.alignment : null;
        if (rawHAlign !== null) {
            const hAlign = normalizeHAlign(rawHAlign);
            if (hAlign !== null) { try { txt.textAlignHorizontal = hAlign; changed = true; } catch (_s) { } }
        }
        const rawVAlign = typeof jn.textAlignVertical === 'string' ? jn.textAlignVertical
            : typeof jn.verticalAlignment === 'string' ? jn.verticalAlignment : null;
        if (rawVAlign !== null) {
            const vAlign = normalizeVAlign(rawVAlign);
            if (vAlign !== null) { try { txt.textAlignVertical = vAlign; changed = true; } catch (_t) { } }
        }
    }
    if (doFills && Array.isArray(jn.fills)) {
        try { if (applyFillsFromJson(scene, jn.fills, p.syncImages, stats)) changed = true; } catch (_u) { }
    }
    if (doFills && Array.isArray(jn.strokes) && 'strokes' in scene) {
        try {
            const newStrokes = [];
            for (const js of jn.strokes) {
                const rgb = hexToRgb(String((_b = js.color) !== null && _b !== void 0 ? _b : ''));
                if (!rgb) continue;
                const op = typeof js.opacity === 'number' ? Math.max(0, Math.min(1, js.opacity)) : 1;
                newStrokes.push({ type: 'SOLID', color: rgb, opacity: op });
            }
            scene.strokes = newStrokes; changed = true;
        } catch (_v) { }
    }
    if (doFills && typeof jn.strokeWeight === 'number' && 'strokeWeight' in scene) {
        try { scene.strokeWeight = jn.strokeWeight; changed = true; } catch (_w) { }
    }
    if (doFills && typeof jn.strokeAlign === 'string' && 'strokeAlign' in scene) {
        try { scene.strokeAlign = jn.strokeAlign; changed = true; } catch (_x) { }
    }
    if (doFills && Array.isArray(jn.effects) && 'effects' in scene) {
        try {
            const newEffects = jn.effects.map((je) => {
                var _a, _b, _c, _d, _e, _f;
                const type = String((_a = je.type) !== null && _a !== void 0 ? _a : '');
                const visible = je.visible !== false;
                const radius = typeof je.radius === 'number' ? je.radius : 4;
                const spread = typeof je.spread === 'number' ? je.spread : 0;
                const rgba = typeof je.color === 'string' ? ((_b = hexToRgba(je.color)) !== null && _b !== void 0 ? _b : { r: 0, g: 0, b: 0, a: 0.25 }) : { r: 0, g: 0, b: 0, a: 0.25 };
                const offset = je.offset;
                if (type === 'DROP_SHADOW') return { type: 'DROP_SHADOW', visible, color: rgba, offset: { x: (_c = offset === null || offset === void 0 ? void 0 : offset.x) !== null && _c !== void 0 ? _c : 0, y: (_d = offset === null || offset === void 0 ? void 0 : offset.y) !== null && _d !== void 0 ? _d : 2 }, radius, spread, blendMode: 'NORMAL', showShadowBehindNode: false };
                if (type === 'INNER_SHADOW') return { type: 'INNER_SHADOW', visible, color: rgba, offset: { x: (_e = offset === null || offset === void 0 ? void 0 : offset.x) !== null && _e !== void 0 ? _e : 0, y: (_f = offset === null || offset === void 0 ? void 0 : offset.y) !== null && _f !== void 0 ? _f : 2 }, radius, spread, blendMode: 'NORMAL' };
                if (type === 'LAYER_BLUR') return { type: 'LAYER_BLUR', visible, radius };
                if (type === 'BACKGROUND_BLUR') return { type: 'BACKGROUND_BLUR', visible, radius };
                return null;
            }).filter((e) => e !== null);
            scene.effects = newEffects; changed = true;
        } catch (_y) { }
    }
    if (jn.nineSlice === true) {
        if (p.syncNineSlice) {
            const borderL = typeof jn.nineSliceBorderLeft === 'number' ? jn.nineSliceBorderLeft : 0;
            const borderR = typeof jn.nineSliceBorderRight === 'number' ? jn.nineSliceBorderRight : 0;
            const borderT = typeof jn.nineSliceBorderTop === 'number' ? jn.nineSliceBorderTop : 0;
            const borderB = typeof jn.nineSliceBorderBottom === 'number' ? jn.nineSliceBorderBottom : 0;
            const hasValidBorder = borderL > 0 || borderR > 0 || borderT > 0 || borderB > 0;
            if (!hasValidBorder) {
                stats.errors.add('Nine-slice border is 0 for layer: ' + scene.name);
            } else {
                const nsData = { nineSlice: true, nineSliceBorderLeft: borderL, nineSliceBorderRight: borderR, nineSliceBorderTop: borderT, nineSliceBorderBottom: borderB };
                scene.setPluginData(NINE_SLICE_KEY, JSON.stringify(nsData));
                stats.nineSliceUpdated++; changed = true;
            }
        }
    }
    return changed;
}
// ─── Node creation ────────────────────────────────────────────────────────────
async function createFigmaNode(jn, parent) {
    const type = typeof jn.type === 'string' ? jn.type.toUpperCase() : 'FRAME';
    let node;
    if (type === 'TEXT') {
        const text = figma.createText();
        try { await figma.loadFontAsync({ family: 'Inter', style: 'Regular' }); } catch (_a) { }
        parent.appendChild(text); node = text;
    } else if (type === 'RECTANGLE') {
        const rect = figma.createRectangle(); parent.appendChild(rect); node = rect;
    } else if (type === 'ELLIPSE') {
        const ellipse = figma.createEllipse(); parent.appendChild(ellipse); node = ellipse;
    } else {
        const frame = figma.createFrame(); frame.fills = []; parent.appendChild(frame); node = frame;
    }
    if ('resize' in node) {
        const jw = typeof jn.width === 'number' ? jn.width : 0;
        const jh = typeof jn.height === 'number' ? jn.height : 0;
        const w = jw > 0 ? jw : 100;
        const h = jh > 0 ? jh : 100;
        node.resize(w, h);
    }
    return node;
}
// ─── Figma tree indexing ──────────────────────────────────────────────────────
function getOwnerPage(node) {
    let current = node;
    while (current.type !== 'PAGE' && current.type !== 'DOCUMENT') {
        const parent = current.parent;
        if (!parent) return null;
        current = parent;
    }
    return current.type === 'PAGE' ? current : null;
}
function indexFigmaSubtree(node, byId, byUnityPath) {
    byId.set(node.id, node);
    const up = node.getPluginData(UNITY_PATH_KEY);
    if (up) byUnityPath.set(up, node);
    if ('children' in node) {
        for (const child of node.children) indexFigmaSubtree(child, byId, byUnityPath);
    }
}
// ─── Deletion pass ────────────────────────────────────────────────────────────
function deleteUnvisited(node, visitedIds, stats) {
    if (!('children' in node)) return;
    const frame = node;
    const toDelete = [];
    for (const child of frame.children) { if (!visitedIds.has(child.id)) toDelete.push(child); }
    for (const n of toDelete) { n.remove(); stats.deleted++; }
    for (const child of frame.children) deleteUnvisited(child, visitedIds, stats);
}
// ─── Main sync ────────────────────────────────────────────────────────────────
function buildPath(parentPath, jn) {
    var _a;
    const name = typeof jn.name === 'string' ? jn.name : String((_a = jn.id) !== null && _a !== void 0 ? _a : 'node');
    return parentPath ? parentPath + '/' + name : name;
}
async function syncNode(jn, parentFigma, path, byId, byUnityPath, visitedIds, p, stats, isRoot, counter) {
    var _a, _b, _c, _d, _e;
    counter.count++;
    if (counter.count % 10 === 0) {
        (_a = counter.onProgress) === null || _a === void 0 ? void 0 : _a.call(counter, counter.count);
        await new Promise((r) => setTimeout(r, 0));
    }
    if (jn.nineSlicePart === true) {
        const nodeId = typeof jn.id === 'string' ? jn.id : null;
        if (nodeId) { const existing = (_b = byId.get(nodeId)) !== null && _b !== void 0 ? _b : null; if (existing && !existing.removed) visitedIds.add(existing.id); }
        stats.skipped++; return null;
    }
    const nodeId = typeof jn.id === 'string' ? jn.id : null;
    let scene = null;
    let wasCreated = false;
    if (nodeId) {
        const candidate = (_c = byId.get(nodeId)) !== null && _c !== void 0 ? _c : null;
        if (candidate && !candidate.removed) { const ownerPage = getOwnerPage(candidate); if (ownerPage && ownerPage.id === figma.currentPage.id) scene = candidate; }
        if (!scene) {
            const found = await figma.getNodeByIdAsync(nodeId);
            if (found && !found.removed && found.type !== 'DOCUMENT' && found.type !== 'PAGE') {
                const ownerPage = getOwnerPage(found);
                if (ownerPage && ownerPage.id === figma.currentPage.id) scene = found;
            }
        }
    }
    if (!scene) scene = (_d = byUnityPath.get(path)) !== null && _d !== void 0 ? _d : null;
    if (!scene && parentFigma && 'children' in parentFigma) {
        const name = typeof jn.name === 'string' ? jn.name : null;
        if (name) { for (const c of parentFigma.children) { if (c.name === name && !visitedIds.has(c.id)) { scene = c; break; } } }
    }
    if (!scene) {
        if (!parentFigma) return null;
        scene = await createFigmaNode(jn, parentFigma);
        wasCreated = true;
        if (isRoot) { const vp = figma.viewport.center; scene.x = Math.round(vp.x - scene.width / 2); scene.y = Math.round(vp.y - scene.height / 2); }
    } else if (!isRoot && parentFigma && scene.parent && scene.parent.id !== parentFigma.id) {
        parentFigma.appendChild(scene); stats.reparented++;
    }
    scene.setPluginData(UNITY_PATH_KEY, path);
    visitedIds.add(scene.id);
    let posJn = jn;
    if (parentFigma !== null) {
        const pf = parentFigma;
        if (pf.type === 'GROUP') {
            const ax = typeof jn.x === 'number' ? pf.x + jn.x : jn.x;
            const ay = typeof jn.y === 'number' ? pf.y + jn.y : jn.y;
            posJn = Object.assign({}, jn, { x: ax, y: ay });
        }
    }
    const applyPos = wasCreated || (!isRoot && p.syncPosition);
    const changed = await applyNodeProperties(scene, posJn, p, applyPos, wasCreated, stats);
    if (wasCreated) stats.created++;
    else if (changed) stats.updated++;
    const isNineSliceContainer = jn.nineSlice === true;
    const jChildren = jn.children;
    if (isNineSliceContainer && 'children' in scene) {
        const existingChildren = scene.children;
        for (const child of existingChildren) { visitedIds.add(child.id); }
    } else if (Array.isArray(jChildren) && 'children' in scene) {
        const sceneAsParent = scene;
        const processedChildren = [];
        for (const childJn of jChildren) {
            const childPath = buildPath(path, childJn);
            const childScene = await syncNode(childJn, sceneAsParent, childPath, byId, byUnityPath, visitedIds, p, stats, false, counter);
            if (childScene) processedChildren.push(childScene);
        }
        const sceneFrame = scene;
        for (let i = 0; i < processedChildren.length; i++) {
            try {
                const child = processedChildren[i];
                if (((_e = sceneFrame.children[i]) === null || _e === void 0 ? void 0 : _e.id) !== child.id) { sceneAsParent.insertChild(i, child); }
            } catch (_f) { }
        }
    }
    return scene;
}
// ─── Top-level sync orchestration ─────────────────────────────────────────────
async function runFullSync(rootJn, p, stats, affected, fallbackTarget, onProgress) {
    var _a;
    const rootId = typeof rootJn.id === 'string' ? rootJn.id : null;
    let rootScene = null;
    if (rootId) {
        const found = await figma.getNodeByIdAsync(rootId);
        if (found && !found.removed && found.type !== 'DOCUMENT' && found.type !== 'PAGE') {
            const ownerPage = getOwnerPage(found);
            if (ownerPage && ownerPage.id === figma.currentPage.id) rootScene = found;
        }
    }
    if (!rootScene && fallbackTarget && !fallbackTarget.removed) {
        const ownerPage = getOwnerPage(fallbackTarget);
        if (ownerPage && ownerPage.id === figma.currentPage.id) { rootScene = fallbackTarget; }
    }
    const isNew = !rootScene;
    if (isNew) {
        const pageContainer = figma.currentPage;
        rootScene = await createFigmaNode(rootJn, pageContainer);
        const vp = figma.viewport.center;
        rootScene.x = Math.round(vp.x - rootScene.width / 2);
        rootScene.y = Math.round(vp.y - rootScene.height / 2);
    }
    if (!rootScene) return;
    const byId = new Map();
    const byUnityPath = new Map();
    indexFigmaSubtree(rootScene, byId, byUnityPath);
    const visitedIds = new Set();
    visitedIds.add(rootScene.id);
    const rootPath = buildPath('', rootJn);
    rootScene.setPluginData(UNITY_PATH_KEY, rootPath);
    const rootChanged = await applyNodeProperties(rootScene, rootJn, p, false, isNew, stats);
    if (isNew) stats.created++;
    else if (rootChanged) stats.updated++;
    const isNineSliceRoot = rootJn.nineSlice === true;
    const jChildren = rootJn.children;
    if (isNineSliceRoot && 'children' in rootScene) {
        const existingChildren = rootScene.children;
        for (const child of existingChildren) { visitedIds.add(child.id); }
    } else if (Array.isArray(jChildren) && 'children' in rootScene) {
        const rootAsParent = rootScene;
        const processedChildren = [];
        const counter = { count: 0, onProgress };
        for (const childJn of jChildren) {
            const childPath = buildPath(rootPath, childJn);
            const childScene = await syncNode(childJn, rootAsParent, childPath, byId, byUnityPath, visitedIds, p, stats, false, counter);
            if (childScene) processedChildren.push(childScene);
        }
        for (let i = 0; i < processedChildren.length; i++) {
            try {
                if (((_a = rootScene.children[i]) === null || _a === void 0 ? void 0 : _a.id) !== processedChildren[i].id) { rootAsParent.insertChild(i, processedChildren[i]); }
            } catch (_b) { }
        }
    }
    deleteUnvisited(rootScene, visitedIds, stats);
    affected.push(rootScene);
}
// ─── Pruned-node deletion ─────────────────────────────────────────────────────
function figmaIdFromIrId(irId) {
    const match = irId.match(/__(\d+)_(\d+)$/);
    return match ? match[1] + ':' + match[2] : null;
}
async function deletePrunedNodes(prunedIrIds, stats) {
    for (const irId of prunedIrIds) {
        const figmaId = figmaIdFromIrId(irId);
        if (!figmaId) continue;
        const node = await figma.getNodeByIdAsync(figmaId);
        if (!node || node.removed || node.type === 'DOCUMENT' || node.type === 'PAGE') continue;
        const ownerPage = getOwnerPage(node);
        if (ownerPage && ownerPage.id === figma.currentPage.id) { node.remove(); stats.deleted++; }
    }
}
// ─── Export helpers ───────────────────────────────────────────────────────────
function exportPaint(paint) {
    var _a, _b, _c, _d;
    const base = { type: paint.type, visible: paint.visible !== false, opacity: (_a = paint.opacity) !== null && _a !== void 0 ? _a : 1, blendMode: (_b = paint.blendMode) !== null && _b !== void 0 ? _b : 'NORMAL' };
    if (paint.type === 'SOLID') { const c = paint.color; return Object.assign(Object.assign({}, base), { color: rgbToHex(c.r, c.g, c.b) }); }
    if (paint.type === 'GRADIENT_LINEAR' || paint.type === 'GRADIENT_RADIAL' || paint.type === 'GRADIENT_ANGULAR' || paint.type === 'GRADIENT_DIAMOND') {
        const gp = paint;
        const row0 = gp.gradientTransform[0];
        const row1 = gp.gradientTransform[1];
        return Object.assign(Object.assign({}, base), { transform: { a: row0[0], b: row0[1], tx: row0[2], c: row1[0], d: row1[1], ty: row1[2] }, stops: gp.gradientStops.map((s) => ({ position: s.position, color: rgbaToHex(s.color.r, s.color.g, s.color.b, s.color.a) })) });
    }
    if (paint.type === 'IMAGE') { const ip = paint; return Object.assign(Object.assign({}, base), { imageHash: (_c = ip.imageHash) !== null && _c !== void 0 ? _c : '', imageFile: '', scaleMode: (_d = ip.scaleMode) !== null && _d !== void 0 ? _d : 'FILL' }); }
    return null;
}
function exportEffect(effect) {
    const base = { type: effect.type, visible: effect.visible };
    if (effect.type === 'DROP_SHADOW' || effect.type === 'INNER_SHADOW') {
        const s = effect;
        return Object.assign(Object.assign({}, base), { color: rgbaToHex(s.color.r, s.color.g, s.color.b, s.color.a), offset: { x: s.offset.x, y: s.offset.y }, radius: s.radius, spread: s.spread });
    }
    if (effect.type === 'LAYER_BLUR' || effect.type === 'BACKGROUND_BLUR') { const b = effect; return Object.assign(Object.assign({}, base), { radius: b.radius }); }
    return null;
}
async function exportNode(node) {
    const obj = { id: node.id, name: node.name, type: node.type, visible: node.visible };
    if ('opacity' in node) obj.opacity = Math.round(node.opacity * 1000) / 1000;
    if ('x' in node) obj.x = Math.round(node.x * 100) / 100;
    if ('y' in node) obj.y = Math.round(node.y * 100) / 100;
    if ('width' in node) obj.width = Math.round(node.width * 100) / 100;
    if ('height' in node) obj.height = Math.round(node.height * 100) / 100;
    if ('rotation' in node) obj.rotation = Math.round(node.rotation * 1000) / 1000;
    if ('constraints' in node) { const c = node.constraints; obj.constraints = { horizontal: c.horizontal, vertical: c.vertical }; }
    if ('layoutPositioning' in node) obj.layoutPositioning = node.layoutPositioning;
    if ('layoutSizingHorizontal' in node) obj.layoutSizingHorizontal = node.layoutSizingHorizontal;
    if ('layoutSizingVertical' in node) obj.layoutSizingVertical = node.layoutSizingVertical;
    if (node.type === 'FRAME' || node.type === 'COMPONENT' || node.type === 'INSTANCE') {
        const f = node;
        const layoutObj = { layoutMode: f.layoutMode, clipsContent: f.clipsContent };
        if (f.layoutMode !== 'NONE') {
            layoutObj.paddingTop = f.paddingTop; layoutObj.paddingRight = f.paddingRight;
            layoutObj.paddingBottom = f.paddingBottom; layoutObj.paddingLeft = f.paddingLeft;
            layoutObj.itemSpacing = f.itemSpacing; layoutObj.primaryAxisAlignItems = f.primaryAxisAlignItems;
            layoutObj.counterAxisAlignItems = f.counterAxisAlignItems;
        }
        obj.layout = layoutObj;
    }
    if (node.type === 'RECTANGLE') { const r = node; obj.rectangleCornerRadii = [r.topLeftRadius, r.topRightRadius, r.bottomRightRadius, r.bottomLeftRadius]; }
    else if ('cornerRadius' in node) { const cr = node.cornerRadius; if (cr !== figma.mixed) obj.cornerRadius = cr; }
    if ('fills' in node) { const fills = node.fills; if (fills !== figma.mixed) { const exported = fills.map(exportPaint).filter((p) => p !== null); if (exported.length > 0) obj.fills = exported; } }
    if ('strokes' in node) {
        const strokes = node.strokes;
        if (strokes.length > 0) {
            const exported = strokes.map((p) => { var _a; if (p.type === 'SOLID') return { color: rgbToHex(p.color.r, p.color.g, p.color.b), opacity: (_a = p.opacity) !== null && _a !== void 0 ? _a : 1 }; return null; }).filter((p) => p !== null);
            if (exported.length > 0) obj.strokes = exported;
        }
        if ('strokeWeight' in node) obj.strokeWeight = node.strokeWeight;
        if ('strokeAlign' in node) obj.strokeAlign = node.strokeAlign;
    }
    if ('effects' in node) { const effects = node.effects; if (effects.length > 0) { const exported = effects.map(exportEffect).filter((e) => e !== null); if (exported.length > 0) obj.effects = exported; } }
    if (node.type === 'TEXT') {
        const txt = node;
        obj.characters = txt.characters;
        if (txt.fontName !== figma.mixed) { const fn = txt.fontName; obj.fontFamily = fn.family; obj.fontStyle = fn.style; }
        if (txt.fontSize !== figma.mixed) obj.fontSize = txt.fontSize;
        if (txt.textAlignHorizontal !== figma.mixed) obj.textAlignHorizontal = txt.textAlignHorizontal;
        if (txt.textAlignVertical !== figma.mixed) obj.textAlignVertical = txt.textAlignVertical;
        try {
            const styledSegments = txt.getStyledTextSegments(['fontName', 'fontSize', 'fills']);
            if (styledSegments.length > 1) {
                obj.segments = styledSegments.map((seg) => {
                    var _a;
                    const segObj = { text: seg.characters };
                    if (seg.fontName) { segObj.fontFamily = seg.fontName.family; segObj.fontStyle = seg.fontName.style; }
                    if (seg.fontSize) segObj.fontSize = seg.fontSize;
                    const segFills = seg.fills;
                    if (segFills && segFills.length > 0) { const fill = segFills[0]; if (fill.type === 'SOLID') { segObj.color = rgbaToHex(fill.color.r, fill.color.g, fill.color.b, (_a = fill.opacity) !== null && _a !== void 0 ? _a : 1); } }
                    return segObj;
                });
            }
        } catch (_a) { }
    }
    try {
        const nsRaw = node.getPluginData(NINE_SLICE_KEY);
        if (nsRaw) {
            const ns = JSON.parse(nsRaw);
            if (ns.nineSlice) { obj.nineSlice = true; obj.nineSliceBorderLeft = ns.nineSliceBorderLeft; obj.nineSliceBorderRight = ns.nineSliceBorderRight; obj.nineSliceBorderTop = ns.nineSliceBorderTop; obj.nineSliceBorderBottom = ns.nineSliceBorderBottom; }
        }
    } catch (_b) { }
    if ('children' in node) {
        const frame = node;
        const childObjs = [];
        for (const child of frame.children) { childObjs.push(await exportNode(child)); }
        obj.children = childObjs;
    }
    return obj;
}
// ─── Action ───────────────────────────────────────────────────────────────────
function uniqueSceneNodes(nodes) {
    return [...new Set(nodes)].filter((node) => !node.removed);
}
function attachRelaunch(nodes) {
    const unique = uniqueSceneNodes(nodes);
    const roots = unique.filter((n) => { var _a, _b; return ((_a = n.parent) === null || _a === void 0 ? void 0 : _a.type) === 'PAGE' || ((_b = n.parent) === null || _b === void 0 ? void 0 : _b.type) === 'DOCUMENT'; });
    const targets = roots.length > 0 ? roots : unique.slice(0, 1);
    if (targets.length > 0) { for (const node of targets) node.setRelaunchData({ [TOOL_ID]: DISPLAY_NAME }); }
    else { figma.root.setRelaunchData({ [TOOL_ID]: DISPLAY_NAME }); }
}
async function action_importSync(params, target, fileOverride) {
    const affectedNodes = [];
    const file = fileOverride !== undefined ? fileOverride : params.jsonFile;
    if (!file) return { affectedNodes };
    let parsed;
    try { parsed = JSON.parse(file.text); }
    catch (_a) { figma.notify('Invalid file — expected JSON or XML with matching structure', { error: true }); return { affectedNodes }; }
    let rootNodes = [];
    if (parsed.node && typeof parsed.node === 'object' && !Array.isArray(parsed.node)) { rootNodes = [parsed.node]; }
    else if (Array.isArray(parsed.nodes)) { rootNodes = parsed.nodes.filter((n) => n && typeof n === 'object'); }
    else if (Array.isArray(parsed.node)) { rootNodes = parsed.node.filter((n) => n && typeof n === 'object'); }
    else if (typeof parsed.id === 'string') { rootNodes = [parsed]; }
    if (rootNodes.length === 0) { figma.notify('No node data found in JSON. Expected { node: {...} } or { nodes: [...] }.', { error: true }); return { affectedNodes }; }
    _currentImageMap = new Map();
    _currentImageMapLower = new Map();
    for (const asset of params.imageAssets) { _currentImageMap.set(asset.name, asset.bytes); _currentImageMapLower.set(asset.name.toLowerCase(), asset.bytes); }
    _clearFontCache();
    const stats = { created: 0, updated: 0, skipped: 0, deleted: 0, reparented: 0, imagesUpdated: 0, nineSliceUpdated: 0, missingFileNames: [], errors: new Set() };
    const progressNotify = figma.notify('Syncing…', { timeout: Infinity });
    const onProgress = (count) => { figma.ui.postMessage({ type: 'progress', count }); };
    for (const rootJn of rootNodes) { await runFullSync(rootJn, params, stats, affectedNodes, target, onProgress); }
    const metadata = parsed.metadata;
    const prunedIrIds = Array.isArray(metadata === null || metadata === void 0 ? void 0 : metadata.prunedIrIds) ? metadata.prunedIrIds : [];
    if (prunedIrIds.length > 0) await deletePrunedNodes(prunedIrIds, stats);
    progressNotify === null || progressNotify === void 0 ? void 0 : progressNotify.cancel();
    const parts = [];
    if (stats.created > 0) parts.push(stats.created + ' created');
    if (stats.updated > 0) parts.push(stats.updated + ' updated');
    if (stats.deleted > 0) parts.push(stats.deleted + ' deleted');
    if (stats.reparented > 0) parts.push(stats.reparented + ' moved');
    if (stats.skipped > 0) parts.push(stats.skipped + ' skipped');
    if (stats.imagesUpdated > 0) parts.push(stats.imagesUpdated + ' image' + (stats.imagesUpdated !== 1 ? 's' : '') + ' synced');
    if (stats.nineSliceUpdated > 0) parts.push(stats.nineSliceUpdated + ' nine-slice' + (stats.nineSliceUpdated !== 1 ? 's' : '') + ' updated');
    const uniqueMissing = [...new Set(stats.missingFileNames)];
    if (uniqueMissing.length > 0) { const names = uniqueMissing.slice(0, 3).join(', '); const extra = uniqueMissing.length > 3 ? ' +' + (uniqueMissing.length - 3) + ' more' : ''; parts.push(uniqueMissing.length + ' missing: ' + names + extra); }
    const summary = parts.join(', ') || 'No changes';
    const errCount = stats.errors.size;
    figma.notify(errCount > 0 ? summary + ' (' + errCount + ' warning' + (errCount !== 1 ? 's' : '') + ')' : summary);
    return { affectedNodes };
}
async function runAction_importSync() {
    var _a;
    isExecuting = true;
    const target = (_a = figma.currentPage.selection[0]) !== null && _a !== void 0 ? _a : null;
    try {
        const result = await action_importSync(latestParams, target, latestParams.jsonFile);
        attachRelaunch(result.affectedNodes);
        pushActionStates();
        const roots = result.affectedNodes.filter((n) => { var _a; return ((_a = n.parent) === null || _a === void 0 ? void 0 : _a.type) === 'PAGE'; });
        if (roots.length > 0) figma.viewport.scrollAndZoomIntoView(roots);
    } catch (error) { const message = error instanceof Error ? error.message : String(error); figma.notify(message, { error: true }); throw error; }
    finally { isExecuting = false; }
}
async function runAction_importSyncXml() {
    var _a;
    isExecuting = true;
    const target = (_a = figma.currentPage.selection[0]) !== null && _a !== void 0 ? _a : null;
    try {
        const result = await action_importSync(latestParams, target, latestParams.xmlFile);
        attachRelaunch(result.affectedNodes);
        pushActionStates();
        const roots = result.affectedNodes.filter((n) => { var _a; return ((_a = n.parent) === null || _a === void 0 ? void 0 : _a.type) === 'PAGE'; });
        if (roots.length > 0) figma.viewport.scrollAndZoomIntoView(roots);
    } catch (error) { const message = error instanceof Error ? error.message : String(error); figma.notify(message, { error: true }); throw error; }
    finally { isExecuting = false; }
}
async function runAction_export() {
    isExecuting = true;
    try {
        const selection = figma.currentPage.selection;
        if (selection.length === 0) { figma.notify('Select one or more layers to export', { error: true }); return; }
        const progressNotify = figma.notify('Exporting…', { timeout: Infinity });
        let result;
        if (selection.length === 1) { result = { node: await exportNode(selection[0]) }; }
        else { const nodes = []; for (const node of selection) nodes.push(await exportNode(node)); result = { nodes }; }
        progressNotify === null || progressNotify === void 0 ? void 0 : progressNotify.cancel();
        const baseName = selection[0].name.replace(/[^a-zA-Z0-9_\-. ]/g, '_') || 'export';
        figma.ui.postMessage({ type: 'export-result', json: JSON.stringify(result, null, 2), filename: baseName + '.json', format: latestParams.exportFormat });
        figma.notify('Exported ' + selection.length + ' layer' + (selection.length !== 1 ? 's' : ''));
    } catch (error) { const message = error instanceof Error ? error.message : String(error); figma.notify(message, { error: true }); throw error; }
    finally { isExecuting = false; }
}
function pushActionStates() {
    const hasSelection = figma.currentPage.selection.length > 0;
    figma.ui.postMessage({ type: 'action-state', actions: { importSync: { enabled: true, label: 'Import JSON', status: undefined }, importSyncXml: { enabled: true, label: 'Import XML', status: undefined }, export: { enabled: hasSelection, label: hasSelection ? 'Export' : 'Export', status: undefined } } });
}
function refreshSelection() { if (!isExecuting) pushActionStates(); }
const initialParams = DEFAULTS;
latestParams = initialParams;
let html = __html__;
html = setBooleanControl(html, 'syncPosition', initialParams.syncPosition);
html = setBooleanControl(html, 'syncVisibility', initialParams.syncVisibility);
html = setBooleanControl(html, 'syncText', initialParams.syncText);
html = setBooleanControl(html, 'syncFills', initialParams.syncFills);
html = setBooleanControl(html, 'syncImages', initialParams.syncImages);
html = setBooleanControl(html, 'syncNineSlice', initialParams.syncNineSlice);
figma.root.setRelaunchData({ [TOOL_ID]: DISPLAY_NAME });
figma.showUI(html, { width: 280, height: 320 });
pushActionStates();
figma.on('selectionchange', refreshSelection);
figma.ui.onmessage = (msg) => {
    if (msg.type === 'resize') { figma.ui.resize(280, Math.max(120, Math.min(900, Math.round(msg.height)))); return; }
    if (msg.type === 'action' && msg.id === 'importSync') { latestParams = normalizeParams(msg.params); void runAction_importSync(); }
    if (msg.type === 'action' && msg.id === 'importSyncXml') { latestParams = normalizeParams(msg.params); void runAction_importSyncXml(); }
    if (msg.type === 'action' && msg.id === 'export') { latestParams = normalizeParams(msg.params); void runAction_export(); }
};
