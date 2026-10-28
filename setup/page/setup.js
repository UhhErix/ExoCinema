// Exo Cinema Setup's screens (setup/src/main.cpp draws them in WebView2), as Exo Launcher's Setup draws its own
// (ui/src/setup/main.tsx there): Install or Update with its options, the progress the install engine reports, then
// Open Exo Cinema; or Uninstall. Nothing here touches the system: every step is asked of the setup window, which runs
// the install engine.
//
// Outside the setup window (a browser, while designing) it is a preview with a stand-in state, and the address picks
// the screen: ?screen=start|working|done|failed, &mode=uninstall, &updating, &options, &progress=0.4, &status=...,
// &problem=..., &version=...
'use strict'

const webview = window.chrome && window.chrome.webview
const send = (type, extra) => { if (webview) webview.postMessage(JSON.stringify(Object.assign({ type }, extra))) }

// Exo's glyphs: Tabler's (tabler.io/icons, MIT) at Exo's 1.875 line, the paths Exo Launcher uses (ui/src/brand/tabler.ts).
const GLYPHS = {
  minus: 'M5 12h14',
  x: 'M18 6L6 18M6 6l12 12',
  'chevron-down': 'm6 9l6 6l6-6',
  check: 'M5 12l5 5L20 7',
  'alert-circle': 'M3 12a9 9 0 1 0 18 0a9 9 0 0 0-18 0m9-4v4m0 4h.01',
}

const STAY = 'Your sign-in and settings stay on this PC.'
const GO = 'Your sign-in and settings go too.'

let state = null // { mode: 'install' | 'uninstall', updating, desktop, version }
let screen = 'start' // start | working | done | failed
let work = { value: null, status: '' } // value: 0 to 1, or null until the engine reports one
let problem = ''
let desktop = true
let removeData = false
let optionsOpen = false
let bar = null
let statusLine = null

const root = document.getElementById('root')

function h(tag, props, ...children) {
  const el = document.createElement(tag)
  for (const [key, value] of Object.entries(props || {})) {
    if (value == null || value === false) continue
    if (key === 'class') el.className = value
    else if (key === 'text') el.textContent = value
    else if (key.startsWith('on')) el.addEventListener(key.slice(2), value)
    else el.setAttribute(key, value === true ? '' : String(value))
  }
  for (const child of children) if (child != null) el.append(child)
  return el
}

function glyph(name) {
  const ns = 'http://www.w3.org/2000/svg'
  const svg = document.createElementNS(ns, 'svg')
  svg.setAttribute('viewBox', '0 0 24 24')
  svg.setAttribute('aria-hidden', 'true')
  const path = document.createElementNS(ns, 'path')
  for (const [k, v] of [['d', GLYPHS[name]], ['fill', 'none'], ['stroke', 'currentColor'], ['stroke-width', '1.875'], ['stroke-linecap', 'round'], ['stroke-linejoin', 'round']]) path.setAttribute(k, v)
  svg.append(path)
  return svg
}

const mark = () => h('img', { class: 'exo-setup-mark', src: 'logo.svg', alt: '', draggable: 'false' })

// Exo's own window buttons: minimize and close at the top right, nothing else; the band drags the window.
function titleBand(busy) {
  return h('header', {
    class: 'exo-setup-bar',
    onpointerdown: (event) => { if (event.button === 0 && !event.target.closest('button')) send('drag') },
  }, h('span', { class: 'exo-setup-bar-actions' },
    h('button', { type: 'button', class: 'exo-winbtn', 'aria-label': 'Minimize', onclick: () => send('minimize') }, glyph('minus')),
    h('button', { type: 'button', class: 'exo-winbtn is-close', 'aria-label': 'Close', disabled: busy, onclick: () => send('close') }, glyph('x'))))
}

function toggle(checked, labelledBy, onChange) {
  const sw = h('button', { type: 'button', role: 'switch', class: 'switch', 'aria-checked': String(checked), 'aria-labelledby': labelledBy },
    h('span', { class: 'switch__control' }, h('span', { class: 'switch__thumb' })))
  sw.addEventListener('click', () => {
    const on = sw.getAttribute('aria-checked') !== 'true'
    sw.setAttribute('aria-checked', String(on))
    onChange(on)
  })
  return sw
}

// A panel that opens with height; closed, nothing in it can be reached.
function optionsPanel(open, ...rows) {
  const panel = h('div', { class: 'exo-setup-options', id: 'exo-setup-options', 'data-open': String(open), 'aria-hidden': String(!open) },
    h('div', { class: 'exo-setup-options-inner' }, ...rows))
  panel.inert = !open
  return panel
}

function install() {
  const panel = optionsPanel(optionsOpen, h('div', { class: 'exo-setup-row' },
    h('span', { id: 'exo-setup-desktop', text: 'Desktop shortcut' }),
    toggle(desktop, 'exo-setup-desktop', (on) => { desktop = on })))
  const more = h('button', { type: 'button', class: 'exo-btn is-ghost exo-setup-more', 'aria-expanded': String(optionsOpen), 'aria-controls': 'exo-setup-options' }, 'Options', glyph('chevron-down'))
  more.addEventListener('click', () => {
    optionsOpen = !optionsOpen
    more.setAttribute('aria-expanded', String(optionsOpen))
    panel.dataset.open = String(optionsOpen)
    panel.setAttribute('aria-hidden', String(!optionsOpen))
    panel.inert = !optionsOpen
  })
  return h('section', { class: 'exo-setup-screen' },
    mark(),
    h('h1', { text: 'Exo Cinema' }),
    h('p', { text: state.updating ? `Update to ${state.version}` : `Version ${state.version}` }),
    h('div', { class: 'exo-setup-actions' },
      h('button', { type: 'button', class: 'exo-btn is-primary exo-setup-cta', 'data-autofocus': true, text: state.updating ? 'Update' : 'Install', onclick: go }),
      more),
    panel)
}

// Exo's question: the title asks it, the line under it says what stays. Your things stay unless you ask.
function uninstallAsk() {
  const line = h('p', { text: removeData ? GO : STAY })
  return h('section', { class: 'exo-setup-screen' },
    mark(),
    h('h1', { text: 'Uninstall Exo Cinema?' }),
    line,
    h('div', { class: 'exo-setup-ask' },
      h('button', { type: 'button', class: 'exo-set-btn', 'data-autofocus': true, text: 'Cancel', onclick: () => send('close') }),
      h('button', { type: 'button', class: 'exo-set-btn is-danger', text: 'Uninstall', onclick: go })),
    optionsPanel(true, h('div', { class: 'exo-setup-row' },
      h('span', { id: 'exo-setup-data', text: 'Also remove my sign-in and settings' }),
      toggle(removeData, 'exo-setup-data', (on) => { removeData = on; line.textContent = on ? GO : STAY }))))
}

function working() {
  const title = state.mode === 'uninstall' ? 'Uninstalling' : `${state.updating ? 'Updating to' : 'Installing'} ${state.version}`
  bar = h('span', { class: 'exo-progress', role: 'progressbar', 'aria-label': title, 'aria-valuemin': '0', 'aria-valuemax': '100' },
    h('span', { class: 'exo-progress-track' }, h('span', { class: 'exo-progress-fill' })))
  statusLine = h('p', { class: 'exo-setup-status', 'aria-live': 'polite' })
  showWork()
  return h('section', { class: 'exo-setup-screen', 'aria-busy': 'true' }, mark(), h('h1', { text: title }), bar, statusLine)
}

// Progress changes in place, so the bar moves rather than starting over.
function showWork() {
  if (!bar) return
  const known = typeof work.value === 'number' && Number.isFinite(work.value)
  const value = known ? Math.min(1, Math.max(0.02, work.value)) : 0
  bar.classList.toggle('is-unknown', !known)
  bar.style.setProperty('--exo-progress', String(value))
  if (known) bar.setAttribute('aria-valuenow', String(Math.round(value * 100)))
  else bar.removeAttribute('aria-valuenow')
  statusLine.textContent = work.status || 'Starting'
}

function done() {
  const removed = state.mode === 'uninstall'
  const title = removed ? 'Exo Cinema was removed' : state.updating ? `Updated to ${state.version}` : 'Installed'
  const close = (primary) => h('button', { type: 'button', class: primary ? 'exo-btn is-primary exo-setup-cta' : 'exo-btn is-ghost', 'data-autofocus': primary, text: 'Close', onclick: () => send('close') })
  return h('section', { class: 'exo-setup-screen' },
    h('span', { class: 'exo-setup-badge is-good' }, glyph('check')),
    h('h1', { text: title }),
    removed ? null : h('p', { text: 'Exo Cinema is ready.' }),
    h('div', { class: 'exo-setup-actions' },
      ...(removed
        ? [close(true)]
        : [h('button', { type: 'button', class: 'exo-btn is-primary exo-setup-cta', 'data-autofocus': true, text: 'Open Exo Cinema', onclick: () => send('launch') }), close(false)])))
}

function failed() {
  const title = state.mode === 'uninstall' ? 'Not uninstalled' : state.updating ? 'Not updated' : 'Not installed'
  return h('section', { class: 'exo-setup-screen' },
    h('span', { class: 'exo-setup-badge is-bad' }, glyph('alert-circle')),
    h('h1', { text: title }),
    h('p', { class: 'exo-setup-problem', text: problem }),
    h('div', { class: 'exo-setup-actions' },
      h('button', { type: 'button', class: 'exo-btn is-primary exo-setup-cta', 'data-autofocus': true, text: 'Close', onclick: () => send('close') })))
}

function render() {
  bar = statusLine = null
  const body = screen === 'working' ? working()
    : screen === 'done' ? done()
      : screen === 'failed' ? failed()
        : state.mode === 'uninstall' ? uninstallAsk() : install()
  root.replaceChildren(h('main', { class: 'exo-setup' }, titleBand(screen === 'working'), body))
  const first = root.querySelector('[data-autofocus]')
  if (first) first.focus({ preventScroll: true })
}

function go() {
  screen = 'working'
  work = { value: null, status: '' }
  render()
  if (state.mode === 'uninstall') send('uninstall', { removeData })
  else send('install', { desktop })
}

// The focus ring is for the keyboard: it shows once a key moves focus, and goes with the next click.
document.addEventListener('keydown', (event) => {
  if (event.key === 'Tab' || event.key.startsWith('Arrow')) document.documentElement.classList.add('is-keyboard')
  if (event.key === 'Escape' && state && screen !== 'working') send('close')
}, true)
document.addEventListener('pointerdown', () => document.documentElement.classList.remove('is-keyboard'), true)

if (webview) {
  webview.addEventListener('message', (event) => {
    const message = event.data || {}
    if (message.type === 'state') {
      const first = !state
      state = message
      desktop = !!message.desktop
      render()
      // The window shows once the first screen has drawn, so it never opens empty.
      if (first) requestAnimationFrame(() => requestAnimationFrame(() => send('shown')))
    } else if (message.type === 'progress') {
      work = { value: typeof message.value === 'number' ? message.value : null, status: message.status || '' }
      showWork()
    } else if (message.type === 'done') {
      screen = 'done'
      render()
    } else if (message.type === 'failed') {
      problem = message.message || ''
      screen = 'failed'
      render()
    }
  })
  send('hello')
} else {
  const q = new URLSearchParams(location.search)
  state = { mode: q.get('mode') === 'uninstall' ? 'uninstall' : 'install', updating: q.has('updating'), desktop: true, version: q.get('version') || '1.0.0' }
  screen = ['start', 'working', 'done', 'failed'].includes(q.get('screen')) ? q.get('screen') : 'start'
  optionsOpen = q.has('options')
  work = { value: q.has('progress') ? Number(q.get('progress')) : null, status: q.get('status') || '' }
  problem = q.get('problem') || "Exo Cinema is still running and didn't close. Close it, then try again. Nothing installed was changed."
  render()
}
