// Exo Cinema's library: Jellyfin sign-in, Home, libraries, a title's page and search. The host
// (Jellyfin/JellyfinHost.cs) talks to the server; this page asks it through jf() and draws what comes back.

let nextId = 1
const pending = new Map()
onHost((msg) => {
  if (msg.type !== 'reply') return
  const p = pending.get(msg.id)
  if (!p) return
  pending.delete(msg.id)
  msg.ok ? p.resolve(msg.data) : p.reject(new Error(msg.error))
})

function jf(op, args = {}) {
  const id = nextId++
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject })
    send({ cmd: 'jf', id, op, args })
  })
}

function app(op, args = {}) {
  const id = nextId++
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject })
    send({ cmd: 'app', id, op, args })
  })
}

const view = $('view')
let session = null

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c])

// Cache first: what was drawn last time draws at once, then the server's answer replaces it.
const cache = {
  get(key) { try { return JSON.parse(localStorage.getItem(`exo:${session?.userId}:${key}`)) } catch { return null } },
  set(key, value) { try { localStorage.setItem(`exo:${session?.userId}:${key}`, JSON.stringify(value)) } catch { } },
}

// ---- Images

function image(id, type, tag, width, index) {
  if (!id || !tag) return ''
  return `${session.server}/Items/${id}/Images/${type}${index != null ? `/${index}` : ''}?fillWidth=${Math.round(width * devicePixelRatio)}&quality=80&tag=${tag}`
}

const poster = (item, w = 220) =>
  item.ImageTags?.Primary && item.Type !== 'Episode' ? image(item.Id, 'Primary', item.ImageTags.Primary, w)
  : item.SeriesPrimaryImageTag ? image(item.SeriesId, 'Primary', item.SeriesPrimaryImageTag, w)
  : item.ImageTags?.Primary ? image(item.Id, 'Primary', item.ImageTags.Primary, w)
  : ''

const landscape = (item, w = 420) =>
  item.Type === 'Episode' && item.ImageTags?.Primary ? image(item.Id, 'Primary', item.ImageTags.Primary, w)
  : item.ImageTags?.Thumb ? image(item.Id, 'Thumb', item.ImageTags.Thumb, w)
  : item.ParentThumbImageTag ? image(item.ParentThumbItemId, 'Thumb', item.ParentThumbImageTag, w)
  : item.BackdropImageTags?.length ? image(item.Id, 'Backdrop', item.BackdropImageTags[0], w, 0)
  : item.ParentBackdropImageTags?.length ? image(item.ParentBackdropItemId, 'Backdrop', item.ParentBackdropImageTags[0], w, 0)
  : item.ImageTags?.Primary ? image(item.Id, 'Primary', item.ImageTags.Primary, w)
  : ''

const backdrop = (item, w = 1920) =>
  item.BackdropImageTags?.length ? image(item.Id, 'Backdrop', item.BackdropImageTags[0], w, 0)
  : item.ParentBackdropImageTags?.length ? image(item.ParentBackdropItemId, 'Backdrop', item.ParentBackdropImageTags[0], w, 0)
  : ''

const logo = (item, w = 560) =>
  item.ImageTags?.Logo ? image(item.Id, 'Logo', item.ImageTags.Logo, w)
  : item.ParentLogoImageTag ? image(item.ParentLogoItemId, 'Logo', item.ParentLogoImageTag, w)
  : ''

// ---- Words

const runtime = (ticks) => {
  if (!ticks) return ''
  const m = Math.round(ticks / 6e8)
  return m >= 60 ? `${Math.floor(m / 60)} h ${m % 60} min` : `${m} min`
}

const left = (item) => {
  const t = item.RunTimeTicks, p = item.UserData?.PlaybackPositionTicks
  if (!t || !p) return ''
  const m = Math.max(1, Math.round((t - p) / 6e8))
  return m >= 60 ? `${Math.floor(m / 60)} h ${m % 60} min left` : `${m} min left`
}

const episodeCode = (e) => (e.ParentIndexNumber != null && e.IndexNumber != null ? `S${e.ParentIndexNumber}:E${e.IndexNumber}` : '')

const progress = (item) => {
  const pct = item.UserData?.PlayedPercentage
  return pct > 0 && pct < 100 ? `<span class="progress"><span style="width:${pct.toFixed(1)}%"></span></span>` : ''
}

const watched = (item) => (item.UserData?.Played ? `<span class="watched exo-glass is-over-art" aria-label="Watched">${icon('check')}</span>` : '')

// ---- Cards

function wideCard(item, { play = false } = {}) {
  const title = item.Type === 'Episode' ? item.SeriesName : item.Name
  const sub = item.Type === 'Episode' ? `${episodeCode(item)} · ${item.Name}` : left(item) || item.ProductionYear || ''
  // An episode opens its show's page on the episode's season, not a page for the one episode.
  const target = item.Type === 'Episode' && item.SeriesId ? `${item.SeriesId}${item.SeasonId ? `/${item.SeasonId}` : ''}` : item.Id
  return `<button class="card wide" data-${play ? 'play' : 'open'}="${play ? item.Id : target}">
    <span class="art">${landscape(item) ? `<img loading="lazy" src="${esc(landscape(item))}" alt="">` : ''}${progress(item)}</span>
    <span class="card-title">${esc(title)}</span>
    <span class="card-sub">${esc(sub)}</span>
  </button>`
}

function posterCard(item) {
  const src = poster(item)
  return `<button class="card poster" data-open="${item.Id}">
    <span class="art">${src ? `<img loading="lazy" src="${esc(src)}" alt="">` : `<span class="art-name">${esc(item.Name)}</span>`}${progress(item)}${watched(item)}</span>
    <span class="card-title">${esc(item.Name)}</span>
    <span class="card-sub">${esc(item.ProductionYear ?? '')}</span>
  </button>`
}

const placeholders = (n) => '<span class="card poster is-placeholder" aria-hidden="true"><span class="art"></span><span class="card-title">&nbsp;</span><span class="card-sub"></span></span>'.repeat(n)

// A shelf, as Exo Launcher's Store rows: its name, and arrows that page the row; they hide when the row fits.
const shelf = (title, cards) => cards ? `<section class="shelf">
  <div class="shelf-head"><h2>${esc(title)}</h2><span class="shelf-arrows">
    <button type="button" class="set-btn" data-page="-1" aria-label="Back in ${esc(title)}" disabled>${icon('chevron-left')}</button>
    <button type="button" class="set-btn" data-page="1" aria-label="More ${esc(title)}">${icon('chevron-right')}</button></span></div>
  <div class="row">${cards}</div></section>` : ''

// An arrow is off at its end of the row; they change only when an edge is reached or left.
function drawArrows(row) {
  const head = row.previousElementSibling
  if (!head?.classList.contains('shelf-head')) return
  const [back, more] = head.querySelectorAll('[data-page]')
  back.disabled = row.scrollLeft < 4
  more.disabled = row.scrollLeft + row.clientWidth >= row.scrollWidth - 4
}
const drawAllArrows = () => document.querySelectorAll('#view .row').forEach(drawArrows)
document.addEventListener('scroll', (e) => { if (e.target.classList?.contains('row')) drawArrows(e.target) }, true)
new MutationObserver(() => requestAnimationFrame(drawAllArrows)).observe(view, { childList: true, subtree: true })
addEventListener('resize', drawAllArrows)

// A page at a time: nine tenths of the row, so the last card seen stays in sight.
document.addEventListener('click', (e) => {
  const arrow = e.target.closest('[data-page]')
  const row = arrow?.closest('.shelf')?.querySelector('.row')
  if (!row) return
  row.scrollBy({ left: Number(arrow.dataset.page) * row.clientWidth * 0.9, behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' })
})

// ---- Rooms (the Bar)

// A library's glyph, as Exo's room tabs have: line at rest, solid for the current one where Tabler draws one.
const ROOM_GLYPH = { home: ['home', 'home-filled'], movies: ['movie', 'movie'], tvshows: ['device-tv', 'device-tv-filled'] }

function drawRooms(views = cache.get('views') ?? []) {
  const route = location.hash.slice(2).split('/')
  const current = route[0] === 'view' ? route[1] : route[0] || 'home'
  const tabs = [{ Id: 'home', Name: 'Home', CollectionType: 'home' }, ...views.filter((v) => HOME_KINDS.includes(v.CollectionType))]
  const html = tabs.map((v) => {
    const on = current === v.Id
    const [line, solid] = ROOM_GLYPH[v.CollectionType] ?? ['sparkles', 'sparkles']
    const href = v.Id === 'home' ? '#/home' : `#/view/${v.Id}`
    return `<a class="tab${on ? ' is-on' : ''}" href="${href}"${on ? ' aria-current="page"' : ''}>${icon(on ? solid : line)}<span>${esc(v.Name)}</span></a>`
  }).join('')
  if ($('rooms').innerHTML !== html) $('rooms').innerHTML = html
}

// ---- Pages

const HOME_KINDS = ['movies', 'tvshows', 'homevideos', 'musicvideos', undefined, null]

// Home draws in parts as the server answers: what was there last time at once, then Continue Watching and Next
// Up, then each library's newest row on its own (a slow list never holds up the others). Only the part that
// changed is touched: images already on screen stay put, so nothing flashes.
async function renderHome() {
  const home = { ...(cache.get('home') ?? {}) }
  home.latest ??= []
  if (!view.querySelector('.home')) view.innerHTML = '<div class="home"><div class="home-hero"></div><div class="shelves"></div></div>'
  const draw = () => {
    if (route() !== 'home') return
    const root = view.querySelector('.home')
    if (!root) return
    const hero = home.resume?.[0] ?? home.nextUp?.[0] ?? home.latest[0]?.items?.[0]
    patch(root.querySelector('.home-hero'), hero?.Id ?? '', () => (hero ? homeHero(hero) : ''))
    const rows = [
      { key: 'resume', title: 'Continue Watching', items: home.resume, card: (i) => wideCard(i) },
      { key: 'nextup', title: 'Next Up', items: home.nextUp, card: (i) => wideCard(i) },
      ...home.latest.map((r) => ({ key: r.viewId, title: r.title, items: r.items, card: posterCard })),
    ]
    const shelves = root.querySelector('.shelves')
    for (const r of rows) {
      let el = shelves.querySelector(`:scope > [data-row="${r.key}"]`)
      if (!el) { el = document.createElement('div'); el.dataset.row = r.key; shelves.append(el) }
      // A row still on its way keeps its place with blank cards; an empty one goes.
      const sig = r.items ? r.items.map((i) => `${i.Id}:${i.UserData?.PlaybackPositionTicks ?? 0}:${i.UserData?.Played ?? ''}`).join() : 'loading'
      patch(el, sig, () => r.items ? shelf(r.title, r.items.map(r.card).join('')) : shelf(r.title, placeholders(8)))
    }
    for (const el of shelves.querySelectorAll(':scope > [data-row]')) if (!rows.some((r) => r.key === el.dataset.row)) el.remove()
    if (home.views) drawRooms(home.views)
  }
  draw()
  let top
  try {
    top = await jf('home-top')
  } catch (e) {
    if (!home.views) view.innerHTML = problem(e.message, 'home')
    return
  }
  Object.assign(home, top)
  const shown = top.views.filter((v) => HOME_KINDS.includes(v.CollectionType))
  // Rows keep the libraries' order; a row not back yet keeps last time's items.
  home.latest = shown.map((v) => home.latest.find((r) => r.viewId === v.Id) ?? { kind: 'latest', title: `New in ${v.Name}`, viewId: v.Id })
  cache.set('views', top.views)
  draw()
  await Promise.all(shown.map(async (v) => {
    try {
      const items = await jf('latest', { viewId: v.Id })
      const row = home.latest.find((r) => r.viewId === v.Id)
      if (row) row.items = items
      draw()
    } catch { }
  }))
  cache.set('home', home)
  prefetchLibraries(shown)
}

// Replaces an element's content only when what it shows changed.
function patch(el, sig, html) {
  if (el.dataset.sig === sig) return
  el.dataset.sig = sig
  el.innerHTML = html()
}

// The libraries' first pages, fetched quietly after Home, so a tab opens at once.
let prefetched = false
async function prefetchLibraries(views) {
  if (prefetched) return
  prefetched = true
  for (const v of views) {
    if (cache.get(`view:${v.Id}`)) { warmImages(cache.get(`view:${v.Id}`).Items); continue }
    try {
      const r = await jf('items', { parentId: v.Id, start: 0, limit: 36 })
      cache.set(`view:${v.Id}`, r)
      warmImages(r.Items)
    } catch { }
  }
}

// A backdrop arrives in two steps: a small one at once (about 25 KB), then the full one once it has loaded, so
// a slow server shows the picture in a second instead of after the whole image.
function heroArt(item) {
  const small = backdrop(item, 480)
  if (!small) return ''
  const full = backdrop(item, Math.min(1920, innerWidth))
  return `<img class="hero-art" src="${esc(small)}" data-full="${esc(full)}" alt="" onload="upgradeArt(this)">`
}

function upgradeArt(img) {
  const full = img.dataset.full
  if (!full || img.src === full) return
  delete img.dataset.full
  const hi = new Image()
  hi.onload = () => { img.src = full }
  hi.src = full
}

// Covers for what is one click away, fetched into the image cache in the background.
const warmed = new Set()
function warmImages(items) {
  for (const i of items ?? []) {
    const src = poster(i)
    if (!src || warmed.has(src)) continue
    warmed.add(src)
    new Image().src = src
  }
}

function homeHero(item) {
  const lg = logo(item)
  const name = item.Type === 'Episode' ? item.SeriesName : item.Name
  const resumable = item.UserData?.PlaybackPositionTicks > 0
  return `<section class="hero">
    ${heroArt(item)}
    <div class="hero-body">
      ${lg ? `<img class="hero-logo" src="${esc(lg)}" alt="${esc(name)}">` : `<h1 class="hero-title">${esc(name)}</h1>`}
      <p class="hero-meta">${esc([item.Type === 'Episode' ? `${episodeCode(item)} · ${item.Name}` : item.ProductionYear, left(item)].filter(Boolean).join(' · '))}</p>
      <div class="actions">
        <button class="primary is-primary" data-play="${item.Id}">${icon('player-play-filled')}${resumable ? 'Resume' : 'Play'}</button>
        <button class="secondary exo-glass is-over-art" data-open="${item.Type === 'Episode' ? item.SeriesId : item.Id}">Details</button>
      </div>
    </div>
  </section>`
}

async function renderView(id) {
  drawRooms()
  const key = `view:${id}`
  // The grid is built once per library; later pages only add their cards to its end.
  const draw = (r) => {
    if (!r) { view.innerHTML = '<div class="loading"><div class="spinner"></div></div>'; return }
    let grid = view.querySelector(`.grid[data-view="${id}"]`)
    if (!grid) {
      view.innerHTML = `<section class="grid-page"><div class="grid" data-view="${id}"></div></section>`
      grid = view.querySelector('.grid')
    }
    const items = r.Items ?? []
    const shown = grid.children.length
    if (items.length > shown) grid.insertAdjacentHTML('beforeend', items.slice(shown).map(posterCard).join(''))
  }
  draw(cache.get(key))
  try {
    // The first screenful at once, then the rest.
    const first = await jf('items', { parentId: id, start: 0, limit: 36 })
    if (route() !== `view/${id}`) return
    draw(first)
    let items = first.Items ?? []
    while (items.length < (first.TotalRecordCount ?? 0)) {
      const more = await jf('items', { parentId: id, start: items.length, limit: 300 })
      if (!more.Items?.length) break
      items = items.concat(more.Items)
      if (route() !== `view/${id}`) return
      draw({ Items: items })
    }
    cache.set(key, { Items: items })
  } catch (e) {
    if (!cache.get(key)) view.innerHTML = problem(e.message, `view/${id}`)
  }
}

async function renderItem(id, seasonId) {
  drawRooms()
  const cached = cache.get(`item:${id}`)
  if (cached) drawItem(cached, null, null, seasonId)
  else view.innerHTML = '<div class="loading"><div class="spinner"></div></div>'
  try {
    const item = await jf('item', { id })
    cache.set(`item:${id}`, item)
    if (item.Type !== 'Series') { if (route().startsWith(`item/${id}`)) drawItem(item); return }
    const seasons = (await jf('seasons', { seriesId: id })).Items ?? []
    const season = seasons.find((s) => s.Id === seasonId)
      ?? seasons.find((s) => s.UserData?.UnplayedItemCount > 0 && s.IndexNumber > 0)
      ?? seasons.find((s) => s.IndexNumber > 0) ?? seasons[0]
    const episodes = season ? (await jf('episodes', { seriesId: id, seasonId: season.Id })).Items ?? [] : []
    if (route().startsWith(`item/${id}`)) drawItem(item, seasons, { season, episodes })
  } catch (e) {
    if (!cached) view.innerHTML = problem(e.message, `item/${id}`)
  }
}

function drawItem(item, seasons, episodes) {
  const bg = backdrop(item)
  const lg = logo(item)
  const resumable = item.UserData?.PlaybackPositionTicks > 0
  const meta = [
    item.Type === 'Episode' ? `${item.SeriesName} · ${episodeCode(item)}` : null,
    item.ProductionYear,
    item.OfficialRating,
    item.Type === 'Series' ? (item.ChildCount ? `${item.ChildCount} season${item.ChildCount === 1 ? '' : 's'}` : null) : runtime(item.RunTimeTicks),
    item.CommunityRating ? `★ ${item.CommunityRating.toFixed(1)}` : null,
  ].filter(Boolean).join(' · ')
  const playTarget = item.Type === 'Series' ? (episodes?.episodes.find((e) => !e.UserData?.Played) ?? episodes?.episodes[0]) : item
  const playLabel = item.Type === 'Series'
    ? (playTarget ? `${playTarget.UserData?.PlaybackPositionTicks > 0 ? 'Resume' : 'Play'} ${episodeCode(playTarget)}` : '')
    : resumable ? 'Resume' : 'Play'

  view.innerHTML = `<article class="title-page">
    <section class="hero is-title">
      ${heroArt(item)}
      <div class="hero-body">
        ${lg ? `<img class="hero-logo" src="${esc(lg)}" alt="${esc(item.Name)}">` : `<h1 class="hero-title">${esc(item.Type === 'Episode' ? item.Name : item.Name)}</h1>`}
        <p class="hero-meta">${esc(meta)}</p>
        ${item.Genres?.length ? `<p class="hero-genres">${esc(item.Genres.slice(0, 4).join(' · '))}</p>` : ''}
        <div class="actions">
          ${playTarget ? `<button class="primary is-primary" data-play="${playTarget.Id}">${icon('player-play-filled')}${esc(playLabel)}</button>` : ''}
          ${resumable && item.Type !== 'Series' ? `<button class="secondary exo-glass is-over-art" data-play-start="${item.Id}">From the start</button>` : ''}
          ${item.Type !== 'Series' ? `<button class="secondary exo-glass is-over-art ${item.UserData?.Played ? 'is-on' : ''}" data-played="${item.Id}" aria-pressed="${!!item.UserData?.Played}">${icon('check')}Watched</button>` : ''}
        </div>
        ${item.Overview ? `<p class="overview">${esc(item.Overview)}</p>` : ''}
      </div>
    </section>
    ${seasons?.length ? `<section class="episodes">
      <div class="exo-glass seasons" role="tablist">${seasons.map((s) => `<a role="tab" class="tab ${s.Id === episodes?.season?.Id ? 'is-on' : ''}" href="#/item/${item.Id}/${s.Id}">${esc(s.Name)}</a>`).join('')}</div>
      <ol class="episode-list">${(episodes?.episodes ?? []).map(episodeRow).join('')}</ol>
    </section>` : seasons === null && item.Type === 'Series' ? '<div class="loading"><div class="spinner"></div></div>' : ''}
  </article>`
}

function episodeRow(e) {
  const still = e.ImageTags?.Primary ? image(e.Id, 'Primary', e.ImageTags.Primary, 320) : ''
  return `<li><button class="episode" data-play="${e.Id}">
    <span class="art">${still ? `<img loading="lazy" src="${esc(still)}" alt="">` : ''}${progress(e)}${watched(e)}</span>
    <span class="episode-body">
      <span class="episode-title">${esc(e.IndexNumber != null ? `${e.IndexNumber}. ` : '')}${esc(e.Name)}</span>
      <span class="episode-meta">${esc([runtime(e.RunTimeTicks), left(e)].filter(Boolean).join(' · '))}</span>
      ${e.Overview ? `<span class="episode-overview">${esc(e.Overview)}</span>` : ''}
    </span>
  </button></li>`
}

let searchTimer = 0
async function renderSearch(term) {
  drawRooms()
  if (!term) { view.innerHTML = ''; return }
  view.innerHTML = '<div class="loading"><div class="spinner"></div></div>'
  try {
    const r = await jf('search', { term })
    if (route() !== `search/${encodeURIComponent(term)}`) return
    const items = r.Items ?? []
    view.innerHTML = items.length
      ? `<section class="grid-page"><div class="grid">${items.map((i) => i.Type === 'Episode' ? wideCard(i) : posterCard(i)).join('')}</div></section>`
      : `<p class="empty-words">No results for “${esc(term)}”</p>`
  } catch (e) {
    view.innerHTML = problem(e.message, `search/${encodeURIComponent(term)}`)
  }
}

const problem = (message, again) =>
  `<div class="problem"><p>${esc(message)}</p><a class="secondary exo-glass" href="#/${again}" data-retry>Try again</a></div>`

// ---- Routing

const route = () => location.hash.replace(/^#\/?/, '') || 'home'

function render() {
  if (!session) return
  const [page, a, b] = route().split('/')
  if (page !== 'home') { view.querySelector('.home')?.remove(); window.scrollTo(0, 0) }
  switch (page) {
    case 'view': return renderView(a)
    case 'item': return renderItem(a, b)
    case 'search': return renderSearch(decodeURIComponent(a ?? ''))
    case 'settings': return renderSettings()
    default: return renderHome()
  }
}

window.addEventListener('hashchange', render)

// ---- Playing

// Play: with Settings > Playback > Source on Ask, the version first (unless the title has just one); otherwise the
// host takes the best version for this PC. Up Next passes ask = false, so a countdown never stops for a question.
async function play(id, resume = true, ask = true) {
  let source
  if (ask && pref('source') === 'ask' && knownItem(id)?.MediaSourceCount !== 1) {
    source = await pickSource(id)
    if (source === null) return
  }
  try {
    enterPlayer('')
    $('title').textContent = ''
    $('spinner').hidden = false
    const r = await jf('play', { id, resume, addon: localStorage.getItem('exo:addon') || undefined, source })
    if (r) state.playMethod = r.playMethod === 'DirectPlay' ? 'Direct play' : 'Server transcode'
    if (r?.item) window.dispatchEvent(new CustomEvent('exo:now-playing', { detail: r.item }))
  } catch (e) {
    if (!inPlayer()) return
    leavePlayer()
    toast(e.message)
  }
}

// The item as this PC last drew it, if it has it: its MediaSourceCount says whether there is a version to choose.
function knownItem(id) {
  const prefix = `exo:${session?.userId}:`
  for (let i = 0; i < localStorage.length; i++) {
    const key = localStorage.key(i)
    if (!key?.startsWith(prefix)) continue
    let found = null
    try { JSON.parse(localStorage.getItem(key), (k, v) => { if (!found && v && typeof v === 'object' && v.Id === id && 'MediaSourceCount' in v) found = v; return v }) } catch { }
    if (found) return found
  }
  return null
}

// A version as the sheet lists it: the picture first, then what else sets it apart.
const RANGES = { DOVI: 'Dolby Vision', DOVIWithHDR10: 'Dolby Vision', DOVIWithHLG: 'Dolby Vision', DOVIWithSDR: 'Dolby Vision', HDR10: 'HDR10', HDR10Plus: 'HDR10+', HLG: 'HLG' }
const CODECS = { hevc: 'HEVC', h264: 'H.264', av1: 'AV1', vp9: 'VP9', mpeg2video: 'MPEG-2', truehd: 'TrueHD', eac3: 'E-AC-3', ac3: 'AC-3', dts: 'DTS', aac: 'AAC', flac: 'FLAC', opus: 'Opus', mp3: 'MP3' }
const CHANNELS = { 1: 'Mono', 2: 'Stereo', 6: '5.1', 8: '7.1' }

function sourceRow(s) {
  const h = s.height ?? 0
  const res = h >= 2000 ? '4K' : h >= 1400 ? '1440p' : h >= 1000 ? '1080p' : h >= 700 ? '720p' : h ? `${h}p` : ''
  const range = Object.entries(RANGES).find(([k]) => s.range?.startsWith(k))?.[1] ?? ''
  const picture = [res, range, CODECS[s.videoCodec] ?? s.videoCodec?.toUpperCase()].filter(Boolean).join(' · ') || s.name || 'Version'
  let language = s.language ?? ''
  try { language = language && (new Intl.DisplayNames(undefined, { type: 'language' }).of(language) ?? language) } catch { }
  const audio = [CODECS[s.audioCodec] ?? s.audioCodec?.toUpperCase(), CHANNELS[s.channels] ?? (s.channels ? `${s.channels} ch` : ''), language].filter(Boolean).join(' ')
  // The version's own name only when it says more than the picture does ("4K Remux", not "1080p" again).
  const named = s.name && !picture.toLowerCase().includes(s.name.toLowerCase()) ? s.name : ''
  const detail = [named, s.size ? bytes(s.size) : '', s.bitrate ? `${Math.round(s.bitrate / 1e6)} Mbps` : '', audio, s.direct ? '' : 'Converted by the server'].filter(Boolean).join(' · ')
  return `<button type="button" class="source-row" role="option" data-source="${esc(s.id)}">
    <span class="source-text"><span class="source-name">${esc(picture)}</span><span class="source-detail">${esc(detail)}</span></span>
    ${s.best ? '<span class="source-best">Best</span>' : ''}</button>`
}

// The source sheet: the item's versions, best first, to pick one; null when it is put away. It opens at once and
// fills when the server answers; a title with one version plays without asking.
function pickSource(id) {
  return new Promise((resolve) => {
    const sheet = $('sources'), list = $('sourcesList')
    let done = false
    const finish = (value) => {
      if (done) return
      done = true
      sheet.hidden = true
      sheet.onclick = null
      document.removeEventListener('keydown', onKey, true)
      resolve(value)
    }
    const onKey = (e) => { if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); finish(null) } }
    list.innerHTML = '<div class="spinner sources-wait"></div>'
    sheet.hidden = false
    document.addEventListener('keydown', onKey, true)
    sheet.onclick = (e) => {
      if (e.target.closest('[data-close-sources]')) finish(null)
      const row = e.target.closest('[data-source]')
      if (row) finish(row.dataset.source)
    }
    jf('sources', { id }).then((all) => {
      if (done) return
      if (!all?.length || all.length === 1) { finish(all?.[0]?.id); return }
      list.innerHTML = all.map(sourceRow).join('')
      list.querySelector('[data-source]')?.focus({ preventScroll: true })
    }).catch((err) => { if (!done) { finish(null); toast(err.message) } })
  })
}

window.addEventListener('exo:notice', (e) => toast(e.detail))

window.addEventListener('exo:player-closed', async () => {
  await jf('stop').catch(() => { })
  // The server has the new position now: draw the page again with it.
  render()
})

// ---- Clicks

document.addEventListener('click', async (e) => {
  const t = e.target.closest('[data-play],[data-play-start],[data-open],[data-played],[data-app],[data-retry],[data-pref]')
  if (!t || inPlayer()) return
  if (t.dataset.play) play(t.dataset.play, true)
  else if (t.dataset.playStart) play(t.dataset.playStart, false)
  else if (t.dataset.open) location.hash = `#/item/${t.dataset.open}`
  else if (t.dataset.played) {
    const on = t.getAttribute('aria-pressed') !== 'true'
    t.setAttribute('aria-pressed', on)
    t.classList.toggle('is-on', on)
    try { cache.set(`item:${t.dataset.played}`, await jf('played', { id: t.dataset.played, played: on })) } catch (err) { toast(err.message) }
  } else if (t.dataset.retry != null) { e.preventDefault(); render() }
  else if (t.dataset.app === 'sign-out') {
    closeMenu()
    await jf('sign-out')
    session = null
    showSignIn()
  } else if (t.dataset.app === 'settings') {
    closeMenu()
    location.hash = '#/settings'
  } else if (t.dataset.app === 'clear-cache') {
    t.disabled = true
    try { await jf('cache-clear') } catch { }
    t.disabled = false
    renderSettings()
  } else if (t.dataset.pref) {
    setPref(t.dataset.pref, t.dataset.value)
    if (route() === 'settings') renderSettings()
    window.dispatchEvent(new Event('exo:prefs'))
  } else if (t.dataset.app === 'jellyfin-settings') {
    closeMenu()
    send({ cmd: 'jellyfin-web' })
  }
})

function closeMenu() {
  $('settingsMenu').hidden = true
  $('profileBtn').classList.remove('is-on')
}

$('profileBtn').addEventListener('click', (e) => {
  e.stopPropagation()
  const open = $('settingsMenu').hidden
  $('settingsMenu').hidden = !open
  $('profileBtn').classList.toggle('is-on', open)
})

// ---- Settings: only what can change (Exo's rule), each applied the moment it is picked

const PREFS = {
  material: { label: 'Material', options: [['glass', 'Glass'], ['solid', 'Solid']], fallback: 'glass' },
  source: { label: 'Source', options: [['best', 'Best'], ['ask', 'Ask']], fallback: 'best' },
  autoplay: { label: 'Play next episode', options: [['on', 'On'], ['off', 'Off']], fallback: 'on' },
}

const pref = (key) => { try { return localStorage.getItem(`exo:pref:${key}`) ?? PREFS[key].fallback } catch { return PREFS[key].fallback } }

function setPref(key, value) {
  try { localStorage.setItem(`exo:pref:${key}`, value) } catch { }
  applyPrefs()
}

// The picture and the decoder aren't here: the app picks them from this PC as it plays (MainWindow.Player.cs).
function applyPrefs() {
  document.documentElement.dataset.material = pref('material')
}
applyPrefs()
try { for (const key of ['quality', 'hwdec']) localStorage.removeItem(`exo:pref:${key}`) } catch { }

// One choice as Settings shows it: its name, and its options in Exo's segmented capsule.
const prefRow = (key) => `<div class="set-row"><span class="set-name">${PREFS[key].label}</span>
  <div class="exo-glass segmented" role="radiogroup" aria-label="${PREFS[key].label}">${PREFS[key].options.map(([v, l]) =>
    `<button type="button" role="radio" class="seg${pref(key) === v ? ' is-on' : ''}" aria-checked="${pref(key) === v}" data-pref="${key}" data-value="${v}">${l}</button>`).join('')}</div></div>`

const bytes = (n) => n >= 1e9 ? `${(n / 1e9).toFixed(1)} GB` : n >= 1e6 ? `${Math.round(n / 1e6)} MB` : `${Math.round(n / 1e3)} KB`

async function renderSettings() {
  drawRooms()
  const row = prefRow
  let cache = 0
  try { cache = (await jf('cache-size')).bytes } catch { }
  if (route() !== 'settings') return
  view.innerHTML = `<section class="settings-page">
    <h1>Settings</h1>
    <div class="exo-glass is-panel set-group"><h2>Look</h2>${row('material')}</div>
    <div class="exo-glass is-panel set-group"><h2>Playback</h2>${row('source')}${row('autoplay')}
      <div class="set-row"><span class="set-name">Stream addon</span><input class="exo-field is-secret" id="addonUrl" type="url" inputmode="url" style="width:280px" placeholder="Stremio manifest URL" spellcheck="false" autocapitalize="off" autocomplete="off" value="${esc(localStorage.getItem('exo:addon') ?? '')}"></div></div>
    <div class="exo-glass is-panel set-group"><h2>Account</h2>
      <div class="set-row"><span class="set-name">${esc(session.userName)}</span><span class="set-value">${esc(session.serverName)}</span></div>
      <div class="set-row"><span class="set-name">Jellyfin account</span><button type="button" class="set-btn" data-app="jellyfin-settings">Open</button></div>
      <div class="set-row"><span class="set-name">Sign out</span><button type="button" class="set-btn" data-app="sign-out">Sign out</button></div></div>
    <div class="exo-glass is-panel set-group"><h2>Storage</h2>
      <div class="set-row"><span class="set-name">Image cache</span><span class="set-value">${bytes(cache)}</span><button type="button" class="set-btn" data-app="clear-cache">Clear</button></div></div>
    ${aboutGroup()}
  </section>`
  drawAbout()
  view.querySelector('#addonUrl')?.addEventListener('input', (e) => {
    const v = e.target.value.trim()
    v ? localStorage.setItem('exo:addon', v) : localStorage.removeItem('exo:addon')
  })
}
document.addEventListener('click', (e) => { if (!e.target.closest('#settingsMenu')) closeMenu() })


// Mouse back button and Backspace go back in the library.
document.addEventListener('mouseup', (e) => {
  if (e.button !== 3) return
  if (inPlayer()) actions.back()
  else history.back()
})
document.addEventListener('keydown', (e) => {
  if (inPlayer()) return
  if ((e.ctrlKey && e.key.toLowerCase() === 'k') || (e.key === '/' && document.activeElement?.tagName !== 'INPUT')) {
    e.preventDefault()
    openPalette()
  } else if (e.key === 'Backspace' && document.activeElement?.tagName !== 'INPUT') {
    history.back()
  }
})

// ---- Ctrl+K: search the server as you type; arrows and Enter open a result

let paletteItems = []
let paletteIndex = 0
let paletteAsk = 0

function openPalette() {
  if (inPlayer() || !session) return
  $('palette').hidden = false
  $('paletteInput').value = ''
  $('paletteResults').innerHTML = ''
  paletteItems = []
  searching(false)
  requestAnimationFrame(() => $('paletteInput').focus())
}

function closePalette() {
  $('palette').hidden = true
  $('paletteInput').blur()
  paletteAsk++
  searching(false)
}

// While the server looks, the field's own spinner turns; what was found stays until new results replace it.
const searching = (on) => $('palette').classList.toggle('is-searching', on)

// The results, or when there are none, what happened: no matches, or why the search failed.
function drawPalette(note = '', bad = false) {
  if (!paletteItems.length && note) {
    $('paletteResults').innerHTML = `<p class="palette-note${bad ? ' is-bad' : ''}">${esc(note)}</p>`
    return
  }
  $('paletteResults').innerHTML = paletteItems.map((i, n) => {
    const src = poster(i, 80)
    const kind = i.Type === 'Episode' ? `${i.SeriesName} · ${episodeCode(i)}` : i.Type === 'Series' ? 'Show' : 'Movie'
    const sub = [kind, i.ProductionYear].filter(Boolean).join(' · ')
    return `<button type="button" class="palette-row${n === paletteIndex ? ' is-on' : ''}" role="option" aria-selected="${n === paletteIndex}" data-pick="${n}">
      <span class="palette-art">${src ? `<img src="${esc(src)}" alt="">` : ''}</span>
      <span class="palette-text"><span class="palette-name">${esc(i.Name)}</span><span class="palette-sub">${esc(sub)}</span></span>
    </button>`
  }).join('')
}

function pickPalette(n) {
  const i = paletteItems[n]
  if (!i) return
  closePalette()
  location.hash = `#/item/${i.Type === 'Episode' && i.SeriesId ? `${i.SeriesId}${i.SeasonId ? `/${i.SeasonId}` : ''}` : i.Id}`
}

$('openSearch').addEventListener('click', openPalette)
$('palette').addEventListener('click', (e) => {
  if (e.target.closest('[data-close-palette]')) closePalette()
  const row = e.target.closest('[data-pick]')
  if (row) pickPalette(+row.dataset.pick)
})
$('paletteInput').addEventListener('input', () => {
  clearTimeout(searchTimer)
  const term = $('paletteInput').value.trim()
  const ask = ++paletteAsk
  if (!term) { paletteItems = []; drawPalette(); searching(false); return }
  searching(true)
  // A beat after the last key, so a word typed quickly asks the server once.
  searchTimer = setTimeout(async () => {
    try {
      const r = await jf('search', { term })
      if (ask !== paletteAsk) return
      paletteItems = (r?.Items ?? []).slice(0, 12)
      paletteIndex = 0
      drawPalette('No matches')
    } catch (err) {
      if (ask !== paletteAsk) return
      paletteItems = []
      drawPalette(err.message, true)
    } finally {
      if (ask === paletteAsk) searching(false)
    }
  }, 250)
})
$('paletteInput').addEventListener('keydown', (e) => {
  if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); closePalette() }
  else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    e.preventDefault()
    if (!paletteItems.length) return
    paletteIndex = (paletteIndex + (e.key === 'ArrowDown' ? 1 : -1) + paletteItems.length) % paletteItems.length
    drawPalette()
    $('paletteResults').querySelector('.is-on')?.scrollIntoView({ block: 'nearest' })
  } else if (e.key === 'Enter') { e.preventDefault(); pickPalette(paletteIndex) }
})

function toast(message) {
  const t = document.createElement('div')
  t.className = 'toast exo-glass is-panel'
  t.setAttribute('role', 'status')
  t.textContent = message
  document.body.append(t)
  setTimeout(() => t.classList.add('is-leaving'), 4000)
  setTimeout(() => t.remove(), 4400)
}

// ---- Sign-in

function showSignIn() {
  document.body.classList.add('is-signed-out')
  $('signin').hidden = false
  $('app').hidden = true
  $('signinError').hidden = true
  const last = localStorage.getItem('exo:last-server')
  if (last && !$('server').value) { $('server').value = last; probe() }
  ;($('server').value ? $('username') : $('server')).focus()
}

function signedIn(s) {
  session = s
  localStorage.setItem('exo:last-server', s.server)
  document.body.classList.remove('is-signed-out')
  $('signin').hidden = true
  $('app').hidden = false
  $('accountWho').textContent = `Signed in as ${s.userName} on ${s.serverName}`
  // The profile button: the user's Jellyfin picture, or their initial.
  $('avatar').innerHTML = s.imageTag
    ? `<img src="${esc(`${s.server}/Users/${s.userId}/Images/Primary?fillWidth=${Math.round(48 * devicePixelRatio)}&quality=90&tag=${s.imageTag}`)}" alt="">`
    : esc((s.userName || '?').slice(0, 1).toUpperCase())
  if (!location.hash) location.replace('#/home')
  window.dispatchEvent(new Event('exo:signed-in'))
  return render()
}

let probeTimer = 0
let probed = null
async function probe() {
  const server = $('server').value.trim()
  if (!server) { $('serverFound').hidden = true; return }
  try {
    const info = await jf('probe', { server })
    if ($('server').value.trim() !== server) return
    probed = info
    $('serverFound').hidden = false
    $('serverFound').textContent = `${info.name} · Jellyfin ${info.version}`
    $('quickConnect').hidden = !info.quickConnect
    $('signinError').hidden = true
  } catch (e) {
    if ($('server').value.trim() !== server) return
    probed = null
    $('serverFound').hidden = true
    $('quickConnect').hidden = true
  }
}

$('server').addEventListener('input', () => { clearTimeout(probeTimer); probeTimer = setTimeout(probe, 450) })

$('signinForm').addEventListener('submit', async (e) => {
  e.preventDefault()
  const button = $('signinButton')
  button.disabled = true
  button.textContent = 'Signing in…'
  $('signinError').hidden = true
  window.dispatchEvent(new Event('exo:signing-in'))
  try {
    const s = await jf('sign-in', { server: $('server').value.trim(), username: $('username').value.trim(), password: $('password').value })
    $('password').value = ''
    signedIn(s)
  } catch (err) {
    $('signinError').textContent = err.message
    $('signinError').hidden = false
    window.dispatchEvent(new Event('exo:sign-in-failed'))
  } finally {
    button.disabled = false
    button.textContent = 'Sign in'
  }
})

let quickTimer = 0
$('quickConnect').addEventListener('click', async () => {
  clearInterval(quickTimer)
  const server = $('server').value.trim()
  try {
    const q = await jf('quick-connect-start', { server })
    $('quickCode').hidden = false
    $('quickCode').textContent = q.Code
    quickTimer = setInterval(async () => {
      try {
        const r = await jf('quick-connect-poll', { server, secret: q.Secret })
        if (r && r.done !== false) { clearInterval(quickTimer); $('quickCode').hidden = true; signedIn(r) }
      } catch (err) {
        clearInterval(quickTimer)
        $('signinError').textContent = err.message
        $('signinError').hidden = false
      }
    }, 2000)
  } catch (err) {
    $('signinError').textContent = err.message
    $('signinError').hidden = false
  }
})

// ---- Start

// Behind the opening (boot.js): the saved sign-in, the first screen with its data, and its pictures decoded, so it
// shows whole when the splash lifts instead of filling in after. Then, out of sight, the title pages a click away.
const FIRST_DATA_CAP_MS = 2500
const FIRST_PICTURES_CAP_MS = 1500
const nextFrame = () => new Promise((r) => requestAnimationFrame(() => r()))
const waitMs = (ms) => new Promise((r) => setTimeout(r, Math.max(0, ms)))

async function firstScreenReady(drawing) {
  // With last time's Home on disk it draws at once; on a first start its data is waited for, up to a cap.
  const hadHome = !!cache.get('home')
  if (!hadHome && drawing) await Promise.race([drawing, waitMs(FIRST_DATA_CAP_MS)])
  await document.fonts.ready
  await nextFrame()
  await nextFrame()
  const onScreen = [...document.querySelectorAll('#view img')].filter((img) => {
    const r = img.getBoundingClientRect()
    return r.width > 0 && r.bottom > 0 && r.top < innerHeight && r.right > 0 && r.left < innerWidth
  }).slice(0, 40)
  // A lazy picture out of sight never starts: on the first screen it loads now, so it is there with the screen.
  for (const img of onScreen) if (img.loading === 'lazy') img.loading = 'eager'
  await Promise.race([Promise.allSettled(onScreen.map((img) => img.decode())), waitMs(FIRST_PICTURES_CAP_MS)])
}

// The title pages a click away (the hero, Continue Watching, Next Up), fetched quietly so they open at once.
async function warmTitles() {
  const home = cache.get('home')
  if (!home) return
  const ids = [...new Set([...(home.resume ?? []), ...(home.nextUp ?? [])].slice(0, 12).map((i) => i.Type === 'Episode' ? i.SeriesId : i.Id).filter(Boolean))]
  for (const id of ids) {
    if (cache.get(`item:${id}`)) continue
    try { cache.set(`item:${id}`, await jf('item', { id })) } catch { }
  }
}

;(async () => {
  let drawing = null
  try {
    appInfo = await app('settings')
    Object.assign(update, { latest: appInfo.update?.latest ?? null, notes: appInfo.update?.notes ?? null, installable: !!appInfo.update?.installable })
  } catch { }
  const firstRun = appInfo && !appInfo.onboarded
  try {
    const s = await jf('session')
    if (s) drawing = signedIn(s)
    else if (firstRun) startOnboarding()
    else showSignIn()
  } catch {
    firstRun ? startOnboarding() : showSignIn()
  }
  // Signed in from an earlier version with no first run behind it: nothing to set up.
  if (firstRun && !ob.active) app('set', { onboarded: true }).catch(() => { })
  try { await firstScreenReady(drawing) } catch { }
  exoBoot.reveal()
  setTimeout(warmTitles, 1200)
  setTimeout(() => maybeWhatsNew().catch(() => { }), 600)
})()
