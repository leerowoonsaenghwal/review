// Cat Island game model (v2): one continuous skinned mesh on an anatomical skeleton.
//
//   buildCatModel(shape, coat) -> { group, skeleton, bones, dims, meshes }
//
// How it is built (the same steps an artist would take, done procedurally):
//   1. skeleton   joints placed from the breed shape; legs are digitigrade (cats stand on their toes):
//                 front  Scapula - UpperArm - Forearm - Hand (metacarpus) - Fingers
//                 hind   Thigh - Shin - Foot (metatarsus, the long "heel" segment) - Toes
//                 spine  Hips - Spine1 - Spine2 - Chest - Neck - Head, tail 10 joints, ears, belly (breathing)
//   2. sculpt     ellipsoids and round cones around the bones, merged with smooth unions into one body
//                 (muzzle melts into the head, shoulders and haunches into the trunk, toes into the paw)
//   3. mesh       surface nets + smoothing, snapped onto the sculpt
//   4. skin       each vertex is weighted to the bones of the parts it is close to, then the weights are
//                 smoothed across the surface so joints bend softly
//   5. paint      coat colours from catgen.makeCoat (same breed coats as the catalogue), toe beans, blush
//   6. face       glossy AC eyes, nose, mouth, whiskers on a second mesh with blendshapes:
//                 Blink, MouthOpen; the tongue has its own 3 bones (lick, lap, yawn curl)
//   7. paws       raised toe beans (4 toe pads + 3-lobed main pad) riding on the paw bones
// All bones have identity rest rotations (pose maths stay simple); the whole cat is scaled to metres.
import * as THREE from 'three';
import { makeCoat, SHAPE_DEFAULT, NOISE } from './catgen.js';
import { Field, sdEllipsoid, sdRoundCone, surfaceNets, relax } from './sdfmesh.js';

const { vnoise, sstep } = NOISE;
const C = h => new THREE.Color(h);
const V = (x = 0, y = 0, z = 0) => new THREE.Vector3(x, y, z);
export const METERS = .39;      // model units -> metres: shoulder height about 22 cm, like a real cat

export const BONE_PARENTS = {
  Root: null, Hips: 'Root', Spine1: 'Hips', Spine2: 'Spine1', Chest: 'Spine2', Neck: 'Chest', Head: 'Neck',
  Ear_L: 'Head', Ear_R: 'Head', Belly: 'Spine1', Tongue1: 'Head', Tongue2: 'Tongue1', Tongue3: 'Tongue2',
  Scapula_L: 'Chest', UpperArm_L: 'Scapula_L', Forearm_L: 'UpperArm_L', Hand_L: 'Forearm_L', Fingers_L: 'Hand_L',
  Scapula_R: 'Chest', UpperArm_R: 'Scapula_R', Forearm_R: 'UpperArm_R', Hand_R: 'Forearm_R', Fingers_R: 'Hand_R',
  Thigh_L: 'Hips', Shin_L: 'Thigh_L', Foot_L: 'Shin_L', Toes_L: 'Foot_L',
  Thigh_R: 'Hips', Shin_R: 'Thigh_R', Foot_R: 'Shin_R', Toes_R: 'Foot_R',
  ...Object.fromEntries(Array.from({ length: 10 }, (_, i) => [`Tail${i + 1}`, i ? `Tail${i}` : 'Hips'])),
};
export const BONE_NAMES = Object.keys(BONE_PARENTS);
const BI = Object.fromEntries(BONE_NAMES.map((n, i) => [n, i]));
const SIDES = [['L', 1], ['R', -1]];          // the cat faces +z, so its left side is +x

// planar two-bone solve in the y/z plane: middle joint for a chain from A to T, bend = +1 (forward) / -1 (back)
function bend2(A, T, l1, l2, bend) {
  const dy = T.y - A.y, dz = T.z - A.z, D = Math.min(Math.hypot(dy, dz), l1 + l2 - 1e-4);
  const a = (l1 * l1 - l2 * l2 + D * D) / (2 * D), h = Math.sqrt(Math.max(0, l1 * l1 - a * a));
  const ny = dy / D, nz = dz / D;
  let py = -nz, pz = ny; if (Math.sign(pz) !== bend) { py = -py; pz = -pz; }
  return V(A.x + (T.x - A.x) * a / D, A.y + ny * a + py * h, A.z + nz * a + pz * h);
}
const segT = (p, a, b) => { const ab = b.clone().sub(a); return Math.min(1, Math.max(0, p.clone().sub(a).dot(ab) / ab.lengthSq())); };
const segDist = (p, a, b) => { const t = segT(p, a, b); return p.distanceTo(a.clone().lerp(b, t)); };

export function buildCatModel(shapeIn = {}, coatSpec = {}, opts = {}) {
  const s = { ...SHAPE_DEFAULT, ...shapeIn };
  const colorAt = makeCoat(coatSpec);
  const h = opts.res ?? .016;
  const long = s.fur === 'long', curly = s.fur === 'curly', hairless = s.fur === 'none';
  const L = s.legLen * .8, B = s.bodyBulk, BL = s.bodyLen * .82, RH = 1 + s.rumpHigh;
  const fl = long ? 1.05 + .07 * s.furAmount : curly ? 1.06 : 1;              // fluffy coats add volume
  const thick = (long ? 1.1 : 1) * s.legBulk * 1.18, pawR = .09 * s.legBulk, ballH = pawR * .62;
  const HS = .42 * s.headSize, flat = s.faceFlat;

  // ------------------------------------------------------------------ 1. skeleton (model units)
  // front bones are a little longer than the standing height needs, so the elbow and wrist keep some bend
  // in reserve: that slack is what lets the paw reach forward and back when walking (a straight leg can't)
  const lH = .3 * L, lR = .3 * L, lMc = .1 * L, lF = .3 * L * RH, lT = .28 * L * RH, lMt = .22 * L * RH;
  const shoulderH = ballH + lMc * Math.cos(.3) + .27 * L * Math.cos(.05) + .27 * L * Math.cos(.45);
  const hipH = ballH + lMt * Math.cos(.2) + lT * Math.cos(.7) + lF * Math.cos(.6);
  const bodyY = (shoulderH + .06 + hipH + .03) / 2 * .985;
  const O = V(0, bodyY, 0), at = (x, y, z) => O.clone().add(V(x, y, z));
  const J = {};
  J.Root = V();
  J.Hips = at(0, .04, -.5 * BL); J.Spine1 = at(0, .05, -.22 * BL); J.Spine2 = at(0, .055, .08 * BL); J.Chest = at(0, .05, .36 * BL);
  J.Neck = at(0, .2, .6 * BL);
  const Hc = at(0, .2 + .26 * s.headSize + (s.neckLen - 1) * .3, .6 * BL + .16 * s.headSize + (s.neckLen - 1) * .12);
  J.Head = Hc.clone().add(V(0, -.25 * HS, -.25 * HS));
  J.Belly = at(0, -.18 * B, -.05 * BL);
  // tongue: 3 joints, resting fully inside the muzzle; the clips slide it out and curl it
  const mY = -HS * .2 + flat * HS * .1, mZ = HS * (.93 - flat * .14), tongueLen = HS * .44;
  J.Tongue1 = Hc.clone().add(V(0, mY - HS * .05, mZ - HS * .5));
  J.Tongue2 = J.Tongue1.clone().add(V(0, 0, tongueLen * .36));
  J.Tongue3 = J.Tongue1.clone().add(V(0, 0, tongueLen * .68));
  for (const [S, x] of SIDES) {
    J[`Scapula_${S}`] = at(x * .11 * B, .2, .42 * BL);
    const sh = J[`UpperArm_${S}`] = at(x * .19 * B, -.06, .5 * BL);
    const ball = V(sh.x, ballH, sh.z + .03), dMc = V(0, -Math.cos(.3), Math.sin(.3));
    const wr = J[`Hand_${S}`] = ball.clone().addScaledVector(dMc, -lMc);
    J[`Forearm_${S}`] = bend2(sh, wr, lH, lR, -1);                              // elbow behind
    J[`Fingers_${S}`] = ball;
    const hip = J[`Thigh_${S}`] = at(x * .17 * B, -.03, -.48 * BL);
    const hb = V(hip.x, ballH, hip.z + .02), dMt = V(0, -Math.cos(.2), Math.sin(.2));
    const hock = J[`Foot_${S}`] = hb.clone().addScaledVector(dMt, -lMt);
    J[`Shin_${S}`] = bend2(hip, hock, lF, lT, 1);                                 // knee forward
    J[`Toes_${S}`] = hb;
  }
  const tailN = 10, tailOn = s.tail !== 'none', bob = s.tail === 'bob';
  const seg = i => !tailOn ? .004 : bob ? (i < 3 ? .06 * s.tailLen : .004) : .1 * s.tailLen;
  const tdir = V(0, Math.sin(.25), -Math.cos(.25));
  J.Tail1 = at(0, .17, -.8 * BL);
  for (let i = 1; i < tailN; i++) J[`Tail${i + 1}`] = J[`Tail${i}`].clone().addScaledVector(tdir, seg(i - 1));
  const tailTip = J.Tail10.clone().addScaledVector(tdir, seg(9));
  // ears (head space -> model space), same placement rules as the catalogue model
  const ears = {};
  for (const [S, x] of SIDES) {
    const th = { high: .4, mid: .6, low: .95 }[s.earSet] ?? .6;
    const g = new THREE.Object3D();
    g.position.set(x * Math.sin(th) * HS * s.headW * .9, Math.cos(th) * HS * .81, -HS * .05);
    const tilt = -x * Math.min(th * .8, .62);     // tips lean outwards
    if (s.ear === 'fold') { g.scale.set(1.08, .5, .72); g.rotation.set(1.62, 0, x * .45); g.position.set(x * HS * .44, HS * .76, HS * .16); }
    else if (s.ear === 'curl') { g.rotation.set(-.8, 0, tilt * .7); g.position.z -= HS * .06; }
    else if (s.ear === 'large') g.rotation.set(0, 0, tilt * 1.05);
    else if (s.ear === 'small') { g.scale.setScalar(.75); g.rotation.set(.15, 0, tilt * 1.1); }
    else g.rotation.set(.05, 0, tilt);
    g.position.add(Hc); g.updateMatrix();
    ears[S] = g; J[`Ear_${S}`] = g.position.clone();
  }

  // ------------------------------------------------------------------ 2. sculpt
  const F = new Field();
  const box3 = (pts, r) => { const b = [1e9, 1e9, 1e9, -1e9, -1e9, -1e9]; for (const p of pts) for (let i = 0; i < 3; i++) { const v = p.getComponent(i); b[i] = Math.min(b[i], v - r); b[i + 3] = Math.max(b[i + 3], v + r); } return b; };
  const ELL = (c, r, k, meta) => F.add({ d: (x, y, z) => sdEllipsoid(x - c.x, y - c.y, z - c.z, r[0], r[1], r[2]), box: [c.x - r[0], c.y - r[1], c.z - r[2], c.x + r[0], c.y + r[1], c.z + r[2]], k, meta });
  const CONE = (a, b, r1, r2, k, meta) => F.add({ d: (x, y, z) => sdRoundCone(x, y, z, [a.x, a.y, a.z], [b.x, b.y, b.z], r1, r2), box: box3([a, b], Math.max(r1, r2)), k, meta: { a, b, ...meta } });
  // fur clumps: soft rounded tips (sharp cones read as icicles), blended well into the coat
  const TUFT = (a, dir, len, r1, meta, k = .04) => CONE(a, a.clone().addScaledVector(dir.clone().normalize(), len), r1, Math.max(.014, r1 * .38), k, meta);

  // spine weighting: each bone moves the body ahead of its pivot; blended over +-.12 around the pivots
  const spineW = p => {
    const s1 = sstep(J.Spine1.z - .12, J.Spine1.z + .12, p.z), s2 = sstep(J.Spine2.z - .12, J.Spine2.z + .12, p.z), s3 = sstep(J.Chest.z - .12, J.Chest.z + .12, p.z);
    return [['Hips', 1 - s1], ['Spine1', s1 - s2], ['Spine2', s2 - s3], ['Chest', s3]];
  };
  const BODY = { part: 'body', bones: spineW };
  // trunk: deep chest, waist, round hips
  ELL(at(0, 0, .36 * BL), [.29 * B * fl, .33 * B * fl * (.9 + .1 * s.chest), .37 * BL], 0, BODY);
  ELL(at(0, .03 + s.backArch * .06 + s.tuck * .05, -.06 * BL), [.27 * B * fl * (1 + .14 * s.potBelly), .3 * B * fl * (1 - .18 * s.tuck + .14 * s.potBelly), .42 * BL], .15, BODY);
  ELL(at(0, .04, -.5 * BL), [.285 * B * fl, .31 * B * fl, .33 * BL], .15, BODY);
  if (s.potBelly) ELL(at(0, -.12 * B, -.05 * BL), [.25 * B, .2 * B, .3 * BL], .12, BODY);
  if (s.pouch) ELL(at(0, -.22 * B, -.42 * BL), [.17 * B, .09 * s.pouch + .02, .2 * BL], .1, BODY);
  // neck
  CONE(at(0, .1, .5 * BL), Hc.clone().add(V(0, -.35 * HS, -.15 * HS)), .21 * B * fl, .17 * fl * Math.max(.8, s.neckLen * .9), .08,
    { part: 'body', bones: p => { const t = segT(p, at(0, .1, .5 * BL), Hc); return [['Chest', 1 - sstep(.1, .5, t)], ['Neck', sstep(.1, .5, t)]]; } });
  // head (with the breed warps: wedge / triangle / flat face / roman nose / wrinkles)
  const HEAD = { part: 'head', bones: () => [['Head', 1]] };
  const hr = [HS * s.headW, HS * .9, HS * (.92 - .1 * flat)];
  F.add({
    d: (x, y, z) => {
      x -= Hc.x; y -= Hc.y; z -= Hc.z;
      const ny = y / (HS * .9), nz = z / (HS * .92), low = sstep(.35, -.9, ny), front = sstep(-.2, .9, nz);
      const ws = 1 - s.wedge * .36 * low - s.headTri * .22 * low;
      const X = x / ws, Y = y + HS * front * low * s.wedge * .08;
      let Z = z - HS * front * low * (s.wedge * .22 + s.headTri * .08) + HS * flat * .07 * sstep(.4, 1, nz) * sstep(.5, -.5, ny);
      if (s.romanNose) Z -= HS * s.romanNose * .07 * sstep(.25, .05, Math.abs(x / hr[0])) * sstep(-.4, -.1, ny) * sstep(.3, .05, ny) * sstep(.5, .9, nz);
      let d = sdEllipsoid(X, Y, Z, hr[0], hr[1], hr[2]) * Math.min(1, ws);
      if (s.wrinkles) {
        const m = sstep(.15, .3, ny) * sstep(.9, .62, ny) * sstep(0, .3, nz) * sstep(.62, .32, Math.abs(x / hr[0]));
        d -= HS * s.wrinkles * .04 * m * Math.pow(.5 + .5 * Math.sin(ny * 30 - 1), 3);
      }
      return d;
    }, box: [Hc.x - hr[0], Hc.y - hr[1], Hc.z - hr[2], Hc.x + hr[0], Hc.y + hr[1], Hc.z + hr[2]], k: .06, meta: HEAD,
  });
  const mzC = Hc.clone().add(V(0, -HS * .3 + flat * HS * .06, HS * (.72 - flat * .08)));
  const mzR = [HS * .4 * 1.2 * s.muzzleW * (1 - .25 * s.wedge), HS * .4 * .72 * (.75 + .25 * s.muzzleW), HS * .4 * .7 * s.muzzleLen * (1 - .5 * flat) * (1 + .25 * s.wedge)];
  ELL(mzC, mzR, .035, { part: 'muzzle', bones: () => [['Head', 1]] });
  if (s.cheek) for (const [, x] of SIDES) ELL(Hc.clone().add(V(x * HS * .5 * s.headW, -HS * .3, HS * (.36 - .06 * flat))), [HS * .38 * s.cheek ** .3, HS * .3, HS * .32], .06, HEAD);
  // legs
  const paws = [];
  const legMeta = (kind, bone, t0, t1, a, b) => ({ part: 'leg', kind, t0, t1, bones: () => [[bone, 1]] });
  for (const [S, x] of SIDES) {
    const sh = J[`UpperArm_${S}`], el = J[`Forearm_${S}`], wr = J[`Hand_${S}`], ball = J[`Fingers_${S}`];
    ELL(sh.clone().add(V(-x * .02, .04, -.02)), [.12 * B * thick / 1.18, .17, .15], .09, { part: 'body', bones: p => [['Scapula_' + S, .6], ['UpperArm_' + S, .4]] });
    CONE(sh, el, .1 * thick, .084 * thick, .03, legMeta('front', `UpperArm_${S}`, 0, .4));
    CONE(el, wr, .08 * thick, .064 * thick, .03, legMeta('front', `Forearm_${S}`, .4, .8));
    CONE(wr, ball, .06 * thick, .066 * thick, .025, legMeta('front', `Hand_${S}`, .8, .95));
    const hip = J[`Thigh_${S}`], kn = J[`Shin_${S}`], hk = J[`Foot_${S}`], hb = J[`Toes_${S}`];
    ELL(hip.clone().add(V(-x * .01, -.03, .0)), [.14 * B * thick / 1.18, .2, .19], .1, { part: 'body', bones: p => [['Hips', .45], ['Thigh_' + S, .55]] });
    CONE(hip, kn, .15 * thick, .095 * thick, .035, legMeta('hind', `Thigh_${S}`, 0, .35));
    CONE(kn, hk, .085 * thick, .06 * thick, .03, legMeta('hind', `Shin_${S}`, .35, .65));
    CONE(hk, hb, .056 * thick, .064 * thick, .025, legMeta('hind', `Foot_${S}`, .65, .95));
    // paws: a rounded mitt with four toe lumps (the pink beans are painted underneath)
    for (const [kind, b0, pre, tip] of [['front', ball, `Hand_${S}`, `Fingers_${S}`], ['hind', hb, `Foot_${S}`, `Toes_${S}`]]) {
      const pc = b0.clone().add(V(0, -.02 * pawR, .45 * pawR)), len = kind === 'hind' ? 1.35 : 1.25;
      const PAW = { part: 'paw', kind, pc, bones: p => { const f = sstep(-.3 * pawR, .25 * pawR, p.z - b0.z); return [[pre, 1 - f], [tip, f]]; } };
      paws.push({ pc, len, bones: PAW.bones });
      ELL(pc, [pawR, pawR * .62, pawR * len], .03, PAW);
      for (const [tx, tz] of [[-.42, .85], [-.14, 1], [.14, 1], [.42, .85]]) ELL(pc.clone().add(V(tx * pawR, .12 * pawR, tz * pawR * len * .75)), [pawR * .3, pawR * .32, pawR * .34], .02, PAW);
    }
  }
  // tail
  const tailR = k => {
    const plume = s.tailFluff ? 1 + s.tailFluff * (long ? 1.2 : .9) : 1;
    return (plume > 1 ? .068 * plume * (1 - .22 * k) * (k > .85 ? Math.sqrt(Math.max(.15, 1 - ((k - .85) / .2) ** 2)) : 1) : .068 - .026 * k) * (curly ? 1.2 : 1) * s.tailThick;
  };
  if (tailOn) {
    const n = bob ? 3 : tailN;
    for (let i = 0; i < n; i++) {
      const a = J[`Tail${i + 1}`], b = i < tailN - 1 ? J[`Tail${i + 2}`] : tailTip;
      CONE(a, b, tailR(i / n), tailR((i + 1) / n), i ? .025 : .07, { part: 'tail', t0: i / n, t1: (i + 1) / n, bones: p => [[`Tail${i + 1}`, 1]] });
    }
    if (bob) ELL(J.Tail4.clone().addScaledVector(tdir, .03), [.11 * fl, .1 * fl, .11 * fl], .04, { part: 'tail', t0: 1, t1: 1, bones: () => [['Tail3', .5], ['Tail4', .5]] });
  }
  // long and curly coats: sculpted clumps where cats carry long hair
  if (long || curly) {
    const amt = curly ? .8 : s.furAmount, ruff = Math.max(s.ruff, curly ? .7 : .4) * amt;
    ELL(at(0, -.06, .74 * BL), [.2 * B * fl, .17 * ruff + .05, .14], .08, { part: 'body', bones: spineW });   // chest bib
    for (let j = -1; j <= 1; j++) TUFT(at(j * .1 * B * fl, -.1, .78 * BL), V(j * .4, -1, .45), .16 * ruff, .085, { part: 'body', bones: spineW }, .06);
    for (const [, x] of SIDES) {
      for (const z of [-.25, .02, .28]) TUFT(at(x * .13 * B * fl, -.24 * B * fl, z * BL), V(x * .25, -1, -.2), .13 * amt, .07, { part: 'body', bones: spineW });
      const S = x > 0 ? 'L' : 'R', hip = J[`Thigh_${S}`], kn = J[`Shin_${S}`];
      for (const [u, k] of [[.35, 1], [.65, .8]]) TUFT(hip.clone().lerp(kn, u).add(V(x * .02, 0, -.07)), V(x * .2, -.5, -1), .17 * amt * k, .075, { part: 'leg', kind: 'hind', t0: .1, t1: .1, bones: () => [[`Thigh_${S}`, 1]] });
      // cheek ruff: overlapping round masses along the jaw (a soft mane, like a real Persian's), plus one
      // short flick at the widest point of the cheek
      const ff = (curly ? .9 : s.faceFluff) * amt;
      for (const [ang, k] of [[.55, .8], [.95, 1], [1.35, .95], [1.75, .8]]) {
        const d = V(x * Math.sin(ang), -.55, Math.cos(ang) * .9).normalize();
        ELL(Hc.clone().add(V(d.x * hr[0], d.y * hr[1], d.z * hr[2]).multiplyScalar(.82)), [HS * .22 * ff * k + .01, HS * .2 * ff * k + .01, HS * .2 * ff * k + .01], .07, HEAD);
      }
      { const d = V(x * .92, -.2, .3).normalize(); TUFT(Hc.clone().add(V(d.x * hr[0], d.y * hr[1], d.z * hr[2]).multiplyScalar(.92)), V(x, -.45, .1), HS * .26 * ff, HS * .13, HEAD, .05); }
    }
    if (tailOn && !bob && curly) for (let i = 3; i < tailN; i++) for (const [, x] of SIDES) {
      const k = (i - 2) / (tailN - 2), a = J[`Tail${i + 1}`];
      TUFT(a.clone().add(V(x * .03, 0, 0)), V(x, .15, -.5), (.1 + .1 * k) * Math.max(.6, s.tailFluff), .065, { part: 'tail', t0: (i + .5) / tailN, t1: (i + .5) / tailN, bones: () => [[`Tail${i + 1}`, 1]] }, .02);
    }
  }
  if (curly) {                               // small tight curls over the back and sides (not the face)
    const N = 150;
    for (let i = 0; i < N; i++) {
      const y = 1 - 2 * (i + .5) / N, r = Math.sqrt(1 - y * y), ph = i * 2.39996;
      const d = V(Math.cos(ph) * r, y, Math.sin(ph) * r);
      if (d.y < -.55) continue;
      const c = at(d.x * .27 * B * fl, .03 + d.y * .3 * B * fl, -.06 * BL + d.z * .78 * BL);
      ELL(c, [.045, .045, .045], .02, { part: 'body', bones: spineW });
    }
  }
  if (s.pawTuft) for (const [S] of SIDES) for (const b0 of [J[`Fingers_${S}`], J[`Toes_${S}`]]) for (const f of [-1, 0, 1]) {
    const isF = b0 === J[`Fingers_${S}`];
    TUFT(b0.clone().add(V(f * pawR * .5, -.3 * pawR, pawR * 1.3)), V(f * .3, -.2, 1), pawR * .9 * s.pawTuft, pawR * .32, { part: 'paw', kind: isF ? 'front' : 'hind', pc: b0, bones: () => [[(isF ? 'Fingers_' : 'Toes_') + S, 1]] }, .015);
  }

  // ------------------------------------------------------------------ 3. mesh
  const net = relax(F, surfaceNets(F, h));
  const n = net.pos.length / 3;

  // ------------------------------------------------------------------ 4+5. skin weights and paint
  const NB = BONE_NAMES.length, W = new Float32Array(n * NB), col = new Float32Array(n * 3);
  const p = V(), tmpC = new THREE.Color();
  const blush = SIDES.map(([, x]) => Hc.clone().add(V(x * HS * .62, -HS * .2, HS * .72)));
  const paint = (meta, p) => {
    const dir = () => p.clone().sub(Hc).normalize();
    if (meta.part === 'head' || meta.part === 'muzzle') {
      const c = colorAt(meta.part, p, { dir: dir() });
      if (meta.part === 'head') for (const b of blush) {               // painted blush on the cheeks
        const q = p.clone().sub(b), m = .55 * sstep(1, .45, Math.hypot(q.x / (HS * .2), q.y / (HS * .09), q.z / (HS * .12)));
        if (m > 0) c.lerp(C('#f7a3b6'), m);
      }
      if (s.wrinkles && meta.part === 'head') {
        const q = p.clone().sub(Hc), ny = q.y / (HS * .9), nz = q.z / (HS * .92);
        const m = sstep(.15, .3, ny) * sstep(.9, .62, ny) * sstep(0, .3, nz) * sstep(.62, .32, Math.abs(q.x / hr[0]));
        c.multiplyScalar(1 - .35 * m * (1 - Math.pow(.5 + .5 * Math.sin(ny * 30 - 1), 3)));
      }
      return c;
    }
    if (meta.part === 'body') return colorAt('body', p, { nx: p.x / (.32 * B), ny: (p.y - bodyY) / (.34 * B), nz: p.z / (.9 * BL) });
    if (meta.part === 'leg') return colorAt('leg', p, { kind: meta.kind, t: meta.a ? meta.t0 + (meta.t1 - meta.t0) * segT(p, meta.a, meta.b) : meta.t0 });
    if (meta.part === 'paw') {
      const q = p.clone().sub(meta.pc);
      return colorAt('leg', p, { kind: meta.kind, t: 1 });
    }
    if (meta.part === 'tail') return colorAt('tail', p, { t: meta.a ? meta.t0 + (meta.t1 - meta.t0) * segT(p, meta.a, meta.b) : meta.t0 });
    return colorAt('body', p, { nx: 0, ny: 0, nz: 0 });
  };
  for (let i = 0; i < n; i++) {
    p.set(net.pos[i * 3], net.pos[i * 3 + 1], net.pos[i * 3 + 2]);
    const near = F.near(p.x, p.y, p.z, .1);
    let dmin = 1e9; for (const [, d] of near) dmin = Math.min(dmin, d);
    let cr = 0, cg = 0, cb = 0, cw = 0;
    for (const [pr, d] of near) {
      const wc = Math.exp(-(d - dmin) / .006), ws = Math.exp(-(d - dmin) / .03);
      if (wc > .02) { tmpC.copy(paint(pr.meta, p)); cr += tmpC.r * wc; cg += tmpC.g * wc; cb += tmpC.b * wc; cw += wc; }
      if (ws > .01) for (const [bn, bw] of pr.meta.bones(p)) if (bw > 0) W[i * NB + BI[bn]] += ws * bw;
    }
    let c = tmpC.setRGB(cr / cw, cg / cw, cb / cw);
    // a little fur variation: soft streaks along the coat (long), fine mottling (short), waves (rex)
    const fn = long ? .92 + .1 * vnoise(p.x * 9, p.y * 26, p.z * 9) : hairless ? 1 : .95 + .07 * vnoise(p.x * 40, p.y * 40, p.z * 40);
    c.multiplyScalar(s.fur === 'rex' ? fn * (.95 + .06 * Math.sin(p.z * 60 + p.y * 30 + 4 * vnoise(p.x * 6, p.y * 6, p.z * 6))) : fn);
    col.set([c.r, c.g, c.b], i * 3);
    // breathing: the belly bone takes the underside of the trunk
    const ny = (p.y - bodyY) / (.34 * B), nz = p.z / (.9 * BL);
    const bw = .6 * sstep(-.1, -.7, ny) * sstep(.75, .3, Math.abs(nz + .05));
    if (bw > 0) { let sum = 0; for (let k = 0; k < NB; k++) sum += W[i * NB + k]; for (let k = 0; k < NB; k++) W[i * NB + k] *= (1 - bw); W[i * NB + BI.Belly] += bw * sum; }
    // shoulder blades: the skin over the withers rides on the scapulae
    for (const [S] of SIDES) {
      const d = segDist(p, J[`Scapula_${S}`], J[`UpperArm_${S}`]), w = .7 * sstep(.16, .06, d) * sstep(-.05, .08, p.y - J[`UpperArm_${S}`].y);
      if (w > 0) { let sum = 0; for (let k = 0; k < NB; k++) sum += W[i * NB + k]; for (let k = 0; k < NB; k++) W[i * NB + k] *= (1 - w); W[i * NB + BI[`Scapula_${S}`]] += w * sum; }
    }
  }
  // smooth the weights across the surface (soft joints), then keep the 4 strongest per vertex
  const tmpW = new Float32Array(W.length);
  for (let it = 0; it < 6; it++) {
    for (let i = 0; i < n; i++) {
      const a = net.adj[i];
      for (let k = 0; k < NB; k++) { let sum = 0; for (const j of a) sum += W[j * NB + k]; tmpW[i * NB + k] = .5 * W[i * NB + k] + .5 * (a.length ? sum / a.length : W[i * NB + k]); }
    }
    W.set(tmpW);
  }
  const top4 = (wrow) => {
    const order = Array.from(wrow.keys()).sort((a, b) => wrow[b] - wrow[a]).slice(0, 4);
    const sum = order.reduce((s2, k) => s2 + wrow[k], 0) || 1;
    return [order, order.map(k => wrow[k] / sum)];
  };

  // body geometry + ears (merged so the coat shares one material)
  const bodyPos = [], bodyCol = [], bodyIdx = [], bodySI = [], bodySW = [];
  for (let i = 0; i < n; i++) {
    bodyPos.push(net.pos[i * 3], net.pos[i * 3 + 1], net.pos[i * 3 + 2]); bodyCol.push(col[i * 3], col[i * 3 + 1], col[i * 3 + 2]);
    const [o, w] = top4(W.subarray(i * NB, i * NB + NB)); for (let k = 0; k < 4; k++) { bodySI.push(o[k] ?? 0); bodySW.push(w[k] ?? 0); }
  }
  for (const k of net.idx) bodyIdx.push(k);
  const addGeo = (g, colorFn, bone) => {
    const base = bodyPos.length / 3, q = g.attributes.position, nrm = g.index ? g.index.array : Array.from({ length: q.count }, (_, i) => i);
    for (let i = 0; i < q.count; i++) {
      const v = V(q.getX(i), q.getY(i), q.getZ(i)), c = colorFn(v, i);
      bodyPos.push(v.x, v.y, v.z); bodyCol.push(c.r, c.g, c.b); bodySI.push(BI[bone], 0, 0, 0); bodySW.push(1, 0, 0, 0);
    }
    for (const k of nrm) bodyIdx.push(base + k);
  };
  for (const [S, x] of SIDES) {
    const es = Math.pow(s.earSize, .65) * HS / .42 * .82 * (s.ear === 'large' ? 1.15 : 1);
    const eh = .36 * es, er0 = .16 * es * s.earWide, y0 = .08 * es - eh / 2;
    const prof = [];
    for (let k = 0; k <= 16; k++) { const t = k / 16; prof.push(new THREE.Vector2(Math.max(1e-4, er0 * Math.pow(1 - t, 1 - .6 * s.earRound) * (1 - .12 * Math.sin(Math.PI * t))), -eh / 2 + eh * t)); }
    const eg = new THREE.LatheGeometry(prof, 36); eg.translate(0, .08 * es, 0);
    const local = eg.attributes.position.clone();
    eg.applyMatrix4(ears[S].matrix);
    addGeo(eg, (v, i) => {
      const lx = local.getX(i), ly = local.getY(i), lz = local.getZ(i);
      const y = (ly - y0) / eh, rr = Math.max(1e-4, er0 * (1 - y));
      const c = colorAt('ear', v, { dir: v.clone().sub(Hc).normalize() });
      const inner = sstep(.62, .82, lz / rr) * sstep(.55, .38, Math.abs(lx) / rr) * sstep(.28, .38, y) * sstep(.78, .66, y);
      c.lerp(colorAt('innerEar'), .8 * inner);
      if (coatSpec.earSpot) c.lerp(C('#f4efe4'), .9 * sstep(-.45, -.75, lz / rr) * sstep(.45, .2, Math.abs(lx) / rr) * sstep(.25, .35, y) * sstep(.62, .5, y));
      return c;
    }, `Ear_${S}`);
    if (s.earTuft) { const t = new THREE.ConeGeometry(.022 * es, .09 * es, 8); t.translate(0, .3 * es, 0); t.applyMatrix4(ears[S].matrix); addGeo(t, v => colorAt('ear', v, { dir: V(0, 1, 0) }).multiplyScalar(.55), `Ear_${S}`); }
    if (long && s.ear !== 'fold') {           // pale furnishings inside the ear
      const t = new THREE.ConeGeometry(.026 * es, .13 * es, 8); t.rotateX(-.55); t.translate(0, .07 * es, .04 * es); t.applyMatrix4(ears[S].matrix);
      addGeo(t, () => C('#f5efe4'), `Ear_${S}`);
    }
  }

  // ------------------------------------------------------------------ 6. face mesh (Head bone) with blendshapes
  const face = { pos: [], col: [], uv: [], idx: [[], [], []], blink: [], mouth: [], si: [], sw: [] };
  // face texture atlas: a 4x4 grid of colour slots (flat colours, or a vertical gradient for the eyes), so every
  // face part has clean texture space - an automatic unwrap shreds thin whiskers into thousands of islands
  const atlas = { slots: [], keys: new Map() };
  const slotOf = (key, spec) => { if (!atlas.keys.has(key)) { atlas.keys.set(key, atlas.slots.length); atlas.slots.push(spec); } return atlas.keys.get(key); };
  const slotUV = (i, v = .5) => [((i % 4) + .5) / 4, 1 - (Math.floor(i / 4) + .12 + .76 * v) / 4];
  // add geometry in a local frame given by matrix M; morph(fnLocal) returns the target position in the same frame
  const faceAdd = (g, color, M, group, morphs = {}, grad = null, skin = null) => {
    const slot = grad ? slotOf('g' + grad.color.getHexString(), { grad: grad.color }) : slotOf('f' + color.getHexString(), { flat: color });
    const base = face.pos.length / 3, q = g.attributes.position, lst = g.index ? g.index.array : Array.from({ length: q.count }, (_, i) => i);
    for (let i = 0; i < q.count; i++) {
      const lv = V(q.getX(i), q.getY(i), q.getZ(i)), wv = lv.clone().applyMatrix4(M);
      face.pos.push(wv.x, wv.y, wv.z);
      face.col.push(color.r, color.g, color.b);
      face.uv.push(...slotUV(slot, grad ? clampV((1 - lv.y / grad.h) / 2) : .5));
      for (const key of ['blink', 'mouth']) {
        const t = morphs[key] ? morphs[key](lv.clone()).applyMatrix4(M).sub(wv) : V();
        face[key].push(t.x, t.y, t.z);
      }
      const sk = skin ? skin(wv) : [['Head', 1]];
      for (let j = 0; j < 4; j++) { face.si.push(sk[j] ? BI[sk[j][0]] : 0); face.sw.push(sk[j] ? sk[j][1] : 0); }
    }
    for (const k of lst) face.idx[group].push(base + k);
  };
  const clampV = v => Math.min(1, Math.max(0, v));
  const ell = (r, sx, sy, sz, seg = 40) => { const g = new THREE.SphereGeometry(r, seg, Math.round(seg * .7)); g.scale(sx, sy, sz); return g; };
  const HM = new THREE.Matrix4().makeTranslation(Hc.x, Hc.y, Hc.z);
  {
    const [sx, sy, sharp] = { round: [.84, 1.04, 0], oval: [.9, .86, .08], almond: [1, .72, .32], lemon: [1.04, .76, .48] }[s.eyeShape] || [.84, 1.04, 0];
    const sz = .42;
    for (const [, x] of SIDES) {
      const er = HS * .2 * s.eyeSize * (1 + .55 * (1.04 - sy));
      const ex = x * HS * .37 * s.eyeSpace, ey = HS * .02, ez = HS * (.84 - flat * .05 + (curly ? .03 : long ? .02 : 0));
      const M = HM.clone().multiply(new THREE.Matrix4().compose(V(ex, ey, ez), new THREE.Quaternion().setFromEuler(new THREE.Euler(0, 0, x * s.eyeTilt)), V(1, 1, 1)));
      const pinch = g => { const q = g.attributes.position; for (let i = 0; i < q.count; i++) { const u = q.getX(i) / (er * sx * 1.1); q.setY(i, q.getY(i) * (1 - sharp * u * u)); } return g; };
      // closed eye: squashed onto a soft 'U' line (AC sleepy eye), highlights tucked behind
      const shut = (v, back = 0) => { const u = v.x / (er * sx); v.y = -.1 * er + .16 * er * u * u + (v.y + .1 * er) * .07; v.z -= back; return v; };
      // eye: dark glossy top fading to the breed's eye colour underneath (gradient slot in the atlas)
      faceAdd(pinch(ell(er, sx, sy, sz, 32)), C('#ffffff'), M, 0, { blink: v => shut(v) }, { color: colorAt(x > 0 ? 'eye2' : 'eye'), h: er * sy });
      const lidY = 1 - 2 * s.eyeLid;
      if (s.eyeLid > 0) {
        const lg = new THREE.SphereGeometry(er * 1.06, 28, 10, 0, Math.PI * 2, 0, Math.acos(lidY)); lg.scale(sx, sy, sz * 1.3); pinch(lg); lg.rotateZ(-x * s.eyeTilt);   // lid line stays level on a slanted eye
        const lidC = V(0, er * .9, 0).applyMatrix4(M);
        faceAdd(lg, colorAt('head', lidC, { dir: lidC.clone().sub(Hc).normalize() }), M, 2, { blink: v => shut(v, .03 * er) });
      }
      if (s.eyeLiner) { const lr = new THREE.TorusGeometry(er, er * .085, 6, 40); lr.scale(sx * 1.03, sy * 1.03, .6); faceAdd(pinch(lr), C('#2b2220'), M, 0, { blink: v => shut(v) }); }
      const cs = Math.cos(-x * s.eyeTilt), sn = Math.sin(-x * s.eyeTilt), hsz = Math.min(1, sy + .1);
      for (const [dx, dy, rr] of [[-.3, .34, .3], [.3, -.34, .12]]) {
        let hx = (dx * cs - dy * sn) * er * sx, hy = (dx * sn + dy * cs) * er * sy;
        hy = Math.min(hy, (lidY * sy - .26) * er);
        const yy = hy / (1 - sharp * (hx / (er * sx * 1.1)) ** 2), k = 1 - (hx / (er * sx)) ** 2 - (yy / (er * sy)) ** 2;
        const hz = er * sz * Math.sqrt(Math.max(0, k)) + er * .015;
        const dg = new THREE.CircleGeometry(er * rr * hsz, 16);
        dg.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(V(0, 0, 1), V(hx / sx ** 2, yy / sy ** 2, hz / sz ** 2).normalize())); dg.translate(hx, hy, hz);
        faceAdd(dg, C('#ffffff'), M, 1, { blink: v => v.set(hx * .5, -.1 * er, -er * .5) });
      }
    }
    // nose, mouth, tongue, mouth cavity, whiskers
    const nz = HS * (.97 - flat * .14) * Math.min(1, s.muzzleLen * .1 + .9), my = -HS * .2 + flat * HS * .1, mz = HS * (.93 - flat * .14);
    const ng = ell(HS * .072, 1.3, .78, .6, 20); ng.translate(0, -HS * .14 + flat * HS * .1, nz);
    faceAdd(ng, colorAt('nose'), HM, 0);
    for (const [, x] of SIDES) {
      const m = new THREE.TorusGeometry(HS * .055, HS * .014, 8, 20, Math.PI); m.rotateZ(Math.PI); m.translate(x * HS * .055, my, mz);
      faceAdd(m, C('#3a2a24'), HM, 0, { mouth: v => v.add(V(x * HS * .03, -HS * .07, -HS * .01)) });
    }
    const cav = ell(HS * .1, 1, .85, .45, 16); const cavC = V(0, my - HS * .1, mz - HS * .03);
    faceAdd(cav, C('#5a2b2e'), HM, 0, { mouth: v => v.add(cavC) });
    // the cavity sits collapsed inside the muzzle until MouthOpen / Tongue push it out: base = one hidden point,
    // morph targets = the full shape (the deltas computed above are re-based on that point)
    {
      const a0 = face.pos.length / 3 - cav.attributes.position.count, hp = V(0, my, mz - HS * .2).applyMatrix4(HM);
      for (let i = a0; i < face.pos.length / 3; i++) {
        const k = i * 3, wv = V(face.pos[k], face.pos[k + 1], face.pos[k + 2]);
        face.mouth[k] += wv.x - hp.x; face.mouth[k + 1] += wv.y - hp.y; face.mouth[k + 2] += wv.z - hp.z;
        face.pos[k] = hp.x; face.pos[k + 1] = hp.y; face.pos[k + 2] = hp.z;
      }
    }
    // tongue: flat, rounded tip, a groove down the middle (cats' tongues are long and spoon-like);
    // built along +z from the Tongue1 joint and skinned to the three tongue bones
    {
      const tw = HS * .09, tt = HS * .03, g = new THREE.SphereGeometry(1, 20, 14);
      const q = g.attributes.position;
      for (let i = 0; i < q.count; i++) {
        let x = q.getX(i), y = q.getY(i), z = q.getZ(i);
        const u = (z + 1) / 2;                                   // 0 root .. 1 tip
        const w = tw * (.75 + .3 * Math.sin(Math.PI * Math.min(1, u * 1.15)));
        x *= w; y *= tt; z = u * tongueLen;
        if (y > 0) y -= HS * .012 * Math.exp(-((x / (HS * .022)) ** 2));   // the central groove
        q.setXYZ(i, x, y, z);
      }
      g.computeVertexNormals();
      const TM = new THREE.Matrix4().makeTranslation(J.Tongue1.x, J.Tongue1.y, J.Tongue1.z);
      faceAdd(g, C('#ee8996'), TM, 0, {}, null, wv => {
        const u = (wv.z - J.Tongue1.z) / tongueLen, a = sstep(.22, .45, u), b = sstep(.55, .8, u);
        return [['Tongue1', 1 - a], ['Tongue2', a - b], ['Tongue3', b]].filter(e => e[1] > 1e-3);
      });
    }
    if (s.whisker !== 'none') for (const [, x] of SIDES) for (let w = 0; w < 3; w++) {
      const pts = [], a = (w - 1) * .22, cw = s.whisker === 'curly', Lw = HS * (cw ? .6 : .85);
      for (let k = 0; k <= 16; k++) {
        const t = k / 16, wob = cw ? Math.sin(t * 9) * HS * .05 * t : 0;
        pts.push(V(x * (HS * .28 + t * Lw), -HS * (.24 - Math.sin(a) * .6 * t) - t * t * HS * .12 + wob, HS * (.86 - flat * .14) - t * HS * .35));
      }
      faceAdd(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 12, HS * .012, 4, false), C(coatSpec.whiskerColor || '#fbf8f2'), HM, 0);
    }
  }

  // ------------------------------------------------------------------ 7. toe beans (raised pads under every paw)
  {
    const base = C(coatSpec.base || '#999999'), lum = .2126 * base.r + .7152 * base.g + .0722 * base.b;
    const padC = C(coatSpec.pad || (lum < .03 ? '#5b4349' : '#f2a0ae'));   // pink beans; dusky beans on black cats
    for (const { pc, len, bones: pb } of paws) {
      // each pad is found on the real sculpted underside (search upward from below the paw for the surface),
      // turned to the surface normal and pressed half into it, so it reads as a soft raised bean
      const pads = [[-.5, .62, .25, .22], [-.18, .8, .26, .23], [.18, .8, .26, .23], [.5, .62, .25, .22],   // 4 toe beans
        [0, -.02, .4, .3]];                                                                                   // main (palm) pad
      for (const [bx, bz, rx, rz] of pads) {
        const x = pc.x + bx * pawR, z = pc.z + bz * pawR * len;
        let lo = pc.y - pawR * 2, hi = pc.y;
        if (F.eval(x, lo, z) <= 0 || F.eval(x, hi, z) > 0) continue;
        for (let it = 0; it < 30; it++) { const m = (lo + hi) / 2; if (F.eval(x, m, z) > 0) lo = m; else hi = m; }
        const c = V(x, hi, z), n = V(...F.grad(x, hi, z)).normalize();
        if (n.y > -.3) continue;                                         // not on the underside
        const M = new THREE.Matrix4().compose(c.clone().addScaledVector(n, pawR * .03), new THREE.Quaternion().setFromUnitVectors(V(0, -1, 0), n), V(1, 1, 1));
        const g = ell(pawR, rx, .13, rz, 10);
        if (bz < .3) {                                                   // the palm pad is a soft three-lobed shape
          const q = g.attributes.position;
          for (let i = 0; i < q.count; i++) { const px = q.getX(i) / (pawR * rx), pz = q.getZ(i) / (pawR * rz); q.setZ(i, q.getZ(i) * (1 - .3 * Math.exp(-((px / .25) ** 2)) * Math.max(0, -pz))); }   // notch at the back
        }
        faceAdd(g, padC, M, 0, {}, null, () => pb(c).filter(e => e[1] > 1e-3));
      }
    }
  }

  // ------------------------------------------------------------------ assemble, scale to metres, bind
  const S = METERS * s.size;
  const bones = {};
  for (const name of BONE_NAMES) {
    const b = new THREE.Bone(); b.name = name;
    const par = BONE_PARENTS[name];
    b.position.copy(J[name]).sub(par ? J[par] : V()).multiplyScalar(S);
    if (par) bones[par].add(b);
    bones[name] = b;
  }
  const skeleton = new THREE.Skeleton(BONE_NAMES.map(n2 => bones[n2]));
  const mkGeo = (pos, colr, idx, si, sw) => {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos.map(v => v * S), 3));
    g.setAttribute('color', new THREE.Float32BufferAttribute(colr, 3));
    g.setAttribute('skinIndex', new THREE.Uint16BufferAttribute(si, 4));
    g.setAttribute('skinWeight', new THREE.Float32BufferAttribute(sw, 4));
    g.setIndex(idx); g.computeVertexNormals();
    return g;
  };
  const bodyGeo = mkGeo(bodyPos, bodyCol, bodyIdx, bodySI, bodySW);
  const nf = face.pos.length / 3;
  const faceIdx = face.idx[0].concat(face.idx[1], face.idx[2]);
  const faceGeo = mkGeo(face.pos, face.col, faceIdx, face.si, face.sw);
  faceGeo.setAttribute('uv', new THREE.Float32BufferAttribute(face.uv, 2));
  let faceMap = null;
  if (typeof document !== 'undefined') {
    const N = 64, cv = document.createElement('canvas'); cv.width = cv.height = N;
    const cx = cv.getContext('2d'), sz2 = N / 4, dark = C('#271c18');
    atlas.slots.forEach((sp, i) => {
      const x0 = (i % 4) * sz2, y0 = Math.floor(i / 4) * sz2;
      for (let r = 0; r < sz2; r++) {
        const v = clampV(((r + .5) / sz2 - .12) / .76), y = 1 - 2 * v;   // same mapping as slotUV
        const c = sp.flat || dark.clone().lerp(sp.grad, .72 * sstep(.1, -.85, y));
        cx.fillStyle = '#' + c.getHexString(); cx.fillRect(x0, y0 + r, sz2, 1);
      }
    });
    faceMap = new THREE.CanvasTexture(cv); faceMap.colorSpace = THREE.SRGBColorSpace; faceMap.name = 'FaceAtlas';
    faceGeo.deleteAttribute('color');              // the atlas carries the colours now
  }
  faceGeo.morphTargetsRelative = true;
  faceGeo.morphAttributes.position = ['blink', 'mouth'].map(k => new THREE.Float32BufferAttribute(face[k].map(v => v * S), 3));
  faceGeo.addGroup(0, face.idx[0].length, 0); faceGeo.addGroup(face.idx[0].length, face.idx[1].length, 1); faceGeo.addGroup(face.idx[0].length + face.idx[1].length, face.idx[2].length, 2);
  const coatMat = new THREE.MeshStandardMaterial({ name: 'Coat', vertexColors: true, roughness: hairless ? .55 : coatSpec.sheen ? .9 - .5 * coatSpec.sheen : .9, metalness: 0 });
  const faceMats = [
    new THREE.MeshStandardMaterial({ name: 'FaceGloss', vertexColors: !faceMap, map: faceMap, roughness: .3, metalness: 0 }),
    new THREE.MeshBasicMaterial({ name: 'EyeHighlight', color: '#ffffff' }),
    new THREE.MeshStandardMaterial({ name: 'Lid', vertexColors: !faceMap, map: faceMap, roughness: .9, metalness: 0 }),
  ];
  const body = new THREE.SkinnedMesh(bodyGeo, coatMat); body.name = 'Body';
  const faceMesh = new THREE.SkinnedMesh(faceGeo, faceMats); faceMesh.name = 'Face';
  faceMesh.morphTargetDictionary = { Blink: 0, MouthOpen: 1 };
  faceMesh.morphTargetInfluences = [0, 0];
  const group = new THREE.Group(); group.name = opts.name || 'Cat';
  group.add(bones.Root, body, faceMesh);
  group.updateMatrixWorld(true);              // bones need their rest world matrices before the bind inverses are taken
  skeleton.calculateInverses();
  for (const m of [body, faceMesh]) { m.castShadow = m.receiveShadow = true; m.frustumCulled = false; m.bind(skeleton, new THREE.Matrix4()); }

  // dimensions the motion engine needs (metres)
  const toM = v => v.clone().multiplyScalar(S);
  const dims = {
    S, pawR: pawR * S, ballH: ballH * S, bodyY: bodyY * S, HS: HS * S, Hc: toM(Hc), toeLen: pawR * 1.25 * S,
    joints: Object.fromEntries(Object.entries(J).map(([k, v]) => [k, toM(v)])),
    len: { lH: lH * S, lR: lR * S, lMc: lMc * S, lF: lF * S, lT: lT * S, lMt: lMt * S },
    shoulderH: shoulderH * S, hipH: hipH * S, tongueLen: tongueLen * S, mouth: toM(Hc.clone().add(V(0, mY, mZ))), tail: s.tail, shape: s,
  };
  group.userData = { dims, bones, skeleton, meshes: { body, face: faceMesh }, tris: (bodyIdx.length + faceIdx.length) / 3 };
  return group;
}
