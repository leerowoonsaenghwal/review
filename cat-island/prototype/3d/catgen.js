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
    if (part === 'pad') return C(spec.pad || '#e9a3a3');

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

    let s = 0;
    const tabby = ['mackerel', 'classic', 'spotted', 'ticked'].includes(P);
    if (!tabby) {
      if (part === 'body' && spec.wrinkles) s = 0;
      return c;
    }
    if (part === 'head') {
      const d = info.dir;
      if (d.y > .3 && d.z > 0 && Math.abs(d.x) < .42) s = sstep(.35, .75, Math.sin(d.x * 24));      // forehead "M"
      if (Math.abs(d.x) > .62 && Math.abs(d.x) < .86 && d.z > .42 && d.y > -.3 && d.y < .02) s = Math.max(s, sstep(.55, .85, Math.sin(d.y * 26 + 1.2)) * .75);  // whisker-cheek lines
      if (P === 'ticked' || P === 'spotted') s *= .8;
    } else if (part === 'tail') {
      s = P === 'ticked' ? sstep(.75, .95, info.t) : sstep(.1, .6, Math.sin(info.t * 34)) * .85;
    } else if (part === 'leg') {
      s = P === 'ticked' ? 0 : sstep(.2, .7, Math.sin(info.t * 16)) * sstep(.15, .35, info.t) * .7;
    } else if (part === 'body') {
      const { nx, ny, nz } = info;
      const spine = sstep(.75, .95, ny) * (1 - Math.abs(nx) * 2) * (1 - sstep(.75, .95, Math.abs(nz)));
      const poleFade = 1 - sstep(.72, .92, Math.abs(nz));
      if (P === 'mackerel') s = Math.max(spine, sstep(.15, .6, Math.sin(nz * 17 + ny * 2.2)) * sstep(-.45, -.15, ny) * poleFade);
      else if (P === 'classic') {
        const r = Math.hypot(nz + .15, (ny - .1) * 1.3);
        s = Math.max(spine, sstep(.1, .5, Math.sin(r * 12)) * sstep(-.5, -.2, ny) * sstep(.2, .5, Math.abs(nx)) * poleFade);
      } else if (P === 'spotted') {
        const n = vnoise(p.x * 9 + 11, p.y * 9, p.z * 9 + seed);
        s = Math.max(spine * .6, sstep(.62, .7, n) * sstep(-.5, -.2, ny) * poleFade);
      } else if (P === 'ticked') {
        s = spine * .7;
        c.lerp(dark, vnoise(p.x * 40, p.y * 40, p.z * 40) * .22);
      }
    }
    return c.lerp(dark, Math.min(1, s));
  };
}

// ------------------------------------------------------------------ fur surfaces
const MAT = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .9, metalness: 0 });
const MAT_SKIN = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .45, metalness: 0 });
const MAT_GLOSS = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .35, metalness: 0 });

function furify(geo, fur, k = 0) {
  // fur: {type:'short'|'long'|'curly'|'rex'|'none', amount}
  const pos = geo.attributes.position, v = new THREE.Vector3(), n = new THREE.Vector3();
  geo.computeVertexNormals();
  const nor = geo.attributes.normal;
  for (let i = 0; i < pos.count; i++) {
    v.fromBufferAttribute(pos, i); n.fromBufferAttribute(nor, i);
    let off = 0;
    if (fur.type === 'long') off = fur.amount * .06 * (fbm(v.x * 3.5 + k, v.y * 3.5, v.z * 3.5) - .3);
    else if (fur.type === 'curly') off = fur.amount * (.035 + .05 * Math.pow(vnoise(v.x * 16 + k, v.y * 16, v.z * 16), 2) + .02 * Math.sin(v.x * 40) * Math.sin(v.z * 40));
    else if (fur.type === 'rex') off = fur.amount * .012 * Math.sin(v.z * 46 + v.y * 20);
    v.addScaledVector(n, off);
    pos.setXYZ(i, v.x, v.y, v.z);
  }
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
  eyeSize: 1, eyeShape: 'round',
  bodyLen: 1, bodyBulk: 1, legLen: 1, legBulk: 1,
  fur: 'short', furAmount: 1, ruff: 0,
  tail: 'normal', tailLen: 1, tailFluff: 0,
  wrinkles: 0,
};

// ------------------------------------------------------------------ build
export function buildCat(shapeIn, coatSpec) {
  const s = { ...SHAPE_DEFAULT, ...shapeIn };
  const colorAt = makeCoat(coatSpec);
  const fur = { type: s.fur, amount: s.furAmount };
  const hairless = s.fur === 'none';
  const bodyMat = hairless ? MAT_SKIN : MAT;
  const L = s.legLen * .8, B = s.bodyBulk, BL = s.bodyLen * .82;

  const root = new THREE.Group();
  const rig = { root, legs: {}, tail: [] };

  // leg geometry sizes
  const fUp = .3 * L, fLo = .3 * L, hTh = .3 * L, hSh = .26 * L, hHo = .2 * L, pawR = .09 * s.legBulk;
  const shoulderY = fUp + fLo + pawR * .9;
  const bodyY = shoulderY + .06;

  const body = new THREE.Group(); body.position.y = bodyY; root.add(body); rig.body = body;
  const fluffy = s.fur === 'long' ? 1.08 : s.fur === 'curly' ? 1.05 : 1;
  const bodyNorm = p => ({ nx: p.x / (.32 * B), ny: (p.y - bodyY) / (.34 * B), nz: p.z / (.9 * BL) });

  // torso: one smooth lofted shape - deep chest, slight waist, round hips (no segment seams)
  {
    const g = new THREE.SphereGeometry(1, 120, 80);
    g.rotateX(Math.PI / 2);                         // poles on the z axis (tail <-> chest)
    const pos = g.attributes.position;
    const prof = t => .305 + .055 * Math.exp(-(((t - .45) / .32) ** 2)) + .035 * Math.exp(-(((t + .5) / .3) ** 2));
    for (let i = 0; i < pos.count; i++) {
      const x = pos.getX(i), y = pos.getY(i), z = pos.getZ(i);
      const r = prof(z) * B * fluffy;
      pos.setXYZ(i, x * r * .93, y * r + .03 - .025 * Math.max(0, z), z * .9 * BL);
    }
    g.computeVertexNormals();
    if (!hairless) furify(g, fur, 1);
    body.add(mk(g, 'body', 'bodyNorm', bodyMat));
  }
  if (s.ruff) {
    const g = ellipsoid(1, .38 * B, .34, .3, 64); g.translate(0, .12, .58 * BL);
    furify(g, { type: 'long', amount: 1.6 * s.ruff }, 9);
    body.add(mk(g, 'body', 'bodyNorm'));
  }

  // legs
  const legs = [
    ['frontL', -1, 'front', .5], ['frontR', 1, 'front', .5],
    ['hindL', -1, 'hind', -.48], ['hindR', 1, 'hind', -.48],
  ];
  for (const [name, side, kind, z] of legs) {
    const top = new THREE.Group();
    top.position.set(side * .19 * B, kind === 'front' ? -.08 : -.02, z * BL);
    body.add(top);
    const chain = [];
    const segs = kind === 'front' ? [[fUp, .1, .085], [fLo, .085, .075]] : [[hTh, .14, .1], [hSh, .095, .075], [hHo, .072, .07]];
    let parent = top, acc = 0, total = segs.reduce((a, b) => a + b[0], 0);
    const thick = (s.fur === 'long' ? 1.15 : 1) * s.legBulk * 1.18;
    for (const [len, r0, r1] of segs) {
      const j = new THREE.Group(); parent.add(j);
      const g = capsuleY(r0 * thick, r1 * thick, len);
      const t0 = acc / total, t1 = (acc + len) / total;
      if (!hairless) furify(g, { ...fur, amount: fur.amount * .5 }, side + acc);
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
    let parent = new THREE.Group(); parent.position.set(0, .16, -.78 * BL); body.add(parent);
    const segLen = (s.tail === 'bob' ? .06 : .1) * s.tailLen;
    const plume = s.tailFluff ? 1 + s.tailFluff * .9 : 1;
    for (let i = 0; i < tailN; i++) {
      const j = new THREE.Group(); parent.add(j);
      const rad = k => (plume > 1 ? .068 * plume * (1 - .22 * k) * (k > .85 ? Math.sqrt(Math.max(.15, 1 - ((k - .85) / .2) ** 2)) : 1) : .068 - .026 * k) * (s.fur === 'curly' ? 1.2 : 1);
      const r0 = rad(i / tailN), r1 = rad((i + 1) / tailN);
      const g = capsuleY(r0, r1, segLen, 24);
      g.rotateX(Math.PI);                    // grow backwards/upwards along +y of the joint
      if (s.fur === 'curly') furify(g, { type: 'curly', amount: 1 }, 0);
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
  const neck = new THREE.Group(); neck.position.set(0, .2, .62 * BL); body.add(neck); rig.neck = neck;
  {
    const g = ellipsoid(1, .25 * B * fluffy, .27 * B * fluffy, .28, 64); g.translate(0, .1, .06);
    if (!hairless) furify(g, fur, 5);
    neck.add(mk(g, 'body', 'bodyNorm', bodyMat));
  }
  const head = new THREE.Group(); head.position.set(0, .26 * s.headSize, .16 * s.headSize); neck.add(head); rig.head = head;
  const HS = .42 * s.headSize;
  {
    const g = ellipsoid(HS, s.headW, .9, .92 - s.faceFlat * .1, 96);
    if (!hairless) furify(g, { ...fur, amount: fur.amount * .7 }, 11);
    head.add(mk(g, 'head', 'headDir', bodyMat));
    if (s.cheek) for (const x of [-1, 1]) {
      const c = ellipsoid(HS * .38, 1, .8, .85, 48); c.translate(x * HS * .5 * s.headW, -HS * .3, HS * .36);
      head.add(mk(c, 'head', 'headDir', bodyMat));
    }
  }
  // ears
  for (const x of [-1, 1]) {
    const es = Math.pow(s.earSize, .65) * HS / .42 * .82 * (s.ear === 'large' ? 1.15 : 1);
    // one cone per ear (base sunk into the skull); the pink inner ear is painted on its front face
    const eh = .36 * es, er0 = .16 * es;
    // lathe cone (ConeGeometry with heightSegments > 1 flips half of its triangles in three r169)
    const prof = [];
    for (let k = 0; k <= 14; k++) { const t = k / 14; prof.push(new THREE.Vector2(Math.max(1e-4, er0 * (1 - t) * (1 - .12 * Math.sin(Math.PI * t))), -eh / 2 + eh * t)); }
    const eg = new THREE.LatheGeometry(prof, 40); eg.translate(0, .08 * es, 0);
    const ear = new THREE.Group();
    const earMesh = mk(eg, 'ear', { ear: true, h: eh, r: er0, y0: .08 * es - eh / 2 }, bodyMat);
    earMesh.receiveShadow = false;   // thin cones self-shadow into moire bands
    ear.add(earMesh);
    const ex = x * HS * .5 * s.headW, ey = HS * .6;
    ear.position.set(ex, ey, -HS * .05);
    if (s.ear === 'fold') { ear.scale.set(1.08, .5, .72); ear.rotation.set(1.62, 0, x * .45); ear.position.set(x * HS * .44, HS * .76, HS * .16); }
    else if (s.ear === 'curl') { ear.rotation.set(-1.0, 0, -x * .2); }
    else if (s.ear === 'large') { ear.rotation.set(0, 0, -x * .42); }
    else if (s.ear === 'small') { ear.scale.setScalar(.75); ear.rotation.set(.15, 0, -x * .45); }
    else ear.rotation.set(.05, 0, -x * .32);
    if (s.earTuft) {
      const t = new THREE.ConeGeometry(.022 * es, .09 * es, 8); t.translate(0, .3 * es, 0);
      ear.add(mk(t, 'tuft', null));
    }
    head.add(ear);
  }
  // muzzle, nose, eyes, blush, whisker pads
  {
    const flat = s.faceFlat;
    const mz = ellipsoid(HS * .4, 1.2, .72, .7 * s.muzzleLen * (1 - flat * .5), 48);
    mz.translate(0, -HS * .3 + flat * HS * .06, HS * (.72 - flat * .14));
    head.add(mk(mz, 'muzzle', 'headDir', bodyMat));
    const ng = ellipsoid(HS * .072, 1.3, .78, .6, 20); ng.translate(0, -HS * .14 + flat * HS * .1, HS * (.97 - flat * .2) * Math.min(1, s.muzzleLen * .1 + .9));
    head.add(mk(ng, 'nose', null, MAT_GLOSS));
    // mouth: a tiny 'w' under the nose
    for (const x of [-1, 1]) {
      const m = new THREE.TorusGeometry(HS * .055, HS * .014, 8, 20, Math.PI);
      m.rotateZ(Math.PI); m.translate(x * HS * .055, -HS * .2 + flat * HS * .1, HS * (.93 - flat * .2));
      head.add(mk(m, 'mouth', null, MAT_GLOSS));
    }
    for (const x of [-1, 1]) {
      // big, round, glossy dark eyes; the breed's eye colour only glows softly at the bottom
      const er = HS * .2 * s.eyeSize;
      const furPush = s.fur === 'curly' ? .07 : s.fur === 'long' ? .045 : 0;      // keep eyes in front of fluffy fur
      const ex = x * HS * .37, ey = HS * .02, ez = HS * (.84 - flat * .05 + furPush);
      const sx = s.eyeShape === 'almond' ? .9 : .82, sy = 1.04, sz = .42;
      const ig = ellipsoid(er, sx, sy, sz, 48);
      ig.translate(ex, ey, ez);
      head.add(mk(ig, 'eye', { cy: ey, er }, MAT_GLOSS));
      // highlights: flat discs lying on the eye surface, same light direction (upper-left) for both eyes
      for (const [dx, dy, rr] of [[-.3, .36, .3], [.3, -.36, .12]]) {
        const hx = dx * er, hy = dy * er;
        const k = 1 - (hx / (er * sx)) ** 2 - (hy / (er * sy)) ** 2;
        const hz = ez + er * sz * Math.sqrt(Math.max(0, k)) + er * .015;
        const disc = new THREE.Mesh(new THREE.CircleGeometry(er * rr, 24), new THREE.MeshBasicMaterial({ color: '#ffffff' }));
        disc.position.set(ex + hx, ey + hy, hz);
        disc.lookAt(ex + hx * 1.6, ey + hy * 1.6, hz + er);                    // follow the eye's curvature
        head.add(disc);
      }
      const bl = new THREE.Mesh(new THREE.SphereGeometry(HS * .13, 16, 12), new THREE.MeshStandardMaterial({ color: '#f7a3b6', roughness: 1, transparent: true, opacity: .6 }));
      bl.scale.set(1.35, .55, .3); bl.position.set(x * HS * .62, -HS * .2, HS * .72); head.add(bl);
    }
  }

  // ---------- paint every mesh in rest pose, using root-space positions
  root.updateMatrixWorld(true);
  const inv = new THREE.Matrix4().copy(root.matrixWorld).invert();
  const headInv = new THREE.Matrix4().copy(head.matrixWorld).invert();
  const v = new THREE.Vector3(), w = new THREE.Vector3();
  root.traverse(o => {
    if (!o.isMesh || !o.userData.part) return;
    const part = o.userData.part === 'tuft' ? 'ear' : o.userData.part;
    const pos = o.geometry.attributes.position, col = new Float32Array(pos.count * 3);
    const m = new THREE.Matrix4().multiplyMatrices(inv, o.matrixWorld);
    const mh = new THREE.Matrix4().multiplyMatrices(headInv, o.matrixWorld);
    for (let i = 0; i < pos.count; i++) {
      v.fromBufferAttribute(pos, i);
      const p = w.copy(v).applyMatrix4(m).clone();
      let info = o.userData.info, c;
      if (o.userData.part === 'tuft') c = colorAt('ear', p, { dir: new THREE.Vector3(0, 1, 0) }).multiplyScalar(.55);
      else if (o.userData.part === 'mouth') c = C('#3a2a24');
      else if (info && info.ear) {
        const y = (v.y - info.y0) / info.h, rr = Math.max(1e-4, info.r * (1 - y));
        const outer = colorAt('ear', p, { dir: v.clone().applyMatrix4(mh).normalize() });
        const w = sstep(.62, .82, v.z / rr) * sstep(.55, .38, Math.abs(v.x) / rr) * sstep(.28, .38, y) * sstep(.78, .66, y);
        c = outer.lerp(colorAt('innerEar'), .8 * w);
      }
      else if (o.userData.part === 'eye') {
        const t = (v.y - info.cy) / info.er;
        c = C('#271c18').lerp(colorAt('eye'), .5 * sstep(-.15, -.95, t));
      } else {
        if (info === 'bodyNorm') info = bodyNorm(p);
        else if (info === 'headDir') info = { dir: v.clone().applyMatrix4(mh).normalize() };
        else if (info && info.len !== undefined) {
          const along = Math.min(1, Math.max(0, part === 'tail' ? v.y / (info.len || 1) : -v.y / (info.len || 1)));
          info = { ...info, t: info.t0 + (info.t1 - info.t0) * along };
        }
        c = colorAt(part, p, info);
      }
      col.set([c.r, c.g, c.b], i * 3);
    }
    o.geometry.setAttribute('color', new THREE.BufferAttribute(col, 3));
  });

  root.scale.setScalar(s.size);
  root.userData.rig = rig;
  root.userData.shape = s;
  setPose(root, 'stand', 0);
  return root;
}

// ------------------------------------------------------------------ poses / motion
// walk: lateral-sequence gait (LH, LF, RH, RF), phase 0..1
export function setPose(cat, mode = 'walk', phase = 0) {
  const { legs, tail, body, neck, head } = cat.userData.rig;
  const s = cat.userData.shape;
  const TAU = Math.PI * 2;
  const offsets = { hindL: 0, frontL: .25, hindR: .5, frontR: .75 };
  for (const [name, leg] of Object.entries(legs)) {
    const ph = (phase + offsets[name]) % 1;
    const swing = mode === 'walk' ? Math.sin(ph * TAU) : 0;
    const lift = mode === 'walk' ? Math.max(0, Math.sin(ph * TAU - .6)) : 0;
    const [a, b, c, d] = leg.chain;
    if (leg.kind === 'front') {
      a.rotation.x = -.05 + swing * .45;
      b.rotation.x = -lift * .9;
      c.rotation.x = lift * .6;
    } else {
      a.rotation.x = .55 + swing * .4;          // thigh angled forward
      b.rotation.x = -1.0 - lift * .5;          // shin back
      c.rotation.x = .5 + lift * .7;            // hock down
      d.rotation.x = 0;
    }
  }
  body.position.y = body.position.y; // keep
  const bob = mode === 'walk' ? Math.sin(phase * TAU * 2) * .015 : 0;
  body.rotation.x = 0;
  body.children[0] && (cat.userData.baseY ??= body.position.y);
  body.position.y = cat.userData.baseY + bob;
  neck.rotation.x = -.15 + (mode === 'walk' ? Math.sin(phase * TAU * 2 + 1) * .04 : 0);
  head.rotation.x = .12;
  head.rotation.y = mode === 'look' ? -.5 : 0;
  // tail: up-and-curved, swaying while walking
  const curl = s.tail === 'bob' ? 0 : 1;
  // relaxed 'J': leaves the rump pointing back-down, then rises with a soft curl at the tip
  tail.forEach((j, i) => {
    const k = i / Math.max(1, tail.length - 1);
    j.rotation.x = i === 0 ? -1.45 : (k < .7 ? .15 : .28) * curl;
    j.rotation.z = (mode === 'walk' ? Math.sin(phase * TAU - k * 2) * .1 : .04) * curl;
  });
}

// ------------------------------------------------------------------ catalogue
const SH = (o) => o;
export const BREEDS = [
  { id: 'korean_shorthair', ko: '코리안 숏헤어', shape: {}, coat: { pattern: 'mackerel', base: '#a39d93', dark: '#57514a' } },
  { id: 'russian_blue', ko: '러시안 블루', shape: { eyeShape: 'almond', earSize: 1.12, bodyBulk: .92, legLen: 1.08 }, coat: { pattern: 'solid', base: '#8794a3', eye: '#5fae6a', lightMuzzle: false, nose: '#7d8794' } },
  { id: 'persian', ko: '페르시안', shape: { faceFlat: 1, ear: 'small', fur: 'long', ruff: 1, tailFluff: 1, cheek: 1, eyeSize: 1.15, legLen: .82, bodyBulk: 1.1 }, coat: { pattern: 'solid', base: '#f4efe6', eye: '#d7902f' } },
  { id: 'himalayan', ko: '히말라얀', shape: { faceFlat: 1, ear: 'small', fur: 'long', ruff: 1, tailFluff: 1, cheek: 1, legLen: .82, bodyBulk: 1.1 }, coat: { pattern: 'point', base: '#f2e8d8', point: '#6b4f40', eye: '#4f8fd8' } },
  { id: 'siamese', ko: '샴', shape: { ear: 'large', earSize: 1.3, eyeShape: 'almond', bodyBulk: .82, legLen: 1.15, headW: .95, muzzleLen: 1.2, tailLen: 1.2 }, coat: { pattern: 'point', base: '#f1e6d2', point: '#4f3a30', eye: '#5aa6e0' } },
  { id: 'scottish_fold', ko: '스코티시 폴드', shape: { ear: 'fold', eyeSize: 1.2, cheek: .6, bodyBulk: 1.05 }, coat: { pattern: 'mackerel', base: '#a9adb6', dark: '#686c74' } },
  { id: 'british_shorthair', ko: '브리티시 숏헤어', shape: { cheek: 1, ear: 'small', bodyBulk: 1.15, headW: 1.18, eyeSize: 1.12, legBulk: 1.15 }, coat: { pattern: 'solid', base: '#8a92a0', eye: '#e19a2b', lightMuzzle: false } },
  { id: 'munchkin', ko: '먼치킨', shape: { legLen: .5 }, coat: { pattern: 'classic', base: '#e8b878', dark: '#b4703a' } },
  { id: 'ragdoll', ko: '랙돌', shape: { fur: 'long', furAmount: .8, ruff: .8, tailFluff: 1, eyeSize: 1.1, bodyBulk: 1.1, size: 1.1 }, coat: { pattern: 'mitted', base: '#f3ece2', point: '#6b5a52', eye: '#4f8fd8' } },
  { id: 'birman', ko: '버먼', shape: { fur: 'long', furAmount: .7, ruff: .6, tailFluff: .8 }, coat: { pattern: 'mitted', base: '#efe3cc', point: '#4e3a2f', eye: '#4f8fd8' } },
  { id: 'american_shorthair', ko: '아메리칸 숏헤어', shape: { cheek: .5, bodyBulk: 1.05, legBulk: 1.1 }, coat: { pattern: 'classic', base: '#c9c9c4', dark: '#3d3d3d' } },
  { id: 'norwegian_forest', ko: '노르웨이 숲', shape: { fur: 'long', ruff: 1, tailFluff: 1.2, earTuft: 1, earSize: 1.1, size: 1.12 }, coat: { pattern: 'mackerel', base: '#b8a68e', dark: '#6e5b45', eye: '#9db24a', whiteLevel: .25 } },
  { id: 'maine_coon', ko: '메인쿤', shape: { fur: 'long', ruff: 1.2, tailFluff: 1.2, earTuft: 1, earSize: 1.25, bodyLen: 1.15, size: 1.22, muzzleLen: 1.2 }, coat: { pattern: 'classic', base: '#8a6a4f', dark: '#3e2d22', whiteLevel: .2 } },
  { id: 'siberian', ko: '시베리안', shape: { fur: 'long', ruff: 1, tailFluff: 1, cheek: .5, size: 1.1 }, coat: { pattern: 'mackerel', base: '#c9c4bb', dark: '#5d5850', eye: '#9db24a' } },
  { id: 'bengal', ko: '벵갈', shape: { bodyBulk: .95, eyeShape: 'almond', legLen: 1.08 }, coat: { pattern: 'spotted', base: '#d9a95c', dark: '#4a3322', eye: '#7cae3d' } },
  { id: 'egyptian_mau', ko: '이집션 마우', shape: { bodyBulk: .9, legLen: 1.1, eyeSize: 1.15 }, coat: { pattern: 'spotted', base: '#d4d1c8', dark: '#3c3a36', eye: '#9cc04a', seed: 4 } },
  { id: 'abyssinian', ko: '아비시니안', shape: { earSize: 1.3, eyeShape: 'almond', bodyBulk: .86, legLen: 1.1 }, coat: { pattern: 'ticked', base: '#c98a52', dark: '#7a4a28', eye: '#b8a03a' } },
  { id: 'somali', ko: '소말리', shape: { earSize: 1.25, fur: 'long', furAmount: .6, tailFluff: 1.2, bodyBulk: .9 }, coat: { pattern: 'ticked', base: '#c47a45', dark: '#7a4a28', eye: '#b8a03a' } },
  { id: 'savannah', ko: '사바나', shape: { ear: 'large', earSize: 1.45, legLen: 1.35, bodyBulk: .85, bodyLen: 1.1, size: 1.15, tailLen: .9 }, coat: { pattern: 'spotted', base: '#d8b46f', dark: '#2c241c', eye: '#c9a227', seed: 7 } },
  { id: 'oriental', ko: '오리엔탈', shape: { ear: 'large', earSize: 1.5, bodyBulk: .78, legLen: 1.2, headW: .9, muzzleLen: 1.3, eyeShape: 'almond', tailLen: 1.3 }, coat: { pattern: 'solid', base: '#3a3432', eye: '#7cae3d', lightMuzzle: false } },
  { id: 'sphynx', ko: '스핑크스', shape: { ear: 'large', earSize: 1.55, fur: 'none', wrinkles: 1, bodyBulk: .9, eyeShape: 'almond', tailLen: 1.1 }, coat: { pattern: 'calico', base: '#efc4b6', second: '#b5a2a4', white: '#f6d6cc', whiteLevel: 0, eye: '#9cb84a', innerEar: '#eba8a0', seed: 3 } },
  { id: 'turkish_angora', ko: '터키시 앙고라', shape: { fur: 'long', furAmount: .6, tailFluff: 1.2, earSize: 1.15, bodyBulk: .88, legLen: 1.08 }, coat: { pattern: 'solid', base: '#fbf8f2', eye: '#7fb6ea' } },
  { id: 'turkish_van', ko: '터키시 반', shape: { fur: 'long', furAmount: .4, tailFluff: .8, size: 1.08 }, coat: { pattern: 'van', base: '#d9853c' } },
  { id: 'exotic_shorthair', ko: '엑조틱 숏헤어', shape: { faceFlat: 1, ear: 'small', cheek: 1, eyeSize: 1.2, bodyBulk: 1.12, legLen: .85 }, coat: { pattern: 'classic', base: '#e2a35e', dark: '#b06a2d', eye: '#d7902f' } },
  { id: 'american_curl', ko: '아메리칸 컬', shape: { ear: 'curl', earSize: 1.15 }, coat: { pattern: 'bicolor', base: '#55504c' } },
  { id: 'devon_rex', ko: '데본 렉스', shape: { ear: 'large', earSize: 1.6, eyeSize: 1.25, headW: 1.0, bodyBulk: .82, fur: 'rex', legLen: 1.1 }, coat: { pattern: 'solid', base: '#cdbfae', eye: '#9cb84a' } },
  { id: 'cornish_rex', ko: '코니시 렉스', shape: { ear: 'large', earSize: 1.45, bodyBulk: .78, legLen: 1.25, fur: 'rex', headW: .9, muzzleLen: 1.2, tailLen: 1.25 }, coat: { pattern: 'bicolor', base: '#7d6a62', whiteLevel: .5 } },
  { id: 'selkirk_rex', ko: '셀커크 렉스', shape: { fur: 'curly', furAmount: 1.1, cheek: .8, bodyBulk: 1.1, ear: 'small', eyeSize: 1.15, tailFluff: .6 }, coat: { pattern: 'calico', base: '#3b3735', second: '#d98a43', whiteLevel: .5, seed: 6 } },
  { id: 'laperm', ko: '라팜', shape: { fur: 'curly', furAmount: .8, earSize: 1.15, bodyBulk: .9, tailFluff: .5 }, coat: { pattern: 'tortie', base: '#3a2e29', second: '#c4773a', seed: 2 } },
  { id: 'manx', ko: '맹크스 (꼬리 없음)', shape: { tail: 'none', bodyBulk: 1.08, bodyLen: .9, legLen: 1.0 }, coat: { pattern: 'mackerel', base: '#bfa486', dark: '#6e5640' } },
  { id: 'japanese_bobtail', ko: '재패니즈 밥테일', shape: { tail: 'bob', earSize: 1.15, bodyBulk: .9, legLen: 1.1 }, coat: { pattern: 'calico', base: '#2f2c2c', second: '#df8d3d', whiteLevel: .6, seed: 9 } },
  { id: 'bombay', ko: '봄베이', shape: { eyeSize: 1.2, bodyBulk: 1.0 }, coat: { pattern: 'solid', base: '#232125', eye: '#d9a52c', lightMuzzle: false, nose: '#2d2a2e' } },
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
