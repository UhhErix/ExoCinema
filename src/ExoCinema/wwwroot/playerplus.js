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
  tellAnime()
  send({ cmd: 'media', title: plus.item.Name ?? '', subtitle: mediaLine(plus.item), image: poster(plus.item, 300) })
  try {
    const s = await jf('segments', { id })
    if (plus.item?.Id === id && s?.length) plus.segments = s.map((x) => ({ type: x.Type, start: x.StartTicks / 1e7, end: x.EndTicks / 1e7 }))
  } catch { }
  try {
    const n = await jf('next', { id })
    if (plus.item?.Id === id) { plus.next = n; send({ cmd: 'media', next: !!n }) }
  } catch { }
})

// Windows' media controls and Exo Launcher's Now Playing (MainWindow.Media.cs) show the title, this line under it
// and the poster; their Next button plays the next episode.
const mediaLine = (item) => item.Type === 'Episode'
  ? [item.SeriesName, item.ParentIndexNumber != null && item.IndexNumber != null ? `S${item.ParentIndexNumber}:E${item.IndexNumber}` : ''].filter(Boolean).join(' · ')
  : String(item.ProductionYear ?? '')

onHost((m) => {
  if (m.type === 'media-key' && m.key === 'next') playNext()
  else if (m.type === 'event' && m.name === 'file-loaded') {
    plus.applyLanguages = true
    send({ cmd: 'tracks' })
    send({ cmd: 'chapters' })
  } else if (m.type === 'tracks') {
    plus.tracks = m.list ?? []
    if (plus.applyLanguages) { plus.applyLanguages = false; tellAnime(); applyRememberedLanguages() }
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

// ── The tracks a video starts on ───────────────────────────────────────────────────────────────────────────────
// The show's languages as last chosen, so the next episode starts the way the last one was set. Otherwise: the
// file's own language (Japanese for anime, which is watched in it), in its best recording (lossless before lossy,
// more channels before fewer, never a commentary or description track); and, when that isn't the viewer's language,
// full subtitles in theirs (styled ones first, as anime's are; never signs-only or forced), or, when it is, only the
// signs and songs.

// Languages compare by name: "jpn", "ja" and "ja-JP" are all Japanese. Matroska writes some in their older codes.
const OLD_CODES = { ger: 'deu', fre: 'fra', chi: 'zho', dut: 'nld', cze: 'ces', gre: 'ell', per: 'fas', rum: 'ron', slo: 'slk', ice: 'isl', alb: 'sqi', arm: 'hye', baq: 'eus', bur: 'mya', geo: 'kat', mac: 'mkd', mao: 'mri', may: 'msa', tib: 'bod', wel: 'cym' }
const languageNames = new Intl.DisplayNames(['en'], { type: 'language' })
function langName(code) {
  const base = (code ?? '').toLowerCase().split(/[-_]/)[0]
  // Untagged ("und") says nothing either way.
  if (!base || ['und', 'zxx', 'mul', 'mis', 'unk'].includes(base)) return ''
  try { return languageNames.of(OLD_CODES[base] ?? base) ?? base } catch { return base }
}
const viewerLanguage = langName(navigator.language || 'en')

const SIDE_TRACK = /comment|descri|\bAD\b|narrat/i
const SIGNS = /sign|song|forced|karaoke/i

function audioRank(t) {
  const c = (t.codec ?? '').toLowerCase(), p = `${t.profile ?? ''} ${t.title ?? ''}`.toLowerCase()
  const kind = c === 'truehd' ? (p.includes('atmos') ? 7 : 6)
    : c.startsWith('pcm') || c === 'flac' || c === 'alac' || (c === 'dts' && /ma|hd|x/.test(p)) ? 5
    : c === 'eac3' ? (p.includes('atmos') ? 5 : 4) : c === 'dts' || c === 'ac3' ? 3 : c === 'opus' || c === 'aac' ? 2 : 1
  return kind * 100 + Number(t.channels || 0) * 2 + (t.isDefault ? 1 : 0)
}

function subRank(t) {
  const c = (t.codec ?? '').toLowerCase()
  const kind = c === 'ass' || c === 'ssa' ? 3 : c === 'hdmv_pgs_subtitle' || c === 'dvd_subtitle' || c === 'dvb_subtitle' ? 1 : 2
  return kind * 10 + (/full|dialog/i.test(t.title ?? '') ? 4 : 0) + (t.hearing || /sdh|\bcc\b|hearing/i.test(t.title ?? '') ? 0 : 2) + (t.isDefault ? 1 : 0)
}

// Anime: so tagged, or animation made in Japan (its country, its Japanese title, or its Japanese audio).
const isAnime = (item) => {
  const genres = [...(item?.Genres ?? []), ...(item?.SeriesGenres ?? [])].join(' ')
  if (/anime/i.test(genres)) return true
  if (!/animation|animated/i.test(genres)) return false
  return [...(item?.ProductionLocations ?? []), ...(item?.SeriesProductionLocations ?? [])].some((p) => /japan/i.test(p))
    || /[぀-ヿ一-鿿]/.test(`${item?.OriginalTitle ?? ''} ${item?.SeriesOriginalTitle ?? ''}`)
    || plus.tracks.some((t) => t.type === 'audio' && langName(t.lang) === 'Japanese')
}

// The host draws anime with ArtCNN (MainWindow.Player.cs › ApplyAnime): told as soon as the title is known, and again
// once the file's tracks are, which can show Japanese audio.
const tellAnime = () => send({ cmd: 'anime', on: !!plus.item && isAnime(plus.item) })

function applyRememberedLanguages() {
  let saved = {}
  try { saved = JSON.parse(localStorage.getItem(languageKey(plus.item)) ?? '{}') } catch { }
  const choose = (type, id) => { if (id != null && !plus.tracks.find((t) => t.type === type && t.id === id)?.selected) send({ cmd: 'set', name: type === 'audio' ? 'aid' : 'sid', value: String(id) }) }
  const audios = plus.tracks.filter((t) => t.type === 'audio')
  const subs = plus.tracks.filter((t) => t.type === 'sub')

  const mains = audios.filter((t) => !SIDE_TRACK.test(t.title ?? ''))
  const pool = mains.length ? mains : audios
  const language = saved.audio ? langName(saved.audio)
    : isAnime(plus.item) && pool.some((t) => langName(t.lang) === 'Japanese') ? 'Japanese'
    : langName((pool.find((t) => t.selected) ?? pool.find((t) => t.isDefault) ?? pool[0])?.lang)
  const audio = pool.filter((t) => langName(t.lang) === language).sort((a, b) => audioRank(b) - audioRank(a))[0]
  if (audio) choose('audio', audio.id)

  if (saved.sub === 'off') { send({ cmd: 'set', name: 'sid', value: 'no' }); return }
  if (saved.sub) {
    const s = subs.filter((t) => langName(t.lang) === langName(saved.sub)).sort((a, b) => Number(SIGNS.test(a.title ?? '') || a.forced) - Number(SIGNS.test(b.title ?? '') || b.forced) || subRank(b) - subRank(a))[0]
    if (s) choose('sub', s.id)
    return
  }
  const mine = subs.filter((t) => langName(t.lang) === viewerLanguage)
  const spoken = langName((audio ?? audios.find((t) => t.selected))?.lang)
  if (spoken && spoken !== viewerLanguage) {
    const full = mine.filter((t) => !t.forced && !SIGNS.test(t.title ?? '')).sort((a, b) => subRank(b) - subRank(a))[0]
    if (full) choose('sub', full.id)
  } else if (spoken) {
    const signs = mine.find((t) => t.forced || SIGNS.test(t.title ?? ''))
    if (signs) choose('sub', signs.id)
    else if (subs.some((t) => t.selected)) send({ cmd: 'set', name: 'sid', value: 'no' })
  }
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

// Four times a second while a video is open, never outside the player.
let skipWatch = 0
window.addEventListener('exo:now-playing', () => { if (!skipWatch) skipWatch = setInterval(watchSkips, 250) })
window.addEventListener('exo:player-closed', () => { clearInterval(skipWatch); skipWatch = 0 })

function watchSkips() {
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
}

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
