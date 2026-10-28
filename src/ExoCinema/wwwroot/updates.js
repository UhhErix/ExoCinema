// What's New after an update and from Settings › About, and updates (the host checks GitHub: MainWindow.App.cs).

// whats-new.json names icons in letters only; these are Exo Cinema's glyphs for them.
const NEWS_ICONS = { Library: 'movie', Sync: 'device-tv', Play: 'player-play-filled', Glass: 'sparkles', Tracks: 'adjustments-horizontal', Skip: 'player-skip-forward-filled', Home: 'home', Search: 'search' }

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
    if (m.state === 'available') {
      Object.assign(update, { state: 'idle', latest: m.latest, notes: m.notes ?? null })
      tellUpdateOnce()
    } else {
      update.state = m.state
      if (m.percent != null) update.percent = m.percent
      update.message = m.message ?? ''
    }
    drawAbout()
  }
})

// A new version is told once, wherever you are; Settings › About installs it.
function tellUpdateOnce() {
  const key = `exo:update-told:${update.latest}`
  try { if (localStorage.getItem(key)) return; localStorage.setItem(key, '1') } catch { }
  toast(`Exo Cinema ${update.latest} is ready to install. Settings › About`)
}

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
  $('whatsNew').hidden = false
  $('whatsNewContinue').focus()
}

function closeWhatsNew() { $('whatsNew').hidden = true }

$('whatsNew').addEventListener('click', (e) => {
  if (e.target.closest('[data-close-whats-new], #whatsNewContinue')) closeWhatsNew()
  else if (e.target.closest('#whatsNewInstall')) { closeWhatsNew(); installUpdate() }
})
document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !$('whatsNew').hidden) { e.stopPropagation(); closeWhatsNew() } }, true)

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
  app('install-update').catch((err) => { Object.assign(update, { state: 'failed', message: err.message }); drawAbout() })
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
