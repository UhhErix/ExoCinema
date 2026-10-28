// The player beyond play and seek: audio and subtitle tracks (the languages remembered per show), speed, Skip intro
// and Skip credits (Jellyfin's segments, or the file's own chapter names), and Up Next with autoplay.

const plus = { item: null, segments: [], next: null, nextCancelled: false, nextTimer: 0, tracks: [], applyLanguages: false }
const SPEEDS = [0.75, 1, 1.25, 1.5, 2]
const AUTOPLAY_S = 10

const languageKey = (item) => `exo:lang:${item?.SeriesId ?? 'movies'}`

window.addEventListener('exo:now-playing', async (e) => {
  plus.item = e.detail
  plus.segments = []
  plus.next = null
  plus.nextCancelled = false
  hideUpNext()
  $('skipBtn').hidden = true
  const id = plus.item.Id
  try {
    const s = await jf('segments', { id })
    if (plus.item?.Id === id && s?.length) plus.segments = s.map((x) => ({ type: x.Type, start: x.StartTicks / 1e7, end: x.EndTicks / 1e7 }))
  } catch { }
  try {
    const n = await jf('next', { id })
    if (plus.item?.Id === id) plus.next = n
  } catch { }
})

onHost((m) => {
  if (m.type === 'event' && m.name === 'file-loaded') {
    plus.applyLanguages = true
    send({ cmd: 'tracks' })
    send({ cmd: 'chapters' })
  } else if (m.type === 'tracks') {
    plus.tracks = m.list ?? []
    if (plus.applyLanguages) { plus.applyLanguages = false; applyRememberedLanguages() }
    if (!$('trackMenu').hidden) drawTrackMenu()
  } else if (m.type === 'chapters') {
    // A file that marks its own opening and credits (common in anime) stands in for Jellyfin's segments.
    if (plus.segments.length) return
    const list = m.list ?? []
    list.forEach((c, i) => {
      const end = list[i + 1]?.time ?? state.duration
      const title = (c.title ?? '').trim()
      if (/^(intro|opening|op|title sequence)$/i.test(title) || /\bopening\b/i.test(title)) plus.segments.push({ type: 'Intro', start: c.time, end })
      else if (/^(outro|ending|ed|credits|end credits)$/i.test(title) || /\b(ending|credits)\b/i.test(title)) plus.segments.push({ type: 'Outro', start: c.time, end })
    })
  }
})

// ── Audio, subtitles and speed ─────────────────────────────────────────────────────────────────────────────────

function trackName(t) {
  const lang = t.lang ? new Intl.DisplayNames(undefined, { type: 'language' }).of(t.lang.length === 3 ? t.lang : t.lang) ?? t.lang : null
  const channels = t.type === 'audio' && t.channels ? ({ 1: 'Mono', 2: 'Stereo', 6: '5.1', 8: '7.1' }[t.channels] ?? `${t.channels} ch`) : null
  const parts = [t.title || lang || `Track ${t.id}`, t.title && lang && !t.title.toLowerCase().includes(lang.toLowerCase()) ? lang : null, channels, t.forced ? 'Forced' : null]
  return parts.filter(Boolean).join(' · ')
}

function drawTrackMenu() {
  const audio = plus.tracks.filter((t) => t.type === 'audio')
  const subs = plus.tracks.filter((t) => t.type === 'sub')
  const speed = Number(state.props.speed ?? 1)
  const row = (label, on, attrs) => `<button type="button" class="menu-row" role="menuitemradio" aria-checked="${on}" ${attrs}>
    <span class="row-check">${on ? icon('check') : ''}</span><span>${esc(label)}</span></button>`
  $('trackMenu').innerHTML = `
    ${audio.length ? `<div class="menu-head">Audio</div>${audio.map((t) => row(trackName(t), t.selected, `data-aid="${t.id}" data-lang="${esc(t.lang ?? '')}"`)).join('')}` : ''}
    <div class="menu-head">Subtitles</div>
    ${row('Off', !subs.some((t) => t.selected), 'data-sid="no"')}
    ${subs.map((t) => row(trackName(t), t.selected, `data-sid="${t.id}" data-lang="${esc(t.lang ?? '')}"`)).join('')}
    <div class="menu-head">Speed</div>
    <div class="speed-row">${SPEEDS.map((s) => `<button type="button" class="seg${Math.abs(s - speed) < 0.01 ? ' is-on' : ''}" data-speed="${s}">${s === 1 ? 'Normal' : `${s}×`}</button>`).join('')}</div>`
  placeGlass()
}

window.exoTrackMenu = () => {
  const menu = $('trackMenu')
  menu.hidden = !menu.hidden
  if (!menu.hidden) {
    $('readings').hidden = true
    send({ cmd: 'tracks' })
    send({ cmd: 'get', name: 'speed' })
    drawTrackMenu()
  }
  placeGlass()
}

function remember(kind, lang) {
  if (!plus.item) return
  let saved = {}
  try { saved = JSON.parse(localStorage.getItem(languageKey(plus.item)) ?? '{}') } catch { }
  saved[kind] = lang
  try { localStorage.setItem(languageKey(plus.item), JSON.stringify(saved)) } catch { }
}

// The show's languages as last chosen: the next episode starts the way the last one was set.
function applyRememberedLanguages() {
  let saved = {}
  try { saved = JSON.parse(localStorage.getItem(languageKey(plus.item)) ?? '{}') } catch { }
  const pick = (type, lang) => plus.tracks.filter((t) => t.type === type && t.lang === lang).sort((a, b) => Number(a.forced) - Number(b.forced))[0]
  if (saved.audio) { const a = pick('audio', saved.audio); if (a && !a.selected) send({ cmd: 'set', name: 'aid', value: String(a.id) }) }
  if (saved.sub === 'off') send({ cmd: 'set', name: 'sid', value: 'no' })
  else if (saved.sub) { const s = pick('sub', saved.sub); if (s && !s.selected) send({ cmd: 'set', name: 'sid', value: String(s.id) }) }
}

$('trackMenu').addEventListener('click', (e) => {
  const b = e.target.closest('button')
  if (!b) return
  if (b.dataset.aid) { send({ cmd: 'set', name: 'aid', value: b.dataset.aid }); remember('audio', b.dataset.lang) }
  else if (b.dataset.sid) { send({ cmd: 'set', name: 'sid', value: b.dataset.sid }); remember('sub', b.dataset.sid === 'no' ? 'off' : b.dataset.lang) }
  else if (b.dataset.speed) { send({ cmd: 'set', name: 'speed', value: b.dataset.speed }); state.props.speed = Number(b.dataset.speed) }
  setTimeout(() => send({ cmd: 'tracks' }), 120)
  drawTrackMenu()
})

onHost((m) => { if (m.type === 'value' && m.name === 'speed') { state.props.speed = Number(m.value); if (!$('trackMenu').hidden) drawTrackMenu() } })

document.addEventListener('pointerdown', (e) => {
  if (!$('trackMenu').hidden && !e.target.closest('#trackMenu, [data-action="tracks"]')) { $('trackMenu').hidden = true; placeGlass() }
})

// ── Skip intro, Skip credits, Up Next ──────────────────────────────────────────────────────────────────────────

let skipTarget = null

setInterval(() => {
  if (!inPlayer() || state.loading) return
  const t = currentTime()
  const d = state.duration
  const intro = plus.segments.find((s) => s.type === 'Intro' && t >= s.start && t < s.end - 1)
  const outro = plus.segments.find((s) => s.type === 'Outro' && t >= s.start && t < s.end - 1)
  // Up Next from the credits, or in the last 30 seconds of an episode without marks.
  const nextTime = plus.next && !plus.nextCancelled && (outro || (d > 120 && d - t <= 30))
  if (nextTime) showUpNext()
  const skip = !nextTime && (intro ? ['Skip intro', intro.end] : outro ? ['Skip credits', outro.end] : null)
  const btn = $('skipBtn')
  if (skip) { skipTarget = skip[1]; btn.querySelector('span').textContent = skip[0] }
  if (btn.hidden === !!skip) { btn.hidden = !skip; placeGlass() }
}, 250)

$('skipBtn').addEventListener('click', () => { if (skipTarget != null) seekTo(skipTarget) })

function showUpNext() {
  const card = $('upNext')
  if (!card.hidden) return
  const n = plus.next
  const still = n.ImageTags?.Primary ? image(n.Id, 'Primary', n.ImageTags.Primary, 320) : landscape(n, 320)
  const auto = pref('autoplay') === 'on'
  card.innerHTML = `<span class="up-art">${still ? `<img src="${esc(still)}" alt="">` : ''}</span>
    <span class="up-text"><span class="up-name">${esc(n.Name)}</span><span class="up-sub">${esc(episodeCode(n))}</span></span>
    <span class="up-actions">
      <button type="button" class="primary is-primary up-play${auto ? ' is-counting' : ''}" style="--count:${AUTOPLAY_S}s">${icon('player-play-filled')}<span>Play next</span></button>
      <button type="button" class="icon up-cancel" aria-label="Not now">${icon('x')}</button>
    </span>`
  card.hidden = false
  placeGlass()
  clearTimeout(plus.nextTimer)
  if (auto) plus.nextTimer = setTimeout(playNext, AUTOPLAY_S * 1000)
}

function hideUpNext() {
  clearTimeout(plus.nextTimer)
  $('upNext').hidden = true
  placeGlass()
}

function playNext() {
  const n = plus.next
  if (!n || !inPlayer()) return
  hideUpNext()
  play(n.Id, true, false)
}

$('upNext').addEventListener('click', (e) => {
  if (e.target.closest('.up-play')) playNext()
  else if (e.target.closest('.up-cancel')) { plus.nextCancelled = true; hideUpNext() }
})

// The end of an episode: the next one, unless autoplay is off or Up Next was waved away (player.js asks).
window.exoOnEnded = () => {
  if (!plus.next || plus.nextCancelled || pref('autoplay') !== 'on') return false
  playNext()
  return true
}

window.addEventListener('exo:player-closed', () => {
  plus.item = null
  plus.next = null
  hideUpNext()
  $('skipBtn').hidden = true
  $('trackMenu').hidden = true
})
