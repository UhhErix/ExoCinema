// The first run, as Exo Launcher's: Welcome, then Jellyfin, Playback and Your look. Continue keeps what is shown;
// only Jellyfin has to be done, since without a server there is nothing to watch. The sign-in form is the sign-in
// sheet's own, lent to the Jellyfin step while it shows.

const OB_STEPS = ['welcome', 'jellyfin', 'playback', 'look']
const ob = { active: false, step: 0 }

function startOnboarding() {
  ob.active = true
  document.body.classList.add('is-onboarding')
  $('onboarding').hidden = false
  drawOb(0, 0)
}

function drawOb(n, dir) {
  ob.step = n
  const name = OB_STEPS[n]
  const step = $('obStep')
  const form = $('signinForm')
  if (form.parentElement !== $('signin')) $('signin').append(form)
  $('onboarding').classList.toggle('is-welcome', name === 'welcome')
  $('obProgress').style.setProperty('--p', String(n / (OB_STEPS.length - 1)))
  $('obProgress').setAttribute('aria-valuenow', String(n))
  $('obProgress').setAttribute('aria-valuetext', `Step ${n} of ${OB_STEPS.length - 1}`)
  $('obBack').hidden = n === 0
  note('')
  const cta = $('obNext')
  cta.disabled = false

  const title = (words) => `<h1 class="ob-title" tabindex="-1">${words}</h1>`
  switch (name) {
    case 'welcome':
      step.innerHTML = `<img class="ob-logo" src="logo.svg" alt="" draggable="false">${title('Welcome to Exo Cinema')}`
      cta.textContent = 'Get started'
      break
    case 'jellyfin':
      step.innerHTML = `${title('Jellyfin')}<div class="ob-body" id="obBody"></div>`
      if (session) {
        $('obBody').innerHTML = `<div class="exo-glass is-panel set-group"><div class="set-row">
          <span class="set-name">${esc(session.userName)} on ${esc(session.serverName)}</span><span class="set-value">Signed in</span></div></div>`
        cta.textContent = 'Continue'
      } else {
        $('obBody').append(form)
        cta.textContent = 'Sign in'
        const last = localStorage.getItem('exo:last-server')
        if (last && !$('server').value) { $('server').value = last; probe() }
      }
      break
    case 'playback':
      step.innerHTML = `${title('Playback')}<div class="ob-body"><div class="exo-glass is-panel set-group">${prefRow('source')}${prefRow('autoplay')}</div></div>`
      cta.textContent = 'Continue'
      break
    case 'look':
      step.innerHTML = `${title('Your look')}<div class="ob-body"><div class="exo-glass is-panel set-group">${prefRow('material')}</div></div>`
      cta.textContent = 'Start watching'
      break
  }
  step.classList.remove('is-forward', 'is-back')
  if (dir) { void step.offsetWidth; step.classList.add(dir > 0 ? 'is-forward' : 'is-back') }
  if (name === 'jellyfin' && !session) focusWhenActive($('server').value ? $('username') : $('server'))
  else focusWhenActive(step.querySelector('.ob-title'))
}

function note(words, bad = false) {
  $('obNote').textContent = words
  $('obNote').classList.toggle('is-bad', bad)
}

async function finishOnboarding() {
  ob.active = false
  $('signin').append($('signinForm'))
  try { await app('set', { onboarded: true, whatsNewSeen: appInfo?.version }) } catch { }
  const el = $('onboarding')
  el.classList.add('is-leaving')
  setTimeout(() => {
    el.hidden = true
    el.classList.remove('is-leaving')
    document.body.classList.remove('is-onboarding')
  }, 220)
}

$('obNext').addEventListener('click', () => {
  if (OB_STEPS[ob.step] === 'jellyfin' && !session) { $('signinForm').requestSubmit(); return }
  if (ob.step < OB_STEPS.length - 1) drawOb(ob.step + 1, 1)
  else finishOnboarding()
})
$('obBack').addEventListener('click', () => drawOb(Math.max(0, ob.step - 1), -1))

// Signing in from the step: the button says so while it happens, and the step shows who signed in after.
window.addEventListener('exo:signing-in', () => {
  if (!ob.active) return
  $('obNext').disabled = true
  $('obNext').textContent = 'Signing in…'
})
window.addEventListener('exo:sign-in-failed', () => {
  if (!ob.active) return
  $('obNext').disabled = false
  $('obNext').textContent = 'Sign in'
})
window.addEventListener('exo:signed-in', () => { if (ob.active && OB_STEPS[ob.step] === 'jellyfin') drawOb(ob.step, 0) })
window.addEventListener('exo:prefs', () => { if (ob.active && ['playback', 'look'].includes(OB_STEPS[ob.step])) drawOb(ob.step, 0) })
