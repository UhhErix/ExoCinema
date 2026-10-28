// Exo's icons are Tabler's (tabler.io/icons, MIT), at Exo's 1.875 line; the paths are Exo Launcher's (ui/src/brand/tabler.ts).
const GLYPHS = {
  'player-play-filled': [1, 'M6 4v16a1 1 0 0 0 1.524.852l13-8a1 1 0 0 0 0-1.704l-13-8A1 1 0 0 0 6 4'],
  'player-pause-filled': [1, 'M9 4H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h2a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2m8 0h-2a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h2a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2'],
  'arrows-maximize': [0, 'M16 4h4v4m-6 2l6-6M8 20H4v-4m0 4l6-6m6 6h4v-4m-6-2l6 6M8 4H4v4m0-4l6 6'],
  'arrows-minimize': [0, 'M5 9h4V5M3 3l6 6m-4 6h4v4m-6 2l6-6m10-6h-4V5m0 4l6-6m-2 12h-4v4m0-4l6 6'],
  'volume': [0, 'M15 8a5 5 0 0 1 0 8m2.7-11a9 9 0 0 1 0 14M6 15H4a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1h2l3.5-4.5A.8.8 0 0 1 11 5v14a.8.8 0 0 1-1.5.5z'],
  'volume-off': [0, 'M15 8a5 5 0 0 1 1.912 4.934m-1.377 2.602A5 5 0 0 1 15 16m2.7-11a9 9 0 0 1 2.362 11.086m-1.676 2.299A9 9 0 0 1 17.7 19M9.069 5.054L9.5 4.5A.8.8 0 0 1 11 5v2m0 4v8a.8.8 0 0 1-1.5.5L6 15H4a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1h2l1.294-1.664M3 3l18 18'],
  'chevron-left': [0, 'm15 6l-6 6l6 6'],
  'chevron-right': [0, 'm9 6l6 6l-6 6'],
  'info': [0, 'M3 12a9 9 0 1 0 18 0a9 9 0 0 0-18 0m9-3h.01M11 12h1v4h1'],
  'check': [0, 'M5 12l5 5L20 7'],
  'minus': [0, 'M5 12h14'],
  'x': [0, 'M18 6L6 18M6 6l12 12'],
  'square': [0, 'M3 5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z'],
  'box-multiple': [0, 'M7 5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2z', 'M17 17v2a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2h2'],
  'search': [0, 'M3 10a7 7 0 1 0 14 0a7 7 0 1 0-14 0m18 11l-6-6'],
  'settings': [0, 'M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 0 0 2.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 0 0 1.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 0 0-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 0 0-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 0 0-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 0 0-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 0 0 1.066-2.573c-.94-1.543.826-3.31 2.37-2.37c1 .608 2.296.07 2.572-1.065', 'M9 12a3 3 0 1 0 6 0a3 3 0 0 0-6 0'],
  'home': [0, 'M5 12H3l9-9l9 9h-2M5 12v7a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-7', 'M9 21v-6a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v6'],
  'home-filled': [1, 'm12.707 2.293l9 9c.63.63.184 1.707-.707 1.707h-1v6a3 3 0 0 1-3 3h-1v-7a3 3 0 0 0-2.824-2.995L13 12h-2a3 3 0 0 0-3 3v7H7a3 3 0 0 1-3-3v-6H3c-.89 0-1.337-1.077-.707-1.707l9-9a1 1 0 0 1 1.414 0M13 14a1 1 0 0 1 1 1v7h-4v-7a1 1 0 0 1 .883-.993L11 14z'],
  'movie': [0, 'M4 6a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2zm4-2v16m8-16v16M4 8h4m-4 8h4m-4-4h16m-4-4h4m-4 8h4'],
  'device-tv': [0, 'M3 9a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2zm13-6l-4 4l-4-4'],
  'device-tv-filled': [1, 'M8.707 2.293L12 5.585l3.293-3.292a1 1 0 0 1 1.32-.083l.094.083a1 1 0 0 1 0 1.414L14.414 6H19a3 3 0 0 1 3 3v9a3 3 0 0 1-3 3H5a3 3 0 0 1-3-3V9a3 3 0 0 1 3-3h4.585L7.293 3.707a1 1 0 0 1 1.414-1.414'],
  'sparkles': [0, 'M16 18a2 2 0 0 1 2 2a2 2 0 0 1 2-2a2 2 0 0 1-2-2a2 2 0 0 1-2 2m0-12a2 2 0 0 1 2 2a2 2 0 0 1 2-2a2 2 0 0 1-2-2a2 2 0 0 1-2 2M9 18a6 6 0 0 1 6-6a6 6 0 0 1-6-6a6 6 0 0 1-6 6a6 6 0 0 1 6 6'],
  'door-exit': [0, 'M13 12v.01M3 21h18M5 21V5a2 2 0 0 1 2-2h7.5M17 13.5V21M14 7h7m-3-3l3 3l-3 3'],
  'adjustments-horizontal': [0, 'M12 6a2 2 0 1 0 4 0a2 2 0 1 0-4 0M4 6h8m4 0h4M6 12a2 2 0 1 0 4 0a2 2 0 1 0-4 0m-2 0h2m4 0h10m-5 6a2 2 0 1 0 4 0a2 2 0 1 0-4 0M4 18h11m4 0h1'],
  'player-skip-forward-filled': [1, 'M3 5v14a1 1 0 0 0 1.504.864l12-7a1 1 0 0 0 0-1.728l-12-7A1 1 0 0 0 3 5m17-1a1 1 0 0 1 .993.883L21 5v14a1 1 0 0 1-1.993.117L19 19V5a1 1 0 0 1 1-1'],
  'user': [0, 'M8 7a4 4 0 1 0 8 0a4 4 0 0 0-8 0M6 21v-2a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v2'],
  'lock': [0, 'M5 13a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2h-10a2 2 0 0 1-2-2zM11 16a1 1 0 1 0 2 0a1 1 0 0 0-2 0M8 11v-4a4 4 0 1 1 8 0v4'],
}

function glyph(svg, name) {
  const [filled, ...paths] = GLYPHS[name]
  svg.setAttribute('viewBox', '0 0 24 24')
  svg.setAttribute('aria-hidden', 'true')
  svg.dataset.glyph = name
  svg.innerHTML = paths.map((d) => filled
    ? `<path fill="currentColor" d="${d}"/>`
    : `<path fill="none" stroke="currentColor" stroke-width="1.875" stroke-linecap="round" stroke-linejoin="round" d="${d}"/>`).join('')
}

function icon(name) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
  glyph(svg, name)
  return svg.outerHTML
}

document.querySelectorAll('svg[data-glyph]').forEach((s) => glyph(s, s.dataset.glyph))

// Light from the press: glass lights from within where it is pressed (glass.css › ::after).
document.addEventListener('pointerdown', (event) => {
  const glass = event.target.closest?.('.exo-glass')
  if (!glass) return
  const box = glass.getBoundingClientRect()
  glass.style.setProperty('--glass-x', `${event.clientX - box.left}px`)
  glass.style.setProperty('--glass-y', `${event.clientY - box.top}px`)
}, { capture: true, passive: true })

// Focus rings only for moving with Tab: a field clicked into, or a button clicked and then a key pressed (Space to
// pause, the arrows to seek), wore Chromium's ring though nobody was tabbing. Tab turns them on; the pointer, off.
document.addEventListener('keydown', (event) => { if (event.key === 'Tab') document.documentElement.classList.add('is-tabbing') }, true)
document.addEventListener('pointerdown', () => document.documentElement.classList.remove('is-tabbing'), true)

// Focus the page moves by itself (a sheet, sign-in, the first run) lands only while Exo Cinema is the active window:
// moving it in a window that just opened in the background brought that window to the front, over a game. Otherwise
// it lands when the window is next activated, if nothing else took it.
function focusWhenActive(el) {
  if (document.hasFocus()) { el.focus({ preventScroll: true }); return }
  window.addEventListener('focus', () => { if (el.isConnected && !el.closest('[hidden], [inert]') && document.activeElement === document.body) el.focus({ preventScroll: true }) }, { once: true })
}

// In the background (another window focused, or minimized) the page's loops pause (app.css › data-exo-inactive).
const markInactive = () => { document.documentElement.dataset.exoInactive = String(document.hidden || !document.hasFocus()) }
for (const type of ['focus', 'blur']) window.addEventListener(type, markInactive)
document.addEventListener('visibilitychange', markInactive)
markInactive()
