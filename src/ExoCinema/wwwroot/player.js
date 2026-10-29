// Exo Cinema's player chrome. The host (PlayerWindow.cs) owns mpv; this page sends commands and draws state
// from the property changes the host posts.

const $ = (id) => document.getElementById(id)
const stage = $('stage')
const send = (msg) => window.chrome.webview.postMessage(msg)

const state = {
  pause: true,
  duration: 0,
  time: 0,
  timeAt: performance.now(),
  cache: 0,
  buffering: false,
  mute: false,
  fullscreen: false,
  dragging: false,
  props: {},
  timings: {},
  tuning: {},
}

// The host's replies and pushes, for app.js too.
const listeners = new Set()
const onHost = (fn) => listeners.add(fn)

const fmt = (s) => {
  if (!Number.isFinite(s) || s < 0) s = 0
  s = Math.floor(s)
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60
  return (h ? `${h}:${String(m).padStart(2, '0')}` : `${m}`) + `:${String(sec).padStart(2, '0')}`
}

const inPlayer = () => document.body.classList.contains('mode-player')

// Between the host's posts (every 100 ms) the position moves on its own, so the bar glides; never more than a quarter
// second past mpv's last word, so a stream that stalls without saying so can't run the bar ahead of the picture.
// While a seek is on its way the bar holds its target.
const AHEAD_MAX = 0.25

function currentTime() {
  if (state.seekTarget != null) return state.seekTarget
  if (state.pause || state.waiting) return state.time
  return Math.min(state.duration || Infinity, state.time + Math.min(AHEAD_MAX, (performance.now() - state.timeAt) / 1000))
}

// The spinner says what mpv is doing, never what an event last said: loading a file, seeking, or out of data while
// playing. It shows after a beat, so a quick seek doesn't flash it.
let spinnerTimer = 0
function drawSpinner() {
  const p = state.props
  const busy = inPlayer() && !p['eof-reached'] && (state.loading || p.seeking || p['paused-for-cache'] || (p['core-idle'] && !state.pause))
  state.waiting = !!busy
  clearTimeout(spinnerTimer)
  if (!busy) { $('spinner').hidden = true; return }
  spinnerTimer = setTimeout(() => { if (state.waiting) $('spinner').hidden = false }, 250)
}

// The bar glides frame by frame only while it can be seen and something moves it: playing, dragging or a seek on its
// way. Outside the player, with the controls gone, paused or minimized, nothing runs; a host post or the controls
// coming back draw it again.
let drawFrame = 0
function draw() {
  drawFrame = 0
  if (!inPlayer() || document.hidden || stage.classList.contains('is-idle')) return
  const t = state.dragging ? state.dragTime : currentTime()
  const d = state.duration
  const p = d > 0 ? Math.min(1, t / d) : 0
  $('played').style.transform = `scaleX(${p})`
  $('thumbRail').style.translate = `${p * 100}% 0`
  $('cached').style.transform = `scaleX(${d > 0 ? Math.min(1, state.cache / d) : 0})`
  const pos = fmt(t), dur = fmt(d)
  if ($('pos').textContent !== pos) $('pos').textContent = pos
  if ($('dur').textContent !== dur) $('dur').textContent = dur
  if (!state.pause || state.dragging || state.seekTarget != null) drawFrame = requestAnimationFrame(draw)
}
const redraw = () => { if (!drawFrame) drawFrame = requestAnimationFrame(draw) }
document.addEventListener('visibilitychange', redraw)

function setPause(paused) {
  state.pause = paused
  const btn = document.querySelector('.play')
  glyph(btn.querySelector('svg'), paused ? 'player-play-filled' : 'player-pause-filled')
  btn.setAttribute('aria-label', paused ? 'Play' : 'Pause')
  wake()
}

function drawReadings() {
  const el = $('readings')
  if (el.hidden) return
  const p = state.props
  const rows = [
    ['GPU', state.tuning.gpu],
    ['Picture', state.picture],
    ['Plays by', state.playMethod],
    // Software decoding says what it is decoding, which says why (10-bit H.264 has no hardware decoder anywhere).
    ['Decoder', p['hwdec-current'] && p['hwdec-current'] !== 'no' ? p['hwdec-current'] : `software · ${[p['video-format'], p['video-params/pixelformat']].filter(Boolean).join(' ')}`],
    ['Video', [p['video-codec']?.split(' ')[0], p['video-params/w'] && `${p['video-params/w']}×${p['video-params/h']}`].filter(Boolean).join(' · ')],
    ['Frame rate', p['container-fps'] ? p['container-fps'].toFixed(3) : ''],
    ['Transfer', [p['video-params/gamma'], p['video-params/primaries']].filter(Boolean).join(' · ')],
    ['Audio', p['audio-codec-name']],
    ['First frame', state.timings['first-frame'] != null ? `${state.timings['first-frame']} ms` : ''],
    ['Last seek', state.timings.seek != null ? `${state.timings.seek} ms` : ''],
  ].filter(([, v]) => v)
  el.innerHTML = `<dl>${rows.map(([k, v]) => `<dt>${k}</dt><dd>${v}</dd>`).join('')}</dl>`
  placeGlass()
}

// Glass over video. mpv blurs and tones the picture under the page's glass itself as it draws each frame
// (Shaders/exo-glass.glsl), since backdrop-filter can't see it. The host gets the glass shapes in device pixels,
// topmost first, with how visible each is; while anything over the video moves or fades, they follow it.
let glassSent = ''
let glassUntil = 0
let glassLoop = false
let glassAt = 0

const half = (v) => Math.round(v * 2) / 2

function glassShapes() {
  if (!inPlayer() || document.documentElement.dataset.material === 'solid') return []
  const k = devicePixelRatio
  const shapes = []
  for (const el of [...document.querySelectorAll('[data-over-video]')].reverse()) {
    if (el.hidden) continue
    let seen = 1
    for (let e = el; e && e !== document.body; e = e.parentElement) seen *= Number(getComputedStyle(e).opacity)
    const r = el.getBoundingClientRect()
    if (seen < 0.01 || !r.width || !r.height) continue
    const radius = Math.min(parseFloat(getComputedStyle(el).borderTopLeftRadius) || 0, r.height / 2, r.width / 2)
    shapes.push({ x: half(r.left * k), y: half(r.top * k), w: half(r.width * k), h: half(r.height * k), r: half(radius * k), k: el.classList.contains('is-panel') ? 1 : 0, a: Math.round(seen * 100) / 100 })
  }
  return shapes
}

function sendGlass() {
  const showing = inPlayer() && !stage.classList.contains('is-idle')
  const msg = {
    shapes: glassShapes(),
    scale: devicePixelRatio,
    // Where the subtitles rise to while the controls show: just above them.
    lift: showing ? half((innerHeight - document.querySelector('.controls').getBoundingClientRect().top + 8) * devicePixelRatio) : 0,
  }
  const key = JSON.stringify(msg)
  if (key === glassSent) return
  glassSent = key
  send({ cmd: 'glass', ...msg })
}

// The shapes now and through whatever has just started moving (30 times a second at most).
function placeGlass(ms = 450) {
  sendGlass()
  glassUntil = Math.max(glassUntil, performance.now() + ms)
  if (glassLoop) return
  glassLoop = true
  const step = (t) => {
    if (t - glassAt >= 33) { glassAt = t; sendGlass() }
    if (t < glassUntil) requestAnimationFrame(step)
    else { glassLoop = false; sendGlass() }
  }
  requestAnimationFrame(step)
}

// Each glass shape follows its own size too: a longer title or a menu filling in changes it with nothing else moving.
const glassSizes = new ResizeObserver(() => placeGlass())
glassSizes.observe(document.documentElement)
for (const el of document.querySelectorAll('[data-over-video]')) glassSizes.observe(el)
for (const type of ['transitionrun', 'animationstart']) stage.addEventListener(type, () => placeGlass())

window.chrome.webview.addEventListener('message', ({ data: msg }) => {
  switch (msg.type) {
    case 'hello':
      state.tuning = msg.tuning
      state.picture = msg.picture
      Object.assign(state.timings, msg.timings)
      break
    case 'picture':
      state.picture = msg.text
      drawReadings()
      break
    case 'video-glass':
      document.documentElement.dataset.videoGlass = msg.ok ? 'gpu' : 'still'
      break
    case 'prop': {
      state.props[msg.name] = msg.value
      redraw()
      switch (msg.name) {
        case 'pause':
          // Held exactly where it stopped, not where the glide had got to.
          if (msg.value) { state.time = currentTime(); state.timeAt = performance.now() }
          setPause(!!msg.value)
          drawSpinner()
          break
        case 'duration': state.duration = msg.value || 0; break
        case 'time-pos':
          // A seek still held with nothing on its way (one that landed off its mark): the bar follows mpv again.
          if (state.seekTarget != null && !state.props.seeking && !state.loading && performance.now() - (state.restartAt ?? 0) > 2000 && performance.now() - state.seekAt > 2000) settleSeek()
          if (msg.value != null && state.seekTarget == null) { state.time = msg.value; state.timeAt = performance.now() }
          break
        case 'demuxer-cache-time': state.cache = msg.value || 0; break
        case 'paused-for-cache':
        case 'core-idle':
        case 'seeking':
          drawSpinner()
          break
        case 'mute':
          state.mute = !!msg.value
          drawVolume()
          break
        case 'volume':
          if (!volumeDragging && typeof msg.value === 'number') { state.volume = msg.value; drawVolume() }
          break
        case 'media-title': $('title').textContent = msg.value || ''; break
        case 'eof-reached':
          drawSpinner()
          if (!msg.value || !inPlayer() || state.loading) break
          if (currentTime() < 1) streamFailed('The stream ended as it started.')
          // The end: the next episode if there is one (playerplus.js), otherwise back where it was opened from.
          else if (!window.exoOnEnded?.()) setTimeout(() => { if (state.props['eof-reached']) actions.back() }, 800)
          break
      }
      drawReadings()
      break
    }
    case 'event':
      if (msg.name === 'start-file') { state.loading = true; delete state.timings.seek; drawSpinner() }
      if (msg.name === 'playback-restart') {
        // Loaded or a seek landed: the bar follows mpv again from here. Seeks sent in a row (a fast scrub, keys held)
        // can land one by one; one that lands away from the latest target is an earlier one, and the bar holds the
        // latest until it lands too, instead of jumping back and forth.
        state.loading = false
        state.restartAt = performance.now()
        const pos = msg.data?.pos
        if (state.seekTarget != null && state.seeksOut > 1 && pos != null && Math.abs(pos - state.seekTarget) > 1) state.seeksOut--
        else settleSeek()
        drawSpinner()
        send({ cmd: 'get', name: 'pause' })
      }
      if (msg.name === 'end-file') {
        state.loading = false
        settleSeek()
        drawSpinner()
        if (msg.data?.error && inPlayer()) streamFailed(`The stream couldn't be played: ${msg.data.error}.`)
      }
      break
    case 'value':
      if (msg.name === 'pause') setPause(msg.value === 'yes')
      break
    case 'timing':
      state.timings[msg.timing.what] = msg.timing.ms
      drawReadings()
      break
    case 'fullscreen':
      state.fullscreen = msg.on
      glyph(document.querySelector('[data-action="fullscreen"] svg'), msg.on ? 'arrows-minimize' : 'arrows-maximize')
      break
  }
  listeners.forEach((fn) => fn(msg))
})

function enterPlayer(playMethod) {
  state.playMethod = playMethod
  state.time = 0
  state.duration = 0
  state.cache = 0
  state.seekTarget = null
  state.seeksOut = 0
  state.loading = true
  for (const k of ['eof-reached', 'seeking', 'paused-for-cache', 'core-idle']) state.props[k] = false
  document.body.classList.replace('mode-app', 'mode-player')
  resetPeek()
  enteredAt = performance.now()
  wake()
  placeGlass()
  redraw()
}

function leavePlayer() {
  if (state.fullscreen && state.fullscreenByPlayer) send({ cmd: 'fullscreen', on: false })
  state.fullscreenByPlayer = false
  state.loading = false
  state.seekTarget = null
  state.seeksOut = 0
  clearTimeout(spinnerTimer)
  $('spinner').hidden = true
  document.body.classList.replace('mode-player', 'mode-app')
  sendGlass()
  send({ cmd: 'media', off: true })
}

// Volume: the slider sets mpv's volume as it moves; the wheel over it steps by 5; the level is kept for next time.
const volumeInput = $('volume')
let volumeDragging = false
state.volume = (() => { try { return Number(localStorage.getItem('exo:volume') ?? 100) } catch { return 100 } })()

function drawVolume() {
  const shown = state.mute ? 0 : Math.round(state.volume)
  if (!volumeDragging) volumeInput.value = shown
  volumeInput.style.setProperty('--fill', `${shown}%`)
  volumeInput.setAttribute('aria-valuetext', `${shown} percent`)
  glyph(document.querySelector('[data-action="mute"] svg'), state.mute || shown === 0 ? 'volume-off' : 'volume')
}

function setVolume(v) {
  state.volume = Math.max(0, Math.min(100, Math.round(v)))
  if (state.mute && state.volume > 0) send({ cmd: 'set', name: 'mute', value: 'no' })
  send({ cmd: 'set', name: 'volume', value: String(state.volume) })
  try { localStorage.setItem('exo:volume', String(state.volume)) } catch { }
  drawVolume()
}

volumeInput.addEventListener('input', () => setVolume(Number(volumeInput.value)))
volumeInput.addEventListener('pointerdown', () => { volumeDragging = true })
volumeInput.addEventListener('pointerup', () => { volumeDragging = false; drawVolume() })
volumeInput.closest('.volume').addEventListener('wheel', (e) => {
  if (!e.deltaY) return
  e.preventDefault()
  setVolume(state.volume + (e.deltaY < 0 ? 5 : -5))
  wake()
}, { passive: false })
send({ cmd: 'set', name: 'volume', value: String(state.volume) })
drawVolume()

function streamFailed(message) {
  if (!inPlayer()) return
  actions.back()
  window.dispatchEvent(new CustomEvent('exo:notice', { detail: message }))
}

const actions = {
  pause: () => send({ cmd: 'toggle-pause' }),
  mute: () => send({ cmd: 'command', args: ['cycle', 'mute'] }),
  fullscreen: () => {
    state.fullscreenByPlayer = !state.fullscreen
    send({ cmd: 'fullscreen', on: !state.fullscreen })
  },
  info: () => {
    $('readings').hidden = !$('readings').hidden
    $('trackMenu').hidden = true
    drawReadings()
    placeGlass()
  },
  tracks: () => window.exoTrackMenu?.(),
  back: () => {
    if (!inPlayer()) return
    leavePlayer()
    window.dispatchEvent(new Event('exo:player-closed'))
  },
}

// The player opens on the first click of Play: a second click, or a double click, landed on the picture and paused the
// video before it began. The picture takes no clicks while a video opens, nor for a moment after the player shows.
let enteredAt = 0
const pictureListens = () => !state.loading && performance.now() - enteredAt > 700

stage.addEventListener('click', (e) => {
  const btn = e.target.closest('[data-action]')
  if (btn) { actions[btn.dataset.action](); return }
  // A click on the picture itself plays or pauses; a double click goes full screen.
  if (e.target === stage && pictureListens()) actions.pause()
})
stage.addEventListener('dblclick', (e) => {
  if (e.target === stage && pictureListens()) actions.fullscreen()
})

// Exact by default: a keyframe seek lands up to a few seconds before its target, and the bar jumped back to it.
function seekTo(seconds, mode = 'absolute+exact') {
  const to = Math.max(0, Math.min(state.duration || Infinity, seconds))
  state.seekTarget = to
  state.seeksOut = (state.seeksOut ?? 0) + 1
  state.seekAt = performance.now()
  state.time = to
  state.timeAt = performance.now()
  send({ cmd: 'seek', to, mode })
  redraw()
}

function settleSeek() {
  if (state.seekTarget != null) { state.time = state.seekTarget; state.timeAt = performance.now() }
  state.seekTarget = null
  state.seeksOut = 0
}

// Relative seeks go as absolute ones from where the bar is, so presses in a row add up from what is shown.
function seekBy(seconds) {
  seekTo(currentTime() + seconds)
  wake()
}

// The bar's place is read once as the pointer arrives or presses, not on every move.
const scrub = $('scrub')
let scrubRect = null
const ratioAt = (x) => {
  const r = scrubRect ??= scrub.getBoundingClientRect()
  return Math.max(0, Math.min(1, (x - r.left) / r.width))
}
scrub.addEventListener('pointerenter', (e) => {
  scrubRect = null
  // A picture from somewhere else on the bar doesn't show before the one for here.
  peek.shown = null
  $('hoverPeek').classList.remove('has-picture')
  drawPeek(e.clientX)
})

// The bar, the time and the picture move together, in the same frame as the pointer: nothing waits on mpv.
scrub.addEventListener('pointermove', (e) => {
  if (state.dragging) { state.dragTime = ratioAt(e.clientX) * state.duration; redraw() }
  drawPeek(e.clientX)
})
scrub.addEventListener('pointerdown', (e) => {
  scrubRect = null
  scrub.setPointerCapture(e.pointerId)
  state.dragging = true
  state.dragTime = ratioAt(e.clientX) * state.duration
  scrub.classList.add('is-dragging')
  drawPeek(e.clientX)
  redraw()
})
// Exact on release: the frame the user let go on, not the keyframe before it. A drag the window loses (it went to the
// back mid-drag) ends where it was.
const endDrag = (time) => {
  if (!state.dragging) return
  state.dragging = false
  scrub.classList.remove('is-dragging')
  seekTo(time, 'absolute+exact')
}
scrub.addEventListener('pointerup', (e) => endDrag(ratioAt(e.clientX) * state.duration))
scrub.addEventListener('lostpointercapture', () => endDrag(state.dragTime))

// ── The picture under the pointer ──────────────────────────────────────────────────────────────────────────────
// Jellyfin's own scrub pictures (its trickplay tiles) when the server made them for this version; otherwise the host
// draws the frame at that time with a second, hidden mpv (FramePeek.cs), except for a server conversion, where each
// picture would start another one. Pictures are kept per step along the bar (a tenth of a second of bar per pixel
// at most), the latest 150 of them.
const peek = { tiles: null, cache: new Map(), shown: null, asked: null, none: false, hovered: null, video: 0 }
const PEEK_KEEP = 150

// The picture's size: about a seventh of the window's width, in the video's shape.
function peekSize() {
  const w = Math.round(Math.min(304, Math.max(176, innerWidth * 0.15)))
  const vw = peek.tiles?.Width ?? state.props['video-params/w'], vh = peek.tiles?.Height ?? state.props['video-params/h']
  const aspect = vw > 0 && vh > 0 ? Math.min(2.4, Math.max(4 / 3, vw / vh)) : 16 / 9
  return { w, h: Math.round(w / aspect) }
}

// The step pictures are kept at: about 600 along the video, never finer than a second.
const peekStep = () => Math.max(1, Math.round(state.duration / 600))

// The Jellyfin item that is playing and the version picked (app.js): its tiles, at the width nearest the picture's.
function peekFrom(item, sourceId, server) {
  const sets = item?.Trickplay?.[sourceId] ?? Object.values(item?.Trickplay ?? {})[0]
  const widths = Object.keys(sets ?? {}).map(Number).filter((w) => sets[w]?.ThumbnailCount > 0).sort((a, b) => a - b)
  if (!widths.length) { peek.tiles = null; return }
  const want = peekSize().w * devicePixelRatio
  const width = widths.find((w) => w >= want) ?? widths.at(-1)
  const q = sourceId ? `?mediaSourceId=${encodeURIComponent(sourceId)}` : ''
  peek.tiles = { ...sets[width], src: (i) => `${server}/Videos/${item.Id}/Trickplay/${width}/${i}.jpg${q}` }
}

function resetPeek() {
  peek.video++
  peek.tiles = null
  peek.cache.clear()
  peek.shown = peek.asked = peek.hovered = null
  peek.none = false
  $('hoverPeek').classList.remove('has-picture')
}

function drawPeek(clientX) {
  const box = $('hoverPeek')
  const ratio = ratioAt(clientX)
  const t = ratio * state.duration
  const text = fmt(t)
  if ($('hoverTime').textContent !== text) $('hoverTime').textContent = text
  const size = peekSize()
  if (state.duration > 0) showPeekAt(t, size)
  // Centred on the pointer, kept inside the window.
  const r = scrubRect ??= scrub.getBoundingClientRect()
  const half = box.classList.contains('has-picture') ? size.w / 2 + 4 : 30
  const x = Math.max(half + 8, Math.min(innerWidth - half - 8, r.left + ratio * r.width)) - r.left
  box.style.translate = `calc(${x}px - 50%) 0`
}

function showPeekAt(t, size) {
  const frame = $('peekFrame')
  frame.style.setProperty('--peek-w', `${size.w}px`)
  frame.style.setProperty('--peek-h', `${size.h}px`)
  const tiles = peek.tiles
  if (tiles) {
    // The thumbnail's place in its tile: TileWidth across, read row by row; the last tile has only the rows it needs.
    const perTile = tiles.TileWidth * tiles.TileHeight
    const i = Math.min(tiles.ThumbnailCount - 1, Math.floor(t * 1000 / tiles.Interval))
    const tile = Math.floor(i / perTile), at = i % perTile
    const rows = Math.ceil(Math.min(perTile, tiles.ThumbnailCount - tile * perTile) / tiles.TileWidth)
    const key = `${tile}:${at}`
    if (peek.shown !== key) {
      peek.shown = key
      frame.style.backgroundImage = `url("${tiles.src(tile)}")`
      frame.style.backgroundSize = `${tiles.TileWidth * size.w}px ${rows * size.h}px`
      frame.style.backgroundPosition = `${-(at % tiles.TileWidth) * size.w}px ${-Math.floor(at / tiles.TileWidth) * size.h}px`
      $('hoverPeek').classList.add('has-picture')
    }
    return
  }
  if (peek.none || state.playMethod === 'Server transcode') return
  const step = peekStep()
  const at = Math.min(Math.floor(t / step) * step, Math.max(0, state.duration - 1))
  peek.hovered = at
  const seen = peek.cache.get(at)
  if (seen) { showPicture(at, seen); return }
  if (peek.asked === at) return
  peek.asked = at
  const k = devicePixelRatio
  send({ cmd: 'peek', t: at, w: Math.round(size.w * k), h: Math.round(size.h * k), d: state.duration, v: peek.video })
}

function showPicture(at, url) {
  if (peek.shown === at) return
  peek.shown = at
  const frame = $('peekFrame')
  frame.style.backgroundImage = `url("${url}")`
  frame.style.backgroundSize = 'cover'
  frame.style.backgroundPosition = 'center'
  $('hoverPeek').classList.add('has-picture')
}

onHost((m) => {
  // Only pictures of the video playing now.
  if (m.type !== 'peek' || m.v !== peek.video) return
  if (m.none) { peek.none = true; return }
  const url = `data:image/jpeg;base64,${m.jpg}`
  peek.cache.set(m.t, url)
  if (peek.cache.size > PEEK_KEEP) peek.cache.delete(peek.cache.keys().next().value)
  if (peek.asked === m.t) peek.asked = null
  // The picture for where the pointer is now, or the nearest one while that comes.
  if (peek.hovered === m.t || peek.shown == null) showPicture(m.t, url)
  else if (peek.hovered != null && peek.hovered !== m.t && !peek.cache.has(peek.hovered) && peek.asked == null) {
    peek.asked = peek.hovered
    const size = peekSize(), k = devicePixelRatio
    send({ cmd: 'peek', t: peek.hovered, w: Math.round(size.w * k), h: Math.round(size.h * k), d: state.duration, v: peek.video })
  }
})

document.addEventListener('keydown', (e) => {
  if (!inPlayer()) return
  if (e.repeat && !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(e.key)) return
  switch (e.key) {
    case ' ': case 'k': case 'K': actions.pause(); break
    case 'ArrowLeft': seekBy(-5); break
    case 'ArrowRight': seekBy(5); break
    case 'j': case 'J': seekBy(-10); break
    case 'l': case 'L': seekBy(10); break
    case 'ArrowUp': setVolume(state.volume + 5); break
    case 'ArrowDown': setVolume(state.volume - 5); break
    case 'm': case 'M': actions.mute(); break
    case 'f': case 'F': actions.fullscreen(); break
    case 'i': case 'I': actions.info(); break
    case 'Escape': if (state.fullscreen && state.fullscreenByPlayer) actions.fullscreen(); else actions.back(); break
    case 'Backspace': actions.back(); break
    default: return
  }
  e.preventDefault()
  wake()
})

// Idle: after 2.5 s without the pointer moving, while playing, the chrome and the cursor leave.
let idleTimer = 0
function wake() {
  if (stage.classList.contains('is-idle')) { stage.classList.remove('is-idle'); placeGlass(); redraw() }
  clearTimeout(idleTimer)
  idleTimer = setTimeout(() => {
    if (!state.pause && !state.dragging && inPlayer()) {
      stage.classList.add('is-idle')
      placeGlass()
    }
  }, 2500)
}
document.addEventListener('pointermove', () => { if (inPlayer()) wake() })

send({ cmd: 'ready' })
