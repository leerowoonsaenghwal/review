// Parametric quadruped cat generator (toy / Animal-Crossing-like look).
//
// cat = SHAPE (breed silhouette) x COAT (colour + pattern)
// The model is a rigid-segment rig so it can walk:
//
//   root
//    └ body (bob)                      chest / waist / hips shells
//       ├ neck ─ head                  ears, muzzle, eyes ...
//       ├ frontL/R: upper ─ lower ─ paw
//       ├ hindL/R:  thigh ─ shin ─ hock ─ paw
//       └ tail0 ─ tail1 ─ ... tail9
//
// The same hierarchy maps 1:1 onto a Unity Transform rig.
import * as THREE from 'three';

// ------------------------------------------------------------------ noise
function hash(x, y, z) { const h = Math.sin(x * 127.1 + y * 311.7 + z * 74.7) * 43758.5453; return h - Math.floor(h); }
function vnoise(x, y, z) {
  const xi = Math.floor(x), yi = Math.floor(y), zi = Math.floor(z);
  const s = t => t * t * (3 - 2 * t), u = s(x - xi), v = s(y - yi), w = s(z - zi);
  let r = 0;
  for (let a = 0; a < 2; a++) for (let b = 0; b < 2; b++) for (let c = 0; c < 2; c++)
    r += (a ? u : 1 - u) * (b ? v : 1 - v) * (c ? w : 1 - w) * hash(xi + a, yi + b, zi + c);
  return r;
}
// cellular noise: [F1, F2] = distances to the nearest and second-nearest jittered feature point.
// F1 is 0 at a tuft centre; F2 - F1 is 0 on the crease between two tufts.
function worley2(x, y, z) {
  const xi = Math.floor(x), yi = Math.floor(y), zi = Math.floor(z);
  let d = 9, d2 = 9;
  for (let a = -1; a <= 1; a++) for (let b = -1; b <= 1; b++) for (let c = -1; c <= 1; c++) {
    const cx = xi + a, cy = yi + b, cz = zi + c;
    const fx = cx + hash(cx, cy, cz), fy = cy + hash(cy + 7, cz, cx), fz = cz + hash(cz + 13, cx, cy);
    const q = (x - fx) ** 2 + (y - fy) ** 2 + (z - fz) ** 2;
    if (q < d) { d2 = d; d = q; } else if (q < d2) d2 = q;
  }
  return [Math.sqrt(d), Math.sqrt(d2)];
}
const fbm = (x, y, z, k = 0) => .6 * vnoise(x + k, y, z) + .4 * vnoise(2 * x, 2 * y + k, 2 * z);
const sstep = (a, b, x) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
const C = h => new THREE.Color(h);

// ------------------------------------------------------------------ coat
// colorAt(part, p, info)
//   p    : vertex position in cat root space (rest pose)
//   info : body -> {nx,ny,nz} normalised body coords (x side, y up, z front)
//          head -> {dir} unit direction in head space (z = face)
//          leg  -> {t: 0 top .. 1 paw, kind: 'front'|'hind'}
//          tail -> {t: 0 base .. 1 tip}
export function makeCoat(spec) {
  const P = spec.pattern || 'solid';
  const base = C(spec.base), dark = spec.dark ? C(spec.dark) : C(spec.base).multiplyScalar(.6);
  const white = C(spec.white || '#fbf7ef'), point = C(spec.point || '#5b4337'), second = C(spec.second || '#e8964a');
  const seed = spec.seed || 1;
  const W = spec.whiteLevel ?? ({ tuxedo: .45, bicolor: .55, cow: .7, van: .9, calico: .5, mitted: .2 }[P] || 0);
  const smoke = spec.smoke || 0;

  function isWhite(part, p, info) {
    if (W <= 0) return false;
    if (part === 'muzzle') return W >= .2;
    if (part === 'leg') return info.t > 1.05 - W * (info.kind === 'front' ? 1.1 : .9);
    if (part === 'tail') return P === 'van' ? false : W > .85;
    if (part === 'body') {
      if (P === 'van') return true;
      return info.ny < -1 + W * 1.35 || (info.nz > .55 && info.ny < .35 && W >= .3);
    }
    if (part === 'head') {
      const d = info.dir;
      if (P === 'van') return !(d.y > .3);
      return W >= .4 && d.y < -.05 && d.z > .25 && Math.abs(d.x) < .5 + (W - .4);
    }
    return false;
  }

  return function colorAt(part, p, info) {
    if (part === 'nose') return C(spec.nose || '#e9837a');
    if (part === 'innerEar') return C(spec.innerEar || '#f2b0be');
    if (part === 'eye') return C(spec.eye || '#c9a227');
    if (part === 'eye2') return C(spec.eye2 || spec.eye || '#c9a227');   // odd-eyed cats (white Turkish Angora / Van)
    if (part === 'pad') return C(spec.pad || '#e9a3a3');
    // tear marks (Savannah, like a cheetah): a dark line from the inner eye corner down the side of the muzzle.
    // Checked before anything else so it also paints the muzzle mesh, which covers most of the line.
    if (spec.tearMark && (part === 'head' || part === 'muzzle') && info.dir.z > .45) {
      const d = info.dir, t = Math.min(1, Math.max(0, (-d.y + .02) / .5));
      const lx = .17 + .17 * t, dist = Math.abs(Math.abs(d.x) - lx), wd = .035 + .015 * t;
      if (d.y < .02 && d.y > -.58 && dist < wd) return C(spec.dark || '#2c241c').lerp(C(spec.base), sstep(wd * .5, wd, dist));
    }

    // --- colourpoint (Siamese, Himalayan, Birman, Ragdoll): dark mask, ears, legs, tail
    if (P === 'point' || P === 'mitted') {
      let t = 0;
      if (part === 'ear' || part === 'tail') t = 1;
      else if (part === 'leg') t = P === 'mitted' && info.t > .7 ? -1 : sstep(.1, .7, info.t);
      else if (part === 'muzzle') t = P === 'mitted' ? 0 : .6;
      else if (part === 'head') t = .8 * sstep(0, .6, info.dir.z - Math.abs(info.dir.x) * .7 - Math.max(0, info.dir.y) * .6);
      else if (part === 'body') t = .12 * sstep(.2, 1, info.ny);
      if (t < 0) return white.clone();
      if (P === 'mitted' && part === 'body' && info.ny < -.3 && info.nz > .3) return white.clone();
      return base.clone().lerp(point, t);
    }

    // shaded / chinchilla: white undercoat with dark tips, heaviest on the back, head top and tail
    if (P === 'shaded') {
      let t = 0;
      if (part === 'body') t = .85 * sstep(-.3, .8, info.ny);
      else if (part === 'head') t = .7 * sstep(-.2, .8, info.dir.y) * (1 - sstep(.4, .9, info.dir.z));
      else if (part === 'tail') t = .45 + .2 * sstep(.6, 1, info.t);
      else if (part === 'ear') t = .8;
      else if (part === 'leg') t = .3 * (1 - info.t);
      return white.clone().lerp(dark, t);
    }
    if (isWhite(part, p, info)) return white.clone();

    if (P === 'cow') return fbm(p.x * 2.2 + 3, p.y * 2.2, p.z * 2.2, seed * 7) > .58 ? base.clone() : white.clone();
    if (part === 'muzzle') return spec.lightMuzzle === false ? base.clone() : base.clone().lerp(white, .7);

    let c = base.clone();
    if (P === 'calico' || P === 'tortie') {
      const n = fbm(p.x * 2.6 + 5, p.y * 2.6, p.z * 2.6, seed * 13);
      c = n > .52 ? second.clone() : base.clone();
      if (P === 'tortie') c.lerp(n > .5 ? base : second, vnoise(p.x * 14, p.y * 14, p.z * 14) * .4);
      return c;
    }
    if (smoke) c.lerp(white, smoke * sstep(-.2, -1, part === 'body' ? info.ny : 0));
    // silver tipping (Russian Blue): pale hair tips scattered over the coat -> a silvery shimmer
    if (spec.silverTip && part !== 'muzzle') c.lerp(C('#dfe4ea'), spec.silverTip * (.14 + .14 * vnoise(p.x * 52, p.y * 52, p.z * 52)));
    // glitter (Bengal): tiny bright golden flecks
    if (spec.glitter && part !== 'muzzle') c.lerp(C('#fff1c4'), spec.glitter * .55 * sstep(.78, .86, vnoise(p.x * 48 + 3, p.y * 48, p.z * 48)));

    let s = 0;
    const tabby = ['mackerel', 'classic', 'spotted', 'ticked'].includes(P);
    if (!tabby) {
      if (part === 'body' && spec.wrinkles) s = 0;
      return c;
    }
    if (part === 'head') {
      const d = info.dir;
      // forehead "M": three stripes centred on the face (cos -> one in the middle, one each side), the patch faded
      // out softly at its edges (a hard cut through a stripe left a broken, dotted sliver on the game mesh)
      // Each stripe tapers towards the eyes (wide at the crown), the side ones a little shorter; the soft
      // edge ramp keeps the edge smooth on the game mesh (a sharp edge came out jagged)
      if (d.y > .26 && d.z > 0 && Math.abs(d.x) < .4) {
        const ax = Math.abs(d.x), top = sstep(.3, .62, d.y), lo = .5 - .42 * top;
        s = sstep(lo, lo + .55, Math.cos(d.x * 24)) * sstep(.27 + .07 * sstep(.14, .24, ax), .4 + .07 * sstep(.14, .24, ax), d.y) * sstep(.4, .34, ax) * sstep(0, .12, d.z);
      }
      if (Math.abs(d.x) > .6 && Math.abs(d.x) < .88 && d.z > .38) s = Math.max(s, sstep(.06, .0, Math.abs(d.y + .1)) * sstep(.6, .7, Math.abs(d.x)) * sstep(.88, .78, Math.abs(d.x)) * .65);   // one soft cheek stripe, wide enough not to break into dashes on a game mesh  // whisker-cheek lines
      if (P === 'ticked' || P === 'spotted') s *= .8;
    } else if (part === 'tail') {
      s = P === 'ticked' ? Math.max(sstep(.75, .95, info.t), .3) : spec.bold ? sstep(.25, .45, Math.sin(info.t * 20)) : sstep(.1, .6, Math.sin(info.t * 34)) * .85;
    } else if (part === 'leg') {
      s = P === 'ticked' ? 0 : spec.bold ? sstep(.3, .5, Math.sin(info.t * 10)) * sstep(.15, .35, info.t) * .85 : sstep(.2, .7, Math.sin(info.t * 16)) * sstep(.15, .35, info.t) * .7;
    } else if (part === 'body') {
      const { nx, ny, nz } = info;
      const spine = sstep(.75, .95, ny) * (1 - Math.abs(nx) * 2) * (1 - sstep(.75, .95, Math.abs(nz)));
      const poleFade = 1 - sstep(.72, .92, Math.abs(nz));
      // bold (style 'ac'): fewer, wider stripes with clean edges - big shapes read better than fine fur detail
      if (P === 'mackerel') s = Math.max(spine, (spec.bold ? sstep(.2, .4, Math.sin(nz * 10 + ny * 1.6)) : sstep(.15, .6, Math.sin(nz * 17 + ny * 2.2))) * sstep(-.45, -.15, ny) * poleFade);
      else if (P === 'classic') {
        const r = Math.hypot(nz + .15, (ny - .1) * 1.3);
        s = Math.max(spine, sstep(.1, .5, Math.sin(r * 12)) * sstep(-.5, -.2, ny) * sstep(.2, .5, Math.abs(nx)) * poleFade);
      } else if (P === 'spotted') {
        const f = 9 / (spec.spotSize || 1);   // spotSize > 1: bigger, bolder spots (Savannah)
        const n = vnoise(p.x * f + 11, p.y * f, p.z * f + seed), side = sstep(-.5, -.2, ny) * poleFade;
        if (spec.rosette) {
          // Bengal rosettes: a dark rim around a warmer, mid-tone centre
          const rim = sstep(.58, .62, n) * (1 - sstep(.66, .7, n)), inner = sstep(.66, .7, n);
          c.lerp(dark, .45 * inner * side).lerp(C(spec.rosetteFill || '#a8642c'), .35 * inner * side);
          s = Math.max(spine * .5, rim * side);
        } else s = Math.max(spine * .6, sstep(.62, .7, n) * side);
      } else if (P === 'ticked') {
        // agouti ticking read at toy scale: darker back shading into a pale belly, plus fine dark specks
        s = spine * .7 + .35 * sstep(-.1, .9, ny);
        c.lerp(white, .35 * sstep(-.2, -.9, ny));
        s = Math.max(s, .55 * sstep(.6, .72, vnoise(p.x * 26 + 7, p.y * 26, p.z * 26)) * sstep(-.5, .2, ny));
      }
    }
    return c.lerp(dark, Math.min(1, s));
  };
}

// ------------------------------------------------------------------ fur surfaces
const MAT = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .9, metalness: 0 });
const MAT_SKIN = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .45, metalness: 0 });
const MAT_GLOSS = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .35, metalness: 0 });

// fur: {type:'short'|'long'|'curly'|'rex'|'none', amount}
// opts.weight(v, n) -> 0..1   how much fur texture this area gets
// Long and curly fur also store a per-vertex 'fluff' (0 dark .. 1 light) that the paint pass uses to
// shade strands and curls so the coat has a fur feel without lumps.
function furify(geo, fur, k = 0, opts = {}) {
  const pos = geo.attributes.position, v = new THREE.Vector3(), n = new THREE.Vector3();
  const weight = opts.weight || (() => 1);
  geo.computeVertexNormals();
  const nor = geo.attributes.normal;
  const tufted = fur.type === 'long' || fur.type === 'curly';
  const fluff = tufted ? new Float32Array(pos.count) : null;
  for (let i = 0; i < pos.count; i++) {
    v.fromBufferAttribute(pos, i); n.fromBufferAttribute(nor, i);
    let off = 0;
    if (fur.type === 'long') {
      // fine combed strands: a smooth silhouette (cats read as cats by their outline), streaks for texture.
      // The long-hair *shape* comes from the tuft pieces added in addTufts().
      const t = vnoise(v.x * 9 + k, v.y * 26, v.z * 9), wgt = weight(v, n);
      off = fur.amount * wgt * (.008 + .012 * t);
      fluff[i] = .55 + .45 * t;
    } else if (fur.type === 'curly') {
      // small tight curls as surface texture; the visible ringlets are hooked tufts (addTufts)
      const [d, d2] = worley2(v.x * 17 + k, v.y * 17, v.z * 17);
      const t = Math.sqrt(Math.max(0, 1 - d * d / .75)) * sstep(0, .25, d2 - d), wgt = weight(v, n);
      off = fur.amount * wgt * (.01 + .02 * t);
      fluff[i] = .5 + .5 * t;
    }
    else if (fur.type === 'rex') off = fur.amount * .008 * Math.sin(v.z * 30 + v.y * 14 + 6 * vnoise(v.x * 5 + k, v.y * 5, v.z * 5)) * (.4 + .6 * vnoise(v.x * 9, v.y * 9 + k, v.z * 9));   // soft broken waves, not rings
    v.addScaledVector(n, off);
    pos.setXYZ(i, v.x, v.y, v.z);
  }
  if (fluff) geo.setAttribute('fluff', new THREE.BufferAttribute(fluff, 1));
  geo.computeVertexNormals();
  return geo;
}

function ellipsoid(r, sx, sy, sz, seg = 72) {
  const g = new THREE.SphereGeometry(r, seg, Math.round(seg * .7));
  g.scale(sx, sy, sz);
  return g;
}
function capsuleY(r0, r1, len, seg = 28) {
  // tapered capsule hanging down from the pivot (0,0,0) to (0,-len,0)
  const pts = [];
  const N = 10;
  for (let i = 0; i <= N; i++) { const a = -Math.PI / 2 + Math.PI / 2 * i / N; pts.push(new THREE.Vector2(r1 * Math.cos(a), -len + r1 * Math.sin(a))); }
  for (let i = 0; i <= N; i++) { const a = Math.PI / 2 * i / N; pts.push(new THREE.Vector2(r0 * Math.cos(a), r0 * Math.sin(a))); }
  return new THREE.LatheGeometry(pts, seg);
}
// A single stylised fur tuft: a soft cone along +y that bends towards +z (bend) and, for curly coats,
// hooks over into a ringlet (curl). 'fluff' runs 0 at the root to 1 at the tip (tip painted lighter).
function tuftGeo(len, r0, bend = .3, curl = 0) {
  const prof = [];
  for (let i = 0; i <= 12; i++) {
    const t = i / 12, r = r0 * Math.pow(1 - t, .55) * (1 + .3 * Math.sin(Math.PI * t * .8)) * Math.sqrt(Math.min(1, (1 - t) * 6));   // plump, soft tip
    prof.push(new THREE.Vector2(Math.max(1e-4, r), t * len));
  }
  const g = new THREE.LatheGeometry(prof, 14);
  const pos = g.attributes.position, fl = new Float32Array(pos.count);
  for (let i = 0; i < pos.count; i++) {
    let x = pos.getX(i), y = pos.getY(i), z = pos.getZ(i);
    const t = y / len;
    z += bend * len * t * t;                                  // gentle sweep
    if (curl) {                                               // ringlet: roll the outer half around x
      const a = curl * sstep(.35, 1, t), cy = len * .45, cz = bend * len * .2 + len * .18;
      const yy = y - cy, zz = z - cz;
      y = cy + yy * Math.cos(a) - zz * Math.sin(a); z = cz + yy * Math.sin(a) + zz * Math.cos(a);
    }
    pos.setXYZ(i, x, y, z); fl[i] = .5 + .5 * t;
  }
  g.setAttribute('fluff', new THREE.BufferAttribute(fl, 1));
  g.computeVertexNormals();
  return g;
}
// put a tuft at `at` (parent space) growing along `dir`, sweeping towards `toward`
function placeTuft(parent, geo, at, dir, toward, part, info, mat) {
  const Y = dir.clone().normalize(), Z = toward.clone().addScaledVector(Y, -toward.dot(Y)).normalize(), X = new THREE.Vector3().crossVectors(Y, Z);
  const m = mk(geo, part, info, mat);
  m.matrix.makeBasis(X, Y, Z).setPosition(at); m.matrixAutoUpdate = false;
  parent.add(m);
  return m;
}

function mk(geo, part, info, mat = MAT) {
  const m = new THREE.Mesh(geo, mat);
  m.castShadow = m.receiveShadow = true;
  m.userData.part = part; m.userData.info = info;
  return m;
}

// ------------------------------------------------------------------ shape
export const SHAPE_DEFAULT = {
  size: 1, headSize: 1.5, headW: 1.08, faceFlat: 0, cheek: 0, muzzleLen: 1,
  ear: 'upright', earSize: 1, earTuft: 0,
  eyeSize: 1, eyeShape: 'round', eyeTilt: 0, eyeLid: 0, eyeLiner: 0, eyeSpace: 1,
  bodyLen: 1, bodyBulk: 1, legLen: 1, legBulk: 1,
  fur: 'short', furAmount: 1, ruff: 0,
  faceFluff: .55,    // long fur on the cheeks/chin: Persian types 1.2, semi-longhairs (Maine Coon ...) keep a short face
  tail: 'normal', tailLen: 1, tailFluff: 0, tailThick: 1,
  wrinkles: 0, pawTuft: 0, romanNose: 0,
  whisker: 'straight',   // 'straight' | 'curly' (rex / curly coats) | 'none' (Sphynx)
  // silhouette shaping
  wedge: 0,          // 0 round ... 1 Siamese/Oriental wedge (narrow chin, long muzzle)
  headTri: 0,        // triangular head with straight profile (Norwegian, Japanese Bobtail)
  muzzleW: 1,        // square, broad muzzle (Maine Coon, American Shorthair)
  earSet: 'mid',     // 'high' (on top) | 'mid' | 'low' (wide, on the sides: Devon, Siamese, Persian)
  earWide: 1,        // base width of the ear
  earRound: 0,       // 0 pointed ... 1 rounded tips (British, Persian)
  chest: 1,          // chest depth
  backArch: 0,       // arched back (Cornish Rex)
  tuck: 0,           // tucked-up belly (Cornish Rex, Oriental)
  potBelly: 0,       // round belly (Sphynx)
  pouch: 0,          // primordial pouch under the rear belly (Egyptian Mau, Bengal)
  rumpHigh: 0,       // hind legs longer than front: rump sits higher (Manx, Egyptian Mau)
  neckLen: 1,        // neck length (Savannah, Siamese long / Persian short)
};

// ------------------------------------------------------------------ build
export function buildCat(shapeIn, coatSpec) {
  const s = { ...SHAPE_DEFAULT, ...shapeIn };
  const colorAt = makeCoat(coatSpec);
  const fur = { type: s.fur, amount: s.furAmount };
  const hairless = s.fur === 'none';
  // coat sheen: Bombay's patent-leather black, Russian Blue's silvery shimmer
  const bodyMat = hairless ? MAT_SKIN : coatSpec.sheen ? new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .9 - .5 * coatSpec.sheen, metalness: 0 }) : MAT;
  const L = s.legLen * .8, B = s.bodyBulk, BL = s.bodyLen * .82;

  const root = new THREE.Group();
  const rig = { root, legs: {}, tail: [] };

  // leg geometry sizes
  const RH = 1 + s.rumpHigh;
  const fUp = .3 * L, fLo = .3 * L, hTh = .3 * L * RH, hSh = .26 * L * RH, hHo = .2 * L * RH, pawR = .09 * s.legBulk;
  const shoulderY = fUp + fLo + pawR * .9;
  const hindExtra = (hTh + hSh + hHo) * (1 - 1 / RH) * .8;
  const bodyY = shoulderY + .06 + hindExtra / 2;

  // `move` carries whole-body motion (jump arc, rolling onto the side); the root itself stays free for the game
  // to place and turn the cat
  const move = new THREE.Group(); root.add(move); rig.move = move;
  const body = new THREE.Group(); body.position.y = bodyY; move.add(body); rig.body = body;
  // spine: two bones pivoting at mid-body, so the torso can arch (stretch), curl (sleep) and tuck (sit).
  // Front legs, neck and ruff ride on the chest bone; hind legs and tail on the hip bone.
  const chest = new THREE.Bone(), hip = new THREE.Bone(); body.add(chest, hip); rig.chest = chest; rig.hip = hip;
  rig.dims = { fUp, fLo, hTh, hSh, hHo, pawR, L, BL, B, bodyY, fluffy: s.fur === 'long' ? 1.06 + .08 * s.furAmount : s.fur === 'curly' ? 1.08 : 1 };
  rig.pitch = Math.atan2(hindExtra, .96 * BL);           // rump-high breeds lean forward
  const fluffy = s.fur === 'long' ? 1.06 + .08 * s.furAmount : s.fur === 'curly' ? 1.08 : 1;
  const bodyNorm = p => ({ nx: p.x / (.32 * B), ny: (p.y - bodyY) / (.34 * B), nz: p.z / (.9 * BL) });

  // torso: one smooth lofted shape - deep chest, slight waist, round hips (no segment seams)
  {
    const g = new THREE.SphereGeometry(1, 120, 80);
    g.rotateX(Math.PI / 2);                         // poles on the z axis (tail <-> chest)
    const pos = g.attributes.position;
    const prof = t => .305 + .055 * s.chest * Math.exp(-(((t - .45) / .32) ** 2)) + .035 * Math.exp(-(((t + .5) / .3) ** 2));
    for (let i = 0; i < pos.count; i++) {
      const x = pos.getX(i), y = pos.getY(i), z = pos.getZ(i);
      const r = prof(z) * B * fluffy, mid = 1 - z * z, under = Math.max(0, -y), over = Math.max(0, y);
      let yy = y * r + .03 - .025 * Math.max(0, z);
      yy += s.backArch * .1 * mid * over;                                       // arched spine
      yy += s.tuck * .13 * mid * under;                                          // tucked-up waist
      yy -= s.potBelly * .1 * mid * under;                                       // round belly
      yy -= s.pouch * .08 * Math.exp(-(((z + .42) / .24) ** 2)) * under;         // primordial pouch
      const xx = x * r * .93 * (1 + s.potBelly * .14 * mid * under);
      pos.setXYZ(i, xx, yy, z * .9 * BL);
    }
    g.computeVertexNormals();
    if (!hairless) furify(g, fur, 1);
    // skin weights: front half follows the chest bone, back half the hip bone, blended over the waist
    const n = pos.count, si = new Uint16Array(n * 4), sw = new Float32Array(n * 4);
    for (let i = 0; i < n; i++) { const w = sstep(-.32 * BL, .32 * BL, pos.getZ(i)); si[i * 4 + 1] = 1; sw[i * 4] = w; sw[i * 4 + 1] = 1 - w; }
    g.setAttribute('skinIndex', new THREE.Uint16BufferAttribute(si, 4));
    g.setAttribute('skinWeight', new THREE.Float32BufferAttribute(sw, 4));
    const torso = new THREE.SkinnedMesh(g, bodyMat);
    torso.castShadow = torso.receiveShadow = true; torso.userData.part = 'body'; torso.userData.info = 'bodyNorm';
    body.add(torso); rig.torso = torso;
  }
  if (s.ruff) {
    const g = ellipsoid(1, .4 * B * fluffy, .36, .3, 96); g.translate(0, .1, .6 * BL);
    furify(g, { type: s.fur === 'curly' ? 'curly' : 'long', amount: s.ruff }, 9);
    chest.add(mk(g, 'body', 'bodyNorm'));
  }

  // legs
  const legs = [
    ['frontL', -1, 'front', .5], ['frontR', 1, 'front', .5],
    ['hindL', -1, 'hind', -.48], ['hindR', 1, 'hind', -.48],
  ];
  for (const [name, side, kind, z] of legs) {
    const top = new THREE.Group();
    top.position.set(side * .19 * B, kind === 'front' ? -.08 : -.02, z * BL);
    (kind === 'front' ? chest : hip).add(top);
    const chain = [];
    const segs = kind === 'front' ? [[fUp, .1, .085], [fLo, .085, .075]] : [[hTh, .14, .1], [hSh, .095, .075], [hHo, .072, .07]];
    let parent = top, acc = 0, total = segs.reduce((a, b) => a + b[0], 0);
    const thick = (s.fur === 'long' ? 1.15 : 1) * s.legBulk * 1.18;
    for (const [len, r0, r1] of segs) {
      const j = new THREE.Group(); parent.add(j);
      const g = capsuleY(r0 * thick, r1 * thick, len);
      const t0 = acc / total, t1 = (acc + len) / total;
      if (!hairless) furify(g, { ...fur, amount: fur.amount * .6 }, side + acc);
      j.add(mk(g, 'leg', { kind, t0, t1, len }, bodyMat));
      chain.push(j);
      const next = new THREE.Group(); next.position.y = -len; j.add(next);
      parent = next; acc += len;
    }
    const paw = new THREE.Group(); parent.add(paw);
    const pg = ellipsoid(1, pawR * 1.05, pawR * .62, pawR * 1.35, 40); pg.translate(0, -pawR * .2, pawR * .35);
    paw.add(mk(pg, 'leg', { kind, t0: 1, t1: 1, len: 0 }, bodyMat));
    chain.push(paw);
    rig.legs[name] = { top, chain, kind, side };
  }

  // tail
  const tailN = s.tail === 'none' ? 0 : s.tail === 'bob' ? 3 : 10;
  if (tailN) {
    let parent = new THREE.Group(); parent.position.set(0, .16, -.78 * BL); hip.add(parent);
    const segLen = (s.tail === 'bob' ? .06 : .1) * s.tailLen;
    const plume = s.tailFluff ? 1 + s.tailFluff * (s.fur === 'long' ? 1.2 : .9) : 1;
    for (let i = 0; i < tailN; i++) {
      const j = new THREE.Group(); parent.add(j);
      const rad = k => (plume > 1 ? .068 * plume * (1 - .22 * k) * (k > .85 ? Math.sqrt(Math.max(.15, 1 - ((k - .85) / .2) ** 2)) : 1) : .068 - .026 * k) * (s.fur === 'curly' ? 1.2 : 1);
      const r0 = rad(i / tailN) * s.tailThick, r1 = rad((i + 1) / tailN) * s.tailThick;
      const g = capsuleY(r0, r1, segLen, 24);
      g.rotateX(Math.PI);                    // grow backwards/upwards along +y of the joint
      if (s.fur === 'curly') furify(g, { type: 'curly', amount: .9 }, i * 3);
      else if (s.fur === 'long') furify(g, { type: 'long', amount: s.furAmount }, i * 3);
      j.add(mk(g, 'tail', { t0: i / tailN, t1: (i + 1) / tailN, len: segLen }, bodyMat));
      const next = new THREE.Group(); next.position.y = segLen; j.add(next);
      rig.tail.push(j); parent = next;
    }
    if (s.tail === 'bob') {
      const g = ellipsoid(1, .1, .1, .1, 32); furify(g, { type: 'long', amount: 1.6 }, 4);
      parent.add(mk(g, 'tail', { t0: 1, t1: 1, len: 0 }, bodyMat));
    }
  }

  // neck + head
  const neck = new THREE.Group(); neck.position.set(0, .2, .62 * BL); chest.add(neck); rig.neck = neck;
  {
    const g = ellipsoid(1, .25 * B * fluffy * (1 - (s.neckLen - 1) * .3), .27 * B * fluffy * s.neckLen, .28, 64); g.translate(0, .1 * s.neckLen, .06);
    if (!hairless) furify(g, fur, 5);
    if (s.wrinkles) {   // Sphynx: loose skin folds around the neck and shoulders
      const q = g.attributes.position;
      const sh = new Float32Array(q.count);
      for (let i = 0; i < q.count; i++) {
        const y = q.getY(i), r = Math.pow(.5 + .5 * Math.sin(y * 70), 3), k = 1 + s.wrinkles * .05 * r;
        q.setX(i, q.getX(i) * k); q.setZ(i, q.getZ(i) * k); sh[i] = .667 - .45 * (1 - r) + .12 * r;
      }
      g.setAttribute('fluff', new THREE.BufferAttribute(sh, 1));
      g.computeVertexNormals();
    }
    neck.add(mk(g, 'body', 'bodyNorm', bodyMat));
  }
  const head = new THREE.Group(); head.position.set(0, .26 * s.headSize + (s.neckLen - 1) * .3, .16 * s.headSize + (s.neckLen - 1) * .12); neck.add(head); rig.head = head;
  const HS = .42 * s.headSize;
  {
    const g = ellipsoid(HS, s.headW, .9, .92 - s.faceFlat * .1, 96);
    {
      const pos = g.attributes.position;
      const shade = s.wrinkles ? new Float32Array(pos.count).fill(.667) : null;   // 'fluff' shading: .667 = unchanged
      for (let i = 0; i < pos.count; i++) {
        let x = pos.getX(i), y = pos.getY(i), z = pos.getZ(i);
        const ny = y / (HS * .9), nz = z / (HS * .92);
        const low = sstep(.35, -.9, ny), front = sstep(-.2, .9, nz);
        // wedge: narrow towards the chin, longer muzzle (Siamese / Oriental)
        x *= 1 - s.wedge * .36 * low - s.headTri * .22 * low;
        z += HS * front * low * (s.wedge * .22 + s.headTri * .08);
        y -= HS * front * low * s.wedge * .08;
        // flat face: lower front pushed in, domed forehead (Persian / Exotic)
        z -= HS * s.faceFlat * .07 * sstep(.4, 1, nz) * sstep(.5, -.5, ny);
        y += HS * s.faceFlat * .05 * sstep(.2, 1, ny);
        // roman nose (Birman, Cornish Rex): the bridge between the eyes bulges forward -> convex profile
        if (s.romanNose) {
          const nx = x / (HS * s.headW);
          z += HS * s.romanNose * .07 * sstep(.25, .05, Math.abs(nx)) * sstep(-.4, -.1, ny) * sstep(.3, .05, ny) * sstep(.5, .9, nz);
        }
        // Sphynx: soft horizontal forehead wrinkles (raised ridges, not grooves)
        if (s.wrinkles) {
          const nx = x / (HS * s.headW), m = sstep(.15, .3, ny) * sstep(.9, .62, ny) * sstep(0, .3, nz) * sstep(.62, .32, Math.abs(nx));
          const ridge = Math.pow(.5 + .5 * Math.sin(ny * 30 - 1), 3), k = 1 + s.wrinkles * .045 * m * ridge;
          x *= k; y *= k; z *= k;
          shade[i] = .667 - .55 * m * (1 - ridge) + .15 * m * ridge;     // darker creases, lighter ridges
        }
        pos.setXYZ(i, x, y, z);
      }
      if (shade) g.setAttribute('fluff', new THREE.BufferAttribute(shade, 1));
      g.computeVertexNormals();
    }
    // fur texture stays off the front of the face (AC faces are smooth); cheek tufts come from addTufts()
    const wHead = (v, n) => 1 - .85 * sstep(.25, .8, n.z);
    if (!hairless) furify(g, { ...fur, amount: fur.amount * .8 }, 11, { weight: wHead });
    head.add(mk(g, 'head', 'headDir', bodyMat));
    if (s.cheek) for (const x of [-1, 1]) {
      const c = ellipsoid(HS * .38, 1, .8, .85, 48); c.translate(x * HS * .5 * s.headW, -HS * .3, HS * (.36 - .06 * s.faceFlat));   // cheeks follow the flattened face
      head.add(mk(c, 'head', 'headDir', bodyMat));
    }
  }
  // ears
  const earGroups = []; rig.ears = earGroups; rig.eyes = [];
  for (const x of [-1, 1]) {
    const es = Math.pow(s.earSize, .65) * HS / .42 * .82 * (s.ear === 'large' ? 1.15 : 1);
    // one cone per ear (base sunk into the skull); the pink inner ear is painted on its front face
    const eh = .36 * es, er0 = .16 * es * s.earWide;
    // lathe cone (ConeGeometry with heightSegments > 1 flips half of its triangles in three r169)
    const prof = [];
    for (let k = 0; k <= 14; k++) { const t = k / 14; prof.push(new THREE.Vector2(Math.max(1e-4, er0 * Math.pow(1 - t, 1 - .6 * s.earRound) * (1 - .12 * Math.sin(Math.PI * t))), -eh / 2 + eh * t)); }
    const eg = new THREE.LatheGeometry(prof, 40); eg.translate(0, .08 * es, 0);
    const ear = new THREE.Group();
    const earMesh = mk(eg, 'ear', { ear: true, h: eh, r: er0, y0: .08 * es - eh / 2 }, bodyMat);
    earMesh.receiveShadow = false;   // thin cones self-shadow into moire bands
    ear.add(earMesh);
    // ear set: angle from the top of the skull
    const th = { high: .4, mid: .6, low: .95 }[s.earSet] ?? .6;
    const ex = x * Math.sin(th) * HS * s.headW * .9, ey = Math.cos(th) * HS * .9 * .9;
    ear.position.set(ex, ey, -HS * .05);
    const tilt = -x * Math.min(th * .8, .62);   // even low-set ears stay well above horizontal
    if (s.ear === 'fold') { ear.scale.set(1.08, .5, .72); ear.rotation.set(1.62, 0, x * .45); ear.position.set(x * HS * .44, HS * .76, HS * .16); }
    else if (s.ear === 'curl') { ear.rotation.set(-.8, 0, tilt * .7); ear.position.z -= HS * .06; }
    else if (s.ear === 'large') { ear.rotation.set(0, 0, tilt * 1.05); }
    else if (s.ear === 'small') { ear.scale.setScalar(.75); ear.rotation.set(.15, 0, tilt * 1.1); }
    else ear.rotation.set(.05, 0, tilt);
    if (s.earTuft) {
      const t = new THREE.ConeGeometry(.022 * es, .09 * es, 8); t.translate(0, .3 * es, 0);
      ear.add(mk(t, 'tuft', null));
    }
    ear.userData.x = x; ear.userData.es = es; ear.userData.base = ear.rotation.clone(); earGroups.push(ear);
    head.add(ear);
  }
  // muzzle, nose, eyes, blush, whisker pads
  {
    const flat = s.faceFlat;
    const mz = ellipsoid(HS * .4, 1.2 * s.muzzleW * (1 - s.wedge * .25), .72 * (.75 + .25 * s.muzzleW), .7 * s.muzzleLen * (1 - flat * .5) * (1 + s.wedge * .25), 48);
    mz.translate(0, -HS * .3 + flat * HS * .06, HS * (.72 - flat * .08));
    head.add(mk(mz, 'muzzle', 'headDir', bodyMat));
    const ng = ellipsoid(HS * .072, 1.3, .78, .6, 20); ng.translate(0, -HS * .14 + flat * HS * .1, HS * (.97 - flat * .14) * Math.min(1, s.muzzleLen * .1 + .9));
    head.add(mk(ng, 'nose', null, MAT_GLOSS));
    // tongue (shown while grooming): a small pink tongue tip just under the mouth
    const tg = ellipsoid(HS * .07, 1, .4, 1.1, 20); tg.translate(0, -HS * .06, HS * .05);
    const tongue = new THREE.Group(); tongue.position.set(0, -HS * .2 + flat * HS * .1, HS * (.88 - flat * .14)); head.add(tongue);
    tongue.add(mk(tg, 'tongue', null, MAT_GLOSS)); tongue.scale.setScalar(.001); rig.tongue = tongue;
    // mouth: a tiny 'w' under the nose
    for (const x of [-1, 1]) {
      const m = new THREE.TorusGeometry(HS * .055, HS * .014, 8, 20, Math.PI);
      m.rotateZ(Math.PI); m.translate(x * HS * .055, -HS * .2 + flat * HS * .1, HS * (.93 - flat * .14));
      head.add(mk(m, 'mouth', null, MAT_GLOSS));
    }
    // eyes: the same glossy AC eye everywhere, but each breed sets its shape and expression
    //   eyeShape  round (Persian, British) | oval (Maine Coon, Ragdoll) | almond (Siamese, Abyssinian) | lemon (Sphynx)
    //   eyeTilt   outer corner raised (radians)        eyeLid   upper lid in coat colour -> hooded / calm look
    //   eyeLiner  dark rim (chinchilla, Mau, Abyssinian) eyeSpace wide-set (Persian) .. close-set (Siamese)
    const [sx, sy, sharp] = { round: [.84, 1.04, 0], oval: [.9, .86, .08], almond: [1, .72, .32], lemon: [1.04, .76, .48] }[s.eyeShape] || [.84, 1.04, 0];
    const sz = .42;
    for (const x of [-1, 1]) {
      const er = HS * .2 * s.eyeSize * (1 + .55 * (1.04 - sy));   // narrow eyes get wider/taller overall: same cuteness, different shape
      const furPush = s.fur === 'curly' ? .03 : s.fur === 'long' ? .02 : 0;      // keep eyes in front of the fur texture
      const ex = x * HS * .37 * s.eyeSpace, ey = HS * .02, ez = HS * (.84 - flat * .05 + furPush);
      const eye = new THREE.Group(); eye.position.set(ex, ey, ez); eye.rotation.z = x * s.eyeTilt; head.add(eye);
      eye.userData.discs = []; rig.eyes.push(eye);
      const pinch = g => {                    // almond / lemon: pinch the inner and outer corners
        const q = g.attributes.position;
        for (let i = 0; i < q.count; i++) { const u = q.getX(i) / (er * sx * 1.1); q.setY(i, q.getY(i) * (1 - sharp * u * u)); }
        g.computeVertexNormals(); return g;
      };
      eye.add(mk(pinch(ellipsoid(er, sx, sy, sz, 48)), 'eye', { cy: 0, er: er * sy, side: x }, MAT_GLOSS));
      // upper lid: a cap of the eye shape in coat colour, cut by a straight lid line
      const lidY = 1 - 2 * s.eyeLid;
      if (s.eyeLid > 0) {
        const lg = new THREE.SphereGeometry(er * 1.06, 40, 18, 0, Math.PI * 2, 0, Math.acos(lidY));
        lg.scale(sx, sy, sz * 1.3); pinch(lg);
        // keep the lid line level even on a slanted eye: following the slant drops the inner corner (a frown),
        // over-correcting lifts it (a worried look)
        lg.rotateZ(-x * s.eyeTilt);
        eye.add(mk(lg, 'head', 'headDir', bodyMat));
      }
      if (s.eyeLiner) {
        const lr = new THREE.TorusGeometry(er, er * .085, 8, 64); lr.scale(sx * 1.03, sy * 1.03, .6);
        eye.add(mk(pinch(lr), 'liner', null, MAT_GLOSS));
      }
      // highlights: flat discs on the eye surface; same light direction (upper-left) for both eyes,
      // so the offsets are given in head space and rotated into the tilted eye; kept below the lid
      const cs = Math.cos(-x * s.eyeTilt), sn = Math.sin(-x * s.eyeTilt), hsz = Math.min(1, sy + .1);
      for (const [dx, dy, rr] of [[-.3, .34, .3], [.3, -.34, .12]]) {
        let hx = (dx * cs - dy * sn) * er * sx, hy = (dx * sn + dy * cs) * er * sy;
        hy = Math.min(hy, (lidY * sy - .26) * er);
        const y0 = hy / (1 - sharp * (hx / (er * sx * 1.1)) ** 2);
        const k = 1 - (hx / (er * sx)) ** 2 - (y0 / (er * sy)) ** 2;
        const hz = er * sz * Math.sqrt(Math.max(0, k)) + er * .015;
        const disc = new THREE.Mesh(new THREE.CircleGeometry(er * rr * hsz, 24), new THREE.MeshBasicMaterial({ color: '#ffffff' }));
        disc.position.set(hx, hy, hz);
        disc.quaternion.setFromUnitVectors(new THREE.Vector3(0, 0, 1), new THREE.Vector3(hx / sx ** 2, y0 / sy ** 2, hz / sz ** 2).normalize());
        eye.add(disc); eye.userData.discs.push(disc);
      }
      // whiskers: three per side from the whisker pad; curly for rex/curly coats, none on a Sphynx
      if (s.whisker !== 'none') for (let w = 0; w < 3; w++) {
        const pts = [], a = (w - 1) * .22, curly = s.whisker === 'curly', L = HS * (curly ? .6 : .85);
        for (let k = 0; k <= 16; k++) {
          const t = k / 16, wob = curly ? Math.sin(t * 9) * HS * .05 * t : 0;
          pts.push(new THREE.Vector3(x * (HS * .28 + t * L), -HS * (.24 - Math.sin(a) * .6 * t) - t * t * HS * .12 + wob, HS * (.86 - flat * .14) - t * HS * .35));
        }
        const wg = new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 24, HS * .012, 6, false)   // bold enough to read at full-body size;
        head.add(mk(wg, 'whisker', null, MAT_GLOSS));
      }
      const bl = new THREE.Mesh(new THREE.SphereGeometry(HS * .13, 16, 12), new THREE.MeshStandardMaterial({ color: '#f7a3b6', roughness: 1, transparent: true, opacity: .6 }));
      bl.scale.set(1.35, .55, .3); bl.position.set(x * HS * .62, -HS * .2, HS * .72); head.add(bl);
    }
  }

  // ---------- long / curly fur: tufts where cats actually carry long hair
  // (cartoon cats are drawn the same way: a zigzag chest bib, cheek flicks, trousers, a plumed tail)
  if (s.fur === 'long' || s.fur === 'curly') {
    const curly = s.fur === 'curly', amt = curly ? .8 : s.furAmount;
    const ruff = Math.max(s.ruff, curly ? .7 : .4) * amt;
    const curl = curly ? 2.6 : 0, V = (x, y, z) => new THREE.Vector3(x, y, z);
    const T = (len, r, bend = .3) => tuftGeo(len, r, bend, curl);
    // chest bib: a row of tufts hanging from the front of the collar, longest in the middle
    // (wide, overlapping, lying on the collar: a scalloped bib, not a row of spikes)
    for (let j = -2; j <= 2; j++) {
      const f = j / 2, len = (.22 - .06 * Math.abs(f)) * ruff;
      placeTuft(chest, T(len, .12, .6), V(f * .19 * B * fluffy, .02 - .04 * Math.abs(f), .78 * BL + .04 * (1 - Math.abs(f))),
        V(f * .45, -1, .55), V(0, 0, 1), 'body', 'bodyNorm', bodyMat);
    }
    // belly fringe and 'trousers' on the back of the thighs
    for (const x of [-1, 1]) {
      for (const z of [-.25, .02, .28]) placeTuft(z > 0 ? chest : hip, T(.14 * amt, .07, .3), V(x * .15 * B * fluffy, -.2 * B * fluffy, z * BL), V(x * .25, -1, -.2), V(0, 0, -1), 'body', 'bodyNorm', bodyMat);
      const thigh = rig.legs[x < 0 ? 'hindL' : 'hindR'].chain[0];
      for (const [y, k] of [[-.35, 1], [-.65, .8]]) placeTuft(thigh, T(.17 * amt * k, .075, .35), V(x * .03, hTh * y, -.09), V(x * .2, -.5, -1), V(0, -1, 0), 'body', 'bodyNorm', bodyMat);
    }
    // cheek flicks: three per side from the lower cheek, pointing out and down
    const ff = (curly ? .9 : s.faceFluff) * amt;
    for (const x of [-1, 1]) for (const [dy, dz, k] of [[-.18, .42, 1], [-.42, .32, .85], [-.64, .2, .65]]) {
      const d = V(x * .88, dy, dz).normalize();
      const at = V(d.x * HS * s.headW, d.y * HS * .9, d.z * HS * .9).multiplyScalar(.93);
      placeTuft(head, T(HS * .4 * ff * k, HS * .17, .25), at, V(x, -.35 - .5 * (1 - k), .15), V(0, -1, 0), 'head', 'headDir', bodyMat);
    }
    // feathered tail: tufts on both sides of the outer tail segments
    for (let i = 3; i < rig.tail.length; i++) for (const x of [-1, 1]) {
      const k = (i - 2) / (rig.tail.length - 2);
      placeTuft(rig.tail[i], T((.1 + .1 * k) * Math.max(.6, s.tailFluff), .075, .5), V(x * .04, .05, 0), V(x, .7, .1), V(0, 1, 0), 'tail', { t0: (i + .5) / rig.tail.length, t1: (i + .5) / rig.tail.length, len: 0 }, bodyMat);
    }
    // toe tufts ('snowshoes', Maine Coon / Norwegian Forest): fur sticking out between the toes
    if (s.pawTuft) for (const name of ['frontL', 'frontR', 'hindL', 'hindR']) {
      const paw = rig.legs[name].chain.at(-1);
      for (const f of [-1, 0, 1]) placeTuft(paw, T(pawR * .9 * s.pawTuft, pawR * .3, .2), V(f * pawR * .55, -pawR * .25, pawR * 1.25), V(f * .3, -.25, 1), V(0, -1, 0), 'leg', { kind: 'front', t0: 1, t1: 1, len: 0 }, bodyMat);
    }
    // ear furnishings: pale wisps from the inner ear
    if (!curly && s.ear !== 'fold') for (const ear of earGroups) placeTuft(ear, tuftGeo(.13 * ear.userData.es, .025 * ear.userData.es, .4, 0), V(0, .02, .03), V(-ear.userData.x * .25, 1, .55), V(0, 0, 1), 'furnish', null);
  }

  // ---------- paint every mesh in rest pose, using root-space positions
  root.updateMatrixWorld(true);
  rig.torso.bind(new THREE.Skeleton([chest, hip]));
  const inv = new THREE.Matrix4().copy(root.matrixWorld).invert();
  const headInv = new THREE.Matrix4().copy(head.matrixWorld).invert();
  const v = new THREE.Vector3(), w = new THREE.Vector3();
  root.traverse(o => {
    if (!o.isMesh || !o.userData.part) return;
    const part = ['tuft', 'furnish', 'liner', 'whisker', 'tongue'].includes(o.userData.part) ? 'ear' : o.userData.part;
    const pos = o.geometry.attributes.position, col = new Float32Array(pos.count * 3);
    const fl = o.geometry.attributes.fluff;
    const m = new THREE.Matrix4().multiplyMatrices(inv, o.matrixWorld);
    const mh = new THREE.Matrix4().multiplyMatrices(headInv, o.matrixWorld);
    for (let i = 0; i < pos.count; i++) {
      v.fromBufferAttribute(pos, i);
      const p = w.copy(v).applyMatrix4(m).clone();
      let info = o.userData.info, c;
      if (o.userData.part === 'tuft') c = colorAt('ear', p, { dir: new THREE.Vector3(0, 1, 0) }).multiplyScalar(.55);
      else if (o.userData.part === 'furnish') c = C('#f5efe4');
      else if (o.userData.part === 'liner') c = C('#2b2220');
      else if (o.userData.part === 'tongue') c = C('#ef8c98');
      else if (o.userData.part === 'whisker') c = C(coatSpec.whiskerColor || '#fbf8f2');
      else if (o.userData.part === 'mouth') c = C('#3a2a24');
      else if (info && info.ear) {
        const y = (v.y - info.y0) / info.h, rr = Math.max(1e-4, info.r * (1 - y));
        const outer = colorAt('ear', p, { dir: v.clone().applyMatrix4(mh).normalize() });
        const w = sstep(.62, .82, v.z / rr) * sstep(.55, .38, Math.abs(v.x) / rr) * sstep(.28, .38, y) * sstep(.78, .66, y);
        c = outer.lerp(colorAt('innerEar'), .8 * w);
        if (coatSpec.earSpot) {   // ocelli: white thumbprint on the back of the ear (Savannah / serval)
          const b = sstep(-.45, -.75, v.z / rr) * sstep(.45, .2, Math.abs(v.x) / rr) * sstep(.25, .35, y) * sstep(.62, .5, y);
          c.lerp(C('#f4efe4'), .9 * b);
        }
      }
      else if (o.userData.part === 'eye') {
        const t = (v.y - info.cy) / info.er;
        c = C('#271c18').lerp(colorAt(info.side > 0 ? 'eye2' : 'eye'), .72 * sstep(.1, -.85, t));   // breed eye colour fills the lower half
      } else {
        if (info === 'bodyNorm') info = bodyNorm(p);
        else if (info === 'headDir') info = { dir: v.clone().applyMatrix4(mh).normalize() };
        else if (info && info.len !== undefined) {
          const along = Math.min(1, Math.max(0, part === 'tail' ? v.y / (info.len || 1) : -v.y / (info.len || 1)));
          info = { ...info, t: info.t0 + (info.t1 - info.t0) * along };
        }
        c = colorAt(part, p, info);
      }
      if (fl) c.multiplyScalar(.8 + .3 * fl.getX(i));   // tuft tips lighter, gaps between clumps darker
      col.set([c.r, c.g, c.b], i * 3);
    }
    o.geometry.setAttribute('color', new THREE.BufferAttribute(col, 3));
  });

  root.scale.setScalar(s.size);
  root.userData.rig = rig;
  root.userData.shape = s;
  setPose(root, 'stand', 0);
  // where each paw rests when standing (root space): jump/landing spots are planted relative to these
  rig.restFeet = Object.fromEntries(Object.entries(rig.legs).map(([n, leg]) => { const p = root.worldToLocal(leg.chain.at(-1).getWorldPosition(new THREE.Vector3())); return [n, p]; }));
  return root;
}

// ------------------------------------------------------------------ poses / motion
// walk: lateral-sequence gait (LH, LF, RH, RF), phase 0..1
// ------------------------------------------------------------------ poses & motion
// Paws are placed with two-bone IK, so the legs bend the way a real cat's do:
//   front leg: elbow behind (upper arm down-back, forearm down-forward), paw flat on the ground
//   hind leg:  knee (stifle) forward, hock (heel) back, metatarsus near-vertical; the hock angle is set
//              per pose (flat on the ground when sitting or loafing)
// setPose(cat, mode, phase, opts)
//   mode  'stand' | 'walk' | 'run' | 'jump' | 'sit' | 'loaf' | 'groom' | 'sleep' | 'stretch' | 'flop' | 'paw' | 'look'
//   opts.jump = { dist, up }: how far forward / how high the jump lands (root units)
//   phase 0..1 inside the motion's loop (gait cycle, breath, lick ...)
//   opts.from = { mode, phase } and opts.blend 0..1: cross-fade between two poses (stand -> sit, ...)
// The same joint maths ports to Unity as a small procedural animator (or baked into clips).
const TAU = Math.PI * 2;
const easeIO = t => t * t * (3 - 2 * t);
const clamp = (x, a, b) => Math.min(b, Math.max(a, x));
const lerpN = (a, b, t) => a + (b - a) * t;
const _v = new THREE.Vector3(), _q = new THREE.Quaternion(), _q2 = new THREE.Quaternion(), _qx = new THREE.Quaternion();
const AX = new THREE.Vector3(1, 0, 0);
const pulse = (t, a, b) => sstep(a, (a + b) / 2, t) * sstep(b, (a + b) / 2, t);   // 0 -> 1 -> 0 between a and b

// angles of a two-bone chain hanging from its pivot so its end reaches (ty, tz) in the pivot frame.
// rotation.x = +a swings a hanging bone backwards (-z). knee < 0: middle joint behind (elbow); > 0: in front (stifle).
function twoBone(ty, tz, L1, L2, knee) {
  const D = clamp(Math.hypot(ty, tz), Math.abs(L1 - L2) + 1e-3, L1 + L2 - 1e-4);
  const phi = Math.atan2(-tz, -ty);
  const al = Math.acos(clamp((L1 * L1 + D * D - L2 * L2) / (2 * L1 * D), -1, 1));
  const be = Math.PI - Math.acos(clamp((L1 * L1 + L2 * L2 - D * D) / (2 * L1 * L2), -1, 1));
  return knee < 0 ? [phi + al, -be] : [phi - al, be];
}

// place one leg's paw joint at `target` (cat root space). o.hock: hind metatarsus angle in the leg-top frame
// (+ back, - forward, -1.5 = lying flat forward); o.flex: toe/wrist curl; o.az: sideways spread
function solveLeg(cat, leg, target, o = {}) {
  const d = cat.userData.rig.dims, { top, chain, kind } = leg;
  top.updateWorldMatrix(true, false);
  const loc = top.worldToLocal(cat.localToWorld(_v.copy(target)));
  const paw = chain.at(-1);
  chain[0].rotation.z = o.az || 0;
  if (kind === 'front') {
    const [a, b] = twoBone(loc.y, loc.z, d.fUp, d.fLo, -1);
    chain[0].rotation.x = a; chain[1].rotation.x = b;
  } else {
    const psi = o.hock ?? .12;
    const [a, b] = twoBone(loc.y + d.hHo * Math.cos(psi), loc.z + d.hHo * Math.sin(psi), d.hTh, d.hSh, 1);
    chain[0].rotation.x = a; chain[1].rotation.x = b; chain[2].rotation.x = psi - a - b;
  }
  // paw pad flat to the ground (the cat's own up), then curled by `flex`
  paw.parent.updateWorldMatrix(true, false);
  paw.parent.getWorldQuaternion(_q); cat.getWorldQuaternion(_q2);
  paw.quaternion.copy(_q.invert().multiply(_q2)).multiply(_qx.setFromAxisAngle(AX, o.flex || 0));
}

function setEyes(r, closed) {
  for (const e of r.eyes) { e.scale.y = 1 - .9 * closed; for (const dd of e.userData.discs) dd.visible = closed < .45; }
}

function resetRig(cat) {
  const r = cat.userData.rig, d = r.dims;
  r.move.rotation.set(0, 0, 0); r.move.position.set(0, 0, 0);
  r.body.position.set(0, d.bodyY, 0); r.body.rotation.set(r.pitch || 0, 0, 0); r.body.scale.set(1, 1, 1);
  r.chest.rotation.set(0, 0, 0); r.hip.rotation.set(0, 0, 0);
  r.neck.rotation.set(-.15 - (r.pitch || 0), 0, 0);       // the neck cancels the body pitch: head stays level
  r.head.rotation.set(.12, 0, 0);
  for (const e of r.ears) e.rotation.copy(e.userData.base);
  setEyes(r, 0); r.tongue.scale.setScalar(.001);
  for (const leg of Object.values(r.legs)) for (const j of leg.chain) j.rotation.set(0, 0, 0);
}

// tail: base angle (more negative = higher), bend per joint, sideways curl per joint, wave amplitude/phase
function poseTail(cat, base, bend, curl, wave = 0, ph = 0) {
  const r = cat.userData.rig, s = cat.userData.shape, n = r.tail.length, k0 = s.tail === 'bob' ? 0 : 1;
  r.tail.forEach((j, i) => {
    const t = i / Math.max(1, n - 1);
    j.rotation.x = i === 0 ? base : bend * k0 * (t < .7 ? 1 : 1.8);
    j.rotation.z = (curl + wave * t * Math.sin(ph * TAU - t * 2.6)) * k0;
  });
}

const rootPos = (cat, obj) => cat.worldToLocal(obj.getWorldPosition(new THREE.Vector3()));

function posePure(cat, mode, phase) {
  const r = cat.userData.rig, d = r.dims, s = cat.userData.shape;
  resetRig(cat);
  const legs = r.legs, P = d.pawR * .9, L = d.L;
  const breath = Math.sin(phase * TAU);
  const blink = pulse(phase, .9, .97);
  const feet = {};                                    // name -> [target, opts]
  const under = (name, dz = 0, dy = 0, o = {}) => { const t = rootPos(cat, legs[name].top); feet[name] = [new THREE.Vector3(t.x, P + dy, t.z + dz), o]; };
  const sideways = (name, k) => { feet[name][0].x *= k; };

  if (mode === 'stand' || mode === 'look' || mode === 'paw') {
    r.body.scale.set(1 + .006 * breath, 1 + .01 * breath, 1);
    setEyes(r, blink);
    if (mode === 'look') { r.head.rotation.y = -.5; r.neck.rotation.y = -.15; }
    poseTail(cat, -1.45, .16, 0, .12, phase);
    cat.updateMatrixWorld(true);
    for (const n of ['frontL', 'frontR']) under(n, .02);
    for (const n of ['hindL', 'hindR']) under(n, .03, 0, { hock: .12 });
    if (mode === 'paw') {
      // bat at the water: lift (0-.4), tap down into the bowl (.4-.55), back to the ground (.55-1)
      const up = phase < .4 ? easeIO(phase / .4) : phase < .55 ? 1 - easeIO((phase - .4) / .15) : 0;
      const fwd = phase < .55 ? easeIO(Math.min(1, phase / .45)) : 1 - easeIO((phase - .55) / .45);
      const [t] = feet.frontR;
      t.y = P + up * .42 * L + (phase > .4 && phase < .6 ? .02 : 0); t.z += fwd * .32 * L; t.x *= .85;
      feet.frontR[1] = { flex: .9 * up };
      r.head.rotation.x = .12 + .32 * Math.max(up, fwd * .8); r.head.rotation.y = -.1 * fwd;
      r.body.rotation.x += -.05 * up;                   // shifts weight back a little
      poseTail(cat, -1.4, .16, 0, .2, phase * 2);
    }
  } else if (mode === 'walk') {
    // lateral-sequence walk: hind-left, fore-left, hind-right, fore-right; each foot is planted for 62% of
    // the cycle and slides back under the body (stance), then lifts, curls and swings forward
    const offs = { hindL: 0, frontL: .25, hindR: .5, frontR: .75 }, duty = .62, S = .5 * L;
    r.body.position.y -= .012 * (.5 - .5 * Math.cos(phase * TAU * 2));          // dips twice per cycle
    r.body.rotation.z = .025 * Math.sin(phase * TAU);                              // gentle roll
    r.chest.rotation.y = .06 * Math.sin(phase * TAU);                              // shoulders and hips sway
    r.hip.rotation.y = -.06 * Math.sin(phase * TAU + .5);                          // in opposite directions
    r.neck.rotation.y = -.05 * Math.sin(phase * TAU);                              // head stays steady
    r.head.rotation.x = .12 + .025 * Math.cos(phase * TAU * 2);
    poseTail(cat, -1.35, .14, 0, .16, phase);
    cat.updateMatrixWorld(true);
    for (const [name, leg] of Object.entries(legs)) {
      const ph = (phase + offs[name]) % 1, fr = leg.kind === 'front', lift = (fr ? .13 : .1) * L;
      let dz, dy = 0, o;
      if (ph < duty) { const u = ph / duty; dz = S / 2 - S * u; o = fr ? { flex: 0 } : { hock: .08 + .4 * u }; }
      else {
        const u = (ph - duty) / (1 - duty), arc = Math.sin(Math.PI * u);
        dz = -S / 2 + S * easeIO(u); dy = lift * arc;
        o = fr ? { flex: 1.25 * arc } : { hock: .48 + .45 * arc - .4 * u, flex: .5 * arc };
      }
      under(name, dz + (fr ? .03 : .02), dy, o);
    }
  } else if (mode === 'sit' || mode === 'groom') {
    // haunches on the ground, front legs straight, chest up, tail wrapped round the paws
    r.body.rotation.x = (r.pitch || 0) - .62;
    r.chest.rotation.x = -.12; r.hip.rotation.x = -.22;
    r.body.position.y = d.bodyY * .72 + .03;
    r.neck.rotation.x = -.15 - (r.pitch || 0) + .62;
    r.body.scale.set(1 + .006 * breath, 1 + .01 * breath, 1);
    poseTail(cat, -.35, -.02, -.36, mode === 'sit' ? .05 : 0, phase * 2);   // down to the ground, round the front paws; tip twitches
    setEyes(r, mode === 'groom' ? .75 : blink);
    cat.updateMatrixWorld(true);
    for (const n of ['frontL', 'frontR']) under(n, .04);
    for (const n of ['hindL', 'hindR']) { under(n, .32 * L, 0, { hock: -1.45 }); sideways(n, 1.25); }
    if (mode === 'groom') {
      // licks the right paw: paw raised to the mouth, head tilted to it, little licking nods
      const lick = .5 + .5 * Math.sin(phase * TAU * 3);
      r.head.rotation.set(.32 + .1 * lick, .32, .16);
      r.tongue.scale.setScalar(.001 + lick);
      cat.updateMatrixWorld(true);
      const mouth = rootPos(cat, r.tongue);
      feet.frontR = [mouth.add(new THREE.Vector3(.04, -.07, .12)), { flex: 1.1 }];   // paw held up in front of the mouth
    }
  } else if (mode === 'loaf' || mode === 'sleep') {
    // 'loaf': belly down, paws tucked away, eyes half shut. 'sleep': curled into a doughnut, head to tail
    const curl = mode === 'sleep';
    r.body.position.y = .3 * d.B * d.fluffy + .02;
    r.body.rotation.x = 0;
    r.neck.rotation.x = -.15 + (curl ? .35 : .08);
    r.body.scale.set(1 + .012 * breath, 1 + .02 * breath, 1);
    if (curl) {
      // body curled into a C with the tail round the front; the chin rests on the paws, face to the viewer
      // (a big toy head turned towards the tail would only show the back of the head)
      r.body.rotation.z = .22;
      r.chest.rotation.y = .38; r.hip.rotation.y = -.5;
      r.neck.rotation.set(-.15 + .6, .12, 0); r.head.rotation.set(-.32, .12, .3);   // neck lowered, face level
      setEyes(r, 1);
      poseTail(cat, -1.72, -.02, -.44, 0);             // wrapped round to the front paws
      for (const e of r.ears) e.rotation.x = e.userData.base.x - .25;    // ears relaxed back
    } else { setEyes(r, .55 + .45 * blink); poseTail(cat, -1.72, -.02, -.3, 0); }   // lying along its side
    cat.updateMatrixWorld(true);
    for (const n of ['frontL', 'frontR']) under(n, -.1, -P * .25, { flex: 1.3 });
    for (const n of ['hindL', 'hindR']) { under(n, .2 * L, -P * .25, { hock: -1.5 }); sideways(n, 1.1); }
  } else if (mode === 'stretch') {
    // play-bow stretch: front legs reach forward along the ground, chest low, rump and tail high
    r.body.rotation.x = (r.pitch || 0) + .42;
    r.chest.rotation.x = .2; r.hip.rotation.x = -.08;
    r.body.position.y = d.bodyY * .78;
    r.neck.rotation.x = -.15 - (r.pitch || 0) - .5;
    r.head.rotation.x = -.05;
    setEyes(r, .85);
    poseTail(cat, -2.1, .12, 0, .1, phase);
    cat.updateMatrixWorld(true);
    for (const n of ['frontL', 'frontR']) under(n, .8 * L, 0, { flex: -.2 });      // reach far forward
    for (const n of ['hindL', 'hindR']) under(n, -.04, 0, { hock: .2 });           // hind legs straight, rump up
  } else if (mode === 'run') {
    // rotary gallop (how cats run fast): hind-left, hind-right land close together, then fore-right, fore-left.
    // The spine does the work: it curls when the hind feet swing forward under the chest (gathered) and
    // stretches out when the front legs reach and the hind legs push back (extended). Two airborne moments:
    // one stretched out, one bunched up.
    const offs = { hindL: 0, hindR: .07, frontR: .42, frontL: .5 }, duty = .3, S = 1.15 * L;
    const flex = Math.cos(TAU * (phase - .97));                               // +1 gathered .. -1 extended
    r.chest.rotation.x = .17 * flex; r.hip.rotation.x = -.22 * flex;
    r.body.position.y += .045 * L * Math.cos(TAU * 2 * (phase - .35)) - .02 * L;   // highest in both flights
    const dp = .09 * Math.sin(TAU * (phase - .3));                            // nose dips while the fronts are down
    r.body.rotation.x += dp; r.body.rotation.z = .02 * Math.sin(TAU * phase);
    r.neck.rotation.x -= dp + .17 * flex * .6; r.head.rotation.x = .05;     // head stays level, eyes on the target
    for (const e of r.ears) e.rotation.x = e.userData.base.x - .4;          // ears swept back
    poseTail(cat, -1.62, .02, 0, .1, phase + .2);                            // tail straight back: counterbalance
    cat.updateMatrixWorld(true);
    for (const [name, leg] of Object.entries(legs)) {
      const ph = ((phase - offs[name]) % 1 + 1) % 1, fr = leg.kind === 'front';
      let dz, dy = 0, o;
      if (ph < duty) { const u = ph / duty; dz = S * (.5 - u); o = fr ? { flex: -.15 * u } : { hock: .05 + .75 * u }; }   // push off
      else {
        const u = (ph - duty) / (1 - duty), arc = Math.sin(Math.PI * u);
        dz = -S / 2 + S * easeIO(u); dy = (fr ? .32 : .26) * L * Math.pow(arc, .8);
        o = fr ? { flex: 1.7 * arc } : { hock: .8 + .5 * arc - .75 * u, flex: .8 * arc };   // paws tucked in flight
      }
      under(name, dz + (fr ? .12 : .02) * L, dy, o);
    }
  } else if (mode === 'jump') {
    // a real cat's jump onto something (opts via setPose: dist forward, up = landing height):
    // crouch + bum wiggle (0-.27) -> hind legs fire, front lifts (.27-.36) -> flight, body arcs from nose-up to
    // nose-down, front legs reach for the landing (.36-.64) -> front paws land, hind paws follow (.64-.74)
    // -> absorb in a crouch (.74-.9) -> stand
    const o = r.jumpOpts || {}, D = o.dist ?? 1.5 * d.BL / .82, U = o.up ?? 0, t = phase;
    const seg = (a, b) => clamp((t - a) / (b - a), 0, 1);
    const fly = seg(.3, .68), Hx = .32 + U * .35;
    r.move.position.set(0, U * fly + 4 * Hx * fly * (1 - fly), D * fly);
    const crouch = easeIO(seg(0, .12)) * (1 - easeIO(seg(.27, .33))) + .7 * easeIO(seg(.68, .76)) * (1 - easeIO(seg(.8, .92)));
    r.body.position.y = d.bodyY * (1 - .3 * crouch);
    const pitch = t < .27 ? .1 * crouch : t < .36 ? -.55 * easeIO(seg(.27, .33)) : t < .64 ? -.55 + .9 * easeIO(seg(.36, .64)) : .35 * (1 - easeIO(seg(.66, .9)));
    r.body.rotation.x = (r.pitch || 0) + pitch;
    const sp = t < .36 ? -easeIO(seg(.27, .33)) : t < .64 ? -1 + 1.5 * Math.sin(Math.PI * seg(.36, .64)) : -.5 * (1 - seg(.64, .8));   // spine: - stretched, + tucked
    r.chest.rotation.x = .12 * sp; r.hip.rotation.x = -.16 * sp;
    const wig = Math.sin(TAU * 6 * t) * sstep(.06, .12, t) * (1 - sstep(.22, .27, t));
    r.hip.rotation.y = .14 * wig;                                                     // the famous bum wiggle
    r.neck.rotation.x = -.15 - (r.pitch || 0) - pitch * .8 + .25 * crouch;            // eyes fixed on the target
    for (const e of r.ears) e.rotation.x = e.userData.base.x + (t < .3 ? .12 : -.3 * Math.sin(Math.PI * fly));
    // crouched: tail low behind, only the tip twitching; in the air it lifts as a counterweight/rudder
    poseTail(cat, t < .3 ? -1.68 : -1.55 - .45 * Math.sin(Math.PI * fly), t < .3 ? .02 : -.02, 0, t < .3 ? .3 : .06, t * 4);
    cat.updateMatrixWorld(true);
    // planted paws stay put on the ground (launch spot, then landing spot) while the body moves
    // (root space: `move` carries the body through the air, the ground spots stay where they are)
    const planted = (name, land) => { const f = r.restFeet[name]; return new THREE.Vector3(f.x, P + (land ? U : 0), f.z + (land ? D : 0)); };
    for (const [name, leg] of Object.entries(legs)) {
      const fr = leg.kind === 'front', step = !fr ? .02 * Math.max(0, Math.sin(TAU * 6 * t + (leg.side > 0 ? 0 : Math.PI))) * (wig ? 1 : 0) : 0;
      const liftOff = fr ? .28 : .34, touch = fr ? .64 : .73;
      if (t < liftOff) { const p = planted(name, false); if (fr) p.z += .06 * crouch; p.y += step; feet[name] = [p, fr ? {} : { hock: .1 - .35 * crouch }]; }
      else if (t >= touch) feet[name] = [planted(name, true), fr ? {} : { hock: .1 - .35 * crouch }];
      else {
        const u = (t - liftOff) / (touch - liftOff), top = rootPos(cat, leg.top);
        // fronts: tuck to the chest, then reach forward-down for the landing; hinds: trail stretched, then tuck under
        const rel = fr ? new THREE.Vector3(top.x, top.y - lerpN(.35, .55, u) * L - .1 * L * Math.sin(Math.PI * u), top.z + lerpN(.15, .5, u) * L)
                       : new THREE.Vector3(top.x, top.y - lerpN(.85, .55, Math.sin(Math.PI * u)) * L * .8, top.z + lerpN(-.5, .15, easeIO(u)) * L);
        const k = sstep(.7, 1, u);                                                   // home in on the landing spot
        feet[name] = [rel.lerp(planted(name, true), k), fr ? { flex: 1.4 * (1 - u) - .1 } : { hock: .9 - .7 * u, flex: .5 * (1 - u) }];
      }
    }
  } else if (mode === 'flop') {
    // lying on its side, fully relaxed (Ragdoll); limbs loose, upper legs draped towards the ground
    r.move.rotation.z = 1.42; r.move.position.y = .3 * s.bodyBulk * (s.fur === 'long' ? 1.12 : 1);
    r.body.scale.set(1 + .018 * breath, 1 + .025 * breath, 1);
    r.head.rotation.set(-.1, 0, -.35 + .02 * breath); r.hip.rotation.y = -.12;
    setEyes(r, .6);
    r.tail.forEach((j, i) => { const t = i / (r.tail.length - 1); j.rotation.x = i === 0 ? -1.75 : .04; j.rotation.z = t > .6 ? Math.sin(phase * TAU * 2 - t * 3) * .25 * t : 0; });
    for (const [name, leg] of Object.entries(legs)) {
      const [a, b, c, dd] = leg.chain, fr = leg.kind === 'front', lim = Math.sin(phase * TAU + (fr ? 0 : 1)) * .03;
      a.rotation.z = leg.side > 0 ? -.75 : -.15;
      if (fr) { a.rotation.x = .35 + lim; b.rotation.x = -.45; c.rotation.x = .5; }
      else { a.rotation.x = -.25 + lim; b.rotation.x = .7; c.rotation.x = -.35; dd.rotation.x = .3; }
    }
  }
  if (Object.keys(feet).length) {
    cat.updateMatrixWorld(true);
    for (const [name, [t, o]] of Object.entries(feet)) solveLeg(cat, legs[name], t, o);
  }
}

// every animated value, in a fixed order, for cross-fading two poses
function rigChannels(cat) {
  const r = cat.userData.rig, out = [];
  const rot = (o, axes = 'xyz') => { for (const a of axes) out.push([o.rotation, a]); };
  out.push([r.move.position, 'y'], [r.move.position, 'z']); rot(r.move, 'z');
  out.push([r.body.position, 'y'], [r.body.scale, 'x'], [r.body.scale, 'y']); rot(r.body, 'xz');
  rot(r.chest); rot(r.hip); rot(r.neck); rot(r.head);
  for (const j of r.tail) rot(j, 'xz');
  for (const leg of Object.values(r.legs)) for (const j of leg.chain) rot(j);
  for (const e of r.ears) rot(e);
  for (const e of r.eyes) out.push([e.scale, 'y']);
  out.push([r.tongue.scale, 'x'], [r.tongue.scale, 'y'], [r.tongue.scale, 'z']);
  return out;
}

export function setPose(cat, mode = 'stand', phase = 0, opts = {}) {
  cat.userData.rig.jumpOpts = opts.jump;   // { dist, up } for 'jump'
  let from = opts.from;
  if (!from && mode === 'flop' && opts.blend != null) from = { mode: 'stand', phase: 0 };
  if (!from) { posePure(cat, mode, phase); return; }
  const ch = rigChannels(cat), k = easeIO(clamp(opts.blend ?? 1, 0, 1));
  posePure(cat, from.mode, from.phase ?? 0); const A = ch.map(([o, a]) => o[a]);
  posePure(cat, mode, phase); const B = ch.map(([o, a]) => o[a]);
  ch.forEach(([o, a], i) => { o[a] = A[i] + (B[i] - A[i]) * k; });
  const r = cat.userData.rig;
  for (const e of r.eyes) for (const dd of e.userData.discs) dd.visible = e.scale.y > .55;
}

// ------------------------------------------------------------------ catalogue
const SH = (o) => o;
export const BREEDS = [
  { id: 'korean_shorthair', ko: '코리안 숏헤어', shape: { earSet: 'mid', eyeShape: 'round', eyeTilt: .06 }, coat: { pattern: 'mackerel', base: '#a39d93', dark: '#57514a' } },
  { id: 'russian_blue', ko: '러시안 블루', shape: { eyeShape: 'oval', earSize: 1.12, bodyBulk: .92, legLen: 1.08, wedge: .35, earSet: 'high', neckLen: 1.1, tailThick: .85, eyeTilt: .12, eyeSpace: 1.06 }, coat: { pattern: 'solid', base: '#8794a3', eye: '#5fae6a', lightMuzzle: false, nose: '#7d8794', sheen: .45, silverTip: 1 } },
  { id: 'persian', ko: '페르시안', shape: { faceFlat: 1, ear: 'small', fur: 'long', ruff: 1, tailFluff: 1, cheek: 1, eyeSize: 1.15, legLen: .78, bodyBulk: 1.15, earSet: 'low', earRound: .8, bodyLen: .82, neckLen: .75, tailThick: 1.2, tailLen: .85, faceFluff: 1.2, eyeShape: 'round', eyeSpace: 1.1 }, coat: { pattern: 'solid', base: '#f4efe6', eye: '#d7902f' } },
  { id: 'himalayan', ko: '히말라얀', shape: { faceFlat: 1, ear: 'small', fur: 'long', ruff: 1, tailFluff: 1, cheek: 1, legLen: .78, bodyBulk: 1.15, earSet: 'low', earRound: .8, bodyLen: .82, neckLen: .75, tailThick: 1.2, tailLen: .85, faceFluff: 1.2, eyeShape: 'round', eyeSpace: 1.1 }, coat: { pattern: 'point', base: '#f2e8d8', point: '#6b4f40', eye: '#4f8fd8' } },
  { id: 'chinchilla_persian', ko: '친칠라 페르시안', shape: { faceFlat: 1, ear: 'small', fur: 'long', ruff: 1.1, tailFluff: 1.1, cheek: 1, eyeSize: 1.18, legLen: .78, bodyBulk: 1.12, earSet: 'low', earRound: .8, bodyLen: .82, neckLen: .75, tailThick: 1.2, tailLen: .85, faceFluff: 1.2, eyeShape: 'round', eyeSpace: 1.1, eyeLiner: 1 }, coat: { pattern: 'shaded', base: '#f7f5f0', white: '#f8f6f2', dark: '#55565e', eye: '#3fae6a', nose: '#e0857f' } },
  { id: 'siamese', ko: '샴', shape: { ear: 'large', earSize: 1.2, eyeShape: 'almond', bodyBulk: .78, legLen: 1.25, headW: .95, muzzleLen: 1.2, tailLen: 1.35, wedge: 1, earSet: 'low', earWide: 1.05, bodyLen: 1.2, tailThick: .65, neckLen: 1.2, tuck: .4, eyeTilt: .38, eyeSpace: .94, eyeLid: .08 }, coat: { pattern: 'point', base: '#f1e6d2', point: '#4f3a30', eye: '#5aa6e0' } },
  { id: 'scottish_fold', ko: '스코티시 폴드', shape: { ear: 'fold', eyeSize: 1.2, cheek: .6, bodyBulk: 1.05, earRound: .5, bodyLen: .92, tailThick: 1.1, eyeShape: 'round' }, coat: { pattern: 'mackerel', base: '#a9adb6', dark: '#686c74' } },
  { id: 'british_shorthair', ko: '브리티시 숏헤어', shape: { cheek: 1, ear: 'small', bodyBulk: 1.15, headW: 1.18, eyeSize: 1.12, legBulk: 1.15, earRound: 1, earSet: 'low', bodyLen: .85, legLen: .9, neckLen: .8, tailThick: 1.3, tailLen: .9, chest: 1.15, eyeShape: 'round', eyeSpace: 1.08 }, coat: { pattern: 'solid', base: '#8a92a0', eye: '#e19a2b', lightMuzzle: false } },
  { id: 'munchkin', ko: '먼치킨', shape: { legLen: .5, eyeShape: 'oval', eyeTilt: .1 }, coat: { pattern: 'classic', base: '#e8b878', dark: '#b4703a' } },
  { id: 'ragdoll', ko: '랙돌', shape: { fur: 'long', furAmount: .8, ruff: .8, tailFluff: 1, eyeSize: 1.1, bodyBulk: 1.1, size: 1.1, bodyLen: 1.12, tailThick: 1.1, eyeShape: 'oval', eyeTilt: .06 }, coat: { pattern: 'mitted', base: '#f3ece2', point: '#6b5a52', eye: '#4f8fd8' } },
  { id: 'birman', ko: '버먼', shape: { fur: 'long', furAmount: .7, ruff: .6, tailFluff: .8, bodyLen: 1.05, eyeShape: 'round', eyeTilt: .04, romanNose: .8 }, coat: { pattern: 'mitted', base: '#efe3cc', point: '#4e3a2f', eye: '#4f8fd8' } },
  { id: 'american_shorthair', ko: '아메리칸 숏헤어', shape: { cheek: .5, bodyBulk: 1.05, legBulk: 1.1, muzzleW: 1.2, chest: 1.1, tailThick: 1.1, eyeShape: 'oval', eyeTilt: .05, eyeLid: .1 }, coat: { pattern: 'classic', base: '#c9c9c4', dark: '#3d3d3d' } },
  { id: 'norwegian_forest', ko: '노르웨이 숲', shape: { fur: 'long', ruff: 1, tailFluff: 1.2, earTuft: 1, earSize: 1.1, size: 1.12, headTri: 1, earSet: 'high', rumpHigh: .12, eyeShape: 'almond', eyeTilt: .22, eyeLid: .12, pawTuft: .8 }, coat: { pattern: 'mackerel', base: '#b8a68e', dark: '#6e5b45', eye: '#9db24a', whiteLevel: .25 } },
  { id: 'maine_coon', ko: '메인쿤', shape: { fur: 'long', ruff: 1.2, tailFluff: 1.2, earTuft: 1, earSize: 1.25, bodyLen: 1.2, size: 1.22, muzzleLen: 1.2, muzzleW: 1.35, earSet: 'high', chest: 1.1, eyeShape: 'oval', eyeTilt: .18, eyeLid: .12, eyeSpace: 1.06, pawTuft: 1 }, coat: { pattern: 'classic', base: '#8a6a4f', dark: '#3e2d22', whiteLevel: .2 } },
  { id: 'siberian', ko: '시베리안', shape: { fur: 'long', ruff: 1.3, tailFluff: 1.2, cheek: .5, size: 1.1, earRound: .3, chest: 1.1, eyeShape: 'oval', eyeTilt: .12, pawTuft: .5, furAmount: 1.25 }, coat: { pattern: 'mackerel', base: '#c9c4bb', dark: '#5d5850', eye: '#9db24a' } },
  { id: 'bengal', ko: '벵갈', shape: { bodyBulk: .95, eyeShape: 'oval', legLen: 1.08, pouch: .5, bodyLen: 1.1, chest: 1.1, tailThick: 1.1, eyeTilt: .1, eyeLid: .22, eyeLiner: 1, earSize: .85, earRound: .4 }, coat: { pattern: 'spotted', base: '#d9a95c', dark: '#4a3322', eye: '#7cae3d', rosette: 1, rosetteFill: '#b0662a', sheen: .35, glitter: 1 } },
  { id: 'egyptian_mau', ko: '이집션 마우', shape: { bodyBulk: .9, legLen: 1.1, eyeSize: 1.15, pouch: 1, rumpHigh: .2, wedge: .25, earSet: 'high', eyeShape: 'round', eyeTilt: .14, eyeLiner: 1 }, coat: { pattern: 'spotted', base: '#d4d1c8', dark: '#3c3a36', eye: '#9cc04a', seed: 4 } },
  { id: 'abyssinian', ko: '아비시니안', shape: { earSize: 1.3, eyeShape: 'almond', bodyBulk: .86, legLen: 1.1, wedge: .4, earSet: 'high', earWide: 1.1, tailLen: 1.1, tailThick: .85, eyeTilt: .13, eyeLiner: 1 }, coat: { pattern: 'ticked', base: '#c98a52', dark: '#7a4a28', eye: '#b8a03a' } },
  { id: 'somali', ko: '소말리', shape: { earSize: 1.25, fur: 'long', furAmount: .6, tailFluff: 1.2, bodyBulk: .9, wedge: .35, earSet: 'high', earWide: 1.1, eyeShape: 'almond', eyeTilt: .13, eyeLiner: 1 }, coat: { pattern: 'ticked', base: '#c47a45', dark: '#7a4a28', eye: '#b8a03a' } },
  { id: 'savannah', ko: '사바나', shape: { ear: 'large', earSize: 1.45, legLen: 1.35, bodyBulk: .85, bodyLen: 1.1, size: 1.15, tailLen: .9, neckLen: 1.3, earSet: 'high', wedge: .3, tailThick: .9, eyeShape: 'oval', eyeTilt: .1, eyeLid: .15 }, coat: { pattern: 'spotted', base: '#d8b46f', dark: '#2c241c', eye: '#c9a227', seed: 7, earSpot: 1, spotSize: 1.5, tearMark: 1 } },
  { id: 'oriental', ko: '오리엔탈', shape: { ear: 'large', earSize: 1.35, bodyBulk: .78, legLen: 1.25, headW: .9, muzzleLen: 1.3, eyeShape: 'almond', tailLen: 1.4, wedge: 1, earSet: 'low', earWide: 1.05, bodyLen: 1.2, tailThick: .6, neckLen: 1.2, tuck: .45, eyeTilt: .4, eyeSpace: .94, eyeLid: .08 }, coat: { pattern: 'solid', base: '#3a3432', eye: '#7cae3d', lightMuzzle: false } },
  { id: 'sphynx', ko: '스핑크스', shape: { ear: 'large', earSize: 1.55, fur: 'none', wrinkles: 1, bodyBulk: .9, eyeShape: 'lemon', tailLen: 1.1, potBelly: 1, earSet: 'low', earWide: 1.15, wedge: .3, tailThick: .6, chest: 1.1, eyeTilt: .28, whisker: 'none' }, coat: { pattern: 'calico', base: '#efc4b6', second: '#b5a2a4', white: '#f6d6cc', whiteLevel: 0, eye: '#9cb84a', innerEar: '#eba8a0', seed: 3 } },
  { id: 'turkish_angora', ko: '터키시 앙고라', shape: { fur: 'long', furAmount: .6, tailFluff: 1.2, earSize: 1.15, bodyBulk: .88, legLen: 1.08, wedge: .35, earSet: 'high', neckLen: 1.15, tailThick: .9, eyeShape: 'almond', eyeTilt: .18 }, coat: { pattern: 'solid', base: '#fbf8f2', eye: '#7fb6ea', eye2: '#d9a52c' } },
  { id: 'turkish_van', ko: '터키시 반', shape: { fur: 'long', furAmount: .4, tailFluff: .8, size: 1.08, bodyLen: 1.1, chest: 1.1, eyeShape: 'oval', eyeTilt: .12 }, coat: { pattern: 'van', base: '#d9853c' } },
  { id: 'exotic_shorthair', ko: '엑조틱 숏헤어', shape: { faceFlat: 1, ear: 'small', cheek: 1, eyeSize: 1.2, bodyBulk: 1.12, legLen: .85, earSet: 'low', earRound: .8, bodyLen: .82, neckLen: .75, tailThick: 1.2, tailLen: .85, eyeShape: 'round', eyeSpace: 1.1 }, coat: { pattern: 'classic', base: '#e2a35e', dark: '#b06a2d', eye: '#d7902f' } },
  { id: 'american_curl', ko: '아메리칸 컬', shape: { ear: 'curl', earSize: 1.15, eyeShape: 'oval', eyeTilt: .14 }, coat: { pattern: 'bicolor', base: '#55504c' } },
  { id: 'devon_rex', ko: '데본 렉스', shape: { ear: 'large', earSize: 1.6, eyeSize: 1.25, headW: 1.0, bodyBulk: .82, fur: 'rex', legLen: 1.1, earSet: 'low', earWide: 1.2, wedge: .4, tailThick: .7, eyeShape: 'oval', eyeTilt: .12, eyeSpace: 1.08, whisker: 'curly' }, coat: { pattern: 'solid', base: '#cdbfae', eye: '#9cb84a' } },
  { id: 'cornish_rex', ko: '코니시 렉스', shape: { ear: 'large', earSize: 1.45, bodyBulk: .78, legLen: 1.25, fur: 'rex', headW: .86, muzzleLen: 1.2, tailLen: 1.25, backArch: 1, tuck: 1, wedge: .5, tailThick: .55, earSet: 'high', chest: 1.15, eyeShape: 'oval', eyeTilt: .16, romanNose: 1, whisker: 'curly' }, coat: { pattern: 'bicolor', base: '#7d6a62', whiteLevel: .5 } },
  { id: 'selkirk_rex', ko: '셀커크 렉스', shape: { fur: 'curly', furAmount: 1.1, cheek: .8, bodyBulk: 1.1, ear: 'small', eyeSize: 1.15, tailFluff: .6, earRound: .5, bodyLen: .9, tailThick: 1.2, eyeShape: 'round', whisker: 'curly' }, coat: { pattern: 'calico', base: '#3b3735', second: '#d98a43', whiteLevel: .5, seed: 6 } },
  { id: 'laperm', ko: '라팜', shape: { fur: 'curly', furAmount: .8, earSize: 1.15, bodyBulk: .9, tailFluff: .5, wedge: .3, earSet: 'high', eyeShape: 'almond', eyeTilt: .16, whisker: 'curly' }, coat: { pattern: 'tortie', base: '#3a2e29', second: '#c4773a', seed: 2 } },
  { id: 'manx', ko: '맹크스 (꼬리 없음)', shape: { tail: 'none', bodyBulk: 1.08, bodyLen: .85, legLen: 1.0, rumpHigh: .3, earRound: .4, eyeShape: 'round', eyeTilt: .04 }, coat: { pattern: 'mackerel', base: '#bfa486', dark: '#6e5640' } },
  { id: 'japanese_bobtail', ko: '재패니즈 밥테일', shape: { tail: 'bob', earSize: 1.15, bodyBulk: .9, legLen: 1.15, headTri: 1, earSet: 'high', rumpHigh: .1, eyeShape: 'oval', eyeTilt: .3 }, coat: { pattern: 'calico', base: '#2f2c2c', second: '#df8d3d', whiteLevel: .6, seed: 9 } },
  { id: 'bombay', ko: '봄베이', shape: { eyeSize: 1.2, bodyBulk: 1.0, bodyLen: .95, earRound: .3, chest: 1.1, eyeShape: 'round' }, coat: { pattern: 'solid', base: '#232125', eye: '#d9a52c', lightMuzzle: false, nose: '#2d2a2e', sheen: .8 } },
];

export const KOREAN_COATS = [
  { id: 'godeungeo', ko: '고등어 태비', coat: { pattern: 'mackerel', base: '#9c968c', dark: '#4d4740' } },
  { id: 'cheese', ko: '치즈 태비', coat: { pattern: 'mackerel', base: '#f0b46a', dark: '#c47a33' } },
  { id: 'cheese_white', ko: '치즈 (흰 바탕)', coat: { pattern: 'mackerel', base: '#f0b46a', dark: '#c47a33', whiteLevel: .55 } },
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
export const NOISE = { vnoise, fbm, sstep, worley2 };
