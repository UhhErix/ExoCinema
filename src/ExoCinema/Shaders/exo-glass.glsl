// Exo Cinema's glass over the video. Exo's glass blurs what is behind it (backdrop-filter), but mpv draws the
// picture under the page, where the page's filters can't see it; so mpv blurs and tones the picture itself, under
// the page's glass shapes, as it draws each frame. Nothing leaves the GPU.
//
// The host (MainWindow.Glass.cs) sets the shapes through glsl-shader-opts, in pixels of the video's rectangle:
//   exoN        how many shapes, topmost first
//   exoScale    device pixels per CSS pixel, for the blur's reach
//   exoIx, exoIy, exoIw, exoIh   shape I's box; exoIr its corner radius; exoIk its kind; exoIa how visible it is
// Kind 0, small glass over video: blur(10px) brightness(0.34) saturate(1.6).
// Kind 1, large glass (menus, panels): blur(20px) saturate(1.8) brightness(0.66); the page adds its 36% black.

//!PARAM exoN
//!TYPE DYNAMIC float
0.0

//!PARAM exoScale
//!TYPE DYNAMIC float
1.0

//!PARAM exo0x
//!TYPE DYNAMIC float
0.0

//!PARAM exo0y
//!TYPE DYNAMIC float
0.0

//!PARAM exo0w
//!TYPE DYNAMIC float
0.0

//!PARAM exo0h
//!TYPE DYNAMIC float
0.0

//!PARAM exo0r
//!TYPE DYNAMIC float
0.0

//!PARAM exo0k
//!TYPE DYNAMIC float
0.0

//!PARAM exo0a
//!TYPE DYNAMIC float
0.0

//!PARAM exo1x
//!TYPE DYNAMIC float
0.0

//!PARAM exo1y
//!TYPE DYNAMIC float
0.0

//!PARAM exo1w
//!TYPE DYNAMIC float
0.0

//!PARAM exo1h
//!TYPE DYNAMIC float
0.0

//!PARAM exo1r
//!TYPE DYNAMIC float
0.0

//!PARAM exo1k
//!TYPE DYNAMIC float
0.0

//!PARAM exo1a
//!TYPE DYNAMIC float
0.0

//!PARAM exo2x
//!TYPE DYNAMIC float
0.0

//!PARAM exo2y
//!TYPE DYNAMIC float
0.0

//!PARAM exo2w
//!TYPE DYNAMIC float
0.0

//!PARAM exo2h
//!TYPE DYNAMIC float
0.0

//!PARAM exo2r
//!TYPE DYNAMIC float
0.0

//!PARAM exo2k
//!TYPE DYNAMIC float
0.0

//!PARAM exo2a
//!TYPE DYNAMIC float
0.0

//!PARAM exo3x
//!TYPE DYNAMIC float
0.0

//!PARAM exo3y
//!TYPE DYNAMIC float
0.0

//!PARAM exo3w
//!TYPE DYNAMIC float
0.0

//!PARAM exo3h
//!TYPE DYNAMIC float
0.0

//!PARAM exo3r
//!TYPE DYNAMIC float
0.0

//!PARAM exo3k
//!TYPE DYNAMIC float
0.0

//!PARAM exo3a
//!TYPE DYNAMIC float
0.0

//!PARAM exo4x
//!TYPE DYNAMIC float
0.0

//!PARAM exo4y
//!TYPE DYNAMIC float
0.0

//!PARAM exo4w
//!TYPE DYNAMIC float
0.0

//!PARAM exo4h
//!TYPE DYNAMIC float
0.0

//!PARAM exo4r
//!TYPE DYNAMIC float
0.0

//!PARAM exo4k
//!TYPE DYNAMIC float
0.0

//!PARAM exo4a
//!TYPE DYNAMIC float
0.0

//!PARAM exo5x
//!TYPE DYNAMIC float
0.0

//!PARAM exo5y
//!TYPE DYNAMIC float
0.0

//!PARAM exo5w
//!TYPE DYNAMIC float
0.0

//!PARAM exo5h
//!TYPE DYNAMIC float
0.0

//!PARAM exo5r
//!TYPE DYNAMIC float
0.0

//!PARAM exo5k
//!TYPE DYNAMIC float
0.0

//!PARAM exo5a
//!TYPE DYNAMIC float
0.0

//!PARAM exo6x
//!TYPE DYNAMIC float
0.0

//!PARAM exo6y
//!TYPE DYNAMIC float
0.0

//!PARAM exo6w
//!TYPE DYNAMIC float
0.0

//!PARAM exo6h
//!TYPE DYNAMIC float
0.0

//!PARAM exo6r
//!TYPE DYNAMIC float
0.0

//!PARAM exo6k
//!TYPE DYNAMIC float
0.0

//!PARAM exo6a
//!TYPE DYNAMIC float
0.0

//!PARAM exo7x
//!TYPE DYNAMIC float
0.0

//!PARAM exo7y
//!TYPE DYNAMIC float
0.0

//!PARAM exo7w
//!TYPE DYNAMIC float
0.0

//!PARAM exo7h
//!TYPE DYNAMIC float
0.0

//!PARAM exo7r
//!TYPE DYNAMIC float
0.0

//!PARAM exo7k
//!TYPE DYNAMIC float
0.0

//!PARAM exo7a
//!TYPE DYNAMIC float
0.0

//!HOOK OUTPUT
//!BIND HOOKED
//!SAVE EXOGLASS
//!WHEN exoN 0 >
//!DESC Exo glass (across)

vec4 exoBox(int i)
{
    return i == 0 ? vec4(exo0x, exo0y, exo0w, exo0h)
         : i == 1 ? vec4(exo1x, exo1y, exo1w, exo1h)
         : i == 2 ? vec4(exo2x, exo2y, exo2w, exo2h)
         : i == 3 ? vec4(exo3x, exo3y, exo3w, exo3h)
         : i == 4 ? vec4(exo4x, exo4y, exo4w, exo4h)
         : i == 5 ? vec4(exo5x, exo5y, exo5w, exo5h)
         : i == 6 ? vec4(exo6x, exo6y, exo6w, exo6h)
         : vec4(exo7x, exo7y, exo7w, exo7h);
}

// Corner radius, kind, how visible.
vec3 exoLook(int i)
{
    return i == 0 ? vec3(exo0r, exo0k, exo0a)
         : i == 1 ? vec3(exo1r, exo1k, exo1a)
         : i == 2 ? vec3(exo2r, exo2k, exo2a)
         : i == 3 ? vec3(exo3r, exo3k, exo3a)
         : i == 4 ? vec3(exo4r, exo4k, exo4a)
         : i == 5 ? vec3(exo5r, exo5k, exo5a)
         : i == 6 ? vec3(exo6r, exo6k, exo6a)
         : vec3(exo7r, exo7k, exo7a);
}

float exoSigma(float kind)
{
    return (kind > 0.5 ? 20.0 : 10.0) * exoScale;
}

// One direction of the Gaussian, two taps to a fetch (the filtering between neighbours weighs them).
vec4 exoBlur(vec2 step, float sigma, float reach)
{
    float k = -0.5 / (sigma * sigma);
    vec4 sum = vec4(0.0);
    float total = 0.0;
    for (float o = -reach; o <= reach; o += 2.0) {
        float w0 = exp(o * o * k);
        float w1 = exp((o + 1.0) * (o + 1.0) * k);
        float w = w0 + w1;
        sum += HOOKED_tex(HOOKED_pos + step * (o + w1 / w)) * w;
        total += w;
    }
    return sum / total;
}

// Across each shape, and as far above and below it as the pass down reads.
vec4 hook()
{
    vec2 p = HOOKED_pos * HOOKED_size;
    for (int i = 0; i < 8; i++) {
        if (float(i) >= exoN) break;
        vec4 b = exoBox(i);
        vec3 l = exoLook(i);
        if (l.z <= 0.0) continue;
        float sigma = exoSigma(l.y);
        float reach = ceil(sigma * 3.0);
        if (p.x >= b.x && p.x <= b.x + b.z && p.y >= b.y - reach && p.y <= b.y + b.w + reach)
            return exoBlur(vec2(HOOKED_pt.x, 0.0), sigma, reach);
    }
    return HOOKED_tex(HOOKED_pos);
}

//!HOOK OUTPUT
//!BIND HOOKED
//!BIND EXOGLASS
//!WHEN exoN 0 >
//!DESC Exo glass (down)

vec4 exoBox(int i)
{
    return i == 0 ? vec4(exo0x, exo0y, exo0w, exo0h)
         : i == 1 ? vec4(exo1x, exo1y, exo1w, exo1h)
         : i == 2 ? vec4(exo2x, exo2y, exo2w, exo2h)
         : i == 3 ? vec4(exo3x, exo3y, exo3w, exo3h)
         : i == 4 ? vec4(exo4x, exo4y, exo4w, exo4h)
         : i == 5 ? vec4(exo5x, exo5y, exo5w, exo5h)
         : i == 6 ? vec4(exo6x, exo6y, exo6w, exo6h)
         : vec4(exo7x, exo7y, exo7w, exo7h);
}

// Corner radius, kind, how visible.
vec3 exoLook(int i)
{
    return i == 0 ? vec3(exo0r, exo0k, exo0a)
         : i == 1 ? vec3(exo1r, exo1k, exo1a)
         : i == 2 ? vec3(exo2r, exo2k, exo2a)
         : i == 3 ? vec3(exo3r, exo3k, exo3a)
         : i == 4 ? vec3(exo4r, exo4k, exo4a)
         : i == 5 ? vec3(exo5r, exo5k, exo5a)
         : i == 6 ? vec3(exo6r, exo6k, exo6a)
         : vec3(exo7r, exo7k, exo7a);
}

float exoSigma(float kind)
{
    return (kind > 0.5 ? 20.0 : 10.0) * exoScale;
}

// One direction of the Gaussian, two taps to a fetch (the filtering between neighbours weighs them).
vec4 exoBlur(vec2 step, float sigma, float reach)
{
    float k = -0.5 / (sigma * sigma);
    vec4 sum = vec4(0.0);
    float total = 0.0;
    for (float o = -reach; o <= reach; o += 2.0) {
        float w0 = exp(o * o * k);
        float w1 = exp((o + 1.0) * (o + 1.0) * k);
        float w = w0 + w1;
        sum += EXOGLASS_tex(HOOKED_pos + step * (o + w1 / w)) * w;
        total += w;
    }
    return sum / total;
}

// Distance from a rounded box's edge, negative inside.
float exoEdge(vec2 p, vec4 b, float r)
{
    vec2 hs = b.zw * 0.5;
    vec2 q = abs(p - b.xy - hs) - hs + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

// CSS's brightness() and saturate(), in the same order as the page's glass.
vec3 exoTone(vec3 c, float kind)
{
    const vec3 luma = vec3(0.213, 0.715, 0.072);
    if (kind > 0.5)
        return clamp(mix(vec3(dot(c, luma)), c, 1.8), 0.0, 1.0) * 0.66;
    c *= 0.34;
    return clamp(mix(vec3(dot(c, luma)), c, 1.6), 0.0, 1.0);
}

vec4 hook()
{
    vec2 p = HOOKED_pos * HOOKED_size;
    vec4 c = HOOKED_tex(HOOKED_pos);
    for (int i = 0; i < 8; i++) {
        if (float(i) >= exoN) break;
        vec4 b = exoBox(i);
        vec3 l = exoLook(i);
        float cover = clamp(0.5 - exoEdge(p, b, l.x), 0.0, 1.0) * l.z;
        if (cover <= 0.0) continue;
        float sigma = exoSigma(l.y);
        vec3 glass = exoTone(exoBlur(vec2(0.0, HOOKED_pt.y), sigma, ceil(sigma * 3.0)).rgb, l.y);
        c.rgb = mix(c.rgb, glass, cover);
        break;
    }
    return c;
}
