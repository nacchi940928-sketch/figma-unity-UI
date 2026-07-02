type Params = { includeAssets: boolean }
type Attachment = { version: 1; params: Params; state: unknown | null }
type RunMsg =
  | { type: 'action'; id: string; params: Partial<Params> }
  | { type: 'resize'; height: number }

const TOOL_ID = "3bc4284c-7b77-4024-a251-a0d8c39c3249"
const DISPLAY_NAME = "Full design exporter v2"
const ATTACH_KEY = TOOL_ID + ':state'
const DEFAULTS: Params = { includeAssets: true }
let latestParams: Params = DEFAULTS
let isExecuting = false

function normalizeParams(input: Partial<Params> | null | undefined): Params {
  const includeAssets = typeof input?.includeAssets === 'boolean' ? input.includeAssets : true
  return { includeAssets }
}

function uniqueSceneNodes(nodes: readonly SceneNode[]): SceneNode[] {
  return [...new Set(nodes)].filter((n) => !n.removed)
}

function attachRelaunch(nodes: readonly SceneNode[]): void {
  const unique = uniqueSceneNodes(nodes)
  if (unique.length > 0) {
    for (const n of unique) n.setRelaunchData({ [TOOL_ID]: DISPLAY_NAME })
  } else {
    figma.root.setRelaunchData({ [TOOL_ID]: DISPLAY_NAME })
  }
}

function singleSelectedTarget(): SceneNode | null {
  const sel = figma.currentPage.selection
  return sel.length === 1 ? (sel[0] ?? null) : null
}

function readAttachment(node: SceneNode): Attachment | null {
  try {
    const parsed = JSON.parse(node.getPluginData(ATTACH_KEY)) as Partial<Attachment>
    if (parsed?.version !== 1) return null
    return { version: 1, params: normalizeParams(parsed.params), state: (parsed.state ?? null) as unknown | null }
  } catch { return null }
}

function writeAttachment(node: SceneNode, params: Params, state: unknown | null): void {
  node.setPluginData(ATTACH_KEY, JSON.stringify({ version: 1, params, state }))
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function slugify(name: string): string {
  return name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'node'
}

function makeIrId(name: string, id: string): string {
  return slugify(name) + '__' + id.replace(/[^a-z0-9]/gi, '_')
}

function absolutePosition(node: SceneNode): { x: number; y: number } {
  const t = node.absoluteTransform
  return { x: t[0][2], y: t[1][2] }
}

// ─── Color ────────────────────────────────────────────────────────────────────

function toHex2(v: number): string {
  return Math.round(Math.max(0, Math.min(1, v)) * 255).toString(16).padStart(2, '0')
}

function exportColor(color: RGB | RGBA): string {
  const base = '#' + toHex2(color.r) + toHex2(color.g) + toHex2(color.b)
  return 'a' in color && color.a < 1 ? base + toHex2(color.a) : base
}

// ─── Mixed value helper ───────────────────────────────────────────────────────

function mixedVal<T>(value: T | symbol): T | 'mixed' {
  return typeof value === 'symbol' ? 'mixed' : (value as T)
}

// ─── Asset collection ─────────────────────────────────────────────────────────

interface ImageEntry { filename: string; bytes: Uint8Array | null }
interface VectorEntry { filename: string; bytes: Uint8Array | null }

interface AssetMaps {
  images: Record<string, ImageEntry>
  vectors: Record<string, VectorEntry>
}

// ─── Nine-slice component info (from pre-scan) ────────────────────────────────

interface ImageHashMeta {
  filename: string
  width: number
  height: number
}

interface NineSliceComponentInfo {
  primaryHash: string
  primaryFilename: string
  refWidth: number
  refHeight: number
  borders: { borderLeft: number; borderRight: number; borderTop: number; borderBottom: number }
  componentName: string
}

// ─── Paint export ─────────────────────────────────────────────────────────────

function generateImageFilename(
  nodeSlug: string,
  fillIdx: number,
  hash: string,
  usedFilenames: Set<string>
): string {
  const base = nodeSlug + (fillIdx > 0 ? '-' + String(fillIdx) : '')
  let filename = base + '.png'
  if (usedFilenames.has(filename)) {
    filename = base + '-' + hash.slice(0, 6) + '.png'
  }
  if (usedFilenames.has(filename)) {
    filename = 'img-' + hash.slice(0, 12) + '.png'
  }
  return filename
}

function exportPaint(
  paint: Paint,
  imageMap: Record<string, ImageEntry>,
  includeAssets: boolean,
  nodeSlug: string,
  fillIdx: number,
  usedFilenames: Set<string>,
  imageHashMeta: Record<string, ImageHashMeta>
): object {
  if (!paint.visible) return { type: paint.type, visible: false }
  if (paint.type === 'SOLID') {
    return { type: 'SOLID', color: exportColor(paint.color), opacity: paint.opacity ?? 1, blendMode: paint.blendMode }
  }
  if (
    paint.type === 'GRADIENT_LINEAR' || paint.type === 'GRADIENT_RADIAL' ||
    paint.type === 'GRADIENT_ANGULAR' || paint.type === 'GRADIENT_DIAMOND'
  ) {
    const t = paint.gradientTransform
    return {
      type: paint.type,
      transform: { a: t[0][0], b: t[0][1], tx: t[0][2], c: t[1][0], d: t[1][1], ty: t[1][2] },
      stops: paint.gradientStops.map((s) => ({ position: s.position, color: exportColor(s.color) })),
      opacity: paint.opacity ?? 1,
      blendMode: paint.blendMode,
    }
  }
  if (paint.type === 'IMAGE') {
    let imageFile: string | null = null
    if (paint.imageHash) {
      if (imageMap[paint.imageHash]) {
        imageFile = imageMap[paint.imageHash].filename
      } else {
        const meta = imageHashMeta[paint.imageHash]
        const filename = meta
          ? meta.filename
          : generateImageFilename(nodeSlug, fillIdx, paint.imageHash, usedFilenames)
        usedFilenames.add(filename)
        imageMap[paint.imageHash] = { filename, bytes: null }
        imageFile = filename
      }
    }
    return {
      type: 'IMAGE',
      imageHash: paint.imageHash ?? null,
      imageFile,
      scaleMode: paint.scaleMode,
      opacity: paint.opacity ?? 1,
      blendMode: paint.blendMode,
    }
  }
  return { type: paint.type, opacity: paint.opacity ?? 1 }
}

// ─── Stroke props ─────────────────────────────────────────────────────────────

interface StrokeProps {
  strokeWeight: number | null
  strokeAlign: string | null
  dashPattern: readonly number[]
  strokeCap: string | null
  strokeJoin: string | null
  strokeMiterLimit: number | null
  strokeTopWeight: number | null
  strokeRightWeight: number | null
  strokeBottomWeight: number | null
  strokeLeftWeight: number | null
}

function exportStrokeProps(node: SceneNode): StrokeProps {
  const empty: StrokeProps = {
    strokeWeight: null, strokeAlign: null, dashPattern: [],
    strokeCap: null, strokeJoin: null, strokeMiterLimit: null,
    strokeTopWeight: null, strokeRightWeight: null, strokeBottomWeight: null, strokeLeftWeight: null,
  }
  if (!('strokeWeight' in node)) return empty

  type WithStrokeCap = { strokeCap: StrokeCap | symbol }
  type WithStrokeJoin = { strokeJoin: StrokeJoin | symbol }
  type WithMiter = { strokeMiterLimit: number }
  const strokeCap = 'strokeCap' in node
    ? (typeof (node as unknown as WithStrokeCap).strokeCap === 'symbol'
        ? 'mixed'
        : String((node as unknown as WithStrokeCap).strokeCap))
    : null
  const strokeJoin = 'strokeJoin' in node
    ? (typeof (node as unknown as WithStrokeJoin).strokeJoin === 'symbol'
        ? 'mixed'
        : String((node as unknown as WithStrokeJoin).strokeJoin))
    : null
  const strokeMiterLimit = 'strokeMiterLimit' in node
    ? ((node as unknown as WithMiter).strokeMiterLimit ?? null)
    : null

  type WithSideWeights = {
    strokeTopWeight?: number; strokeRightWeight?: number
    strokeBottomWeight?: number; strokeLeftWeight?: number
  }
  const sw = node as unknown as WithSideWeights
  const strokeTopWeight = 'strokeTopWeight' in node ? (sw.strokeTopWeight ?? null) : null
  const strokeRightWeight = 'strokeRightWeight' in node ? (sw.strokeRightWeight ?? null) : null
  const strokeBottomWeight = 'strokeBottomWeight' in node ? (sw.strokeBottomWeight ?? null) : null
  const strokeLeftWeight = 'strokeLeftWeight' in node ? (sw.strokeLeftWeight ?? null) : null

  return {
    strokeWeight: typeof (node as FrameNode).strokeWeight === 'number'
      ? (node as FrameNode).strokeWeight as number
      : null,
    strokeAlign: 'strokeAlign' in node ? String((node as FrameNode).strokeAlign) : null,
    dashPattern: 'dashPattern' in node ? ((node as FrameNode).dashPattern as readonly number[]) : [],
    strokeCap,
    strokeJoin,
    strokeMiterLimit,
    strokeTopWeight,
    strokeRightWeight,
    strokeBottomWeight,
    strokeLeftWeight,
  }
}

// ─── Layout grid export ───────────────────────────────────────────────────────

function exportLayoutGrids(grids: ReadonlyArray<LayoutGrid>): object[] {
  return grids.map((g) => {
    const base: Record<string, unknown> = {
      pattern: g.pattern,
      visible: g.visible,
      color: g.color ? exportColor(g.color) : null,
      sectionSize: g.sectionSize,
    }
    if (g.pattern === 'COLUMNS' || g.pattern === 'ROWS') {
      base.count = g.count
      base.gutterSize = g.gutterSize
      base.alignment = g.alignment
      base.offset = g.offset
    }
    return base
  })
}

// ─── Text segments ────────────────────────────────────────────────────────────

function exportTextSegments(node: TextNode): object[] {
  try {
    const segments = node.getStyledTextSegments([
      'fontName',
      'fontSize',
      'fontWeight',
      'fills',
      'letterSpacing',
      'lineHeight',
      'textDecoration',
      'textCase',
      'hyperlink',
      'indentation',
      'listSpacing',
    ])
    return segments.map((seg) => {
      const solidFill = (seg.fills as Paint[]).find((p): p is SolidPaint => p.type === 'SOLID')
      return {
        text: seg.characters,
        start: seg.start,
        end: seg.end,
        fontFamily: seg.fontName.family,
        fontStyle: seg.fontName.style,
        fontSize: seg.fontSize,
        fontWeight: seg.fontWeight,
        color: solidFill ? exportColor(solidFill.color) : null,
        fills: (seg.fills as Paint[]).map((p) => {
          if (p.type === 'SOLID') return { type: 'SOLID', color: exportColor(p.color), opacity: p.opacity ?? 1 }
          return { type: p.type }
        }),
        letterSpacing: seg.letterSpacing,
        lineHeight: seg.lineHeight,
        textDecoration: seg.textDecoration,
        textCase: seg.textCase,
        hyperlink: seg.hyperlink ?? null,
        indentation: seg.indentation,
        listSpacing: seg.listSpacing,
      }
    })
  } catch {
    return [{ text: node.characters }]
  }
}

// ─── Node export ──────────────────────────────────────────────────────────────

type ExportedNode = {
  id: string
  irId: string
  name: string
  type: string
  visible: boolean
  locked: boolean
  x: number
  y: number
  rootX: number
  rootY: number
  width: number
  height: number
  rotation: number
  opacity: number
  blendMode: string
  isMask: boolean
  maskType: string
  absoluteRenderBounds: { x: number; y: number; width: number; height: number } | null
  fills: object[]
  strokes: object[]
  strokeWeight: number | null
  strokeAlign: string | null
  dashPattern: readonly number[]
  strokeCap: string | null
  strokeJoin: string | null
  strokeMiterLimit: number | null
  strokeTopWeight: number | null
  strokeRightWeight: number | null
  strokeBottomWeight: number | null
  strokeLeftWeight: number | null
  effects: object[]
  cornerRadius: number
  rectangleCornerRadii?: [number, number, number, number]
  constraints?: object
  layoutSizingHorizontal?: string
  layoutSizingVertical?: string
  layoutPositioning?: string
  layout: object
  layoutGrids?: object[]
  characters?: string
  fontFamily?: string
  fontStyle?: string
  fontSize?: number | string
  fontWeight?: number | string
  letterSpacing?: object | string
  lineHeight?: object | string
  paragraphSpacing?: number | string
  paragraphIndent?: number | string
  textDecoration?: string
  textCase?: string
  textAutoResize?: string
  maxLines?: number | null
  listSpacing?: number | string
  textAlignHorizontal?: string
  textAlignVertical?: string
  segments?: object[]
  componentPropertyDefinitions?: object
  variantProperties?: object | null
  componentProperties?: object
  mainComponentId?: string | null
  vectorFile?: string
  exportScale?: number
  nineSlice?: boolean
  nineSliceImageFile?: string
  nineSliceBorderLeft?: number
  nineSliceBorderRight?: number
  nineSliceBorderTop?: number
  nineSliceBorderBottom?: number
  nineSliceReferenceWidth?: number
  nineSliceReferenceHeight?: number
  nineSlicePart?: boolean
  children: ExportedNode[]
}

// ─── 9-slice structure detection ──────────────────────────────────────────────

interface NineSliceInfo {
  borderLeft: number
  borderRight: number
  borderTop: number
  borderBottom: number
  sharedImageHash: string | null
}

function detectNineSlice(node: SceneNode): NineSliceInfo | null {
  if (!('children' in node)) return null
  const frame = node as FrameNode
  if (frame.children.length !== 9) return null

  const hashes: string[] = []
  for (const child of frame.children as SceneNode[]) {
    if (!('fills' in child)) return null
    const fills = (child as RectangleNode).fills
    if (!Array.isArray(fills)) return null
    const imgFill = (fills as Paint[]).find((f): f is ImagePaint => f.type === 'IMAGE')
    if (!imgFill) return null
    if (imgFill.imageHash) hashes.push(imgFill.imageHash)
  }

  const children = frame.children as SceneNode[]
  const xs = [...new Set(children.map((c) => Math.round(c.x)))].sort((a, b) => a - b)
  const ys = [...new Set(children.map((c) => Math.round(c.y)))].sort((a, b) => a - b)
  if (xs.length !== 3 || ys.length !== 3) return null

  for (const x of xs) {
    for (const y of ys) {
      if (!children.some((c) => Math.round(c.x) === x && Math.round(c.y) === y)) return null
    }
  }

  const borderLeft   = xs[1]
  const borderTop    = ys[1]
  const borderRight  = Math.round(node.width)  - xs[2]
  const borderBottom = Math.round(node.height) - ys[2]
  if (borderLeft <= 0 || borderTop <= 0 || borderRight <= 0 || borderBottom <= 0) return null

  const sharedImageHash = hashes.length === 9 && new Set(hashes).size === 1 ? hashes[0] : null

  return { borderLeft, borderRight, borderTop, borderBottom, sharedImageHash }
}

// ─── Page pre-scan ────────────────────────────────────────────────────────────

function preScanPage(page: PageNode): {
  imageHashMeta: Record<string, ImageHashMeta>
  hashToNineSlice: Record<string, NineSliceComponentInfo>
  nameToNineSlice: Record<string, NineSliceComponentInfo>
} {
  const imageHashMeta: Record<string, ImageHashMeta> = {}
  const hashToNineSlice: Record<string, NineSliceComponentInfo> = {}
  const nameToNineSlice: Record<string, NineSliceComponentInfo> = {}

  function collectFills(node: SceneNode, nameNode: SceneNode): void {
    if (!('fills' in node) || !Array.isArray((node as FrameNode).fills)) return
    for (const f of (node as FrameNode).fills as Paint[]) {
      if (f.type !== 'IMAGE') continue
      const ip = f as ImagePaint
      if (!ip.imageHash || ip.scaleMode === 'CROP') continue
      if (!imageHashMeta[ip.imageHash]) {
        imageHashMeta[ip.imageHash] = {
          filename: slugify(nameNode.name) + '.png',
          width: Math.round(nameNode.width),
          height: Math.round(nameNode.height),
        }
      }
    }
  }

  function visitPass1(node: SceneNode): void {
    if (node.type === 'COMPONENT' && detectNineSlice(node)) return
    const isContainer = node.type === 'FRAME' || node.type === 'COMPONENT' || node.type === 'COMPONENT_SET'
    if (isContainer) {
      collectFills(node, node)
    }
    if ('children' in node) {
      for (const child of (node as FrameNode).children) visitPass1(child)
    }
  }

  function visitPass2(node: SceneNode): void {
    if (node.type === 'COMPONENT') {
      const compNode = node as ComponentNode
      const ns = detectNineSlice(compNode)
      if (ns && Array.isArray(compNode.fills)) {
        const imageFills = (compNode.fills as Paint[]).filter(
          (f): f is ImagePaint => f.type === 'IMAGE' && !!(f as ImagePaint).imageHash
        )
        if (imageFills.length > 0) {
          const primaryHash = imageFills[0].imageHash!
          const meta = imageHashMeta[primaryHash]
          const info: NineSliceComponentInfo = {
            primaryHash,
            primaryFilename: meta ? meta.filename : slugify(node.name) + '.png',
            refWidth:  meta ? meta.width  : Math.round(node.width),
            refHeight: meta ? meta.height : Math.round(node.height),
            borders: {
              borderLeft: ns.borderLeft, borderRight: ns.borderRight,
              borderTop: ns.borderTop,   borderBottom: ns.borderBottom,
            },
            componentName: node.name,
          }
          nameToNineSlice[node.name] = info
          for (const f of imageFills) {
            if (f.imageHash && !hashToNineSlice[f.imageHash]) {
              hashToNineSlice[f.imageHash] = info
            }
          }
        }
      }
    }
    if ('children' in node) {
      for (const child of (node as FrameNode).children) visitPass2(child)
    }
  }

  for (const top of page.children) visitPass1(top as SceneNode)
  for (const top of page.children) visitPass2(top as SceneNode)

  return { imageHashMeta, hashToNineSlice, nameToNineSlice }
}

// ─── Nine-slice image registration ───────────────────────────────────────────

async function registerNineSliceImage(
  ns: NineSliceInfo,
  componentId: string,
  componentName: string,
  fallbackNode: FrameNode | ComponentNode,
  ctx: ExportCtx
): Promise<string> {
  const filename = slugify(componentName) + '.png'

  if (ns.sharedImageHash) {
    const hash = ns.sharedImageHash
    if (!ctx.maps.images[hash]) {
      ctx.usedFilenames.add(filename)
      ctx.maps.images[hash] = { filename, bytes: null }
    }
    return ctx.maps.images[hash].filename
  }

  const mapKey = '__nineSlice__' + componentId
  if (!ctx.maps.images[mapKey]) {
    ctx.usedFilenames.add(filename)
    ctx.maps.images[mapKey] = { filename, bytes: null }
    if (ctx.includeAssets) {
      try {
        const bytes = await fallbackNode.exportAsync({ format: 'PNG', constraint: { type: 'SCALE', value: 1 } })
        ctx.maps.images[mapKey].bytes = bytes
      } catch { /* export failed */ }
    }
  }
  return ctx.maps.images[mapKey].filename
}

// ─── Apply nine-slice info ────────────────────────────────────────────────────

function applyNineSliceInfo(
  result: ExportedNode,
  info: NineSliceComponentInfo,
  ctx: ExportCtx
): void {
  result.nineSlice = true
  result.nineSliceImageFile = info.primaryFilename
  result.nineSliceBorderLeft   = info.borders.borderLeft
  result.nineSliceBorderRight  = info.borders.borderRight
  result.nineSliceBorderTop    = info.borders.borderTop
  result.nineSliceBorderBottom = info.borders.borderBottom
  result.nineSliceReferenceWidth  = info.refWidth
  result.nineSliceReferenceHeight = info.refHeight

  if (!ctx.maps.images[info.primaryHash]) {
    ctx.usedFilenames.add(info.primaryFilename)
    ctx.maps.images[info.primaryHash] = { filename: info.primaryFilename, bytes: null }
  }

  result.fills = [{
    type: 'IMAGE',
    imageHash: info.primaryHash,
    imageFile: info.primaryFilename,
    scaleMode: 'FILL',
    opacity: 1,
    blendMode: 'NORMAL',
  }]
}

interface ExportCtx {
  maps: AssetMaps
  rootAbsX: number
  rootAbsY: number
  includeAssets: boolean
  usedFilenames: Set<string>
  imageHashMeta: Record<string, ImageHashMeta>
  hashToNineSlice: Record<string, NineSliceComponentInfo>
  nameToNineSlice: Record<string, NineSliceComponentInfo>
}

async function exportNode(node: SceneNode, ctx: ExportCtx, isRoot: boolean): Promise<ExportedNode> {
  const abs = absolutePosition(node)

  const x = isRoot ? 0 : node.x
  const y = isRoot ? 0 : node.y
  const rootX = abs.x - ctx.rootAbsX
  const rootY = abs.y - ctx.rootAbsY

  const nodeSlug = slugify(node.name)
  const fills: object[] = 'fills' in node && Array.isArray(node.fills)
    ? (node.fills as Paint[]).map((p, i) => exportPaint(p, ctx.maps.images, ctx.includeAssets, nodeSlug, i, ctx.usedFilenames, ctx.imageHashMeta))
    : []

  const strokes: object[] = 'strokes' in node
    ? (node.strokes as Paint[]).map((p, i) => exportPaint(p, ctx.maps.images, ctx.includeAssets, nodeSlug + '-stroke', i, ctx.usedFilenames, ctx.imageHashMeta))
    : []

  const sp = exportStrokeProps(node)

  const effects: object[] = 'effects' in node
    ? (node.effects as Effect[])
        .map((e): object | null => {
          if (e.type === 'DROP_SHADOW' || e.type === 'INNER_SHADOW') {
            return {
              type: e.type, visible: e.visible,
              color: e.color ? exportColor(e.color) : null, offset: e.offset,
              radius: e.radius, spread: e.spread ?? 0,
              blendMode: e.blendMode,
            }
          }
          if (e.type === 'LAYER_BLUR' || e.type === 'BACKGROUND_BLUR') {
            return { type: e.type, visible: e.visible, radius: e.radius }
          }
          return null
        })
        .filter((e): e is object => e !== null)
    : []

  let cornerRadius = 0
  let rectangleCornerRadii: [number, number, number, number] | undefined
  if ('cornerRadius' in node) {
    const cr = (node as RectangleNode).cornerRadius
    if (typeof cr === 'symbol') {
      const rect = node as RectangleNode
      const tl = rect.topLeftRadius
      const tr = rect.topRightRadius
      const bl = rect.bottomLeftRadius
      const br = rect.bottomRightRadius
      cornerRadius = Math.max(tl, tr, bl, br)
      rectangleCornerRadii = [tl, tr, br, bl]
    } else {
      cornerRadius = cr ?? 0
    }
  }

  let layout: object = { layoutMode: 'NONE' }
  let layoutGrids: object[] | undefined
  if (node.type === 'FRAME' || node.type === 'COMPONENT' || node.type === 'INSTANCE') {
    const f = node as FrameNode
    const layoutObj: Record<string, unknown> = {
      layoutMode: f.layoutMode,
      primaryAxisAlignItems: f.primaryAxisAlignItems,
      counterAxisAlignItems: f.counterAxisAlignItems,
      primaryAxisSizingMode: f.primaryAxisSizingMode,
      counterAxisSizingMode: f.counterAxisSizingMode,
      paddingTop: f.paddingTop,
      paddingRight: f.paddingRight,
      paddingBottom: f.paddingBottom,
      paddingLeft: f.paddingLeft,
      itemSpacing: f.itemSpacing,
      clipsContent: f.clipsContent,
    }
    if (f.layoutMode !== 'NONE') {
      layoutObj.layoutWrap = f.layoutWrap
      if (f.layoutWrap === 'WRAP') {
        layoutObj.counterAxisAlignContent = f.counterAxisAlignContent
        layoutObj.counterAxisSpacing = f.counterAxisSpacing
      }
      if ('strokesIncludedInLayout' in f) {
        layoutObj.strokesIncludedInLayout = f.strokesIncludedInLayout
      }
    }
    layout = layoutObj
    if (f.layoutGrids && f.layoutGrids.length > 0) {
      layoutGrids = exportLayoutGrids(f.layoutGrids)
    }
  }

  const blendMode = 'blendMode' in node
    ? String((node as FrameNode).blendMode)
    : 'NORMAL'
  const isMask = 'isMask' in node ? (node as FrameNode).isMask : false
  const maskType = 'maskType' in node ? String((node as FrameNode).maskType) : 'ALPHA'
  const locked = 'locked' in node ? (node as FrameNode).locked : false

  let absoluteRenderBounds: { x: number; y: number; width: number; height: number } | null = null
  if ('absoluteRenderBounds' in node) {
    const b = (node as FrameNode).absoluteRenderBounds
    absoluteRenderBounds = b ? { x: b.x, y: b.y, width: b.width, height: b.height } : null
  }

  const result: ExportedNode = {
    id: node.id,
    irId: makeIrId(node.name, node.id),
    name: node.name,
    type: node.type,
    visible: node.visible,
    locked,
    x,
    y,
    rootX,
    rootY,
    width: node.width,
    height: node.height,
    rotation: 'rotation' in node ? ((node as FrameNode).rotation ?? 0) : 0,
    opacity: 'opacity' in node ? ((node as FrameNode).opacity ?? 1) : 1,
    blendMode,
    isMask,
    maskType,
    absoluteRenderBounds,
    fills,
    strokes,
    strokeWeight: sp.strokeWeight,
    strokeAlign: sp.strokeAlign,
    dashPattern: sp.dashPattern,
    strokeCap: sp.strokeCap,
    strokeJoin: sp.strokeJoin,
    strokeMiterLimit: sp.strokeMiterLimit,
    strokeTopWeight: sp.strokeTopWeight,
    strokeRightWeight: sp.strokeRightWeight,
    strokeBottomWeight: sp.strokeBottomWeight,
    strokeLeftWeight: sp.strokeLeftWeight,
    effects,
    cornerRadius,
    layout,
    children: [],
  }

  if (rectangleCornerRadii) result.rectangleCornerRadii = rectangleCornerRadii
  if (layoutGrids) result.layoutGrids = layoutGrids

  if ('constraints' in node) {
    result.constraints = {
      horizontal: (node as RectangleNode).constraints.horizontal,
      vertical: (node as RectangleNode).constraints.vertical,
    }
  }

  if ('layoutSizingHorizontal' in node) result.layoutSizingHorizontal = (node as FrameNode).layoutSizingHorizontal
  if ('layoutSizingVertical' in node) result.layoutSizingVertical = (node as FrameNode).layoutSizingVertical
  if ('layoutPositioning' in node) result.layoutPositioning = (node as FrameNode).layoutPositioning

  if (node.type === 'TEXT') {
    const t = node as TextNode
    result.characters = t.characters
    result.fontFamily = t.fontName !== figma.mixed ? (t.fontName as FontName).family : 'mixed'
    result.fontStyle = t.fontName !== figma.mixed ? (t.fontName as FontName).style : 'mixed'
    result.fontSize = t.fontSize !== figma.mixed ? (t.fontSize as number) : 'mixed'
    result.fontWeight = t.fontWeight !== figma.mixed ? (t.fontWeight as number) : 'mixed'
    result.letterSpacing = mixedVal(t.letterSpacing) as object | 'mixed'
    result.lineHeight = mixedVal(t.lineHeight) as object | 'mixed'
    result.paragraphSpacing = mixedVal(t.paragraphSpacing) as number | 'mixed'
    if ('paragraphIndent' in t) {
      result.paragraphIndent = mixedVal((t as TextNode & { paragraphIndent: number | symbol }).paragraphIndent) as number | 'mixed'
    }
    result.textDecoration = String(mixedVal(t.textDecoration))
    result.textCase = String(mixedVal(t.textCase))
    result.textAutoResize = t.textAutoResize
    result.maxLines = t.maxLines
    if ('listSpacing' in t) {
      result.listSpacing = mixedVal((t as TextNode & { listSpacing: number | symbol }).listSpacing) as number | 'mixed'
    }
    result.textAlignHorizontal = t.textAlignHorizontal
    result.textAlignVertical = t.textAlignVertical
    result.segments = exportTextSegments(t)
  }

  if (node.type === 'COMPONENT') {
    const comp = node as ComponentNode
    result.componentPropertyDefinitions = comp.componentPropertyDefinitions as object
    if ('variantProperties' in comp) {
      result.variantProperties = (comp as ComponentNode & { variantProperties: object | null }).variantProperties
    }
  }

  if (node.type === 'COMPONENT_SET') {
    result.componentPropertyDefinitions = (node as ComponentSetNode).componentPropertyDefinitions as object
  }

  if ('children' in node) {
    result.children = await Promise.all((node as FrameNode).children.map((child) => exportNode(child, ctx, false)))
  }

  // ─── Nine-slice handling ──────────────────────────────────────────────────
  const ns = detectNineSlice(node)

  if (node.type === 'INSTANCE') {
    const inst = node as InstanceNode
    result.componentProperties = inst.componentProperties as object
    const mc = await inst.getMainComponentAsync()
    result.mainComponentId = mc?.id ?? null

    const mcNs = mc ? detectNineSlice(mc) : null
    const effectiveNs = ns ?? mcNs

    if (effectiveNs) {
      for (const child of result.children) child.nineSlicePart = true
      const mcName = mc?.name ?? node.name
      const info = ctx.nameToNineSlice[mcName]
      if (info) {
        applyNineSliceInfo(result, info, ctx)
      } else {
        const srcNs = mc ? (detectNineSlice(mc) ?? effectiveNs) : effectiveNs
        const srcId = mc?.id ?? node.id
        const srcName = mc?.name ?? node.name
        const srcNode = (mc ?? node) as unknown as ComponentNode
        result.nineSlice = true
        result.nineSliceBorderLeft   = effectiveNs.borderLeft
        result.nineSliceBorderRight  = effectiveNs.borderRight
        result.nineSliceBorderTop    = effectiveNs.borderTop
        result.nineSliceBorderBottom = effectiveNs.borderBottom
        result.nineSliceReferenceWidth  = mc ? Math.round(mc.width) : Math.round(node.width)
        result.nineSliceReferenceHeight = mc ? Math.round(mc.height) : Math.round(node.height)
        const imageFile = await registerNineSliceImage(srcNs, srcId, srcName, srcNode, ctx)
        result.nineSliceImageFile = imageFile
        result.fills = [{ type: 'IMAGE', imageHash: null, imageFile, scaleMode: 'FILL', opacity: 1, blendMode: 'NORMAL' }]
      }
    }
  } else if (ns) {
    for (const child of result.children) child.nineSlicePart = true
    const info = ctx.nameToNineSlice[node.name]
    if (info) {
      applyNineSliceInfo(result, info, ctx)
    } else {
      result.nineSlice = true
      result.nineSliceBorderLeft   = ns.borderLeft
      result.nineSliceBorderRight  = ns.borderRight
      result.nineSliceBorderTop    = ns.borderTop
      result.nineSliceBorderBottom = ns.borderBottom
      result.nineSliceReferenceWidth  = Math.round(node.width)
      result.nineSliceReferenceHeight = Math.round(node.height)
      const imageFile = await registerNineSliceImage(ns, node.id, node.name, node as unknown as FrameNode, ctx)
      result.nineSliceImageFile = imageFile
      result.fills = [{ type: 'IMAGE', imageHash: null, imageFile, scaleMode: 'FILL', opacity: 1, blendMode: 'NORMAL' }]
    }
  } else {
    let inheritedInfo: NineSliceComponentInfo | null = null

    if ('fills' in node && Array.isArray((node as FrameNode).fills)) {
      for (const f of (node as FrameNode).fills as Paint[]) {
        if (f.type === 'IMAGE') {
          const hash = (f as ImagePaint).imageHash
          if (hash && ctx.hashToNineSlice[hash]) {
            inheritedInfo = ctx.hashToNineSlice[hash]
            break
          }
        }
      }
    }

    if (!inheritedInfo && ctx.nameToNineSlice[node.name]) {
      inheritedInfo = ctx.nameToNineSlice[node.name]
    }

    if (inheritedInfo) {
      applyNineSliceInfo(result, inheritedInfo, ctx)
    }
  }

  return result
}

// ─── Utils ────────────────────────────────────────────────────────────────────

function countNodes(node: SceneNode): number {
  let count = 1
  if ('children' in node) {
    for (const child of (node as FrameNode).children) count += countNodes(child)
  }
  return count
}

function serializeReplacer(_key: string, value: unknown): unknown {
  if (typeof value === 'symbol') return 'mixed'
  return value
}

function bytesToBase64(bytes: Uint8Array): string {
  let bin = ''
  const len = bytes.length
  for (let i = 0; i < len; i++) bin += String.fromCharCode(bytes[i])
  return btoa(bin)
}

async function collectImageBytes(imageMap: Record<string, ImageEntry>): Promise<void> {
  for (const [hash, entry] of Object.entries(imageMap)) {
    if (hash.startsWith('__nineSlice__') || entry.bytes !== null) continue
    try {
      const img = figma.getImageByHash(hash)
      if (img) entry.bytes = await img.getBytesAsync()
    } catch { /* image unavailable */ }
  }
}

// ─── XML serializer ───────────────────────────────────────────────────────────

function escapeXml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;')
}

function xmlTag(key: string): string {
  const safe = key.replace(/[^a-zA-Z0-9_\-.]/g, '_')
  return /^[0-9\-.]/.test(safe) ? '_' + safe : safe
}

function singularize(key: string): string {
  if (key === 'children') return 'child'
  if (key.endsWith('ies') && key.length > 3) return key.slice(0, -3) + 'y'
  if (key.endsWith('s') && key.length > 1) return key.slice(0, -1)
  return 'item'
}

function valToXml(key: string, value: unknown, indent: string): string {
  const tag = xmlTag(key)
  if (value === null || value === undefined) {
    return indent + '<' + tag + ' xsi:nil="true"/>\n'
  }
  if (typeof value === 'boolean' || typeof value === 'number') {
    return indent + '<' + tag + '>' + String(value) + '</' + tag + '>\n'
  }
  if (typeof value === 'string') {
    return indent + '<' + tag + '>' + escapeXml(value) + '</' + tag + '>\n'
  }
  if (Array.isArray(value)) {
    if (value.length === 0) return indent + '<' + tag + '/>\n'
    const allPrimitive = (value as unknown[]).every(
      (v) => v === null || typeof v !== 'object'
    )
    if (allPrimitive) {
      const joined = (value as unknown[]).map((v) => (v === null ? 'null' : String(v))).join(', ')
      return indent + '<' + tag + '>' + escapeXml(joined) + '</' + tag + '>\n'
    }
    const childTag = singularize(tag)
    let out = indent + '<' + tag + '>\n'
    for (const item of value as unknown[]) {
      out += valToXml(childTag, item, indent + '  ')
    }
    out += indent + '</' + tag + '>\n'
    return out
  }
  if (typeof value === 'object') {
    let out = indent + '<' + tag + '>\n'
    for (const [k, v] of Object.entries(value as Record<string, unknown>)) {
      out += valToXml(k, v, indent + '  ')
    }
    out += indent + '</' + tag + '>\n'
    return out
  }
  return indent + '<' + tag + '>' + escapeXml(String(value)) + '</' + tag + '>\n'
}

function objectToXml(obj: object): string {
  let xml = '<?xml version="1.0" encoding="UTF-8"?>\n'
  xml += '<design-export xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">\n'
  for (const [k, v] of Object.entries(obj)) {
    xml += valToXml(k, v, '  ')
  }
  xml += '</design-export>\n'
  return xml
}

// ─── Shared export builder ────────────────────────────────────────────────────

interface ExportResult {
  content: string
  filename: string
  maps: AssetMaps
  totalElements: number
  slug: string
}

async function buildExport(
  params: Params,
  target: SceneNode,
  format: 'json' | 'xml'
): Promise<ExportResult> {
  const maps: AssetMaps = { images: {}, vectors: {} }
  const rootAbs = absolutePosition(target)

  const { imageHashMeta, hashToNineSlice, nameToNineSlice } = preScanPage(figma.currentPage)

  const ctx: ExportCtx = {
    maps,
    rootAbsX: rootAbs.x,
    rootAbsY: rootAbs.y,
    includeAssets: params.includeAssets,
    usedFilenames: new Set<string>(),
    imageHashMeta,
    hashToNineSlice,
    nameToNineSlice,
  }

  const exported = await exportNode(target, ctx, true)
  const totalElements = countNodes(target)
  const slug = slugify(target.name)

  const output = {
    metadata: {
      exportedAt: new Date().toISOString(),
      componentName: target.name,
      rootIrId: exported.irId,
      totalElements,
      rootNormalized: true,
      plugin: 'Full design exporter v2 v4 (nineSlice:shared-source)',
      artAssetConvention: { lookup: 'filename' },
    },
    node: exported,
  }

  let content: string
  let filename: string
  if (format === 'xml') {
    const sanitized = JSON.parse(JSON.stringify(output, serializeReplacer)) as object
    content = objectToXml(sanitized)
    filename = slug + '-full.xml'
  } else {
    content = JSON.stringify(output, serializeReplacer, 2)
    filename = slug + '-full.json'
  }

  return { content, filename, maps, totalElements, slug }
}

async function sendExport(result: ExportResult, params: Params, format: 'json' | 'xml'): Promise<void> {
  const imageEntries = Object.values(result.maps.images)
  const hasImages = params.includeAssets && imageEntries.length > 0

  if (hasImages) {
    await collectImageBytes(result.maps.images)
    const imageFiles = imageEntries
      .filter((e) => e.bytes !== null)
      .map((e) => ({ name: e.filename, data: bytesToBase64(e.bytes!) }))
    const zipName = result.slug + '-export.zip'
    figma.ui.postMessage({
      type: 'download-zip',
      zipName,
      jsonFile: { name: result.filename, content: result.content },
      imageFiles,
    })
    const label = format === 'xml' ? 'XML' : 'JSON'
    figma.notify('Exported ' + result.totalElements + ' nodes + ' + imageFiles.length + ' image(s) → ZIP (' + label + ')')
  } else {
    figma.ui.postMessage({ type: 'download', filename: result.filename, content: result.content })
    const label = format === 'xml' ? 'XML' : 'JSON'
    figma.notify('Exported ' + result.totalElements + ' nodes → ' + label)
  }
}

// ─── Actions ──────────────────────────────────────────────────────────────────

function evaluateEnabled_exportJson(selection: readonly SceneNode[]): boolean {
  return selection.length === 1
}

function actionTarget_exportJson(): SceneNode | null {
  const target = singleSelectedTarget()
  if (target == null) return null
  return evaluateEnabled_exportJson([target]) ? target : null
}

async function action_exportJson(params: Params, target: SceneNode): Promise<{ affectedNodes: SceneNode[]; state: unknown | null }> {
  figma.notify('Collecting data…', { timeout: 60000 })
  const result = await buildExport(params, target, 'json')
  await sendExport(result, params, 'json')
  return { affectedNodes: [target], state: null }
}

async function runAction_exportJson(target: SceneNode | null): Promise<void> {
  if (target == null) return
  isExecuting = true
  try {
    const result = await action_exportJson(latestParams, target)
    writeAttachment(target, latestParams, result.state)
    attachRelaunch(result.affectedNodes)
    pushActionStates()
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    figma.notify(message, { error: true })
    throw error
  } finally {
    isExecuting = false
  }
}

function evaluateEnabled_exportXml(selection: readonly SceneNode[]): boolean {
  return selection.length === 1
}

function actionTarget_exportXml(): SceneNode | null {
  const target = singleSelectedTarget()
  if (target == null) return null
  return evaluateEnabled_exportXml([target]) ? target : null
}

async function action_exportXml(params: Params, target: SceneNode): Promise<{ affectedNodes: SceneNode[]; state: unknown | null }> {
  figma.notify('Collecting data…', { timeout: 60000 })
  const result = await buildExport(params, target, 'xml')
  await sendExport(result, params, 'xml')
  return { affectedNodes: [target], state: null }
}

async function runAction_exportXml(target: SceneNode | null): Promise<void> {
  if (target == null) return
  isExecuting = true
  try {
    const result = await action_exportXml(latestParams, target)
    writeAttachment(target, latestParams, result.state)
    attachRelaunch(result.affectedNodes)
    pushActionStates()
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    figma.notify(message, { error: true })
    throw error
  } finally {
    isExecuting = false
  }
}

// ─── UI state ─────────────────────────────────────────────────────────────────

function pushActionStates(): void {
  const enabled = actionTarget_exportJson() != null
  figma.ui.postMessage({
    type: 'action-state',
    actions: {
      exportJson: { enabled, label: 'Export JSON', status: undefined },
      exportXml: { enabled, label: 'Export XML', status: undefined },
    },
  })
}

function refreshSelection(): void {
  if (isExecuting) return
  const target = singleSelectedTarget()
  const attachment = target != null ? readAttachment(target) : null
  latestParams = attachment?.params ?? DEFAULTS
  figma.ui.postMessage({ type: 'params-change', params: latestParams })
  pushActionStates()
}

// ─── Boot ─────────────────────────────────────────────────────────────────────

const initialTarget = singleSelectedTarget()
const initialAttachment = initialTarget != null ? readAttachment(initialTarget) : null
const initialParams: Params = initialAttachment?.params ?? DEFAULTS
latestParams = initialParams

const html = __html__

figma.root.setRelaunchData({ [TOOL_ID]: DISPLAY_NAME })
figma.showUI(html, { width: 280, height: 320 })
pushActionStates()
figma.on('selectionchange', refreshSelection)

figma.ui.onmessage = (msg: RunMsg) => {
  if (msg.type === 'resize') {
    figma.ui.resize(280, Math.max(120, Math.min(900, Math.round(msg.height))))
    return
  }
  if (msg.type === 'action' && msg.id === 'exportJson') {
    const target = actionTarget_exportJson()
    if (target == null) return
    latestParams = normalizeParams(msg.params)
    void runAction_exportJson(target)
  }
  if (msg.type === 'action' && msg.id === 'exportXml') {
    const target = actionTarget_exportXml()
    if (target == null) return
    latestParams = normalizeParams(msg.params)
    void runAction_exportXml(target)
  }
}
