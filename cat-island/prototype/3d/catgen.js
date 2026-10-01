// Parametric toy-style cat generator.
// A cat = SHAPE (breed silhouette) x COAT (colour/pattern), so a photo can drive both independently.
import * as THREE from 'three';

// ------------------------------------------------------------------ noise
function hash(x, y, z) {
  let h = Math.sin(x * 127.1 + y * 311.7 + z * 74.7) * 43758.5453;
  return h - Math.floor(h);
}
function vnoise(x, y, z) {
  const xi = Math.floor(x), yi = Math.floor(y), zi = Math.floor(z);
  const xf = x - xi, yf = y - yi, zf = z - zi;
  const s = t => t * t * (3 - 2 * t);
  const u = s(xf), v = s(yf), w = s(zf);
  let r = 0;
  for (let dx = 0; dx < 2; dx++) for (let dy = 0; dy < 2; dy++) for (let dz = 0; dz < 2; dz++) {
    const k = (dx ? u : 1 - u) * (dy ? v : 1 - v) * (dz ? w : 1 - w);
    r += k * hash(xi + dx, yi + dy, zi + dz);
  }
  return r;
}
const sstep = (a, b, x) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
const fbm = (x, y, z, seed = 0) => 0.6 * vnoise(x + seed, y, z) + 0.4 * vnoise(2 * x, 2 * y + seed, 2 * z);

// ------------------------------------------------------------------ coat
// colorAt(part, d, seed): d = unit direction in the part's own frame (x right, y up, z towards camera)
const C = h => new THREE.Color(h);
export function makeCoat(spec) {
  const base = C(spec.base), dark = C(spec.dark || spec.base).multiplyScalar(spec.dark ? 1 : 0.62);
  const white = C(spec.white || '#fbf7ef'), point = C(spec.point || '#5b4337'), second = C(spec.second || '#e8964a');
  const seed = spec.seed || 1;
  const P = spec.pattern;
  const W = spec.whiteLevel ?? ({ tuxedo: .45, cow: .7, van: .9, calico: .55, mitted: .2, bicolor: .5 }[P] || 0);

  function whiteMask(part, d) {
    if (W <= 0) return false;
    if (part === 'muzzle' || part === 'chest') return W >= .2;
    if (part === 'paw') return W >= .15;
    if (part === 'body') return d.y < -1 + W * 1.5 || (d.z > .55 && W >= .3);
    if (part === 'head') {
      if (P === 'van') return !(d.y > .45 && Math.abs(d.x) < .75);
      return W >= .4 && d.y < -.15 && Math.abs(d.x) < .55 && d.z > .2;   // white lower face
    }
    if (part === 'tail') return P !== 'van' && W > .85;
    if (part === 'ear') return false;
    return false;
  }

  return function colorAt(part, d) {
    // fixed parts
    if (part === 'nose') return C(spec.nose || '#e9837a');
    if (part === 'innerEar') return C(spec.innerEar || '#f7b3c2');
    if (part === 'eye') return C(spec.eye || '#c9a227');

    // colourpoint family: pale body, dark extremities (face mask, ears, paws, tail)
    if (P === 'point' || P === 'mitted') {
      let t = 0;
      if (part === 'ear' || part === 'tail') t = 1;
      else if (part === 'paw') t = P === 'mitted' ? 0 : 1;
      else if (part === 'muzzle') t = .9;
      else if (part === 'head') t = Math.max(0, Math.min(1, (d.z - .15) * 1.6 - Math.abs(d.x) * .6 + .1));
      else if (part === 'body') t = Math.max(0, (-d.y - .4) * .5);
      if (P === 'mitted' && (part === 'chest' || part === 'paw')) return white.clone();
      return base.clone().lerp(point, t);
    }

    if (whiteMask(part, d)) return white.clone();
    if (P === 'cow' && part !== 'muzzle') {
      const n = fbm(d.x * 1.6 + 3, d.y * 1.6, d.z * 1.6, seed * 7 + part.length);
      return n > .58 ? base.clone() : white.clone();
    }
    if (part === 'muzzle' || part === 'chest') return spec.lightMuzzle === false ? base.clone() : base.clone().lerp(white, .75);

    let c = base.clone();
    if (P === 'calico' || P === 'tortie') {
      const n = fbm(d.x * 2.2 + 5, d.y * 2.2, d.z * 2.2, seed * 13 + part.length * 3);
      c = n > .52 ? second.clone() : base.clone();
      if (P === 'tortie') c.lerp(n > .5 ? base : second, fbm(d.x * 9, d.y * 9, d.z * 9, seed) * .35);
      return c;
    }
    const tabby = ['mackerel', 'classic', 'spotted', 'ticked'].includes(P);
    if (!tabby || part === 'paw') return c;

    let stripe = 0;
    if (part === 'head') {
      if (d.y > .35 && d.z > -.1) stripe = sstep(.35, .75, Math.sin(d.x * 26)) * (Math.abs(d.x) < .45 ? 1 : 0); // forehead M
      if (Math.abs(d.x) > .72 && d.y > -.35 && d.y < .25) stripe = Math.max(stripe, sstep(.3, .7, Math.sin(d.y * 22)));  // cheeks
    } else if (part === 'tail') {
      stripe = sstep(.1, .6, Math.sin(d._t * 40)) * .7;
    } else if (part === 'body' || part === 'ear') {
      const th = Math.atan2(d.x, -d.z);
      if (P === 'mackerel') stripe = sstep(.2, .6, Math.sin(th * 9 + d.y * 1.5)) * sstep(-.6, -.4, d.y);
      else if (P === 'classic') {
        const side = Math.sign(d.x) || 1, r = Math.hypot(d.y - .05, d.z + .1) + (1 - Math.abs(d.x)) * 1.2;
        stripe = sstep(.1, .5, Math.sin(r * 11)) * sstep(.15, .3, Math.abs(d.x));
      } else if (P === 'spotted') {
        const n = vnoise(d.x * 7 + 11, d.y * 7, d.z * 7 + seed);
        stripe = sstep(.64, .7, n);
        if (n > .62 && n <= .68) c.lerp(dark, .3);
      } else if (P === 'ticked') {
        stripe = (d.y > .82) ? .6 : 0;                                         // only a darker spine
        c.lerp(dark, vnoise(d.x * 30, d.y * 30, d.z * 30) * .18);
      }
    }
    return c.lerp(dark, Math.min(1, stripe));
  };
}

// ------------------------------------------------------------------ geometry helpers
function paint(geo, part, colorAt, frame) {
  const pos = geo.attributes.position, col = new Float32Array(pos.count * 3), v = new THREE.Vector3();
  for (let i = 0; i < pos.count; i++) {
    v.fromBufferAttribute(pos, i);
    const d = frame ? frame(v.clone()) : v.clone().normalize();
    const c = colorAt(part, d);
    col.set([c.r, c.g, c.b], i * 3);
  }
  geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
  return geo;
}
function fluff(geo, amount, freq = 3.2, seed = 0) {           // long fur: jagged surface
  if (!amount) return geo;
  const pos = geo.attributes.position, v = new THREE.Vector3();
  for (let i = 0; i < pos.count; i++) {
    v.fromBufferAttribute(pos, i);
    const n = v.clone().normalize();
    const k = 1 + amount * (vnoise(n.x * freq + seed, n.y * freq, n.z * freq) - .35);
    v.multiplyScalar(k); pos.setXYZ(i, v.x, v.y, v.z);
  }
  geo.computeVertexNormals();
  return geo;
}
const MAT = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .88, metalness: 0 });
const MATS = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .55, metalness: 0 });
function mesh(geo, m = MAT) { const o = new THREE.Mesh(geo, m); o.castShadow = o.receiveShadow = true; return o; }
function blob(r, part, colorAt, sx = 1, sy = 1, sz = 1, fur = 0, seg = 96) {
  const g = new THREE.SphereGeometry(r, seg, Math.round(seg * .7));
  paint(g, part, colorAt);
  fluff(g, fur, 7, r * 10);
  const o = mesh(g); o.scale.set(sx, sy, sz); return o;
}

// ------------------------------------------------------------------ shape presets
export const SHAPE_DEFAULT = {
  headW: 1.12, headH: .92, faceFlat: 0, cheek: 0, ear: 'upright', earSize: 1, earTuft: 0,
  eyeSize: 1, eyeShape: 'round', bodyBulk: 1, legLen: 1, fur: 0, ruff: 0, tail: 'normal', tailFluff: 0,
  hairless: false,
};

export function buildCat(shapeIn, coatSpec) {
  const s = { ...SHAPE_DEFAULT, ...shapeIn };
  const colorAt = makeCoat(coatSpec);
  const g = new THREE.Group();
  const fur = s.fur * .13;
  const lift = (s.legLen - 1) * .25;

  // body (sitting, front-facing)
  const body = blob(.62, 'body', colorAt, s.bodyBulk, .92, s.bodyBulk * .98, fur);
  body.position.set(0, .6 + lift, 0); g.add(body);
  const chest = blob(.36, 'chest', colorAt, 1, 1.05, .6, fur * .8);
  chest.position.set(0, .62 + lift, .42 * s.bodyBulk); g.add(chest);

  // front legs (visible length = breed leg length) + paws
  for (const x of [-1, 1]) {
    const len = .22 + lift * 1.6;
    if (len > .05) {
      const lg = new THREE.CapsuleGeometry(.13, len, 6, 16);
      paint(lg, 'paw', colorAt, v => new THREE.Vector3(x, -1, 1).normalize());
      const leg = mesh(lg); leg.position.set(x * .22, .12 + len / 2 + .02, .42); g.add(leg);
    }
    const paw = blob(.2, 'paw', colorAt, 1, .7, 1.15); paw.position.set(x * .23, .12, .48); g.add(paw);
  }

  // tail
  const tailPts = s.tail === 'bob'
    ? [[.15, .3, -.55], [.35, .38, -.68]]
    : [[.1, .35, -.5], [.6, .4, -.8], [.88, .9, -.62], [.78, 1.3, -.38]];
  const curve = new THREE.CatmullRomCurve3(tailPts.map(p => new THREE.Vector3(p[0], p[1] + lift, p[2])));
  const tr = .11 + s.tailFluff * .09;
  const tg = new THREE.TubeGeometry(curve, 40, tr, 14, false);
  { // paint tail with progress along length (for rings)
    const pos = tg.attributes.position, col = new Float32Array(pos.count * 3);
    for (let i = 0; i < pos.count; i++) {
      const t = Math.floor(i / 15) / 40;
      const c = colorAt('tail', { x: 0, y: 0, z: 0, _t: t });
      col.set([c.r, c.g, c.b], i * 3);
    }
    tg.setAttribute('color', new THREE.BufferAttribute(col, 3));
  }
  g.add(mesh(tg));
  const tip = blob(tr * (s.tail === 'bob' ? 1.6 : 1), 'tail', (p, d) => colorAt('tail', { ...d, _t: 1 }), 1, 1, 1, s.tailFluff * .3);
  tip.position.copy(curve.getPoint(1)); g.add(tip);
  if (s.tailFluff) for (let i = 1; i < 6; i++) {
    const p = curve.getPoint(i / 6);
    const puff = blob(tr * 1.25, 'tail', (q, d) => colorAt('tail', { ...d, _t: i / 6 }), 1, 1, 1, .15); puff.position.copy(p); g.add(puff);
  }

  // neck ruff for long-haired breeds (Maine Coon, Persian, Norwegian, Siberian, Ragdoll)
  if (s.ruff) {
    const ruff = blob(.55, 'chest', colorAt, 1.25, .62, 1.0, .2 * s.ruff, 64);
    ruff.position.set(0, 1.0 + lift, .12); g.add(ruff);
  }

  // head
  const head = new THREE.Group(); head.position.set(0, 1.42 + lift, .08); g.add(head);
  const hg = new THREE.SphereGeometry(.72, 112, 80);
  paint(hg, 'head', colorAt);
  fluff(hg, fur * .8, 6, 3);
  const hm = mesh(hg); hm.scale.set(s.headW, s.headH, .95 - s.faceFlat * .12); head.add(hm);
  if (s.cheek) for (const x of [-1, 1]) {
    const ck = blob(.34, 'head', colorAt, 1, .85, .8, fur * .6); ck.position.set(x * .5 * s.headW, -.22, .28); head.add(ck);
  }

  // ears
  const es = s.earSize;
  for (const x of [-1, 1]) {
    const eg = new THREE.ConeGeometry(.26 * es, .44 * es, 24);
    paint(eg, 'ear', colorAt);
    const ear = mesh(eg);
    const ig = new THREE.ConeGeometry(.15 * es, .27 * es, 18); paint(ig, 'innerEar', colorAt);
    const inner = mesh(ig);
    const ex = x * .5 * s.headW * .92, ey = .58 * s.headH / .92;
    if (s.ear === 'fold') {
      ear.scale.set(1.05, .55, .9); ear.rotation.set(1.05, 0, x * .35); ear.position.set(x * .46 * s.headW / 1.12, .52, .14);
      head.add(ear);
    } else {
      let rz = -x * .32, rx = 0;
      if (s.ear === 'curl') { rx = -1.35; }
      if (s.ear === 'large') { rz = -x * .55; }
      ear.rotation.set(rx, 0, rz); ear.position.set(ex * (s.ear === 'large' ? 1.08 : 1), s.ear === 'curl' ? ey - .06 : ey, s.ear === 'curl' ? -.2 : 0);
      inner.rotation.set(rx, 0, rz); inner.position.set(ex * (s.ear === 'large' ? 1.08 : 1), s.ear === 'curl' ? ey - .02 : ey - .02, s.ear === 'curl' ? -.14 : .1);
      head.add(ear, inner);
      if (s.earTuft) {
        const tuft = new THREE.ConeGeometry(.05, .22, 8); paint(tuft, 'ear', (p, d) => C('#3a302a'));
        const tf = mesh(tuft); tf.rotation.copy(ear.rotation);
        tf.position.copy(ear.position).add(new THREE.Vector3(-Math.sin(rz) * .3 * es, Math.cos(rz) * .3 * es, 0)); head.add(tf);
      }
    }
  }

  // muzzle (flat faces sit closer), nose, eyes, blush
  const mz = blob(.3, 'muzzle', colorAt, 1.15, .72, .6 - s.faceFlat * .25);
  mz.position.set(0, -.25 + s.faceFlat * .05, .5 - s.faceFlat * .16); head.add(mz);
  const ng = new THREE.SphereGeometry(.07, 20, 14); paint(ng, 'nose', colorAt);
  const nose = mesh(ng, MATS); nose.scale.set(1.2, .8, 1); nose.position.set(0, -.12 + s.faceFlat * .07, .68 - s.faceFlat * .14); head.add(nose);
  for (const x of [-1, 1]) {
    const er = .13 * s.eyeSize;
    const ig = new THREE.SphereGeometry(er, 28, 20); paint(ig, 'eye', colorAt);
    const iris = mesh(ig, MATS); iris.scale.set(s.eyeShape === 'almond' ? 1.15 : .9, s.eyeShape === 'almond' ? .8 : 1.1, .45);
    iris.position.set(x * .3, .04, .63 - s.faceFlat * .08); head.add(iris);
    const pupil = new THREE.Mesh(new THREE.SphereGeometry(er * .62, 20, 14), new THREE.MeshStandardMaterial({ color: '#1d1714', roughness: .4 }));
    pupil.scale.set(s.eyeShape === 'almond' ? .8 : .85, 1.15, .45); pupil.position.set(x * .3, .04, .66 - s.faceFlat * .08); head.add(pupil);
    const hl = new THREE.Mesh(new THREE.SphereGeometry(er * .3, 12, 10), new THREE.MeshBasicMaterial({ color: '#ffffff' }));
    hl.position.set(x * .3 - .04, .1, .7 - s.faceFlat * .08); head.add(hl);
    const bl = new THREE.Mesh(new THREE.SphereGeometry(.12, 16, 12), new THREE.MeshStandardMaterial({ color: '#f7a8bb', roughness: 1, transparent: true, opacity: .7 }));
    bl.scale.set(1.3, .6, .3); bl.position.set(x * .5, -.2, .5); head.add(bl);
  }
  if (s.hairless) g.traverse(o => { if (o.isMesh && o.material === MAT) o.material = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .45 }); });
  return g;
}

// ------------------------------------------------------------------ catalogue
export const BREEDS = [
  { id: 'korean_shorthair', ko: '코리안 숏헤어', shape: {}, coat: { pattern: 'mackerel', base: '#a9a49a', dark: '#5e5850', eye: '#c9a227' } },
  { id: 'russian_blue', ko: '러시안 블루', shape: { eyeShape: 'almond', earSize: 1.12, bodyBulk: .92 }, coat: { pattern: 'solid', base: '#8e9aa8', eye: '#5fae6a', lightMuzzle: false, nose: '#7d8794' } },
  { id: 'persian', ko: '페르시안', shape: { faceFlat: 1, earSize: .7, fur: 1, ruff: 1, tailFluff: 1, cheek: 1, eyeSize: 1.15 }, coat: { pattern: 'solid', base: '#f4efe6', eye: '#d7902f' } },
  { id: 'siamese', ko: '샴', shape: { earSize: 1.3, ear: 'large', eyeShape: 'almond', bodyBulk: .85, headW: 1.0 }, coat: { pattern: 'point', base: '#f1e6d2', point: '#4f3a30', eye: '#5aa6e0' } },
  { id: 'scottish_fold', ko: '스코티시 폴드', shape: { ear: 'fold', eyeSize: 1.25, cheek: .6 }, coat: { pattern: 'mackerel', base: '#a9adb6', dark: '#686c74', eye: '#c9a227' } },
  { id: 'british_shorthair', ko: '브리티시 숏헤어', shape: { cheek: 1, earSize: .8, bodyBulk: 1.12, headW: 1.2, eyeSize: 1.15 }, coat: { pattern: 'solid', base: '#8f97a3', eye: '#e19a2b', lightMuzzle: false } },
  { id: 'munchkin', ko: '먼치킨', shape: { legLen: .55 }, coat: { pattern: 'classic', base: '#e8b878', dark: '#b4703a', eye: '#c9a227' } },
  { id: 'ragdoll', ko: '랙돌', shape: { fur: .8, ruff: .9, tailFluff: 1, eyeSize: 1.1 }, coat: { pattern: 'mitted', base: '#f3ece2', point: '#6b5a52', eye: '#4f8fd8' } },
  { id: 'american_shorthair', ko: '아메리칸 숏헤어', shape: { cheek: .5, bodyBulk: 1.05 }, coat: { pattern: 'classic', base: '#c9c9c4', dark: '#3d3d3d', eye: '#c9a227' } },
  { id: 'norwegian_forest', ko: '노르웨이 숲', shape: { fur: 1, ruff: 1, tailFluff: 1, earTuft: 1, earSize: 1.1 }, coat: { pattern: 'mackerel', base: '#b8a68e', dark: '#6e5b45', eye: '#9db24a', whiteLevel: .25 } },
  { id: 'maine_coon', ko: '메인쿤', shape: { fur: .9, ruff: 1.2, tailFluff: 1, earTuft: 1, earSize: 1.25, headW: 1.08, bodyBulk: 1.12 }, coat: { pattern: 'classic', base: '#8a6a4f', dark: '#3e2d22', eye: '#c9a227', whiteLevel: .2 } },
  { id: 'bengal', ko: '벵갈', shape: { bodyBulk: .95, eyeShape: 'almond' }, coat: { pattern: 'spotted', base: '#d9a95c', dark: '#4a3322', eye: '#7cae3d' } },
  { id: 'abyssinian', ko: '아비시니안', shape: { earSize: 1.3, eyeShape: 'almond', bodyBulk: .88 }, coat: { pattern: 'ticked', base: '#c98a52', dark: '#7a4a28', eye: '#b8a03a' } },
  { id: 'sphynx', ko: '스핑크스', shape: { ear: 'large', earSize: 1.5, hairless: true, bodyBulk: .88, eyeShape: 'almond' }, coat: { pattern: 'calico', base: '#f0c3b5', second: '#b9a4a4', white: '#f6d3c8', whiteLevel: 0, eye: '#9cb84a', innerEar: '#eba8a0' } },
  { id: 'turkish_angora', ko: '터키시 앙고라', shape: { fur: .6, tailFluff: 1, earSize: 1.15 }, coat: { pattern: 'solid', base: '#fbf8f2', eye: '#7fb6ea' } },
  { id: 'exotic_shorthair', ko: '엑조틱 숏헤어', shape: { faceFlat: 1, earSize: .72, cheek: 1, eyeSize: 1.2, bodyBulk: 1.08 }, coat: { pattern: 'classic', base: '#e2a35e', dark: '#b06a2d', eye: '#d7902f' } },
  { id: 'american_curl', ko: '아메리칸 컬', shape: { ear: 'curl', earSize: 1.1 }, coat: { pattern: 'bicolor', base: '#55504c', eye: '#c9a227', whiteLevel: .5 } },
  { id: 'devon_rex', ko: '데본 렉스', shape: { ear: 'large', earSize: 1.55, eyeSize: 1.25, headW: 1.0, bodyBulk: .85 }, coat: { pattern: 'solid', base: '#cdbfae', eye: '#9cb84a' } },
];

export const KOREAN_COATS = [
  { id: 'godeungeo', ko: '고등어 태비', coat: { pattern: 'mackerel', base: '#9c968c', dark: '#4d4740' } },
  { id: 'cheese', ko: '치즈 태비', coat: { pattern: 'mackerel', base: '#f0b46a', dark: '#c47a33' } },
  { id: 'cheese_white', ko: '치즈 (흰 바탕)', coat: { pattern: 'mackerel', base: '#f0b46a', dark: '#c47a33', whiteLevel: .5 } },
  { id: 'tuxedo', ko: '턱시도', coat: { pattern: 'tuxedo', base: '#2e2d31' } },
  { id: 'cow', ko: '젖소', coat: { pattern: 'cow', base: '#2e2d31', seed: 3 } },
  { id: 'calico', ko: '삼색이', coat: { pattern: 'calico', base: '#2e2d31', second: '#e2903f', seed: 2 } },
  { id: 'tortie', ko: '카오스', coat: { pattern: 'tortie', base: '#2f2622', second: '#b8652e', seed: 5 } },
  { id: 'black', ko: '올블랙', coat: { pattern: 'solid', base: '#2a292d', lightMuzzle: false, eye: '#e2c13a' } },
  { id: 'white', ko: '올화이트', coat: { pattern: 'solid', base: '#fbf8f2', eye: '#7fb6ea' } },
  { id: 'grey_solid', ko: '회색 단색', coat: { pattern: 'solid', base: '#8e939b', lightMuzzle: false } },
  { id: 'silver_classic', ko: '은색 클래식 태비', coat: { pattern: 'classic', base: '#d6d6d2', dark: '#3a3a3a' } },
  { id: 'van', ko: '반 (머리·꼬리만)', coat: { pattern: 'van', base: '#e2903f' } },
];
