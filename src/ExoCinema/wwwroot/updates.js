// What's New after an update and from Settings › About, and updates (the host checks GitHub: MainWindow.App.cs).

// whats-new.json names icons in letters only; these are Exo Cinema's glyphs for them.
const NEWS_ICONS = { Library: 'movie', Sync: 'device-tv', Play: 'player-play-filled', Glass: 'sparkles', Tracks: 'adjustments-horizontal', Skip: 'player-skip-forward-filled', Home: 'home', Search: 'search', Private: 'lock' }

let appInfo = null
const update = { state: 'idle', latest: null, notes: null, installable: false, percent: 0, message: '' }

const versionAfter = (a, b) => {
  const pa = String(a ?? '0').split('.').map(Number), pb = String(b ?? '0').split('.').map(Number)
  for (let i = 0; i < 3; i++) if ((pa[i] || 0) !== (pb[i] || 0)) return (pa[i] || 0) > (pb[i] || 0)
  return false
}

onHost((m) => {
  if (m.type === 'hello' && m.app) {
    appInfo = m.app
    Object.assign(update, { latest: m.app.update?.latest ?? null, notes: m.app.update?.notes ?? null, installable: !!m.app.update?.installable })
  } else if (m.type === 'update') {
    update.state = m.state
    if (m.percent != null) update.percent = m.percent
    update.message = m.message ?? ''
    drawAbout()
    drawOffer()
  }
})

// ── A sheet holds the window ────────────────────────────────────────────────────────────────────────────────────
// While What's New or the update sheet is up, nothing behind it scrolls or takes input (the user's rule): the rest of
// the page is inert, the page can't scroll, and every key goes to the sheet alone, so no shortcut behind it fires.
// The window's own buttons work, drawn again on the sheet's layer. All of it lifts when the sheet goes.
let holding = null
function holdWindow(layer, onKey) {
  holding = { layer, onKey, inert: [] }
  for (const el of document.body.children) if (el !== layer && !el.inert) { el.inert = true; holding.inert.push(el) }
  document.documentElement.classList.add('is-held')
  layer.hidden = false
  focusWhenActive(layer.querySelector('.whats-new-sheet'))
}
function releaseWindow(layer) {
  if (holding?.layer !== layer) return
  for (const el of holding.inert) el.inert = false
  holding = null
  layer.hidden = true
  document.documentElement.classList.remove('is-held')
}
window.addEventListener('keydown', (e) => {
  if (!holding) return
  e.stopImmediatePropagation()
  if (!holding.layer.contains(e.target)) e.preventDefault()
  holding.onKey?.(e)
}, true)

// ── What's New ──────────────────────────────────────────────────────────────────────────────────────────────────

let bundledNews = null
async function news() {
  bundledNews ??= await (await fetch('whats-new.json')).json()
  return bundledNews
}

// After an update, once: every release since the one last seen, newest first.
async function maybeWhatsNew() {
  if (!appInfo?.onboarded || !versionAfter(appInfo.version, appInfo.whatsNewSeen) || inPlayer() || ob.active) return
  const seen = appInfo.whatsNewSeen
  const releases = (await news()).filter((r) => versionAfter(r.version, seen) && !versionAfter(r.version, appInfo.version))
  appInfo.whatsNewSeen = appInfo.version
  app('set', { whatsNewSeen: appInfo.version }).catch(() => { })
  if (releases.length) openWhatsNew(`What's new in Exo Cinema ${appInfo.version}`, releases)
}

function openWhatsNew(title, releases, install = false) {
  $('whatsNewTitle').textContent = title
  $('whatsNewList').innerHTML = releases.map((r, n) => `${n ? `<h3 class="whats-new-in">In ${esc(r.version)}</h3>` : ''}${(r.items ?? []).map((item) => `
    <div class="whats-new-item"><span class="whats-new-icon">${icon(NEWS_ICONS[item.icon] ?? 'sparkles')}</span>
      <span class="whats-new-text"><span class="whats-new-name">${esc(item.title)}</span><span class="whats-new-detail">${esc(item.detail)}</span></span></div>`).join('')}`).join('')
  $('whatsNewInstall').hidden = !install
  $('whatsNewContinue').classList.toggle('primary', !install)
  $('whatsNewContinue').classList.toggle('is-primary', !install)
  $('whatsNewContinue').classList.toggle('secondary', install)
  // The sheet takes the focus, not Continue: a button focused before anything is pressed wears the keyboard ring.
  // Escape, or Enter on the sheet itself, is Continue.
  holdWindow($('whatsNew'), (e) => {
    if (e.key === 'Escape' || (e.key === 'Enter' && e.target.classList.contains('whats-new-sheet'))) { e.preventDefault(); closeWhatsNew() }
  })
}

function closeWhatsNew() { releaseWindow($('whatsNew')) }

$('whatsNew').addEventListener('click', (e) => {
  if (e.target.closest('#whatsNewContinue')) closeWhatsNew()
  else if (e.target.closest('#whatsNewInstall')) { closeWhatsNew(); installUpdate() }
})

// ── Update available, at open ───────────────────────────────────────────────────────────────────────────────────
// The host looks once as the window opens (MainWindow.App.cs); a newer version is offered here, before anything else,
// and asked again at every open until it's installed. Just the version and Update now: its changes are What's New's,
// shown once the new version opens. Update now downloads it with its progress, checks it and installs it. Nothing is
// ever locked out: offline shows nothing, and a failed download says why, with Try again, and Not now for this time.

async function offerUpdate() {
  let r = null
  try { r = await app('open-check') } catch { return false }
  if (!r?.latest || !r.offer) return false
  Object.assign(update, { latest: r.latest, notes: r.notes ?? null, installable: !!r.installable, retry: !!r.retry, state: 'offer' })
  if (!$('whatsNew').hidden) closeWhatsNew()
  $('updateOfferVersion').textContent = `Exo Cinema ${update.latest}`
  drawOffer()
  // Enter on the sheet is Update now; once Not now is offered (a failure, or an update that didn't take), Escape is it.
  holdWindow($('updateOffer'), (e) => {
    if (e.key === 'Enter' && e.target.classList.contains('whats-new-sheet') && update.state === 'offer') { e.preventDefault(); installUpdate() }
    else if (e.key === 'Escape' && (update.state === 'failed' || (update.state === 'offer' && update.retry))) { e.preventDefault(); releaseWindow($('updateOffer')) }
  })
  return true
}

// The sheet's foot follows the update: its button, then the download's bar and percent, then installing, or why it
// failed, with Copy logs, Not now and Try again. An update that didn't take (Exo Cinema opened on the old version after
// Update now) starts there too, so nobody is held in a loop of restarts.
function drawOffer() {
  const foot = $('updateOfferFoot')
  if (!foot || $('updateOffer').hidden && update.state !== 'offer') return
  const failure = (reason) => `<div class="update-failure"><p class="update-status is-bad" role="alert">${esc(reason)}</p>
      <button type="button" class="set-btn" data-offer="logs">Copy logs</button></div>
    <div class="whats-new-foot"><button type="button" class="secondary" data-offer="later">Not now</button><button type="button" class="primary is-primary" data-offer="update">Try again</button></div>`
  if (update.state === 'downloading' || update.state === 'installing') {
    const busy = update.state === 'installing'
    foot.innerHTML = `<div class="update-progress"><span class="mini-bar${busy ? ' is-busy' : ''}"><span style="--p:${busy ? 1 : update.percent / 100}"></span></span>
      <span class="update-status" role="status">${busy ? 'Installing' : `Downloading · ${update.percent}%`}</span></div>`
  } else if (update.state === 'failed') {
    foot.innerHTML = failure(update.message)
  } else if (update.retry) {
    foot.innerHTML = failure(`Exo Cinema ${update.latest} didn't finish installing last time.`)
  } else {
    foot.innerHTML = '<div class="whats-new-foot"><button type="button" class="primary is-primary" data-offer="update">Update now</button></div>'
  }
}

$('updateOffer').addEventListener('click', async (e) => {
  const b = e.target.closest('[data-offer]')
  if (b?.dataset.offer === 'update') installUpdate()
  else if (b?.dataset.offer === 'later') releaseWindow($('updateOffer'))
  else if (b?.dataset.offer === 'logs') {
    // The end of app.log (it holds no address, key or title), for a problem report; the button says it worked.
    try {
      await navigator.clipboard.writeText((await app('logs')).text)
      b.textContent = 'Copied'
    } catch {
      b.textContent = 'Couldn’t copy'
    }
    setTimeout(() => { if (b.isConnected) b.textContent = 'Copy logs' }, 2000)
  }
})

// ── Settings › About ────────────────────────────────────────────────────────────────────────────────────────────

function aboutGroup() {
  return `<div class="exo-glass is-panel set-group"><h2>About</h2>
    <div class="set-row"><span class="set-name">Exo Cinema</span><span class="set-value" id="aboutStatus"></span><span class="set-actions" id="aboutActions"></span></div>
    <div class="set-row"><span class="set-name">Help</span><span class="set-actions">
      <button type="button" class="set-btn" data-link="issues">Report a problem</button>
      <button type="button" class="set-btn" data-link="privacy">Privacy</button>
      <button type="button" class="set-btn" data-link="notices">Licenses</button></span></div></div>`
}

// The version row: what the last check found, and the one button that fits; a download shows its bar beside the
// words, never inside a button.
function drawAbout() {
  const status = $('aboutStatus'), actions = $('aboutActions')
  if (!status || !actions) return
  const version = appInfo?.version ?? ''
  status.classList.toggle('is-bad', update.state === 'failed')
  if (update.state === 'downloading' || update.state === 'installing') {
    status.textContent = update.state === 'downloading' ? `Downloading ${update.latest} · ${update.percent}%` : `Installing ${update.latest}`
    actions.innerHTML = `<span class="mini-bar${update.state === 'installing' ? ' is-busy' : ''}"><span style="--p:${update.percent / 100}"></span></span>`
    return
  }
  status.textContent = update.state === 'failed' ? update.message
    : update.state === 'checking' ? 'Checking for updates'
    : update.latest ? `Version ${version} · ${update.latest} is out`
    : update.state === 'current' ? `Version ${version} · Up to date`
    : `Version ${version}`
  const main = update.latest && update.installable
    ? '<button type="button" class="set-btn" data-about="install">Install update</button>'
    : `<button type="button" class="set-btn" data-about="check"${update.state === 'checking' ? ' disabled' : ''}>Check for updates</button>`
  actions.innerHTML = `<button type="button" class="set-btn" data-about="whats-new">What's new</button>${main}`
}

async function checkUpdate() {
  update.state = 'checking'
  drawAbout()
  try {
    const r = await app('check-update')
    Object.assign(update, { latest: r.latest ?? null, notes: r.notes ?? null, installable: !!r.installable, state: r.latest ? 'idle' : 'current' })
  } catch (err) {
    Object.assign(update, { state: 'failed', message: err.message })
  }
  drawAbout()
}

function installUpdate() {
  Object.assign(update, { state: 'downloading', percent: 0 })
  drawAbout()
  drawOffer()
  app('install-update').catch((err) => { Object.assign(update, { state: 'failed', message: err.message }); drawAbout(); drawOffer() })
}

document.addEventListener('click', async (e) => {
  const t = e.target.closest('[data-about],[data-link]')
  if (!t || inPlayer()) return
  if (t.dataset.link) app('open', { link: t.dataset.link }).catch(() => { })
  else if (t.dataset.about === 'check') checkUpdate()
  else if (t.dataset.about === 'install') installUpdate()
  else if (t.dataset.about === 'whats-new') {
    const all = (await news()).filter((r) => !versionAfter(r.version, appInfo?.version))
    if (update.latest && update.notes) openWhatsNew(`Coming in Exo Cinema ${update.latest}`, [update.notes, ...all], update.installable)
    else openWhatsNew(`What's new in Exo Cinema ${appInfo?.version ?? ''}`, all)
  }
})
