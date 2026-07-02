<!doctype html>
<html>
<head>
  <script>
    try {
      window.localStorage.getItem('x')
    } catch (e) {
      const store = new Map()
      const shim = {
        getItem: (k) => (store.has(k) ? store.get(k) : null),
        setItem: (k, v) => { store.set(k, String(v)) },
        removeItem: (k) => { store.delete(k) },
        clear: () => { store.clear() },
        key: (i) => Array.from(store.keys())[i] ?? null,
        get length() { return store.size },
      }
      Object.defineProperty(window, 'localStorage', { value: shim, configurable: true })
    }
  </script>
  <style>
    html, body { margin: 0; padding: 0; height: auto; min-height: 0; }
    #plugin-root { height: auto; }
    body { display: block; }
    body > #plugin-root > fig-footer {
      background-color: var(--figma-color-bg);
      box-shadow: inset 0 1px 0 0 var(--figma-color-border);
      border-radius: 0 0 var(--radius-large) var(--radius-large);
    }
    dialog.fig-fill-picker-dialog {
      width: 300px; max-width: 300px; min-width: 300px;
      max-height: none; height: max-content;
    }
    .asset-count {
      font-size: var(--body-medium-fontSize, 11px);
      color: var(--figma-color-text-secondary);
      padding: 2px var(--spacer-3, 12px) 0;
    }
    .expected-files { padding: 4px var(--spacer-3, 12px) 0; }
    .expected-files-title {
      font-size: var(--body-medium-fontSize, 11px);
      color: var(--figma-color-text-secondary);
      margin-bottom: 3px;
    }
    .expected-file-item {
      display: flex; align-items: center; gap: 5px;
      font-size: var(--body-medium-fontSize, 11px); line-height: 1.5;
    }
    .expected-file-item .dot {
      width: 6px; height: 6px; border-radius: 50%; flex-shrink: 0;
    }
    .expected-file-item .dot.found { background: #18a058; }
    .expected-file-item .dot.missing { background: var(--figma-color-text-secondary); }
    .expected-file-item .name { color: var(--figma-color-text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .expected-file-item .name.missing { color: var(--figma-color-text-secondary); }
  </style>
</head>
<body>
  <div id="plugin-root">
    <fig-content>
      <fig-group name="JSON import">
        <fig-field>
          <label>JSON file</label>
          <fig-input-file id="jsonFile" accepts=".json"></fig-input-file>
        </fig-field>
      </fig-group>
      <fig-group name="XML import">
        <fig-field>
          <label>XML file</label>
          <fig-input-file id="xmlFile" accepts=".xml"></fig-input-file>
        </fig-field>
      </fig-group>
      <fig-group name="Art assets">
        <fig-field>
          <label>PNG files</label>
          <fig-input-file id="imageAssets" accepts="image/*" multiple full></fig-input-file>
        </fig-field>
        <p id="asset-count" class="asset-count" style="display:none"></p>
        <div id="expected-files" class="expected-files" style="display:none"></div>
      </fig-group>
      <fig-group name="Sync settings">
        <fig-field>
          <label>Sync position, size, rotation, corner radius &amp; constraints</label>
          <fig-switch id="syncPosition" checked></fig-switch>
        </fig-field>
        <fig-field>
          <label>Sync visibility &amp; opacity</label>
          <fig-switch id="syncVisibility" checked></fig-switch>
        </fig-field>
        <fig-field>
          <label>Sync text content &amp; font</label>
          <fig-switch id="syncText" checked></fig-switch>
        </fig-field>
        <fig-field>
          <label>Sync fills, strokes &amp; effects</label>
          <fig-switch id="syncFills" checked></fig-switch>
        </fig-field>
        <fig-field>
          <label>Sync art assets (imageFile)</label>
          <fig-switch id="syncImages" checked></fig-switch>
        </fig-field>
        <fig-field>
          <label>Sync nine-slice metadata</label>
          <fig-switch id="syncNineSlice" checked></fig-switch>
        </fig-field>
      </fig-group>
      <fig-group name="Export">
        <fig-field>
          <label>Format</label>
          <fig-options id="exportFormat" options="JSON, XML" value="JSON"></fig-options>
        </fig-field>
      </fig-group>
    </fig-content>
    <fig-footer>
      <fig-button id="action-importSync" type="submit" disabled>Import JSON</fig-button>
      <fig-button id="action-importSyncXml" type="submit" disabled>Import XML</fig-button>
      <fig-button id="action-export" variant="secondary" disabled>Export</fig-button>
    </fig-footer>
  </div>
  <script>
    let lastReportedHeight = 0
    let resizeRaf = 0

    function measurePanelHeight() {
      const root = document.getElementById('plugin-root')
      if (!root) return 0
      let h = Math.ceil(root.getBoundingClientRect().height)
      const openDialog = document.querySelector('dialog.fig-fill-picker-dialog[open]')
      if (openDialog) h = Math.max(h, Math.ceil(openDialog.getBoundingClientRect().bottom))
      return h
    }

    function reportHeight() {
      const h = measurePanelHeight()
      if (h && h !== lastReportedHeight) {
        lastReportedHeight = h
        parent.postMessage({ pluginMessage: { type: 'resize', height: h } }, '*')
      }
    }

    const pluginRoot = document.getElementById('plugin-root')
    if (pluginRoot && typeof ResizeObserver !== 'undefined') {
      new ResizeObserver(() => {
        if (resizeRaf) cancelAnimationFrame(resizeRaf)
        resizeRaf = requestAnimationFrame(reportHeight)
      }).observe(pluginRoot)
    }

    new MutationObserver(() => {
      if (resizeRaf) cancelAnimationFrame(resizeRaf)
      resizeRaf = requestAnimationFrame(reportHeight)
    }).observe(document.body, {
      childList: true, attributes: true,
      attributeFilter: ['open'], subtree: true,
    })

    function parseAttrValue(val) {
      if (val === 'true') return true
      if (val === 'false') return false
      if (val !== '' && !isNaN(Number(val))) return Number(val)
      return val
    }

    function xmlElementToObject(el) {
      const obj = {}
      for (const attr of el.attributes) obj[attr.name] = parseAttrValue(attr.value)
      const ARRAY_WRAPPERS = new Set(['fills','strokes','effects','stops','segments','children','nodes','assetFileNames'])
      const TEXT_ITEM_ARRAYS = new Set(['assetFileNames'])
      const OBJECT_WRAPPERS = new Set(['layout','constraints','transform','offset'])
      for (const child of el.children) {
        const tag = child.tagName
        if (ARRAY_WRAPPERS.has(tag)) {
          if (TEXT_ITEM_ARRAYS.has(tag)) {
            obj[tag] = Array.from(child.children).map(c => c.textContent || '')
          } else {
            obj[tag] = Array.from(child.children).map(c => xmlElementToObject(c))
          }
        } else if (tag === 'rectangleCornerRadii') {
          obj[tag] = [
            parseAttrValue(child.getAttribute('tl') || '0'),
            parseAttrValue(child.getAttribute('tr') || '0'),
            parseAttrValue(child.getAttribute('br') || '0'),
            parseAttrValue(child.getAttribute('bl') || '0'),
          ]
        } else if (OBJECT_WRAPPERS.has(tag)) {
          obj[tag] = xmlElementToObject(child)
        } else if (child.children.length === 0 && child.attributes.length === 0) {
          obj[tag] = child.textContent || ''
        } else {
          obj[tag] = xmlElementToObject(child)
        }
      }
      return obj
    }

    function xmlToImportJson(xmlText) {
      const parser = new DOMParser()
      const doc = parser.parseFromString(xmlText, 'text/xml')
      const parseError = doc.querySelector('parsererror')
      if (parseError) throw new Error('XML parse error: ' + parseError.textContent)
      const root = doc.documentElement
      const tag = root.tagName
      if (tag === 'node') return JSON.stringify({ node: xmlElementToObject(root) })
      const nodesEl = root.querySelector(':scope > nodes')
      if (nodesEl) return JSON.stringify({ nodes: Array.from(nodesEl.children).map(c => xmlElementToObject(c)) })
      const nodeEls = Array.from(root.children).filter(c => c.tagName === 'node')
      if (nodeEls.length === 1) return JSON.stringify({ node: xmlElementToObject(nodeEls[0]) })
      if (nodeEls.length > 1) return JSON.stringify({ nodes: nodeEls.map(c => xmlElementToObject(c)) })
      throw new Error('No node data found in XML')
    }

    async function readFileText(file, forceXml) {
      const text = await file.text()
      const isXml = forceXml || file.name.toLowerCase().endsWith('.xml') || file.type === 'text/xml' || file.type === 'application/xml'
      return { name: file.name, mimeType: file.type, size: file.size, text: isXml ? xmlToImportJson(text) : text }
    }

    function extractAssetFileNames(jsonText) {
      try {
        const parsed = JSON.parse(jsonText)
        const meta = parsed && parsed.metadata
        if (meta && Array.isArray(meta.assetFileNames)) {
          return meta.assetFileNames.filter(n => typeof n === 'string' && n.trim())
        }
      } catch { /* ignore */ }
      return []
    }

    const ARRAY_WRAPPERS_EXPORT = new Set(['fills','strokes','effects','stops','segments','children','nodes'])
    const OBJECT_WRAPPERS_EXPORT = new Set(['layout','constraints','transform','offset'])

    function escapeXmlAttr(val) {
      return String(val).replace(/&/g,'&amp;').replace(/"/g,'&quot;').replace(/</g,'&lt;').replace(/>/g,'&gt;')
    }

    function objectToXmlElement(obj, tagName) {
      let attrs = ''
      let childElements = ''
      for (const key of Object.keys(obj)) {
        const value = obj[key]
        if (value === null || value === undefined) continue
        if (key === 'rectangleCornerRadii' && Array.isArray(value)) {
          childElements += '<rectangleCornerRadii tl="' + escapeXmlAttr(value[0]) + '" tr="' + escapeXmlAttr(value[1]) + '" br="' + escapeXmlAttr(value[2]) + '" bl="' + escapeXmlAttr(value[3]) + '"/>'
          continue
        }
        if (ARRAY_WRAPPERS_EXPORT.has(key) && Array.isArray(value)) {
          let items = ''
          for (const item of value) {
            if (typeof item === 'object' && item !== null) {
              items += objectToXmlElement(item, 'item')
            } else {
              items += '<item>' + escapeXmlAttr(String(item)) + '</item>'
            }
          }
          childElements += '<' + key + '>' + items + '</' + key + '>'
          continue
        }
        if (OBJECT_WRAPPERS_EXPORT.has(key) && typeof value === 'object' && !Array.isArray(value)) {
          childElements += objectToXmlElement(value, key)
          continue
        }
        if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
          attrs += ' ' + key + '="' + escapeXmlAttr(value) + '"'
        }
      }
      if (childElements) return '<' + tagName + attrs + '>' + childElements + '</' + tagName + '>'
      return '<' + tagName + attrs + '/>'
    }

    function jsonToXml(parsed) {
      if (parsed.node) {
        const nodeXml = objectToXmlElement(parsed.node, 'node')
        return '<?xml version="1.0" encoding="UTF-8"?>\n<root>\n' + nodeXml + '\n</root>'
      }
      if (Array.isArray(parsed.nodes)) {
        const nodesXml = parsed.nodes.map(n => objectToXmlElement(n, 'node')).join('\n')
        return '<?xml version="1.0" encoding="UTF-8"?>\n<root>\n<nodes>\n' + nodesXml + '\n</nodes>\n</root>'
      }
      return '<?xml version="1.0" encoding="UTF-8"?>\n<root/>'
    }

    function downloadFile(content, filename, mimeType) {
      const blob = new Blob([content], { type: mimeType })
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url; a.download = filename
      document.body.appendChild(a); a.click()
      document.body.removeChild(a); URL.revokeObjectURL(url)
    }

    const fileValues = {}
    let expectedFileNames = []
    let uploadedFileNamesLower = new Set()

    function updateExpectedFilesDisplay() {
      const container = document.getElementById('expected-files')
      if (!container) return
      if (expectedFileNames.length === 0) { container.style.display = 'none'; return }
      container.style.display = 'block'
      const title = document.createElement('div')
      title.className = 'expected-files-title'
      title.textContent = 'Files needed (' + expectedFileNames.length + '):'
      const items = expectedFileNames.map(name => {
        const found = uploadedFileNamesLower.has(name.toLowerCase())
        const row = document.createElement('div')
        row.className = 'expected-file-item'
        const dot = document.createElement('span')
        dot.className = 'dot ' + (found ? 'found' : 'missing')
        const label = document.createElement('span')
        label.className = 'name' + (found ? '' : ' missing')
        label.textContent = name; label.title = name
        row.appendChild(dot); row.appendChild(label)
        return row
      })
      container.innerHTML = ''
      container.appendChild(title)
      items.forEach(el => container.appendChild(el))
    }

    function currentParams() {
      return {
        jsonFile: fileValues['jsonFile'] ?? null,
        xmlFile: fileValues['xmlFile'] ?? null,
        imageAssets: fileValues['imageAssets'] ?? [],
        syncPosition: Boolean(document.getElementById('syncPosition').checked),
        syncVisibility: Boolean(document.getElementById('syncVisibility').checked),
        syncText: Boolean(document.getElementById('syncText').checked),
        syncFills: Boolean(document.getElementById('syncFills').checked),
        syncImages: Boolean(document.getElementById('syncImages').checked),
        syncNineSlice: Boolean(document.getElementById('syncNineSlice').checked),
        exportFormat: (document.getElementById('exportFormat').value || 'JSON').toLowerCase(),
      }
    }

    document.getElementById('action-importSync').addEventListener('click', () => {
      parent.postMessage({ pluginMessage: { type: 'action', id: 'importSync', params: currentParams() } }, '*')
    })
    document.getElementById('action-importSyncXml').addEventListener('click', () => {
      parent.postMessage({ pluginMessage: { type: 'action', id: 'importSyncXml', params: currentParams() } }, '*')
    })
    document.getElementById('action-export').addEventListener('click', () => {
      parent.postMessage({ pluginMessage: { type: 'action', id: 'export', params: currentParams() } }, '*')
    })

    let lastActionState = null
    function isParamPresent(name) {
      const value = currentParams()[name]
      return value !== null && value !== undefined && value !== ''
    }
    function applyActionStates(stateActions) {
      if (!stateActions) return
      {
        const a = stateActions['importSync']
        const button = document.getElementById('action-importSync')
        if (a && button) {
          if (a.enabled !== false && isParamPresent('jsonFile')) button.removeAttribute('disabled')
          else button.setAttribute('disabled', '')
          if (typeof a.label === 'string') button.textContent = a.label
        }
      }
      {
        const a = stateActions['importSyncXml']
        const button = document.getElementById('action-importSyncXml')
        if (a && button) {
          if (a.enabled !== false && isParamPresent('xmlFile')) button.removeAttribute('disabled')
          else button.setAttribute('disabled', '')
          if (typeof a.label === 'string') button.textContent = a.label
        }
      }
      {
        const a = stateActions['export']
        const button = document.getElementById('action-export')
        if (a && button) {
          if (a.enabled !== false) button.removeAttribute('disabled')
          else button.setAttribute('disabled', '')
        }
      }
    }

    window.addEventListener('message', (event) => {
      const msg = event.data && event.data.pluginMessage
      if (!msg) return
      if (msg.type === 'action-state' && msg.actions) {
        lastActionState = msg.actions
        applyActionStates(lastActionState)
        return
      }
      if (msg.type === 'export-result') {
        const format = (msg.format || 'json').toLowerCase()
        if (format === 'xml') {
          try {
            const xmlContent = jsonToXml(JSON.parse(msg.json))
            const filename = msg.filename.replace(/\.json$/, '.xml')
            downloadFile(xmlContent, filename, 'text/xml')
          } catch (e) {
            console.error('XML conversion failed:', e)
            downloadFile(msg.json, msg.filename, 'application/json')
          }
        } else {
          downloadFile(msg.json, msg.filename, 'application/json')
        }
      }
    })

    {
      const el = document.getElementById('jsonFile')
      el.addEventListener('change', async (event) => {
        const file = event.detail && event.detail.files && event.detail.files[0]
        if (!file) {
          fileValues['jsonFile'] = null; expectedFileNames = []; updateExpectedFilesDisplay()
        } else {
          try {
            const result = await readFileText(file, false)
            fileValues['jsonFile'] = result
            expectedFileNames = extractAssetFileNames(result.text)
            updateExpectedFilesDisplay()
          } catch (error) {
            console.error('Could not read JSON file:', error)
            fileValues['jsonFile'] = null; expectedFileNames = []; updateExpectedFilesDisplay()
          }
        }
        applyActionStates(lastActionState)
      })
    }

    {
      const el = document.getElementById('xmlFile')
      el.addEventListener('change', async (event) => {
        const file = event.detail && event.detail.files && event.detail.files[0]
        if (!file) {
          fileValues['xmlFile'] = null; expectedFileNames = []; updateExpectedFilesDisplay()
        } else {
          try {
            const result = await readFileText(file, true)
            fileValues['xmlFile'] = result
            expectedFileNames = extractAssetFileNames(result.text)
            updateExpectedFilesDisplay()
          } catch (error) {
            console.error('Could not read XML file:', error)
            fileValues['xmlFile'] = null; expectedFileNames = []; updateExpectedFilesDisplay()
          }
        }
        applyActionStates(lastActionState)
      })
    }

    {
      const el = document.getElementById('imageAssets')
      const countEl = document.getElementById('asset-count')
      el.addEventListener('change', async (event) => {
        const files = event.detail && event.detail.files
        if (!files || files.length === 0) {
          fileValues['imageAssets'] = []; uploadedFileNamesLower = new Set()
          countEl.style.display = 'none'; updateExpectedFilesDisplay()
        } else {
          const assets = []; uploadedFileNamesLower = new Set()
          for (const file of files) {
            try {
              const buf = await file.arrayBuffer()
              assets.push({ name: file.name, bytes: new Uint8Array(buf) })
              uploadedFileNamesLower.add(file.name.toLowerCase())
            } catch (err) {
              console.error('Could not read image file:', file.name, err)
            }
          }
          fileValues['imageAssets'] = assets
          if (assets.length > 0) {
            countEl.textContent = assets.length + ' file' + (assets.length !== 1 ? 's' : '') + ' ready'
            countEl.style.display = 'block'
          } else {
            countEl.style.display = 'none'
          }
          updateExpectedFilesDisplay()
        }
        applyActionStates(lastActionState)
      })
    }
  </script>
</body>
</html>
