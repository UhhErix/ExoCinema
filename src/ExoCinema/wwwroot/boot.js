// The opening (boot.css). The page's first frames are its busiest: the intro starts once two frames have drawn and a
// beat has passed. app.js calls exoBoot.reveal() when the first screen is ready; the splash always plays in full
// (MIN_MS) and never holds the app back past CAP_MS.
// Reduce motion is Exo Cinema's own setting (Settings › Look), read here before the first frame. As in Exo Launcher,
// the first start takes it from Windows' Animation effects (which WebView2 reports as prefers-reduced-motion) and
// saves it; from then on only the switch decides, and it always shows what's in effect.
try {
  let motion = localStorage.getItem('exo:pref:motion')
  if (motion === null) {
    motion = matchMedia('(prefers-reduced-motion: reduce)').matches ? 'on' : 'off'
    localStorage.setItem('exo:pref:motion', motion)
  }
  if (motion === 'on') document.documentElement.dataset.exoMotion = 'reduce'
} catch { }

const exoBoot = (() => {
  const INTRO_SETTLE_MS = 120
  const MIN_MS = 900
  const CAP_MS = 4000
  const startedAt = performance.now()
  const root = document.getElementById('boot')
  let revealed = false

  requestAnimationFrame(() => requestAnimationFrame(() => setTimeout(() => root.classList.add('is-playing'), INTRO_SETTLE_MS)))

  function reveal() {
    if (revealed || !root) return
    revealed = true
    const wait = Math.max(0, MIN_MS - (performance.now() - startedAt))
    setTimeout(() => {
      const mark = root.querySelector('.exo-boot-icon')
      if (document.documentElement.dataset.exoMotion === 'reduce' || typeof root.animate !== 'function') { root.remove(); return }
      // Exo Cinema, already drawn underneath, comes up as the mark sinks back and fades: transform and opacity only,
      // so it stays on the compositor and sharp (it shrinks, never grows past its raster).
      mark.animate([
        { transform: 'translateY(0) scale(1)', opacity: 1 },
        { transform: 'translateY(-6px) scale(0.86)', opacity: 0 },
      ], { duration: 380, easing: 'cubic-bezier(0.7, 0, 0.84, 0)', fill: 'forwards' })
      const fade = root.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 520, delay: 120, easing: 'cubic-bezier(0.33, 1, 0.68, 1)', fill: 'forwards' })
      fade.onfinish = () => root.remove()
    }, wait)
  }

  // Whatever happens behind it, the splash lifts by the cap.
  setTimeout(reveal, CAP_MS)
  return { reveal, startedAt }
})()
