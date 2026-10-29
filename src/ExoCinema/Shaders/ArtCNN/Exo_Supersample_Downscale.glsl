// Exo Cinema's own pass, after ArtCNN's x2: a picture drawn bigger than twice the size it is shown at comes down to
// exactly twice that, so mpv's last shrink to the screen is an even 2:1. A 1440p source on a 1080p screen goes
// 1440 → 2880 (ArtCNN) → 2160 (here) → 1080 (mpv); a 1080p one is already 2160 after ArtCNN and passes untouched.

//!DESC Exo-Supersample-Downscale-to-2x
//!HOOK MAIN
//!BIND HOOKED
//!WIDTH OUTPUT.w 2 *
//!HEIGHT OUTPUT.h 2 *
//!WHEN MAIN.h OUTPUT.h 2 * >

vec4 hook() {
    return HOOKED_tex(HOOKED_pos);
}
