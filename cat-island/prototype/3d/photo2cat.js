// Photo -> cat parameters. Everything runs on the device; no server, no API key.
//
//   analyzeCoat(img, mask, opts)  colours + pattern + white level + eye colour  -> makeCoat() spec
//   suggestBreeds(coat, toggles)  breed shortlist; the user confirms ears / fur length
//
// `img` is { data: RGBA bytes, width, height }, `mask` a Float32Array (0..1 "is cat") of the same size.
// In the browser prototype the mask comes from MediaPipe DeepLab (photo.html); on iOS it comes from
// Vision's subject mask, and the eye points from VNDetectAnimalBodyPoseRequest. This file only needs
// pixels, so the same rules port 1:1 to Swift / C#.

// ------------------------------------------------------------------ colour helpers
const lin = c => (c /= 255) <= .04045 ? c / 12.92 : Math.pow((c + .055) / 1.055, 2.4);
const gam = c => Math.round(255 * Math.min(1, Math.max(0, c <= .0031308 ? c * 12.92 : 1.055 * Math.pow(c, 1 / 2.4) - .055)));

function lin2lab(r, g, b) {
  let x = (.4124 * r + .3576 * g + .1805 * b) / .95047, y = .2126 * r + .7152 * g + .0722 * b, z = (.0193 * r + .1192 * g + .9505 * b) / 1.08883;
  const f = t => t > .008856 ? Math.cbrt(t) : 7.787 * t + 16 / 116;
  x = f(x); y = f(y); z = f(z);
  return [116 * y - 16, 500 * (x - y), 200 * (y - z)];
}
function lab2lin(L, a, b) {
  const fy = (L + 16) / 116, fx = fy + a / 500, fz = fy - b / 200;
  const g = t => t ** 3 > .008856 ? t ** 3 : (t - 16 / 116) / 7.787;
  const x = g(fx) * .95047, y = g(fy), z = g(fz) * 1.08883;
  return [3.2406 * x - 1.5372 * y - .4986 * z, -.9689 * x + 1.8758 * y + .0415 * z, .0557 * x - .204 * y + 1.057 * z];
}
export const labHex = ([L, a, b]) => '#' + lab2lin(L, a, b).map(gam).map(v => v.toString(16).padStart(2, '0')).join('');
const chroma = c => Math.hypot(c[1], c[2]);
const hue = c => (Math.atan2(c[2], c[1]) * 180 / Math.PI + 360) % 360;

// deterministic RNG so the same photo always gives the same cat
function rng(seed) { return () => (seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296; }

// k-means++ in Lab; returns centres sorted by size
export function kmeans(pts, k, iters = 16, seed = 7) {
  const R = rng(seed), n = pts.length;
  const cs = [pts[Math.floor(R() * n)].slice()];
  const d2 = new Float64Array(n);
  while (cs.length < k) {
    let sum = 0;
    for (let i = 0; i < n; i++) { let m = Infinity; for (const c of cs) m = Math.min(m, dist2(pts[i], c)); d2[i] = m; sum += m; }
    let t = R() * sum, i = 0;
    while (i < n - 1 && (t -= d2[i]) > 0) i++;
    cs.push(pts[i].slice());
  }
  const lab = new Uint8Array(n);
  for (let it = 0; it < iters; it++) {
    const acc = cs.map(() => [0, 0, 0, 0]);
    for (let i = 0; i < n; i++) {
      let best = 0, bd = Infinity;
      for (let j = 0; j < k; j++) { const d = dist2(pts[i], cs[j]); if (d < bd) { bd = d; best = j; } }
      lab[i] = best; const a = acc[best]; a[0] += pts[i][0]; a[1] += pts[i][1]; a[2] += pts[i][2]; a[3]++;
    }
    acc.forEach((a, j) => { if (a[3]) cs[j] = [a[0] / a[3], a[1] / a[3], a[2] / a[3]]; });
  }
  const cnt = cs.map(() => 0); for (let i = 0; i < n; i++) cnt[lab[i]]++;
  return cs.map((c, j) => ({ c, f: cnt[j] / n, j })).sort((a, b) => b.f - a.f).map(o => ({ ...o, members: lab }));
}
function dist2(a, b) { return (a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2; }

// ------------------------------------------------------------------ lighting
// Phone photos of cats are often dark and tinted by indoor light. Neutralise the cast with a
// gentle, clamped grey-world on the whole frame, then expose so the bright end of the frame sits at 0.85.
function lightingFix(img) {
  const { data, width: w, height: h } = img, n = w * h;
  const rgb = new Float32Array(n * 3), Y = new Float32Array(n);
  let sr = 0, sg = 0, sb = 0, cnt = 0;
  for (let i = 0; i < n; i++) {
    const r = lin(data[i * 4]), g = lin(data[i * 4 + 1]), b = lin(data[i * 4 + 2]);
    rgb[i * 3] = r; rgb[i * 3 + 1] = g; rgb[i * 3 + 2] = b;
    const y = .2126 * r + .7152 * g + .0722 * b; Y[i] = y;
    if (y > .01 && y < .9) { sr += r; sg += g; sb += b; cnt++; }
  }
  const m = (sr + sg + sb) / 3 || 1;
  const gain = [sr, sg, sb].map(s => Math.min(1.15, Math.max(.88, Math.pow(m / (s || m), .35))));   // partial: a blue backdrop must not bleach an orange cat
  const ys = Float32Array.from(Y).sort();
  const p95 = ys[Math.floor(n * .95)] || 1;
  const exposure = Math.min(4, Math.max(1, .85 / p95));
  for (let i = 0; i < n; i++) for (let c = 0; c < 3; c++) rgb[i * 3 + c] = Math.min(1, rgb[i * 3 + c] * gain[c] * exposure);
  return { rgb, gain, exposure };
}

const STRIPE_SCORE = 2;   // tuned on the test photos (see CAT_CATALOG.md)

// ------------------------------------------------------------------ main analysis
// opts.eyes: [{x,y},{x,y}] in pixels (optional). Without eyes the head is taken as the top of the mask.
export function analyzeCoat(img, mask, opts = {}) {
  const { width: w, height: h } = img, n = w * h;
  const { rgb, gain, exposure } = lightingFix(img);

  // erode the mask a little: edge pixels mix in the background
  const m = erode(mask, w, h, Math.max(1, Math.round(Math.min(w, h) / 120)));
  let x0 = w, x1 = 0, y0 = h, y1 = 0, area = 0;
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) if (m[y * w + x] > .5) { area++; x0 = Math.min(x0, x); x1 = Math.max(x1, x); y0 = Math.min(y0, y); y1 = Math.max(y1, y); }
  if (area < n * .01) return { ok: false, reason: 'no-cat' };
  const bw = x1 - x0 + 1, bh = y1 - y0 + 1, size = Math.sqrt(area);

  // Lab per cat pixel
  const labAt = new Float32Array(n * 3), Lmap = new Float32Array(n);
  const idx = [];
  for (let i = 0; i < n; i++) if (m[i] > .5) {
    const L = lin2lab(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
    labAt[i * 3] = L[0]; labAt[i * 3 + 1] = L[1]; labAt[i * 3 + 2] = L[2]; Lmap[i] = L[0]; idx.push(i);
  }
  const lab = i => [labAt[i * 3], labAt[i * 3 + 1], labAt[i * 3 + 2]];
  // band-pass luminance: the wide blur removes shading, the narrow one removes single hairs and whiskers,
  // what is left is stripe-sized structure
  const Lwide = maskedBlur(Lmap, m, w, h, Math.max(3, Math.round(size / 9)));
  const Lband = maskedBlur(Lmap, m, w, h, Math.max(1, Math.round(size / 45)));
  const edgeD = distToEdge(m, w, h);

  const step = Math.max(1, Math.floor(idx.length / 24000));
  const pts = [], pix = [];
  for (let k = 0; k < idx.length; k += step) { const i = idx[k]; pts.push(lab(i)); pix.push(i); }

  // ---- regions: face (from the eye points, or the top of the mask) and muzzle/chin
  const eyes = opts.eyes && opts.eyes.length === 2 ? opts.eyes : null;
  const eyeD = eyes ? Math.max(size * .06, Math.hypot(eyes[0].x - eyes[1].x, eyes[0].y - eyes[1].y)) : bw * .22;
  const face = eyes ? { x: (eyes[0].x + eyes[1].x) / 2, y: (eyes[0].y + eyes[1].y) / 2 } : { x: x0 + bw / 2, y: y0 + bh * .2 };
  const inFace = i => { const x = i % w, y = (i / w) | 0; return Math.hypot((x - face.x) / (eyeD * 1.3), (y - face.y - eyeD * .3) / (eyeD * 1.1)) < 1; };
  const inMuzzle = i => { const x = i % w, y = (i / w) | 0; return Math.hypot((x - face.x) / (eyeD * .7), (y - face.y - eyeD * .95) / (eyeD * .55)) < 1; };
  const mean = (pred, f) => { let s = 0, c = 0; for (const i of pix) if (pred(i)) { s += f(i); c++; } return c > 20 ? s / c : null; };

  // ---- colour clusters
  const cl = kmeans(pts, 5);
  const memberOf = cl[0].members;   // per sample: index j of its (unsorted) cluster
  const coatL = cl.filter(o => chroma(o.c) >= 16 || o.c[0] <= 72).reduce((s, o, _, a) => s + o.c[0] * o.f / a.reduce((t, p) => t + p.f, 0), 0);
  // white = bright, colourless AND far lighter than the coat (silver tipping on a Russian Blue is not white)
  // white spotting = a bright, colourless pixel whose whole neighbourhood is bright too. Pale stripes on a
  // silver tabby and silver tipping on a Russian Blue are bright pixels in a darker neighbourhood.
  const whitePix = i => labAt[i * 3] > 76 && chroma(lab(i)) < 14 && Lwide[i] > Math.max(70, labAt[i * 3] - 8) && (coatL < 1 || labAt[i * 3] > coatL + 18);
  const wFlag = pix.map(whitePix);
  const wShare = cl.map(() => [0, 0]);
  for (let k = 0; k < pix.length; k++) { const o = wShare[memberOf[k]]; o[1]++; if (wFlag[k]) o[0]++; }
  const whiteJ = new Set(cl.filter(o => { const [a, b] = wShare[o.j]; return b && a / b > .5; }).map(o => o.j));
  const isWhiteC = c => { const o = cl.find(q => q.c === c); return o ? whiteJ.has(o.j) : false; };
  const isOrangeC = c => chroma(c) > 30 && hue(c) > 35 && hue(c) < 95 && c[0] > 40;   // ginger, not brown tabby
  const isBlackC = c => c[0] < 24;
  const frac = pred => cl.filter(o => pred(o.c)).reduce((s, o) => s + o.f, 0);
  const orangeFrac = frac(isOrangeC), blackFrac = frac(isBlackC);
  // white outside the muzzle/chin (most tabbies have a light chin; that is lightMuzzle, not white spotting)
  let wAll = 0, wCnt = 0;
  for (let k = 0; k < pix.length; k++) { if (inMuzzle(pix[k])) continue; wCnt++; if (wFlag[k]) wAll++; }
  const whiteFrac = wCnt ? wAll / wCnt : 0;

  const colored = cl.filter(o => !isWhiteC(o.c));
  const tot = colored.reduce((s, o) => s + o.f, 0) || 1;
  const avgC = list => { const t = list.reduce((s, o) => s + o.f, 0) || 1; return [0, 1, 2].map(k => list.reduce((s, o) => s + o.c[k] * o.f, 0) / t); };
  const colL = colored.reduce((s, o) => s + o.c[0] * o.f, 0) / tot;

  // ---- colour-point: dark on the thin extremities (ears, face, paws, tail), light on the thick body
  const lightest = cl.slice().sort((a, b) => b.c[0] - a.c[0]);
  const bodyLight = lightest[0].c[0];
  let darkEdge = [], lightEdge = [];
  for (const i of pix) { const L = labAt[i * 3]; if (L < bodyLight - 32) darkEdge.push(edgeD[i]); else if (L > bodyLight - 14) lightEdge.push(edgeD[i]); }
  const med = a => a.length ? a.sort((p, q) => p - q)[a.length >> 1] : 0;
  const darkF = darkEdge.length / pix.length, extremity = lightEdge.length ? med(darkEdge) / (med(lightEdge) || 1) : 1;
  const faceL = mean(inFace, i => labAt[i * 3]);
  const bodyL = mean(i => !inFace(i) && ((i / w) | 0) > face.y + eyeD * 1.6, i => labAt[i * 3]);
  const pointByFace = eyes && faceL !== null && bodyL !== null && bodyL - faceL > 18 && bodyL > 62;
  const pointByShape = bodyLight > 70 && darkF > .04 && darkF < .45 && extremity < .55 && orangeFrac < .08;

  // ---- stripes: band-pass contrast on the coloured coat, relative to how bright the coat is
  // only the body interior: the mask rim and the face (eyes, nose) would look like stripes too
  const inHead = i => { const x = i % w, y = (i / w) | 0; return Math.hypot((x - face.x) / (eyeD * 2), (y - face.y) / (eyeD * 1.9)) < 1; };
  let tex = 0, tc = 0;
  for (const i of pix) {
    if (edgeD[i] < size / 16 || inHead(i) || whitePix(i)) continue;
    const d = Lband[i] - Lwide[i]; tex += d * d; tc++;
  }
  tex = tc ? Math.sqrt(tex / tc) : 0;

  // stripe crossings: walk rows and columns inside the body and count dark<->light alternations of the
  // band-passed luminance (with hysteresis). Stripes alternate many times per body length; studio shading
  // or a shadowed flank alternates once or twice.
  let cross = 0, run = 0;
  const amp = 3;
  const walk = (i0, di, len) => {
    let st = 0;
    for (let t = 0, i = i0; t < len; t++, i += di) {
      if (m[i] <= .5 || edgeD[i] < size / 16 || inHead(i)) { st = 0; continue; }
      const d = Lband[i] - Lwide[i]; run++;
      if (d > amp && st !== 1) { if (st) cross++; st = 1; } else if (d < -amp && st !== -1) { if (st) cross++; st = -1; }
    }
  };
  for (let y = y0; y <= y1; y += 2) walk(y * w + x0, 1, bw);
  for (let x = x0; x <= x1; x += 2) walk(y0 * w + x, w, bh);
  const stripeRate = run ? cross / run * size : 0;   // alternations per body-size of walked length

  // ---- pattern rules (order matters)
  let pattern, base, dark, second, point, whiteLevel = 0, stripeScore = 0, uncertain = false;
  const why = [];
  if (orangeFrac > .08 && blackFrac > .08) {
    pattern = whiteFrac > .12 ? 'calico' : 'tortie';
    base = avgC(cl.filter(o => !isOrangeC(o.c) && !isWhiteC(o.c) && o.c[0] < 45));
    second = avgC(cl.filter(o => isOrangeC(o.c)));
    why.push(`주황 ${pct(orangeFrac)} + 검정 ${pct(blackFrac)}`);
  } else if (pointByFace || pointByShape) {
    pattern = 'point';
    base = avgC(lightest.slice(0, 2));
    point = avgC(cl.filter(o => o.c[0] < bodyLight - 32));
    why.push(pointByFace ? `얼굴이 몸보다 ${Math.round(bodyL - faceL)} 어두움` : `귀·얼굴·발·꼬리 끝만 어두움`);
  } else {
    if (whiteFrac > .9) { pattern = 'solid'; base = [92, 0, 2]; }   // all-white cat
    else if (whiteFrac > .62) {
      // colour only on top of the head (and tail): van; otherwise patches: cow
      let topCol = 0, colAll = 0;
      for (let k = 0; k < pix.length; k++) if (!wFlag[k]) { colAll++; if (((pix[k] / w) | 0) < y0 + bh * .3) topCol++; }
      pattern = colAll && topCol / colAll > .6 ? 'van' : 'cow';
    } else if (whiteFrac > .28) pattern = 'tuxedo';
    if (pattern) why.push(`흰 털 ${pct(whiteFrac)}`);
    else if (whiteFrac > .07) { whiteLevel = Math.min(.35, .12 + whiteFrac); why.push(`흰 털 ${pct(whiteFrac)}`); }

    // tabby needs visible stripe structure; on a near-black coat the "stripes" are just sun glints
    // stripe score: band contrast relative to coat brightness + how often it alternates.
    // ~1.3-1.8 on solid coats, ~2.2-3 on tabbies in the test photos; the band in between is reported as
    // uncertain so the app asks "줄무늬가 있나요?" instead of guessing.
    stripeScore = colL > 24 ? (tex / Math.max(30, colL)) / .14 + stripeRate / 4.4 : 0;
    const tabby = stripeScore > STRIPE_SCORE;
    uncertain = Math.abs(stripeScore - STRIPE_SCORE) < .25;
    if (!pattern) pattern = tabby ? 'mackerel' : 'solid';
    why.push(`줄무늬 점수 ${stripeScore.toFixed(2)}${tabby ? '' : ' (약함)'}`);
    if (tabby && pattern === 'mackerel') {
      base = avgC(colored.filter(o => o.c[0] >= colL));
      dark = avgC(colored.filter(o => o.c[0] < colL));
    } else if (!base) base = avgC(colored.length ? colored : cl);
  }

  // muzzle clearly lighter than the coat -> light muzzle
  const muzzleL = mean(inMuzzle, i => labAt[i * 3]);
  const lightMuzzle = muzzleL !== null ? muzzleL - colL > 10 : pattern !== 'solid';

  // ---- eye colour: ring around each eye point, skipping the pupil and glints
  let eye = null;
  if (eyes) {
    const r = eyeD * .2, ring = [];
    for (const e of eyes) for (let y = Math.round(e.y - r); y <= e.y + r; y++) for (let x = Math.round(e.x - r); x <= e.x + r; x++) {
      if (x < 0 || y < 0 || x >= w || y >= h) continue;
      if (Math.hypot(x - e.x, y - e.y) > r) continue;
      const i = y * w + x, c = lin2lab(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
      if (c[0] > 18 && c[0] < 92) ring.push(c);
    }
    if (ring.length > 10) {
      ring.sort((a, b) => chroma(b) - chroma(a));           // most saturated third = iris
      const top = ring.slice(0, Math.max(5, Math.floor(ring.length / 3)));
      eye = [0, 1, 2].map(k => top.reduce((s, c) => s + c[k], 0) / top.length);
      const hh = hue(eye);
      if (hh < 45 || hh > 300) eye = null;   // pink/red = skin or fur next to a misplaced eye point; use the breed default
    }
  }

  // AC look: a bit lighter and cleaner; near-grey coats are pushed to neutral so a tinted room
  // doesn't turn a grey cat khaki
  const stylize = (c, lift = 0) => {
    if (!c) return c;
    const k = chroma(c) < 10 ? .45 : 1.12;
    return [Math.min(96, c[0] * .85 + 14 + lift), c[1] * k, c[2] * k];
  };
  const coat = { pattern, base: labHex(stylize(base)), lightMuzzle };
  if (dark) coat.dark = labHex(stylize(dark, -4));
  if (second) coat.second = labHex(stylize(second));
  if (point) coat.point = labHex(stylize(point, -6));
  if (whiteLevel) coat.whiteLevel = +whiteLevel.toFixed(2);
  if (eye) { const s = Math.min(3, 34 / (chroma(eye) || 1)); coat.eye = labHex([Math.min(72, Math.max(55, eye[0] + 12)), eye[1] * s, eye[2] * s]); }
  if (pattern === 'solid' && base[0] < 30) coat.lightMuzzle = false;

  let debug = null;
  if (opts.debug) { debug = new Float32Array(n); for (const i of idx) debug[i] = Lband[i] - Lwide[i]; }
  return {
    ok: true, coat, debug, uncertain,
    report: {
      why, clusters: cl.map(o => ({ hex: labHex(o.c), f: +o.f.toFixed(3), L: Math.round(o.c[0]) })),
      whiteFrac: +whiteFrac.toFixed(3), orangeFrac: +orangeFrac.toFixed(3), blackFrac: +blackFrac.toFixed(3), tex: +tex.toFixed(2), stripeRate: +stripeRate.toFixed(2), stripeScore: +stripeScore.toFixed(2),
      darkF: +darkF.toFixed(3), extremity: +extremity.toFixed(2), coatL: Math.round(coatL), faceL, bodyL, muzzleL,
      lighting: { gain: gain.map(g => +g.toFixed(2)), exposure: +exposure.toFixed(2) },
    },
  };
}
const pct = f => Math.round(f * 100) + '%';

function erode(mask, w, h, r) {
  const out = new Float32Array(mask.length);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    let v = mask[y * w + x];
    if (v > .5) for (const [dx, dy] of [[r, 0], [-r, 0], [0, r], [0, -r]]) {
      const xx = x + dx, yy = y + dy;
      if (xx < 0 || yy < 0 || xx >= w || yy >= h || mask[yy * w + xx] <= .5) { v = 0; break; }
    }
    out[y * w + x] = v;
  }
  return out;
}

// city-block distance (px) from each cat pixel to the mask edge: small on ears, legs and tail
function distToEdge(mask, w, h) {
  const d = new Float32Array(w * h), INF = 1e9;
  for (let i = 0; i < w * h; i++) d[i] = mask[i] > .5 ? INF : 0;
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) { const i = y * w + x; if (!d[i]) continue; d[i] = Math.min(d[i], (x ? d[i - 1] : 0) + 1, (y ? d[i - w] : 0) + 1); }
  for (let y = h - 1; y >= 0; y--) for (let x = w - 1; x >= 0; x--) { const i = y * w + x; if (!d[i]) continue; d[i] = Math.min(d[i], (x < w - 1 ? d[i + 1] : 0) + 1, (y < h - 1 ? d[i + w] : 0) + 1); }
  return d;
}

// box blur restricted to the mask (separable, normalised by mask coverage)
function maskedBlur(src, mask, w, h, r) {
  const a = new Float32Array(w * h), c = new Float32Array(w * h), ta = new Float32Array(w * h), tc = new Float32Array(w * h);
  for (let i = 0; i < w * h; i++) { const mm = mask[i] > .5 ? 1 : 0; a[i] = src[i] * mm; c[i] = mm; }
  for (let y = 0; y < h; y++) {
    let sa = 0, sc = 0;
    for (let x = -r; x < w; x++) {
      if (x + r < w) { sa += a[y * w + x + r]; sc += c[y * w + x + r]; }
      if (x - r - 1 >= 0) { sa -= a[y * w + x - r - 1]; sc -= c[y * w + x - r - 1]; }
      if (x >= 0) { ta[y * w + x] = sa; tc[y * w + x] = sc; }
    }
  }
  const out = new Float32Array(w * h);
  for (let x = 0; x < w; x++) {
    let sa = 0, sc = 0;
    for (let y = -r; y < h; y++) {
      if (y + r < h) { sa += ta[(y + r) * w + x]; sc += tc[(y + r) * w + x]; }
      if (y - r - 1 >= 0) { sa -= ta[(y - r - 1) * w + x]; sc -= tc[(y - r - 1) * w + x]; }
      if (y >= 0) out[y * w + x] = sc ? sa / sc : 0;
    }
  }
  return out;
}

// ------------------------------------------------------------------ breed shortlist
// Shape can't be read reliably from one photo, so we suggest 3 breeds from the coat and let the
// user confirm. toggles: { ear: 'upright'|'fold'|'curl', fur: 'short'|'long'|'curly', legs: 'normal'|'short' }
export function suggestBreeds(coat, toggles = {}) {
  const t = { ear: 'upright', fur: 'short', legs: 'normal', ...toggles };
  if (t.ear === 'fold') return pick(['scottish_fold', 'british_shorthair', 'korean_shorthair'], '접힌 귀');
  if (t.ear === 'curl') return pick(['american_curl', 'korean_shorthair', 'devon_rex'], '말린 귀');
  if (t.legs === 'short') return pick(['munchkin', 'korean_shorthair', 'scottish_fold'], '짧은 다리');
  if (t.fur === 'curly') return pick(['selkirk_rex', 'laperm', 'devon_rex'], '곱슬털');
  const long = t.fur === 'long';
  const P = coat.pattern, L = hexL(coat.base);
  const greyBlue = isGrey(coat.base);
  if (P === 'point') return pick(long ? ['ragdoll', 'himalayan', 'birman'] : ['siamese', 'ragdoll', 'birman'], '포인트 무늬');
  if (P === 'calico' || P === 'tortie') return pick(long ? ['norwegian_forest', 'siberian', 'korean_shorthair'] : ['korean_shorthair', 'japanese_bobtail', 'american_shorthair'], '삼색/카오스');
  if (P === 'van') return pick(['turkish_van', 'korean_shorthair', 'turkish_angora'], '머리·꼬리만 색');
  if (P === 'solid' && L > 88) return pick(long ? ['turkish_angora', 'persian', 'ragdoll'] : ['korean_shorthair', 'turkish_angora', 'british_shorthair'], '흰 단색');
  if (P === 'solid' && L < 30) return pick(['bombay', 'korean_shorthair', 'oriental'], '검은 단색');
  if (P === 'solid' && greyBlue) return pick(long ? ['persian', 'siberian', 'norwegian_forest'] : ['russian_blue', 'british_shorthair', 'korean_shorthair'], '회색 단색');
  const warm = !greyBlue && hueOf(coat.base) > 35 && hueOf(coat.base) < 95;
  if (long && warm && (P === 'solid' || P === 'mackerel')) return pick(['persian', 'maine_coon', 'norwegian_forest'], '장모 · 크림/레드');
  if (P === 'mackerel') return pick(long ? ['norwegian_forest', 'siberian', 'maine_coon'] : ['korean_shorthair', 'american_shorthair', 'bengal'], '태비');
  return pick(long ? ['persian', 'siberian', 'maine_coon'] : ['korean_shorthair', 'american_shorthair', 'british_shorthair'], '기본');
}
function pick(ids, reason) { return ids.map((id, i) => ({ id, reason: i === 0 ? reason : '' })); }
function hexRgb(h) { const v = parseInt(h.slice(1), 16); return [v >> 16 & 255, v >> 8 & 255, v & 255]; }
function hexL(h) { const [r, g, b] = hexRgb(h).map(lin); return lin2lab(r, g, b)[0]; }
function hueOf(h) { const [r, g, b] = hexRgb(h).map(lin); return hue(lin2lab(r, g, b)); }
function isGrey(h) { const [r, g, b] = hexRgb(h).map(lin); const c = lin2lab(r, g, b); return chroma(c) < 12; }
