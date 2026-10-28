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

function draw() {
  if (inPlayer()) {
    const t = state.dragging ? state.dragTime : currentTime()
    const d = state.duration
    const p = d > 0 ? Math.min(1, t / d) : 0
    $('played').style.width = `${p * 100}%`
    $('thumb').style.left = `${p * 100}%`
    $('cached').style.width = `${d > 0 ? Math.min(1, state.cache / d) * 100 : 0}%`
    $('pos').textContent = fmt(t)
    $('dur').textContent = fmt(d)
  }
  requestAnimationFrame(draw)
}
requestAnimationFrame(draw)

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
    ['Profile', state.tuning.profile],
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

new ResizeObserver(() => placeGlass()).observe(document.documentElement)
for (const type of ['transitionrun', 'animationstart']) stage.addEventListener(type, () => placeGlass())

window.chrome.webview.addEventListener('message', ({ data: msg }) => {
  switch (msg.type) {
    case 'hello':
      state.tuning = msg.tuning
      Object.assign(state.timings, msg.timings)
      break
    case 'video-glass':
      document.documentElement.dataset.videoGlass = msg.ok ? 'gpu' : 'still'
      break
    case 'prop': {
      state.props[msg.name] = msg.value
      switch (msg.name) {
        case 'pause':
          // Held exactly where it stopped, not where the glide had got to.
          if (msg.value) { state.time = currentTime(); state.timeAt = performance.now() }
          setPause(!!msg.value)
          drawSpinner()
          break
        case 'duration': state.duration = msg.value || 0; break
        case 'time-pos':
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
        // Loaded or a seek landed: the bar follows mpv again from here.
        state.loading = false
        state.seekTarget = null
        drawSpinner()
        send({ cmd: 'get', name: 'pause' })
      }
      if (msg.name === 'end-file') {
        state.loading = false
        state.seekTarget = null
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
  state.loading = true
  for (const k of ['eof-reached', 'seeking', 'paused-for-cache', 'core-idle']) state.props[k] = false
  document.body.classList.replace('mode-app', 'mode-player')
  wake()
  placeGlass()
}

function leavePlayer() {
  if (state.fullscreen && state.fullscreenByPlayer) send({ cmd: 'fullscreen', on: false })
  state.fullscreenByPlayer = false
  state.loading = false
  state.seekTarget = null
  clearTimeout(spinnerTimer)
  $('spinner').hidden = true
  document.body.classList.replace('mode-player', 'mode-app')
  sendGlass()
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

stage.addEventListener('click', (e) => {
  const btn = e.target.closest('[data-action]')
  if (btn) { actions[btn.dataset.action](); return }
  // A click on the picture itself plays or pauses; a double click goes full screen.
  if (e.target === stage) actions.pause()
})
stage.addEventListener('dblclick', (e) => {
  if (e.target === stage) actions.fullscreen()
})

// Exact by default: a keyframe seek lands up to a few seconds before its target, and the bar jumped back to it.
function seekTo(seconds, mode = 'absolute+exact') {
  const to = Math.max(0, Math.min(state.duration || Infinity, seconds))
  state.seekTarget = to
  state.time = to
  state.timeAt = performance.now()
  send({ cmd: 'seek', to, mode })
}

// Relative seeks go as absolute ones from where the bar is, so presses in a row add up from what is shown.
function seekBy(seconds) {
  seekTo(currentTime() + seconds)
  wake()
}

const scrub = $('scrub')
const ratioAt = (x) => {
  const r = scrub.getBoundingClientRect()
  return Math.max(0, Math.min(1, (x - r.left) / r.width))
}

scrub.addEventListener('pointermove', (e) => {
  const tip = $('hoverTime')
  const ratio = ratioAt(e.clientX)
  tip.textContent = fmt(ratio * state.duration)
  tip.style.left = `${ratio * 100}%`
  if (state.dragging) state.dragTime = ratio * state.duration
  placeGlass(100)
})
scrub.addEventListener('pointerdown', (e) => {
  scrub.setPointerCapture(e.pointerId)
  state.dragging = true
  state.dragTime = ratioAt(e.clientX) * state.duration
  scrub.classList.add('is-dragging')
})
scrub.addEventListener('pointerup', (e) => {
  if (!state.dragging) return
  state.dragging = false
  scrub.classList.remove('is-dragging')
  // Exact on release: the frame the user let go on, not the keyframe before it.
  seekTo(ratioAt(e.clientX) * state.duration, 'absolute+exact')
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
  if (stage.classList.contains('is-idle')) { stage.classList.remove('is-idle'); placeGlass() }
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
