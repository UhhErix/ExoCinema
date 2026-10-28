// Exo's window chrome: no Windows caption. Minimize, maximize and close sit in the right capsule after a hairline
// (Exo Launcher's WindowChrome.tsx). The page measures every control in the Bar's band and sends it to the host
// (Exo's shell.setChromeLayout); the host makes the rest of the band Windows' own caption, and the maximize
// button Windows' own maximize region, so dragging, snapping, double-click and the snap layouts flyout are Windows'.

const winState = { maximized: false, fullscreen: false }
const BAND = 52 // CaptionDragRegions.TitleBarDragDip

function windowControls() {
  return `<span class="divider" aria-hidden="true"></span>
    <button type="button" class="exo-winbtn" data-win="minimize" aria-label="Minimize">${icon('minus')}</button>
    <button type="button" class="exo-winbtn" data-win="maximize" data-exo-maximize aria-label="${winState.maximized ? 'Restore' : 'Maximize'}">${icon(winState.maximized ? 'box-multiple' : 'square')}</button>
    <button type="button" class="exo-winbtn is-close" data-win="close" aria-label="Close">${icon('x')}</button>`
}

function drawWindowControls() {
  document.querySelectorAll('[data-window-controls]').forEach((el) => { el.innerHTML = windowControls() })
  measureChrome()
}

// Every visible control whose box reaches into the band is a hole in the caption; the maximize button is
// Windows' maximize region.
let lastChrome = ''
function measureChrome() {
  // Shown means laid out and not in the player's resting chrome; opacity is not asked, since controls fading back in
  // (opacity 0 the moment they return) were left out, and a click on Back started a window drag.
  const visible = (el) => el.offsetParent !== null && getComputedStyle(el).visibility !== 'hidden' && !el.closest('#stage.is-idle')
  const box = (el) => {
    const r = el.getBoundingClientRect()
    return { x: r.left, y: r.top, w: r.width, h: r.height }
  }
  const regions = [...document.querySelectorAll('button, a, input, label, [role="tab"], [data-no-drag], .exo-glass')]
    .filter((el) => visible(el) && !el.matches('[data-exo-maximize]') && el.getBoundingClientRect().top < BAND && el.getBoundingClientRect().bottom > 0)
    .map(box)
  const max = [...document.querySelectorAll('[data-exo-maximize]')].find(visible)
  const chrome = { regions, maximize: max ? box(max) : null }
  const key = JSON.stringify(chrome)
  if (key === lastChrome) return
  lastChrome = key
  send({ cmd: 'chrome', ...chrome })
}

let measureQueued = false
function queueMeasure() {
  if (measureQueued) return
  measureQueued = true
  requestAnimationFrame(() => { measureQueued = false; measureChrome() })
}

new ResizeObserver(queueMeasure).observe(document.documentElement)
new MutationObserver(queueMeasure).observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['class', 'hidden', 'style'] })
document.addEventListener('transitionend', queueMeasure)

drawWindowControls()

window.chrome.webview.addEventListener('message', ({ data: msg }) => {
  if (msg.type === 'window') {
    winState.maximized = msg.maximized
    document.documentElement.toggleAttribute('data-maximized', msg.maximized)
    drawWindowControls()
  } else if (msg.type === 'fullscreen') {
    winState.fullscreen = msg.on
    document.documentElement.toggleAttribute('data-fullscreen', msg.on)
    queueMeasure()
  } else if (msg.type === 'maximize-hover') {
    // Windows owns the maximize button's area (snap layouts), so the page hears of its hover from the host.
    document.querySelectorAll('[data-exo-maximize]').forEach((b) => b.classList.toggle('is-hover', msg.on))
  }
})

document.addEventListener('click', (e) => {
  const b = e.target.closest('[data-win]')
  if (b) send({ cmd: 'window', do: b.dataset.win })
})
