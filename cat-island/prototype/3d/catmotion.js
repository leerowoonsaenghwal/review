// Cat Island motion data: keyframed clips for the v2 model, baked at 30 fps.
//
// A pose is a flat set of channels (hips, spine, neck, head, shoulder blades, four paw targets, tail, ears,
// face). Clips are written like an animator's dope sheet: key poses with timing and easing, plus procedural
// layers that are hard to key by hand (gait foot placement, breathing, blinks). Every frame is then solved
// onto the skeleton and stored as bone rotations, so the result is ordinary animation data (glTF/FBX clips).
//
// Real-cat references used for timing (scaled to the toy cat's leg length by dynamic similarity, i.e. the
// same Froude number v^2 / (g * hip height) as a real cat):
//   walk  0.62 m/s, stride 0.45-0.49 m, cycle 0.8 s, stance 60 % fore / 55 % hind, lateral sequence
//         (Stadig & Bergh 2013, BMC Vet Res 9:129, pressure-walkway data from 18 healthy cats)
//   trot  diagonal pairs, stance ~45 %;  gallop: rotary (hind L, hind R, fore R, fore L), stance ~30 %
//   stance phase details: paw lands, wrist/hock "yield" under load, then the heel lifts and the toes
//   peel off last; in swing the front paw folds back at the wrist, then reaches forward before landing.
import * as THREE from 'three';
import { makeContact, LIMBS } from './catcontact.js';
import { BOWL_SURF, TOY_TOP } from './items.js';

export const FPS = 30;
const TAU = Math.PI * 2, G = 9.81;
const clamp = (x, a, b) => Math.min(b, Math.max(a, x));
const lerp = (a, b, t) => a + (b - a) * t;
const ss = t => { t = clamp(t, 0, 1); return t * t * (3 - 2 * t); };
const mj = t => { t = clamp(t, 0, 1); return t * t * t * (10 + t * (-15 + 6 * t)); };   // minimum-jerk ease
const seg = (t, a, b) => clamp((t - a) / (b - a), 0, 1);
const frac = x => x - Math.floor(x);
const bump = (t, c, w) => Math.exp(-(((t - c) / w) ** 2));
const V = (x = 0, y = 0, z = 0) => new THREE.Vector3(x, y, z);

// monotone cubic key interpolation (no overshoot; equal neighbouring keys give a clean hold)
export function K(keys, t) {
  if (t <= keys[0][0]) return keys[0][1];
  const n = keys.length;
  if (t >= keys[n - 1][0]) return keys[n - 1][1];
  let i = 0; while (keys[i + 1][0] < t) i++;
  const [t0, v0] = keys[i], [t1, v1] = keys[i + 1], h = t1 - t0, u = (t - t0) / h;
  const slope = j => j < 0 || j >= n - 1 ? 0 : (keys[j + 1][1] - keys[j][1]) / (keys[j + 1][0] - keys[j][0]);
  const tan = j => { const a = slope(j - 1), b = slope(j); if (j === 0 || j === n - 1 || a * b <= 0) return 0; return 2 / (1 / a + 1 / b); };
  const m0 = tan(i) * h, m1 = tan(i + 1) * h, u2 = u * u, u3 = u2 * u;
  return (2 * u3 - 3 * u2 + 1) * v0 + (u3 - 2 * u2 + u) * m0 + (-2 * u3 + 3 * u2) * v1 + (u3 - u2) * m1;
}
const blinkAt = (t, times, len = .16) => Math.max(0, ...times.map(c => bump(t, c, len / 2.5)));

// ------------------------------------------------------------------ rig
const FEET = { FL: ['Scapula_L', 'UpperArm_L', 'Forearm_L', 'Hand_L', 'Fingers_L', 1, 'front'], FR: ['Scapula_R', 'UpperArm_R', 'Forearm_R', 'Hand_R', 'Fingers_R', -1, 'front'],
  HL: [null, 'Thigh_L', 'Shin_L', 'Foot_L', 'Toes_L', 1, 'hind'], HR: [null, 'Thigh_R', 'Shin_R', 'Foot_R', 'Toes_R', -1, 'hind'] };
export const FOOT_KEYS = Object.keys(FEET);

export function makeRig(model) {
  const B = model.userData.bones, d = model.userData.dims;
  const rest = Object.fromEntries(Object.entries(B).map(([n, b]) => [n, b.position.clone()]));
  const J = d.joints;
  const len = n => rest[n].length();
  const rig = {
    model, B, d, J, rest, face: model.userData.meshes.face,
    L: { F: [len('Forearm_L'), len('Hand_L'), len('Fingers_L')], H: [len('Shin_L'), len('Foot_L'), len('Toes_L')] },
    ballH: d.ballH, hipY: J.Hips.y, chestY: J.Chest.y, torso: J.Chest.z - J.Hips.z,
    restFoot: { FL: J.Fingers_L, FR: J.Fingers_R, HL: J.Toes_L, HR: J.Toes_R },
  };
  // how far a paw can reach fore and aft from under its shoulder / hip at standing height
  const reach = (pivot, chain, ball) => { const L = chain.reduce((a, b) => a + b, 0) * .97, H = pivot.y - ball.y; return Math.sqrt(Math.max(0, L * L - H * H)); };
  rig.reachF = reach(J.UpperArm_L, [rig.L.F[0], rig.L.F[1], rig.L.F[2]], J.Fingers_L);
  rig.reachH = reach(J.Thigh_L, [rig.L.H[0], rig.L.H[1], rig.L.H[2]], J.Toes_L);
  rig.hipH = J.Thigh_L.y;
  // the sculpted paw bottoms reach a little below the paw-ball height: lift the ground targets by that much
  // so planted paws stand on the floor instead of sinking into it
  const pa = model.userData.meshes.body.geometry.attributes.position; let low = Infinity;
  for (let i = 0; i < pa.count; i++) low = Math.min(low, pa.getY(i));
  rig.floorLift = Math.max(0, -low); rig.ballH += rig.floorLift;
  return rig;
}

// the neutral standing pose
export function stand(rig) {
  const P = {
    rootX: 0, rootY: 0, rootZ: 0, rootYaw: 0,
    hipX: 0, hipY: 0, hipZ: 0, hipPitch: 0, hipYaw: 0, hipRoll: 0,
    spPitch: 0, spYaw: 0, spRoll: 0, nkPitch: 0, nkYaw: 0, nkRoll: 0, hdPitch: 0, hdYaw: 0, hdRoll: 0,
    scapL: 0, scapR: 0, scapLy: 0, scapRy: 0,
    tailBase: 0, tailYaw: 0, tailBend: .04, tailCurl: 0, tailTip: 0, tailWave: 0, tailWph: 0,
    earLp: 0, earLy: 0, earLr: 0, earRp: 0, earRy: 0, earRr: 0,
    blink: 0, mouth: 0, tongue: 0, tongueBend: 0, tongueCurl: 0, tongueSpread: 0, tongueYaw: 0, tongueStretch: 0, breath: 0,
  };
  for (const f of FOOT_KEYS) {
    const r = rig.restFoot[f];
    Object.assign(P, { [f + 'x']: r.x, [f + 'y']: rig.ballH, [f + 'z']: r.z, [f + 'a']: f[0] === 'F' ? .3 : .2, [f + 't']: 0, [f + 'fk']: 0, [f + 'k1']: 0, [f + 'k2']: 0, [f + 'k3']: 0, [f + 'k4']: 0, [f + 'kz']: 0 });
  }
  return P;
}
export const mix = (A, B, k) => { const o = {}; for (const key in A) o[key] = lerp(A[key], B[key] ?? A[key], k); return o; };
const over = (P, ...o) => Object.assign({ ...P }, ...o);

// ------------------------------------------------------------------ solve a pose onto the bones
const _q = new THREE.Quaternion(), _q2 = new THREE.Quaternion(), _e = new THREE.Euler();
const qe = (x, y, z, q = new THREE.Quaternion()) => q.setFromEuler(_e.set(x, y, z, 'YXZ'));
function setDir(bone, off, dir) {
  const pq = bone.parent.getWorldQuaternion(new THREE.Quaternion());
  const v0 = off.clone().normalize().applyQuaternion(pq);
  const sw = new THREE.Quaternion().setFromUnitVectors(v0, dir.clone().normalize());
  bone.quaternion.copy(pq.clone().invert().multiply(sw).multiply(pq));
  bone.updateMatrixWorld(true);
}
function midJoint(A, W, l1, l2, pole) {
  const AW = W.clone().sub(A), D = clamp(AW.length(), Math.abs(l1 - l2) + 1e-4, l1 + l2 - 1e-4), n = AW.normalize();
  const a = (l1 * l1 - l2 * l2 + D * D) / (2 * D), h = Math.sqrt(Math.max(0, l1 * l1 - a * a));
  const pp = pole.clone().addScaledVector(n, -n.dot(pole)).normalize();
  return A.clone().addScaledVector(n, a).addScaledVector(pp, h);
}

// tailPoses: optional per-joint poses (time-lagged for follow-through); earPose likewise
export function applyPose(rig, P, lag = {}) {
  const { B, rest, model } = rig;
  for (const n in B) { B[n].quaternion.identity(); B[n].position.copy(rest[n]); B[n].scale.set(1, 1, 1); }
  B.Root.position.set(P.rootX, P.rootY, P.rootZ); B.Root.quaternion.setFromAxisAngle(V(0, 1, 0), P.rootYaw);
  B.Hips.position.add(V(P.hipX, P.hipY, P.hipZ)); qe(P.hipPitch, P.hipYaw, P.hipRoll, B.Hips.quaternion);
  [['Spine1', .3], ['Spine2', .35], ['Chest', .35]].forEach(([n, w]) => qe(P.spPitch * w, P.spYaw * w, P.spRoll * w, B[n].quaternion));
  qe(P.nkPitch, P.nkYaw, P.nkRoll, B.Neck.quaternion);
  B.Scapula_L.quaternion.setFromAxisAngle(V(1, 0, 0), P.scapL); B.Scapula_L.position.y += P.scapLy;
  B.Scapula_R.quaternion.setFromAxisAngle(V(1, 0, 0), P.scapR); B.Scapula_R.position.y += P.scapRy;
  B.Belly.scale.set(1 + .05 * P.breath, 1 + .09 * P.breath, 1 + .03 * P.breath);
  // tongue (5 bones). tongue 0..1 = how far it slides out; tongueBend bends the whole length down (+) / up (-);
  // tongueCurl rolls mostly the tip: + folds it under (the J cats lap with), - scoops it up (licking, yawning);
  // tongueSpread flattens and widens the front when it presses on fur; tongueYaw swings it sideways (lip lick)
  // it can only bend where it is already out past the lips, so each joint's share of the bend is weighted by
  // how far that joint has slid out of the mouth (otherwise it would poke down through the chin)
  // tongueStretch lengthens it (the joints spread apart) - used when grooming: this toy head is big for its
  // body, so a real-length tongue could not reach its own paw or fur without the head sinking into them
  const ext = clamp(P.tongue, 0, 1.2), HS = rig.d.HS, slide = .38 * HS * ext, st = 1 + clamp(P.tongueStretch || 0, 0, 1.5);
  B.Tongue1.position.add(V(0, -.02 * HS * ext, slide));
  for (let i = 2; i <= 5; i++) B['Tongue' + i].position.multiplyScalar(st);
  const WB = [.1, .3, .28, .2, .12], WC = [0, .05, .15, .32, .48], WY = [.3, .3, .2, .12, .08];
  const out = rig.d.tongueU.map(u => { const z = u * rig.d.tongueLen * st + slide - rig.d.tongueIn; return clamp((z + .04 * HS) / (.14 * HS), 0, 1); });
  const ob = WB.reduce((a, w, i) => a + w * out[i], 0) || 1, oy = WY.reduce((a, w, i) => a + w * out[i], 0) || 1;
  for (let i = 0; i < 5; i++) {
    const kb = WB[i] * out[i] / Math.max(ob, .35), ky = WY[i] * out[i] / Math.max(oy, .35);
    qe(1.5 * P.tongueBend * kb + 2.3 * P.tongueCurl * WC[i] * out[i], 1.1 * P.tongueYaw * ky, 0, B['Tongue' + (i + 1)].quaternion);
  }
  // tail (each joint may come from a slightly earlier pose: overlapping action / follow-through)
  for (let i = 1; i <= 10; i++) {
    const T = lag.tail ? lag.tail[i - 1] : P, k = (i - 1) / 9;
    const wave = T.tailWave * Math.sin(TAU * T.tailWph - i * .55) * (.3 + .7 * k);
    if (i === 1) qe(T.tailBase, T.tailYaw + wave * .5, 0, B.Tail1.quaternion);
    else qe(T.tailBend + (i >= 8 ? T.tailTip : 0), T.tailCurl + wave, 0, B['Tail' + i].quaternion);
  }
  const E = lag.ear || P;
  qe(E.earLp, E.earLy, E.earLr, B.Ear_L.quaternion); qe(E.earRp, -E.earRy, -E.earRr, B.Ear_R.quaternion);
  model.updateMatrixWorld(true);
  // head: authored in the root frame (cats stabilise the head while the body moves)
  const rq = B.Root.getWorldQuaternion(new THREE.Quaternion());
  const want = rq.clone().multiply(qe(P.hdPitch, P.hdYaw, P.hdRoll));
  B.Head.quaternion.copy(B.Neck.getWorldQuaternion(_q).invert().multiply(want));
  B.Head.updateMatrixWorld(true);
  // legs
  const fwd = V(0, 0, 1).applyQuaternion(rq), side = V(1, 0, 0).applyQuaternion(rq), up = V(0, 1, 0);
  for (const f of FOOT_KEYS) {
    const [, n0, n1, n2, n3, sgn, kind] = FEET[f];
    const chain = [B[n0], B[n1], B[n2], B[n3]];
    // inverse kinematics: paw target -> wrist/hock -> elbow/knee
    const a = P[f + 'a'];
    const dMid = up.clone().multiplyScalar(-Math.cos(a)).addScaledVector(fwd, Math.sin(a));
    const T = B.Root.localToWorld(V(P[f + 'x'], P[f + 'y'], P[f + 'z']));
    const lens = kind === 'front' ? rig.L.F : rig.L.H;
    const W = T.clone().addScaledVector(dMid, -lens[2]);
    const A = chain[0].getWorldPosition(new THREE.Vector3());
    const pole = kind === 'front' ? fwd.clone().negate().addScaledVector(side, sgn * .15) : fwd.clone().addScaledVector(side, sgn * .1);
    const M = midJoint(A, W, lens[0], lens[1], pole);
    setDir(chain[0], rest[n1], M.clone().sub(A));
    setDir(chain[1], rest[n2], W.clone().sub(M));
    setDir(chain[2], rest[n3], dMid);
    const toe = fwd.clone().applyAxisAngle(side, P[f + 't']);
    setDir(chain[3], V(0, 0, 1), toe);
    // forward kinematics blend (lying poses): joint angles instead of a ground target
    const fk = P[f + 'fk'];
    if (fk > 0) {
      const ks = [P[f + 'k1'], P[f + 'k2'], P[f + 'k3'], P[f + 'k4']];
      chain.forEach((b, j) => { qe(ks[j], 0, j === 0 ? P[f + 'kz'] : 0, _q2); b.quaternion.slerp(_q2, fk); b.updateMatrixWorld(true); });
    }
  }
  const m = rig.face.morphTargetInfluences; m[0] = clamp(P.blink, 0, 1); m[1] = clamp(P.mouth, 0, 1);
}

// ------------------------------------------------------------------ gaits (foot placement with planted paws)
export function gaitParams(rig, kind) {
  const h = rig.hipH;
  const spec = {
    walk: { Tref: .8, Fr: .157, lambda: 1.88, duty: { F: .6, H: .55 }, off: { HL: 0, FL: .27, HR: .5, FR: .77 }, lift: { F: .32, H: .22 } },
    trot: { Tref: .42, Fr: .5, lambda: 1.87, duty: { F: .46, H: .43 }, off: { HL: 0, FR: .03, HR: .5, FL: .53 }, lift: { F: .42, H: .3 } },
    gallop: { Tref: .3, Fr: 2, lambda: 2.83, duty: { F: .32, H: .28 }, off: { HL: 0, HR: .08, FR: .42, FL: .52 }, lift: { F: .55, H: .45 } },
  }[kind];
  // cycle time from real cats (Tref at 0.25 m hip height), scaled by sqrt(leg length) as dynamic similarity says
  const T = spec.Tref * Math.sqrt(h / .25);
  // stride from the real-cat ratio, but never longer than these short toy legs can actually reach
  const crouch = { walk: .04, trot: .06, gallop: .12 }[kind];
  const reachF = rig.reachF + crouch * h * 1.2, reachH = rig.reachH + crouch * h;
  const S = Math.min(spec.lambda * h, 2 * .9 * reachF / spec.duty.F * (kind === 'gallop' ? 1.9 : 1), 2 * .9 * reachH / spec.duty.H * (kind === 'gallop' ? 1.9 : 1));
  return { ...spec, kind, S, T, v: S / T, crouch };
}
// returns { rootZ, feet: { FL: {x,y,z,a,t,c,u,stance}, ... } } at time t for gait params g
export function gaitFeet(rig, g, t) {
  const rootZ = g.v * t, out = { rootZ, feet: {} };
  for (const f of FOOT_KEYS) {
    const fr = f[0] === 'F', duty = g.duty[fr ? 'F' : 'H'], off = g.off[f], r = rig.restFoot[f];
    const x = t / g.T - off, m = Math.floor(x), c = x - m, reach = duty * g.S / 2;
    const zt = r.z + (m + off) * g.S + reach;
    let z, y = rig.ballH, a, toe, stance = c < duty, u;
    if (stance) {
      u = c / duty; z = zt;
      // stance: lands slightly reaching, yields under load, rolls over the toes (heel-off) before lift-off
      a = fr ? K([[0, .3], [.25, .1], [.75, -.3], [1, -.6]], u) : K([[0, .25], [.3, .05], [.8, -.55], [1, -.85]], u);
      toe = fr ? K([[0, -.05], [.8, 0], [1, -.15]], u) : K([[0, 0], [.85, 0], [1, -.2]], u);
      y += (fr ? .4 : .5) * rig.d.pawR * clamp(-a - .1, 0, 1);       // rolling up onto the toes lifts the paw ball
    } else {
      u = (c - duty) / (1 - duty);
      z = zt + g.S * mj(u);
      // (galloping, a short-legged toy cat cannot tuck its legs as high as a real one: they would fold up into
      // its belly, so the tuck is kept lower and the wrist folds less)
      const tuck = g.kind === 'gallop' ? .4 : g.kind === 'trot' ? .85 : 1;
      y += tuck * g.lift[fr ? 'F' : 'H'] * g.S * .22 * Math.pow(Math.sin(Math.PI * Math.pow(u, .8)), 1.2);
      // swing: front paw folds back at the wrist, then reaches forward (E1) and flattens to land
      a = fr ? K([[0, -.6], [.3, -1.9 * tuck], [.72, .45], [1, .3]], u) : K([[0, -.85], [.35, -1.3 * tuck], [.8, .4], [1, .25]], u);
      toe = fr ? K([[0, -.15], [.25, 1.0], [.7, .4], [1, -.05]], u) : K([[0, -.2], [.3, .4], [.75, .2], [1, 0]], u);   // (toes curl only a little: more scrapes the floor)
    }
    // cats walk on a narrow track: paws step close to the midline
    const narrow = g.kind === 'walk' ? .72 : g.kind === 'trot' ? .8 : .85;
    // galloping, the hind paws land ahead of where the front paws were: on this short body they would meet,
    // so the hind track runs wide (wider still mid-swing) and they pass outside the front legs
    const wide = g.kind !== 'gallop' || fr ? 0 : .4 + (stance ? 0 : .3 * Math.sin(Math.PI * u));
    // (but never so narrow that thick legs brush each other)
    const xx = r.x * (narrow + wide), minX = 1.25 * rig.d.pawR;
    out.feet[f] = { x: Math.sign(r.x) * Math.max(Math.abs(xx), minX), y, z: z - rootZ, a, t: toe, c, u, stance };
  }
  // a hind paw passing its front paw (same side) steps round it: sideways clearance grows as they close in
  const pr = rig.d.pawR, clear = 5 * pr;
  for (const [F, H] of [['FL', 'HL'], ['FR', 'HR']]) {
    const fp = out.feet[F], hp = out.feet[H], close = Math.max(0, 1 - Math.abs(hp.z - fp.z) / clear);
    if (close > 0) hp.x = Math.sign(hp.x) * Math.max(Math.abs(hp.x), Math.abs(fp.x) + 3.2 * pr * mj(close));
  }
  return out;
}

export function gaitPose(rig, g, t) {
  const P = stand(rig), gf = gaitFeet(rig, g, t), h = rig.hipH, ph = t / g.T;
  for (const f of FOOT_KEYS) { const q = gf.feet[f]; P[f + 'x'] = q.x; P[f + 'y'] = q.y; P[f + 'z'] = q.z; P[f + 'a'] = q.a; P[f + 't'] = q.t; }
  P.rootZ = gf.rootZ;
  const c = f => gf.feet[f].c, stanceBump = (f, w = .1) => { const q = gf.feet[f]; return q.stance ? bump(q.u, .25, .3) : 0; };
  if (g.kind === 'gallop') {
    // the spine drives the gallop: flexed (gathered) when the hind paws swing under the chest,
    // extended when the fronts reach and the hinds push back; two flights per stride
    const flex = Math.cos(TAU * (ph - .97));
    P.spPitch = .32 * flex; P.hipPitch = -.2 * flex + .05;
    P.hipY = -g.crouch * h + .05 * h * Math.cos(TAU * 2 * (ph - .35));
    P.hipZ = .015 * flex;
    P.nkPitch = -.1 - .25 * flex; P.hdPitch = .05 + .03 * Math.sin(TAU * ph);
    P.hipRoll = .03 * Math.sin(TAU * ph);
    P.scapL = -.25 + .5 * frac(ph - .52 + .5); P.scapR = -.25 + .5 * frac(ph - .42 + .5);
    P.tailBase = -.25; P.tailBend = -.02; P.tailWave = .12; P.tailWph = ph * 2;
    P.earLp = P.earRp = -.25;
  } else {
    const trot = g.kind === 'trot';
    // body sinks a little as each hind paw takes weight (stance yield), twice per stride; same for the chest
    const dipH = (trot ? .03 : .018) * h, dipF = (trot ? .025 : .014) * h;
    P.hipY = -g.crouch * h - dipH * (stanceBump('HL') + stanceBump('HR'));
    const chestDrop = -g.crouch * h * .7 - dipF * (stanceBump('FL') + stanceBump('FR'));
    P.spPitch = -(chestDrop - P.hipY) / rig.torso;              // positive = chest lower than hips
    P.hipRoll = (trot ? .02 : .035) * Math.sin(TAU * (ph - .1));
    P.hipYaw = (trot ? .03 : .06) * Math.sin(TAU * ph);
    P.spYaw = -1.5 * P.hipYaw;                                 // shoulders swing against the hips
    P.nkPitch = trot ? .12 : .08; P.nkYaw = -.3 * P.spYaw;
    P.hdPitch = .02 + .015 * Math.sin(TAU * 2 * (ph - .3));
    // shoulder blades: rotate back with the leg during stance and ride up while the leg carries weight
    for (const [f, k] of [['FL', 'L'], ['FR', 'R']]) {
      const q = gf.feet[f];
      P['scap' + k] = q.stance ? lerp(-.18, .22, q.u) : lerp(.22, -.18, mj(q.u));
      P['scap' + k + 'y'] = q.stance ? .012 * h * Math.sin(Math.PI * q.u) : 0;
    }
    // tail carried high with a soft question-mark tip on the walk, level on the trot
    P.tailBase = trot ? .25 : 1.0; P.tailBend = trot ? .02 : .06; P.tailTip = trot ? 0 : .22;
    P.tailWave = trot ? .1 : .14; P.tailWph = ph;
    P.earLp = P.earRp = .08;
  }
  P.breath = .3 * Math.sin(TAU * ph);
  return P;
}

// ------------------------------------------------------------------ key poses
// Sitting the way cats really sit: the rump rests ON the ground, the hind legs fold forward along the body with
// the whole hind foot (heel to toes) flat on the floor, and the front legs stand straight under the shoulders.
// Solved per breed: the hips are lowered onto the rump, then the body is tilted until the shoulders sit exactly
// at front-leg height (a Munchkin sits low and flat, a Savannah tall and upright).
const _w = new THREE.Vector3();
function worldOf(rig, name) { return rig.B[name].getWorldPosition(_w).clone(); }
export function fitSit(rig, P, opts = {}) {
  const d = rig.d, hipsTarget = d.rumpR * (opts.rump ?? .92), shoulderTarget = d.joints.UpperArm_L.y * (opts.shoulder ?? .97);
  P.hipY = hipsTarget - rig.J.Hips.y;
  P.spPitch = opts.spPitch ?? -.12;
  let pitch = P.hipPitch || -.6;
  for (let it = 0; it < 10; it++) {
    P.hipPitch = pitch; applyPose(rig, P);
    const sh = worldOf(rig, 'UpperArm_L'), hp = worldOf(rig, 'Hips'), arm = sh.clone().sub(hp).length();
    pitch -= (shoulderTarget - sh.y) / Math.max(.05, arm * Math.cos(pitch)) * .8;
    pitch = clamp(pitch, -1.25, 0);
  }
  P.hipPitch = pitch; applyPose(rig, P);
  // neck carries the head back over the chest; the head itself is authored level in the root frame
  P.nkPitch = -(pitch + P.spPitch) * .85;
  const shL = worldOf(rig, 'UpperArm_L'), shR = worldOf(rig, 'UpperArm_R'), hp = worldOf(rig, 'Hips');
  for (const [f, sh] of [['FL', shL], ['FR', shR]]) { P[f + 'z'] = sh.z + .006; P[f + 'x'] = sh.x * .85; P[f + 'y'] = rig.ballH; P[f + 'a'] = .1; }
  for (const f of ['HL', 'HR']) { P[f + 'z'] = hp.z + d.len.lMt * 1.05; P[f + 'x'] = rig.restFoot[f].x * 1.3; P[f + 'y'] = rig.ballH; P[f + 'a'] = 1.52; }
  return P;
}
// ------------------------------------------------------------------ settling a held posture on the real mesh
// The skinned mesh is measured (catcontact.js), not the bones: the body is lifted until nothing but fur-deep
// contact reaches below the floor, the tail is raised off it, and planted paws that sink into the body are
// moved out along the surface normal. Used on every held posture (sit, loaf, sleep, lying) at build time.
export function contactOf(rig) {
  if (!rig.contact) {
    const C = makeContact(rig);
    C.sets = {
      trunk: C.points((p, i, m) => p === 'torso' || p.endsWith('u'), 'body', 2),
      head: C.points(p => p === 'head', 'body', 2),
      tail: C.points(p => p === 'tail', 'body', 2),
      ...Object.fromEntries(LIMBS.map(L => [L, C.points(p => p === L, 'body', 2)])),
      ...Object.fromEntries(LIMBS.map(L => [L + '_s', C.points(p => p === L, 'body', 4)])),        // sparser, for settling
      ...Object.fromEntries(LIMBS.map(L => [L + 'b', C.points(p => p === L, 'face')])),             // the paw's beans (face mesh)
    };
    // how deep each limb vertex already sits in the rest pose (a long-haired breed's legs are inside its fur
    // skirt): only deeper than that counts as sinking in
    applyPose(rig, stand(rig)); C.update();
    C.rest = Object.fromEntries(LIMBS.flatMap(L => [[L, C.depthEach(C.sets[L], p => p !== L && p !== L + 'u')], [L + '_s', C.depthEach(C.sets[L + '_s'], p => p !== L && p !== L + 'u')]]));
    rig.contact = C;
  }
  return rig.contact;
}
export function settle(rig, P, { fk = [], floor = .002, iters = 12, dense = false } = {}) {
  const C = contactOf(rig);
  for (let it = 0; it < iters; it++) {
    applyPose(rig, { ...P, breath: Math.max(P.breath || 0, 1.3) }); C.update({ face: false });   // (with the belly at its fullest breath)
    let moved = false;
    // body: lift the hips so the lowest trunk point (and any limb posed by joint angles) is at -floor
    let low = C.lowest(C.sets.trunk);
    for (const L of fk) low = Math.min(low, C.lowest(C.sets[L]));
    if (low < -floor - .001) { P.hipY += -floor - low; moved = true; }
    // head: lying on its side the big head would sink into the floor; lift it on the neck (whichever small
    // neck/head turn raises it most)
    const hl = C.lowest(C.sets.head);
    if (hl < -floor - .001) {
      let best = null, bestY = hl;
      for (const [k, dv] of [['nkRoll', .06], ['nkRoll', -.06], ['hdRoll', .06], ['hdRoll', -.06], ['nkPitch', -.06]]) {
        const Q = { ...P, [k]: P[k] + dv }; applyPose(rig, Q); C.update({ face: false });
        const y = C.lowest(C.sets.head); if (y > bestY + .0005) { bestY = y; best = [k, dv]; }
      }
      if (best) { P[best[0]] += best[1]; moved = true; } else if (hl < -.01) { P.hipY += -floor - hl; moved = true; }
      applyPose(rig, P); C.update({ face: false });
    }
    // tail: raise its root until it clears the floor
    // (with a few mm to spare: the idle tail sway dips it a little)
    if (C.lowest(C.sets.tail) < .003 && P.tailBase < 1.2) { P.tailBase += .08; moved = true; }
    // planted (IK) legs pressed into the body or another leg: the paw target and ankle angle are solved (on the
    // mesh) to the nearest place where the whole leg is clear - so the same pose fits short and long legs
    for (const L of LIMBS) {
      if (fk.includes(L) || P[L + 'fk'] > .5) continue;
      const pl = C.lowest(C.sets[L]);                              // a folded paw tucked under the floor
      if (pl < -floor - .001) { P[L + 'y'] += -floor - pl; moved = true; }
      const others = ['torso', 'head', 'tail', ...LIMBS.flatMap(M => M === L ? [] : [M, M + 'u'])];
      const SL = dense ? L : L + '_s', mR = .06;                // (held postures: every other vertex)
      const r = C.depth(C.sets[SL], p => others.includes(p), C.rest[SL], mR);
      if (r.d < -.003) {
        touch(rig, P, { a: SL, b: others, keys: [L + 'x', L + 'y', L + 'z', L + 'a'], bounds: { [L + 'y']: [rig.ballH * .95 - (P.rootY || 0), rig.ballH + .3], [L + 'a']: [-2.2, 1.7] }, pairless: true, iters: 10, lim: .04,
          guard: [{ a: SL, b: others }], maxR: mR });
        moved = true; applyPose(rig, P); C.update({ face: false });
      }
    }
    if (!moved) break;
  }
  applyPose(rig, P);
  return P;
}
// Bring vertex set `a` (the tongue, a paw...) into touch with the surface of parts `b` - optionally only the
// patch within zone.r of a point on a bone - by adjusting pose `keys`, while keeping the `guard` sets out of
// the parts they must not enter (by default `a` itself out of `b`: a forearm must not sink into the head while
// its paw touches the cheek). Each round finds, on the skinned meshes, the nearest vertex/surface pair and
// the deepest guard violations; one damped least-squares step (finite differences) moves the contact pair to
// `gap` along the surface normal (negative gap: pressed into the fur) and every violating vertex back out.
// `at` turns the base pose into the pose at the moment of contact (e.g. the tongue out at mid-lick).
export function touch(rig, P, { a, b, zone = null, keys, bounds = {}, gap = .001, iters = 30, at = Q => Q, lim = .12, guard = null, apart = false, pairless = false, maxR = .06 }) {
  const C = contactOf(rig);
  const setOf = name => name === 'tongue' ? (C.sets.tongue ||= C.points(p => p === 'tongue', 'face', 2)) : C.sets[name] || (C.sets['all' + name] ||= C.points(p => p === name, 'body', 2));
  const A = setOf(a), guards = (guard || [{ a, b }]).map(g => ({ set: setOf(g.a), b: g.b, name: g.a }));
  const pa = new THREE.Vector3(), pb = new THREE.Vector3(), tmp = new THREE.Vector3();
  let res = { d: Infinity }, bestP = { ...P }, bestErr = Infinity, step = lim;
  const pose = Q => { applyPose(rig, at({ ...Q })); };
  const face = A.mesh === 'face' || guards.some(g => g.set.mesh === 'face');
  for (let it = 0; it <= iters; it++) {
    pose(P); C.update({ face });
    const zc = zone ? rig.B[zone.bone].localToWorld(zone.off.clone()) : null;
    // contact: closest vertex pair first (brute force over the target patch), then the exact surface point
    const bp = C.M.body.pos, bPart = C.M.body.part, Bv = [];
    if (!pairless) for (let v = 0; v < C.M.body.n; v++) if (b.includes(bPart[v]) && (!zc || Math.hypot(bp[3 * v] - zc.x, bp[3 * v + 1] - zc.y, bp[3 * v + 2] - zc.z) < zone.r)) Bv.push(v);
    const pos = C.M[A.mesh].pos; let pi = -1, pd = Infinity;
    for (const i of A.ids) for (const v of Bv) {
      const dd = (pos[3 * i] - bp[3 * v]) ** 2 + (pos[3 * i + 1] - bp[3 * v + 1]) ** 2 + (pos[3 * i + 2] - bp[3 * v + 2]) ** 2;
      if (dd < pd) { pd = dd; pi = i; }
    }
    const ok = (part, t) => b.includes(part) && (!zc || C.triCenter(t).distanceTo(zc) < zone.r * 1.2);
    const r0 = pairless ? { d: gap } : pi < 0 ? null : C.nearest(pos[3 * pi], pos[3 * pi + 1], pos[3 * pi + 2], ok, Math.sqrt(pd) + .02);
    if (!r0) break;
    // (apart: only keep `a` from sinking in - no pull toward the surface; pairless: guards only)
    const rows = pairless || (apart && r0.d >= gap) ? [] : [{ mesh: A.mesh, i: pi, t: r0.t, bary: r0.bary, n: r0.n.clone(), want: gap }];
    // guards: the deepest vertex of each guarded set inside its forbidden parts is pushed back out
    let over = 0; const viol = [];
    for (const g of guards) {
      const okG = p => g.b.includes(p), w = C.depth(g.set, okG, C.rest[g.name] || null, maxR);
      if (w.d < -.003) {
        const gp0 = C.M[g.set.mesh].pos; viol.push({ who: g.set === A ? a : 'guard', into: w.part, d: +w.d.toFixed(4), at: [gp0[3 * w.i], gp0[3 * w.i + 1], gp0[3 * w.i + 2]].map(x => +x.toFixed(3)), bone: rig.model.userData.skeleton.bones[C.M[g.set.mesh].si[4 * w.i]].name });
        const gp = C.M[g.set.mesh].pos, r = C.nearest(gp[3 * w.i], gp[3 * w.i + 1], gp[3 * w.i + 2], okG, maxR);
        if (r) { rows.push({ mesh: g.set.mesh, i: w.i, t: r.t, bary: r.bary, n: r.n.clone(), want: .001 }); over += -.003 - w.d; }
      }
    }
    // backtracking: a step that made things worse is undone and the step size halved
    const err = (apart ? Math.max(0, gap - r0.d) : Math.abs(r0.d - gap)) + over;
    if (err > bestErr + 1e-4) { Object.assign(P, bestP); step /= 2; if (step < .005) break; continue; }
    bestErr = err; bestP = { ...P }; step = Math.min(lim, step * 1.5);
    res = { d: r0.d, over, viol };
    if (err < .0008 || it === iters || !rows.length) break;
    const f = Q => {
      pose(Q); C.refresh();
      return rows.map(r => {
        C.skin1(r.mesh, r.i, pa); pb.set(0, 0, 0);
        C.triVerts(r.t).forEach((v, k) => pb.addScaledVector(C.skin1('body', v, tmp), r.bary.getComponent(k)));
        return pa.clone().sub(pb);
      });
    };
    const f0 = f(P), e = rows.map((r, j) => r.n.clone().multiplyScalar(r.want).sub(f0[j]));
    const Jc = keys.map(k => f({ ...P, [k]: P[k] + .01 }).map((v, j) => v.sub(f0[j]).multiplyScalar(100)));
    const m = keys.length, dot = (u, v) => u.reduce((acc, x, j) => acc + x.dot(v[j]), 0);
    const M2 = Array.from({ length: m }, (_, i) => Array.from({ length: m }, (_, j) => dot(Jc[i], Jc[j]) + (i === j ? 1e-5 : 0))), bv = Jc.map(j => dot(j, e));
    for (let i = 0; i < m; i++) for (let k = i + 1; k < m; k++) { const g = M2[k][i] / M2[i][i]; for (let j = i; j < m; j++) M2[k][j] -= g * M2[i][j]; bv[k] -= g * bv[i]; }
    const x = new Array(m).fill(0);
    for (let i = m - 1; i >= 0; i--) { let sum = bv[i]; for (let j = i + 1; j < m; j++) sum -= M2[i][j] * x[j]; x[i] = sum / M2[i][i]; }
    keys.forEach((k, i) => { P[k] += clamp(x[i], -step, step); if (bounds[k]) P[k] = clamp(P[k], bounds[k][0], bounds[k][1]); });
  }
  Object.assign(P, bestP); applyPose(rig, P);
  touch.last = bestErr + gap; touch.info = res;
  return P;
}
// touch() from several starting poses; keeps the closest
export function touchBest(rig, P, opts, starts = [], grid = null) {
  // a coarse scan first (grid: offsets per channel, every combination scored without solving), so a body of
  // any proportions starts from its own nearest good pose; the best few are then solved from
  if (grid) {
    const keys = Object.keys(grid); let combos = [{}];
    for (const k of keys) combos = combos.flatMap(c => grid[k].map(v => ({ ...c, [k]: v })));
    const scored = combos.map(c => {
      const s0 = Object.fromEntries(Object.entries(c).map(([k, v]) => [k, P[k] + v]));
      touch(rig, { ...P, ...s0 }, { ...opts, iters: 0 });
      return { s0, e: Math.abs(touch.last - (opts.gap ?? .001)) };
    }).sort((x, y) => x.e - y.e);
    starts = [...scored.slice(0, 3).map(x => x.s0), ...starts];
  }
  let best = null, bestErr = Infinity, info = null;
  for (const s0 of [{}, ...starts]) {
    const Q = touch(rig, { ...P, ...s0 }, opts), e = Math.abs(touch.last - (opts.gap ?? .001));
    if (e < bestErr) { bestErr = e; best = Q; info = touch.info; }
    if (bestErr < .0015) break;
  }
  Object.assign(P, best); applyPose(rig, P); touch.last = bestErr + (opts.gap ?? .001); touch.info = info;
  return P;
}
// settle a moving clip: settle n+1 sampled frames and blend the corrections in between (cheap at play time)
export function withSettle(rig, pose, dur, opts = {}, n = 12) {
  // (worked out on first use: a clip nobody samples costs nothing)
  let keys = null;                                     // [{ t, dl }] sorted by time
  const fix = (t, o = {}) => { const P = pose(t), Q = settle(rig, { ...P }, { iters: 6, ...opts, ...o }), dl = {}; for (const k in Q) if (typeof Q[k] === 'number' && Math.abs(Q[k] - P[k]) > 1e-6) dl[k] = Q[k] - P[k]; return { t, dl }; };
  const at = t => {
    const P = pose(t); let i = 0;
    while (i < keys.length - 2 && keys[i + 1].t <= t) i++;
    const a = keys[i], b = keys[i + 1], u = clamp((t - a.t) / Math.max(1e-6, b.t - a.t), 0, 1);
    for (const k of new Set([...Object.keys(a.dl), ...Object.keys(b.dl)])) P[k] += lerp(a.dl[k] || 0, b.dl[k] || 0, u);
    return P;
  };
  const build = () => {
    keys = Array.from({ length: n + 1 }, (_, i) => fix(dur * i / n));
    // then every frame in between is checked; where a leg still passes into the body (the blend between two
    // settled points is not itself settled) that frame is settled too
    const C = contactOf(rig), fk = opts.fk || [], m = Math.max(2, Math.round(dur * FPS)), h = dur / m;   // (every frame; a fix also settles half a frame either side)
    for (let round = 0; round < 2; round++) {
      const add = [];
      for (let f = 0; f <= m; f++) {
        const t = dur * f / m; if (keys.some(k => Math.abs(k.t - t) < 1e-4)) continue;
        const P = at(t); applyPose(rig, P); C.update({ face: false });
        for (const L of LIMBS) {
          if (fk.includes(L) || P[L + 'fk'] > .5) continue;
          const others = ['torso', 'head', 'tail', ...LIMBS.flatMap(M => M === L ? [] : [M, M + 'u'])];
          if (C.depth(C.sets[L], p => others.includes(p), C.rest[L], .06).d < -.003) { add.push(t); break; }
        }
      }
      if (!add.length) break;
      // (settled half a step either side too, so the blend into and out of the fixed frame is clear as well)
      for (const t of add) for (const tt of [t - h / 2, t, t + h / 2]) if (tt >= 0 && tt <= dur && !keys.some(k => Math.abs(k.t - tt) < 1e-4)) keys.push(fix(tt, { dense: true, iters: 10 }));
      keys.sort((a, b) => a.t - b.t);
    }
  };
  return t => { if (!keys) build(); return at(t); };
}
function sitPose(rig, S = rig) {
  const P = stand(rig);
  Object.assign(P, { hdPitch: .02, scapL: -.05, scapR: -.05, tailBase: -1.25, tailBend: .02, tailCurl: .3, tailYaw: .45 });
  return settle(S, fitSit(rig, P), { dense: true, iters: 20 });
}
function loafPose(rig) {
  const P = stand(rig), h = rig.hipH, chestR = rig.chestY - rig.ballH;
  Object.assign(P, {
    hipY: -.56 * rig.hipY, spPitch: .02, nkPitch: .12, hdPitch: .04, breath: 0,
    tailBase: -1.0, tailBend: .1, tailCurl: .22, tailYaw: .55,
  });
  for (const f of ['FL', 'FR']) { P[f + 'a'] = -1.62; P[f + 'y'] = rig.ballH * .9; P[f + 'z'] -= .05 * h; P[f + 't'] = .2; }
  for (const f of ['HL', 'HR']) { P[f + 'a'] = 1.5; P[f + 'y'] = rig.ballH * .9; P[f + 'z'] += .25 * h; P[f + 'x'] *= 1.15; }
  return P;
}
function sleepPose(rig) {
  const P = loafPose(rig);
  Object.assign(P, {
    // curled into a doughnut towards its left side: chin down on the paws, tail wrapped round to the nose
    hipYaw: -.38, hipRoll: .16, spYaw: .95, spRoll: -.12, nkPitch: .55, nkYaw: .4, hdPitch: .3, hdYaw: .95, hdRoll: -.3,
    tailBase: -1.15, tailYaw: .6, tailCurl: .4, tailBend: .015, blink: 1,
    earLp: -.25, earRp: -.25, earLr: .1, earRr: .1,
  });
  return P;
}

// ------------------------------------------------------------------ tongue cycles
// One grooming lick (u 0..1, ~0.45 s): the jaw opens first, the tongue reaches out and down with the tip
// curled slightly under, lands on the fur and spreads flat, then drags UP and back over the fur while the head
// lifts (the stroke) and the tip turns up; it snaps back in and the jaw closes a beat later.
export function lickCycle(u) {
  return {
    mouth: K([[0, .06], [.1, .42], [.62, .42], [.8, .1], [1, .06]], u),
    tongue: K([[0, 0], [.08, .12], [.3, .95], [.6, .82], [.76, .08], [1, 0]], u),
    tongueBend: K([[0, .2], [.3, .95], [.42, .75], [.62, -.15], [.78, .1], [1, .2]], u),
    tongueCurl: K([[0, .15], [.24, .45], [.34, .2], [.6, -.45], [.72, -.55], [.88, -.1], [1, .15]], u),
    tongueSpread: K([[0, 0], [.3, 0], [.38, 1], [.58, .75], [.7, 0], [1, 0]], u),
    hdPitch: K([[0, 0], [.3, .07], [.4, .09], [.66, -.11], [.86, -.03], [1, 0]], u),
  };
}
// One lap when drinking (u 0..1 at ~3.5 Hz; Reis et al. 2010): the tongue reaches down with its tip folded
// back under (J), touches the surface with the top of the tip only, then is pulled up much faster than it went
// down; the jaw closes on the liquid column just after the tongue is in.
export function lapCycle(u) {
  return {
    mouth: K([[0, .12], [.14, .55], [.56, .55], [.7, .04], [1, .12]], u),
    tongue: K([[0, .05], [.34, 1], [.42, 1], [.6, .05], [1, .05]], u),
    tongueBend: K([[0, .45], [.34, 1.35], [.42, 1.35], [.6, .45], [1, .45]], u),
    tongueCurl: K([[0, .15], [.3, 1.15], [.44, 1.2], [.6, .3], [1, .15]], u),
    tongueSpread: K([[0, 0], [.33, 0], [.38, .6], [.45, .3], [.52, 0], [1, 0]], u),
    hdPitch: K([[0, 0], [.36, .025], [.5, -.02], [.7, -.03], [1, 0]], u),
  };
}
// A lip lick (u 0..1, ~0.8 s): tip scooped up, the tongue sweeps across the upper lip and nose from one side
// to the other and back in - what cats do after eating or drinking.
export function lipLick(u) {
  return {
    mouth: K([[0, 0], [.12, .36], [.78, .36], [.92, .02], [1, 0]], u),
    tongue: K([[0, 0], [.15, .86], [.72, .86], [.86, 0], [1, 0]], u),
    tongueBend: -.25, tongueCurl: K([[0, -.2], [.2, -.75], [.7, -.75], [1, -.2]], u), tongueSpread: .35,
    tongueYaw: K([[0, .7], [.2, .7], [.5, -.75], [.62, -.75], [.72, 0], [1, 0]], u),
    hdPitch: K([[0, 0], [.3, -.05], [.7, -.05], [1, 0]], u),
  };
}
const addTongue = (P, c, k = 1) => { for (const key in c) P[key] = key === 'hdPitch' ? P[key] + c[key] * k : lerp(P[key], c[key], k); return P; };

// ------------------------------------------------------------------ clips
// A grooming bout for the game's behaviour layer: run these in order (head to tail, as cats do), staying in
// each for its `share` - the fraction of oral grooming real cats spend on that region (Eckstein & Hart 2000).
// Only the regions this toy cat can really reach are animated (the face 31 % and chest 11 %; flanks, hind
// legs, belly and tail are out of its reach). `from` is the posture the clip starts and ends in (play SitDown
// first). The last two are scratching and claw care, not licking, and are sprinkled in rather than weighted.
// a grooming contact further off than this after solving means the clip is not made for that breed
// the part of a lick when the tongue is on the fur (the head is still coming down the stroke; after it, the
// stroke lifts the head away)
export const LICK_CORE = [.3, .44];
export const GROOM_REACH_LIMIT = .005;
export const GROOM_ROUTINE = [
  { clip: 'GroomFace', region: 'front paw (lick) and face (paw wash: cheek, whisker pad, muzzle)', share: .31, from: 'Sit' },
  { clip: 'ScratchEar', region: 'neck behind the jaw (hind-foot scratch)', share: 0, from: 'Sit' },
  { clip: 'NibbleClaws', region: 'front claws', share: 0, from: 'Sit' },
];
export function makeClips(rig, opts = {}) {
  // mesh measurements (settling on the floor, contact solves) run on S: a coarser build of the same cat (same
  // skeleton and proportions) is enough and much faster; the pose channels it finds apply to the fine model
  const S = opts.solveRig || rig;
  if (S !== rig) { rig.ballH = S.ballH; rig.floorLift = S.floorLift; }      // one floor height for both builds
  const h = rig.hipH, P0 = stand(rig), clips = [];
  // last pass on every clip, at every frame: nothing below the floor. (The settle passes run at a dozen or so
  // points per clip; between them a rolling paw or a turning body can dip a few mm under.) A planted leg's paw
  // is raised; a body, head or leg posed by joint angles raises the hips. Worked out on first use.
  const floorFix = (pose, dur, floorAt = () => 0) => {
    let lift = null;
    const build = () => {
      const C = contactOf(S), n = Math.max(2, Math.ceil(dur * FPS)), raw = [];
      for (let i = 0; i <= n; i++) {
        const P = pose(dur * i / n), e = {}, fl = floorAt(P);
        applyPose(S, P); C.update();
        for (const L of LIMBS) {
          const low = Math.min(C.lowest(C.sets[L]), C.lowest(C.sets[L + 'b'])) - fl;   // (the beans stand proud of the fur)
          if (low < -.0015) { const k = P[L + 'fk'] > .5 ? 'hipY' : L + 'y'; e[k] = Math.max(e[k] || 0, -.0005 - low); }
        }
        const low = Math.min(C.lowest(C.sets.trunk), C.lowest(C.sets.head)) - fl;
        if (low < -.0015) e.hipY = Math.max(e.hipY || 0, -.0005 - low);
        // checked again with the lifts on: a leg already stretched to its full length cannot lift its paw by its
        // target alone (lying on the side), so whatever is still under the floor raises the whole body
        if (Object.keys(e).length) {
          const Q = { ...P }; for (const k in e) Q[k] += e[k];
          applyPose(S, Q); C.update();
          let lo2 = Math.min(C.lowest(C.sets.trunk), C.lowest(C.sets.head));
          for (const L of LIMBS) lo2 = Math.min(lo2, C.lowest(C.sets[L]), C.lowest(C.sets[L + 'b']));
          if (lo2 - fl < -.0015) e.hipY = (e.hipY || 0) + (-.0005 - (lo2 - fl));
        }
        raw.push(e);
      }
      // (each lift held over the neighbouring frames, so the blend between frames never undershoots)
      lift = raw.map((e, i) => {
        const o = {};
        for (const q of [raw[i - 1], e, raw[i + 1]]) if (q) for (const k in q) o[k] = Math.max(o[k] || 0, q[k]);
        return o;
      });
    };
    return t => {
      if (!lift) build();
      const P = pose(t), f = clamp(t / dur, 0, 1) * (lift.length - 1), i = Math.min(lift.length - 2, Math.floor(f)), u = f - i;
      for (const k of new Set([...Object.keys(lift[i]), ...Object.keys(lift[i + 1])])) P[k] += lerp(lift[i][k] || 0, lift[i + 1][k] || 0, u);
      return P;
    };
  };
  // grooming licks, last of all, on the finished pose of every half frame: how far the tongue is out is
  // bisected so that mid-lick (lick phase LICK_CORE) it just meets the fur and at no other time is it in it.
  // (lickTrack has already done this on the base pose; this catches what breathing, settling and the floor
  // pass change.) Worked out on first use.
  const tongueFix = (pose, dur, parts) => {
    let offs = null;
    const build = () => {
      const C = contactOf(S), T = C.sets.tongue ||= C.points(p => p === 'tongue', 'face', 2), onB = p => parts.includes(p);
      const n = Math.max(2, Math.ceil(2 * dur * FPS));
      offs = Array.from({ length: n + 1 }, (_, i) => {
        const P = pose(dur * i / n), u = P.lickU;
        if (u === undefined || u < .04 || u > .82 || !(P.tongue > 0)) return 0;
        const gapAt = dt => { const Q = { ...P, tongue: Math.max(0, P.tongue + dt) }; groomTongue(Q); applyPose(S, Q); C.update(); return C.gap(T, onB, .12).d; };
        const core = u >= LICK_CORE[0] && u <= LICK_CORE[1], want = core ? -.001 : .001;
        if (!core && gapAt(0) >= want) return 0;
        if (core && Math.abs(gapAt(0) - want) < .0015) return 0;
        let lo = -P.tongue, hi = core ? Math.max(.15, 1 - P.tongue) : 0;     // (mid-lick it may come all the way out)
        if (gapAt(hi) > want) return hi;
        if (gapAt(lo) < want) return lo;
        for (let it = 0; it < 12; it++) { const m = (lo + hi) / 2; if (gapAt(m) > want) lo = m; else hi = m; }
        return (lo + hi) / 2;
      });
    };
    return t => {
      if (!offs) build();
      const P = pose(t), f = clamp(t / dur, 0, 1) * (offs.length - 1), i = Math.min(offs.length - 2, Math.floor(f)), u = f - i;
      const o = lerp(offs[i], offs[i + 1], u);
      if (o) { P.tongue = Math.max(0, P.tongue + o); groomTongue(P); }
      return P;
    };
  };
  // looping clips are stretched to a whole number of frames so the last frame meets the first exactly
  let tAdd = performance.now();
  const add = (name, dur, loop, pose, extra = {}) => {
    if (globalThis.__timing) { const n = performance.now(); console.log('  ' + name, Math.round(n - tAdd) + 'ms'); tAdd = n; }
    const frames = Math.max(2, Math.round(dur * FPS)), d2 = frames / FPS, f = dur / d2;
    // (after a jump has carried the root onto the deck, the deck top is the floor)
    let fn = floorFix(f === 1 ? pose : t => pose(t * f), d2, extra.jump ? P => (P.rootZ >= .8 * extra.jump.D ? extra.jump.H : 0) : undefined);
    const tc = (extra.contacts || []).find(c => c.a === 'tongue');
    if (tc) fn = tongueFix(fn, d2, tc.b);
    const clip = { name, dur: d2, loop, pose: fn, ...extra };
    if (clip.speed) { clip.speed /= f; clip.cycle = clip.cycle / f; }
    clips.push(clip);
  };

  // Idle: breathing, blinks, an ear twitch, a look around, slow tail swish, weight shift
  add('Idle', 6, true, t => over(P0, {
    breath: Math.sin(TAU * t / 2), blink: blinkAt(t, [1.1, 4.3]),
    hdYaw: K([[0, 0], [1.6, 0], [2.1, .45], [3.2, .45], [3.7, -.15], [4.6, -.15], [5.2, 0], [6, 0]], t),
    hdPitch: K([[0, 0], [2.1, -.08], [3.2, -.05], [3.7, .05], [5.2, 0], [6, 0]], t),
    nkYaw: K([[0, 0], [2.1, .12], [3.2, .12], [3.7, -.05], [5.2, 0], [6, 0]], t),
    earLp: K([[0, 0], [2.6, 0], [2.7, -.5], [2.95, .05], [3.1, 0], [6, 0]], t), earLy: K([[0, 0], [2.6, 0], [2.7, -.4], [3, 0], [6, 0]], t),
    earRp: .05 * Math.sin(TAU * t / 3),
    hipX: .006 * Math.sin(TAU * t / 6), hipRoll: .02 * Math.sin(TAU * t / 6),
    tailBase: -.3, tailBend: .1, tailTip: .15, tailWave: .22, tailWph: t / 3,
  }));
  // locomotion (root motion: the clip moves the cat forward; paws stay planted on the ground)
  for (const [name, kind, cycles] of [['Walk', 'walk', 2], ['Trot', 'trot', 2], ['Gallop', 'gallop', 3]]) {
    const g = gaitParams(rig, kind);
    // every breed's body is a different shape: the paws of each gait are settled on its mesh (stepped round
    // the body where a swinging leg would pass into it, kept on the floor) at 16 points per stride
    const raw = t => gaitPose(rig, g, t), cyc = withSettle(S, raw, g.T, { iters: 6 }, 16);
    add(name, g.T * cycles, true, t => {
      const P = cyc(((t % g.T) + g.T) % g.T), Q = raw(t);
      P.rootZ = Q.rootZ;                                                       // the cycle repeats; the root keeps moving
      P.blink = kind === 'walk' ? blinkAt(t, [g.T * 1.4]) : 0;
      return P;
    }, { rootMotion: true, speed: g.v, stride: g.S, cycle: g.T });
  }
  // JumpUp: crouch, bum wiggle, launch, ballistic flight, front paws land first, absorb, stand
  {
    // the jump carries the cat its own length forward: at take-off the head must be clear of the deck the hind
    // paws then land on (a long cat cannot stand right in front of a deck it can reach in one hop)
    const d0 = rig.d, Hup = .2, tLift = 1.0, tPush = 1.1;
    const headFront = (() => { const C = contactOf(S); applyPose(S, P0); C.update({ face: false }); let z = -Infinity; const p = C.M.body.pos; for (const i of C.sets.head.ids) z = Math.max(z, p[3 * i + 2]); return z; })();
    const D = Math.max(.78, headFront + .12 - rig.restFoot.HL.z), deckBack = D + rig.restFoot.HL.z - .15;   // (room for the raised heel behind the toes)
    const apex = Hup + .1, vy = Math.sqrt(2 * G * apex), Tf = vy / G + Math.sqrt(2 * (apex - Hup) / G);
    const tLand = tPush + Tf, dur = tLand + .9;
    // short forelegs under a big head (Munchkin, Persian) leave no room to drop the chest and nod on landing:
    // the landing crouch and nod shrink with leg length and the head is carried up instead
    const legK = clamp(((d0.joints.UpperArm_L.y - rig.ballH) / d0.HS - .3) / .6, 0, 1);
    add('JumpUp', dur, false, withSettle(S, t => {
      const P = { ...P0 };
      const crouchA = ss(seg(t, 0, .35)) * (1 - ss(seg(t, .95, tPush)));            // before take-off
      const crouchB = (.35 + .4 * legK) * ss(seg(t, tLand - .02, tLand + .14)) * (1 - ss(seg(t, tLand + .3, tLand + .75)));
      const crouch = Math.max(crouchA, crouchB);
      const fly = seg(t, tPush, tLand);
      // root follows the ballistic path of the body; paws are planted in world space before and after
      const tau = clamp(t - tPush, 0, Tf);
      P.rootZ = t < tPush ? 0 : D * Math.min(1, tau / Tf);
      P.rootY = t < tPush ? 0 : t < tLand ? vy * tau - G * tau * tau / 2 : Hup;
      // push-off: the body extends upward from the crouch just before leaving the ground
      const push = ss(seg(t, .92, tPush)) * (1 - ss(seg(t, tPush, tPush + .08)));
      P.hipY = -.2 * h * (.5 + .5 * legK) * crouch + .1 * h * push;          // (a shallow crouch: deeper, the short forelegs press up into the chest)
      const pl = .45 * legK - .1, sl = .25 * legK - .05;                               // landing pitch: nose down only with legs to land on
      P.hipPitch = t < tPush ? lerp(.05 * crouch, -.45, ss(seg(t, .92, tPush))) : t < tLand ? lerp(-.45, pl, ss(fly)) : lerp(pl, 0, ss(seg(t, tLand, tLand + .5)));
      P.spPitch = t < tPush ? .08 * crouch - .25 * ss(seg(t, .95, tPush)) : t < tLand ? K([[0, -.25], [.45, .12], [1, sl]], fly) : lerp(sl, 0, ss(seg(t, tLand, tLand + .55)));
      const wig = Math.sin(TAU * 5 * t) * ss(seg(t, .4, .5)) * (1 - ss(seg(t, .82, .9)));
      P.hipYaw = .1 * wig; P.hipRoll = .05 * wig;                                       // the famous bum wiggle
      const land = ss(seg(t, tLand - .3, tLand)) * (1 - ss(seg(t, tLand + .35, tLand + .8)));
      P.nkPitch = -.1 * crouch - P.hipPitch * .5 - .45 * (1 - legK) * land; P.hdPitch = -.1 * (1 - fly) + .12 * legK * ss(seg(t, tLand - .15, tLand + .1)) * (1 - ss(seg(t, tLand + .2, tLand + .6)));
      P.tailBase = t < tPush ? -.75 + .5 * crouch + .9 * ss(seg(t, .85, tPush)) : t < tLand ? lerp(.15, -.4, ss(fly)) + .3 * Math.sin(Math.PI * fly) : lerp(-.4, -.3, ss(seg(t, tLand, dur)));
      P.tailBend = .03; P.tailWave = t < tPush ? .35 * ss(seg(t, .3, .5)) : .05; P.tailWph = t * 3;
      P.earLp = P.earRp = t < tPush ? .15 : -.2 * Math.sin(Math.PI * fly);
      P.blink = blinkAt(t, [tLand + .05], .2) * .7;
      // paws: planted on the floor (world) until lift-off, then fly, then planted on the box top (world)
      for (const f of FOOT_KEYS) {
        const fr = f[0] === 'F', r = rig.restFoot[f], off = fr ? .04 : 0;
        const liftT = fr ? tLift : tPush, touchT = fr ? tLand : tLand + .07;
        let wz, wy, a = fr ? .3 : .2, toe = 0;
        const tread = !fr ? .015 * Math.max(0, Math.sin(TAU * 5 * t + (f === 'HL' ? 0 : Math.PI))) * (wig ? 1 : 0) : 0;
        if (t < liftT) { wz = r.z + (fr ? (.12 + .1 * (1 - legK)) * crouch : 0); wy = rig.ballH + tread; if (!fr) a = .2 - .5 * crouch; }
        else if (t >= touchT) { wz = r.z + D + (fr ? .05 * crouchB : 0); wy = Hup + rig.ballH; if (!fr) a = .2 - .5 * crouchB; else a = .3 - .2 * crouchB; }
        else {
          const u = (t - liftT) / (touchT - liftT), body = { z: P.rootZ, y: P.rootY };
          // fronts tuck to the chest then reach down for the landing; hinds trail stretched, then tuck under
          const relZ = fr ? K([[0, r.z + .02], [.35, r.z + .08], [.8, r.z + .14], [1, r.z]], u) : K([[0, r.z], [.25, r.z - .14], [.6, r.z - .06], [1, r.z]], u);
          const relY = fr ? K([[0, rig.ballH], [.35, .5 * rig.hipY], [.8, .2 * rig.hipY], [1, rig.ballH]], u) : K([[0, rig.ballH], [.3, lerp(rig.ballH, .2 * rig.hipY, legK)], [.7, lerp(rig.ballH, .25 * rig.hipY, legK)], [1, rig.ballH]], u);   // (short legs trail low: tucked up they fold into the rump)
          const landing = ss(seg(u, .75, 1));
          wz = lerp(body.z + relZ, r.z + D, landing); wy = lerp(body.y + relY, Hup + rig.ballH, landing);
          a = fr ? K([[0, .3], [.3, -1.6], [.8, .4], [1, .3]], u) : K([[0, -.9], [.4, -1.2], [.85, .5], [1, .2]], u);
          toe = fr ? K([[0, 0], [.3, 1.1], [.8, .2], [1, 0]], u) : K([[0, -.2], [.4, .7], [1, 0]], u);
        }
        P[f + 'x'] = r.x * (fr ? 1 : 1 + .25 * crouch); P[f + 'y'] = wy - P.rootY;   // crouching, the hind paws set wider beside the haunches
        P[f + 'z'] = wz - P.rootZ; P[f + 'a'] = a; P[f + 't'] = toe;
      }
      return P;
    }, dur, { iters: 10 }, 36), { rootMotion: true, jump: { D: +D.toFixed(3), H: Hup, deckBack: +deckBack.toFixed(3) } });   // deckBack: where the deck must begin (root space)
  }
  // sitting down, sitting, standing up
  const SIT = sitPose(rig, S), LOAF = settle(S, loafPose(rig), { floor: -.002, dense: true, iters: 20 }), SLEEP = settle(S, sleepPose(rig), { dense: true, iters: 20 });
  // move between two postures; `lifts` raises the named paws in an arc, `splay` swings them out sideways on the
  // way (so a hind paw folding under the haunch passes beside it, not through it)
  const transfer = (A, B, k, lifts = {}, splay = {}) => {
    const P = mix(A, B, ss(k)), arc = Math.sin(Math.PI * ss(k));
    for (const [f, hgt] of Object.entries(lifts)) P[f + 'y'] += hgt * arc;
    for (const [f, w] of Object.entries(splay)) P[f + 'x'] += Math.sign(rig.restFoot[f].x) * w * rig.hipH * arc;
    return P;
  };
  add('SitDown', .9, false, t => over(transfer(P0, SIT, t / .9, { HL: .03, HR: .03 }, { HL: .3, HR: .3 }), { blink: blinkAt(t, [.75]) }));
  add('Sit', 4, true, t => over(SIT, {
    breath: Math.sin(TAU * t / 2), blink: blinkAt(t, [1.4, 3.3]), tailWave: .05, tailWph: t / 2,
    hdYaw: K([[0, 0], [1.8, 0], [2.2, .3], [3.2, .3], [3.6, 0], [4, 0]], t), earRp: K([[0, 0], [2.5, 0], [2.6, -.4], [2.85, 0], [4, 0]], t),
  }));
  add('StandUp', .7, false, t => transfer(SIT, P0, t / .7, { HL: .03, HR: .03 }, { HL: .3, HR: .3 }));
  // lying down into a loaf, loafing, curling up to sleep
  add('LieDown', 1.1, false, withSettle(S, t => over(transfer(P0, LOAF, t / 1.1, { FL: .04, FR: .04 }, { FL: .25, FR: .25 }), { blink: blinkAt(t, [.9]) }), 1.1, {}, 10));
  add('Loaf', 5, true, t => over(LOAF, { breath: Math.sin(TAU * t / 2.5), blink: .55 + .45 * blinkAt(t, [2]), tailWave: .04, tailWph: t / 2.5 }));
  add('Sleep', 5, true, t => over(SLEEP, { breath: 1.3 * Math.sin(TAU * t / 2.5), tailTip: .05 * Math.sin(TAU * t / 5), earLp: -.25 + .25 * bump(t, 3.2, .06) }));
  add('FallAsleep', 2, false, withSettle(S, t => { const P = over(transfer(LOAF, SLEEP, t / 2, { FL: .02, FR: .02 }, { FL: .2, FR: .2 }), { blink: Math.max(.55, ss(seg(t, .6, 1.6))) }); P.nkPitch += .3 * Math.sin(Math.PI * ss(t / 2)); return P; }, 2, {}, 12));   // the head draws back over the front legs on the way
  // ---------------------------------------------------------------- grooming
  // Cats groom sitting or lying, never standing, and a bout runs head to tail (cephalocaudal). Share of oral
  // grooming by region: face 31 %, hind legs 21 %, sides/back 13 %, neck/chest 11 %, anogenital 10 %,
  // belly 9 %, tail 5 % (Eckstein & Hart 2000, Appl. Anim. Behav. Sci. 68:131).
  // This toy cat's head is as big as its body and its legs and tail are short, so its mouth can only reach the
  // front of the body. Every contact below is solved and checked on the skinned meshes (catcontact.js, qa.mjs):
  // the tongue really touches the paw and the chest fur, the paw really rubs the face, the hind toes really
  // scratch the neck - and no paw, forearm or bean sinks into the head or body. Flanks, hind legs, belly and tail
  // were measured out of reach (25-250 mm short even with the grooming tongue), so they are not animated.
  // Each clip starts and ends sitting, so the game can chain them (see GROOM_ROUTINE).
  const report = {};
  // grooming licks use the lengthened tongue (up to 2.2 times its length), growing as it comes out
  const GROOM_REACH = 1.2, groomTongue = Q => { Q.tongueStretch = GROOM_REACH * clamp(Q.tongue / .95, 0, 1); return Q; };
  // (opts.only, for quick checks: the grooming solves are skipped when none of these clips is asked for)
  if (!opts.only || opts.only.some(n => GROOM_ROUTINE.some(g => g.clip === n))) {
  const HS = rig.d.HS, d = rig.d;
  const mouthLocal = d.mouth.clone().sub(d.joints.Head);
  const mouthAt = P => { applyPose(rig, P); return rig.B.Head.localToWorld(mouthLocal.clone()); };
  // a run of licks on a solved base pose; `stroke(P, k)` moves the head along the fur for stroke amount k, and
  // an optional contact track (see lickTrack) keeps the tongue on the surface through the whole stroke
  const licking = (P, t, t0, n, rate, stroke, track = null) => {
    const u = (t - t0) * rate, i = Math.floor(u);
    if (u < 0 || i >= n) return P;
    const c = lickCycle(u - i); const k = c.hdPitch / .1;        // -1 .. +1 over the stroke
    P.lickU = u - i;                                             // (lick phase: the QA contact window)
    delete c.hdPitch; addTongue(P, c); groomTongue(P); stroke(P, k);
    if (track) track(P, u - i);
    return P;
  };
  // While the tongue is out it presses on the fur and drags along it: at a few points of the lick cycle the
  // head/neck pitch is solved (on the meshes) so the tongue just touches the surface, and blended in between.
  const LICK_U = Array.from({ length: 36 }, (_, i) => .08 + .02 * i), LICK_ON = [.04, .82];   // (the whole time the tongue is out)
  const lickTrack = (base, stroke, opts) => {
    // only how far the tongue is out is corrected, by bisection (one channel: it always converges), so the
    // tongue just meets the fur mid-lick and never sinks into it before or after
    const C = contactOf(S), T = C.sets.tongue ||= C.points(p => p === 'tongue', 'face', 2), onB = p => opts.b.includes(p);
    const offs = LICK_U.map(u => {
      const c0 = lickCycle(u), k0 = c0.hdPitch / .1; delete c0.hdPitch;
      const gapAt = dt => { const Q = addTongue({ ...base }, c0); Q.tongue += dt; groomTongue(Q); stroke(Q, k0); applyPose(S, Q); C.update(); return C.gap(T, onB, .12).d; };
      const core = u >= LICK_CORE[0] && u <= LICK_CORE[1], want = core ? -.001 : .001;
      if (!core && gapAt(0) >= want) return 0;                                 // already clear
      let lo = -.8, hi = core ? .12 : 0;                                       // gap falls as the tongue comes further out
      if (gapAt(hi) > want) return hi;                                         // cannot reach: as far as it goes
      for (let it = 0; it < 14; it++) { const m = (lo + hi) / 2; if (gapAt(m) > want) lo = m; else hi = m; }
      return (lo + hi) / 2;
    });
    // smoothed (a tongue does not jerk in and out), never letting the smoothing push it back into the fur
    const sm = offs.map((o, i) => Math.min(o, (offs[Math.max(0, i - 1)] + 2 * o + offs[Math.min(offs.length - 1, i + 1)]) / 4));
    offs.splice(0, offs.length, ...sm);
    return (P, u) => {
      const w = ss(seg(u, LICK_ON[0], LICK_U[0])) * (1 - ss(seg(u, LICK_U[LICK_U.length - 1], LICK_ON[1])));
      const f = clamp((u - LICK_U[0]) / (LICK_U[1] - LICK_U[0]), 0, LICK_U.length - 1.0001), i = Math.floor(f), v = f - i;
      P.tongue = Math.max(0, P.tongue + w * lerp(offs[i], offs[i + 1], v)); groomTongue(P);
    };
  };
  const SITG = over(SIT, { tailWave: .05 });
  // the pose at the moment the tongue is on the fur (mid-lick), for solving contact
  const lickAt = (stroke, u = .37) => Q => { const c = lickCycle(u), k = c.hdPitch / .1; delete c.hdPitch; addTongue(Q, c); groomTongue(Q); stroke(Q, k); return Q; };
  // head limits: turned past ~70 degrees or rolled flat, the toy head hides the face and wrings the neck
  const HB = { hdPitch: [-.3, 1.2], hdYaw: [-1.25, 1.25], hdRoll: [-.7, .7], nkPitch: [-.5, 1.3], nkYaw: [-.7, .7], nkRoll: [-.5, .5], spPitch: [-.35, 1], spYaw: [-.5, .5] };
  const HEADK = ['nkPitch', 'nkYaw', 'nkRoll', 'hdPitch', 'hdYaw', 'hdRoll'];
  const setPaw = (P, p, up, a, toe) => { const g2 = V(P.FLx, P.FLy, P.FLz).lerp(p, up); P.FLx = g2.x; P.FLy = g2.y; P.FLz = g2.z; P.FLa = lerp(P.FLa, a, up); P.FLt = lerp(0, toe, up); };
  const pawUp = (P, p, a = -1.7, toe = 1.2) => { setPaw(P, p, 1, a, toe); return P; };
  // the point on the head's surface in the direction of a head-space offset (bind pose = Head bone frame)
  const headSurf = off => {
    const g = S.model.userData.meshes.body.geometry.attributes.position, C = contactOf(S), dir = off.clone().normalize();
    let best = null, bd = -Infinity;
    for (let i = 0; i < g.count; i++) {
      if (C.M.body.part[i] !== 'head') continue;
      const v = V(g.getX(i), g.getY(i), g.getZ(i)), r = v.clone().sub(d.Hc), sc = r.clone().normalize().dot(dir) + .2 * r.length() / HS;
      if (sc > bd) { bd = sc; best = v; }
    }
    return best.sub(d.joints.Head);
  };
  // the point on part `part`'s surface in direction `dir` from bone `bone`'s joint (bind pose), as a bone-local offset
  const surfOf = (bone, dir, part = 'torso') => {
    const g = S.model.userData.meshes.body.geometry.attributes.position, C = contactOf(S), o = d.joints[bone], u = dir.clone().normalize();
    let best = null, bd = -Infinity;
    for (let i = 0; i < g.count; i++) {
      if (C.M.body.part[i] !== part) continue;
      const v = V(g.getX(i), g.getY(i), g.getZ(i)), r = v.clone().sub(o), sc = r.clone().normalize().dot(u) * 3 + r.length() / d.chestR;
      if (sc > bd) { bd = sc; best = v; }
    }
    return best.sub(o);
  };
  const sx = 1;
  const UPP = { scapL: -.4, scapLy: .012 * rig.hipH };                                            // paw-up support
  const PAWK = ['FLx', 'FLy', 'FLz'], FACEK = [...HEADK, ...PAWK, 'FLa', 'FLt'];
  // every grooming solve also keeps the head off all four legs and the front legs out of the head
  const GUARD = [{ a: 'head', b: ['FL', 'FR', 'HL', 'HR'] }, { a: 'FL', b: ['head', 'torso'] }, { a: 'FR', b: ['head'] }];
  const blendFace = (P, A, B, u, k) => { for (const key of FACEK) P[key] = lerp(P[key], lerp(A[key], B[key], u), k); };
  applyPose(rig, SITG);
  const chestFront = worldOf(rig, 'Chest').z + d.chestR;

  // 1. GroomFace: lift a front paw a little, lick it (4 licks), wash: the head ducks and tips to rub the face
  //    along the paw - over the whisker pad and down the muzzle - twice, 2 more licks, paw down.
  //    The big head's jaw hangs low: a paw lifted to mouth height would push the short forearm up into the
  //    chin, so the paw is held low out in front of the chest and the head bends down to it (and the tongue
  //    reaches out to it). Higher face spots are out of the arm's reach (measured), so the wash stays low.
  const faceStroke = (Q, s2) => { Q.hdPitch += .04 * s2; };
  const lickBase = over(SITG, UPP, { FLx: .05 * sx, FLy: rig.ballH + .02, FLz: chestFront + .06, FLa: .2, FLt: 0, hdPitch: .8, nkPitch: SITG.nkPitch - .6, hdYaw: 0, hdRoll: 0 });
  touchBest(S, lickBase, { a: 'tongue', b: ['FL'], keys: [...PAWK, 'nkPitch', 'nkYaw', 'hdPitch', 'hdYaw', 'hdRoll'], bounds: HB, iters: 40, at: lickAt(faceStroke), guard: [{ a: 'tongue', b: ['FL'] }, ...GUARD] },
    [{ hdPitch: .9, nkPitch: SITG.nkPitch - .4 }, { FLy: rig.ballH, FLz: chestFront + .09 }, { FLx: .1 * sx, hdYaw: .45 * sx, hdRoll: .2 * sx }, { FLx: .12 * sx, FLz: chestFront + .08, hdYaw: .6 * sx, nkYaw: .3 * sx, hdRoll: .25 * sx }, { FLx: .09 * sx, FLz: chestFront + .1, hdYaw: .3 * sx, hdPitch: 1 }, { FLx: .11 * sx, FLz: chestFront + .05, hdYaw: .5 * sx, hdPitch: .9, nkPitch: SITG.nkPitch - .4 }, { FLx: .07 * sx, FLz: chestFront + .12, hdPitch: 1.1, nkPitch: SITG.nkPitch - .7 }],
    { FLx: [0, .04 * sx], FLz: [-.03, 0, .03, .06], hdPitch: [-.2, 0, .2, .4], nkPitch: [-.2, 0, .3], hdYaw: [0, .4 * sx] });
  report.pawLick = Math.abs(touch.last - .001); report.pawLickInfo = touch.info;
  const faceTrack = lickTrack(lickBase, faceStroke, { b: ['FL'], guard: [{ a: 'tongue', b: ['FL'] }, ...GUARD] });
  const WASH = [[.4, -.35, .8], [.32, -.45, .8], [.22, -.52, .75]].map(([x, y, z]) => V(x * sx, y, z).multiplyScalar(HS));
  const washPoses = WASH.map(off => {
    const P = over(lickBase, { FLx: lickBase.FLx + .3 * d.pawR * sx, FLy: lickBase.FLy + .6 * d.pawR, FLa: -.4, FLt: .3, hdRoll: .35 * sx, hdYaw: .25 * sx, nkRoll: .1 * sx });
    touchBest(S, P, { a: 'FL', b: ['head'], zone: { bone: 'Head', off: headSurf(off), r: .04 }, keys: [...HEADK, ...PAWK], bounds: HB, gap: -.002, guard: GUARD },
      [{ hdPitch: 1.0, nkPitch: SITG.nkPitch - .3 }, { hdRoll: .6 * sx }, { hdPitch: .9, nkPitch: SITG.nkPitch + .2, hdRoll: .5 * sx }],
      { hdPitch: [-.2, 0, .2, .4], nkPitch: [-.2, 0, .3], hdRoll: [-.2, 0, .25], hdYaw: [-.2, 0, .2] });
    return { P, err: Math.abs(touch.last + .002), info: touch.info };
  });
  report.wash = Math.max(...washPoses.map(w => w.err)); report.washEach = washPoses.map(w => [+(w.err * 1000).toFixed(1), w.info]);
  add('GroomFace', 6.6, true, withSettle(S, t => {
    const P = { ...SITG };
    P.breath = Math.sin(TAU * t / 2.2);
    const up = mj(seg(t, 0, .45)) * (1 - mj(seg(t, 6.1, 6.6)));
    // the paw comes up first and the head bends down to it after (and the head lifts before the paw goes
    // down): moved together, the paw would rise into the chin coming down
    const upP = mj(seg(t, 0, .3)) * (1 - mj(seg(t, 6.3, 6.6))), upH = mj(seg(t, .12, .45)) * (1 - mj(seg(t, 6.1, 6.42)));
    for (const [key, v] of Object.entries(UPP)) P[key] = (P[key] || 0) * (1 - upP) + v * upP;
    for (const key of FACEK) P[key] = lerp(P[key], lickBase[key], HEADK.includes(key) ? upH : upP);
    P.FLy += .02 * Math.sin(Math.PI * upP);                                     // the paw lifts clear of the floor on the way
    if (t >= 2.05 && t < 5.3) {
      // two wash strokes; each runs whisker pad -> muzzle, then the head lifts back out for the next
      const k = mj(seg(t, 2.05, 2.45)) * (1 - mj(seg(t, 4.9, 5.3)));
      const w = frac(seg(t, 2.25, 5.05) * 2), q = w < .78 ? mj(w / .78) : 1 - mj((w - .78) / .22);
      const f = clamp(q, 0, 1) * (WASH.length - 1), i = clamp(Math.floor(f), 0, WASH.length - 2), u = f - i;
      blendFace(P, washPoses[i].P, washPoses[i + 1].P, u, k);
      const tr = Math.sin(Math.PI * k);                                       // (on the way in and out the paw swings a little wide of the face)
      P.FLx += .02 * sx * tr; P.FLz += .015 * tr;
      P.earLp = -.3 * k; P.earLy = -.2 * k;
      P.blink = Math.max(.6, k);
    } else P.blink = up > .5 ? .75 : blinkAt(t, [.2]);
    if (t >= .45 && t < 2.25) licking(P, t, .45, 4, 2.2, faceStroke, faceTrack);
    if (t >= 5.15 && t < 6.05) licking(P, t, 5.15, 2, 2.2, faceStroke, faceTrack);
    return P;
  }, 6.6, { fk: ['FL'] }, 24), { contacts: [{ a: 'tongue', b: ['FL'], when: P => P.lickU >= LICK_CORE[0] && P.lickU <= LICK_CORE[1] }, { a: 'FL', b: ['head'], when: (P, t) => t > 2.45 && t < 4.9 }] });

  // (chest licking is not animated: with the chin tucked, this big head rests ON the chest, so the tongue can
  //  only hang down in front of the bib - checked on the meshes and in renders, it reads as a dangling tongue)

  // 3. ScratchEar: sitting, a hind foot comes up and scratches fast (~7 strokes/s) on the neck just behind the
  //    jaw, below the ear - as high as the toy's short hind leg reaches round the big head (the ear itself is
  //    ~6 cm out of reach) - head tipped into the foot, eyes squeezed, ear flattened, body leaning away
  {
    const base = over(SITG, { hipRoll: -.32, hipPitch: SITG.hipPitch - .12, spRoll: .2, nkRoll: .25, hdRoll: .55, hdPitch: .35, hdYaw: .3, blink: 1, earLp: -.6, earLy: -.5 });
    Object.assign(base, { HLfk: 1, HLk1: -2.3, HLk2: 1.1, HLk3: -.9, HLk4: .4, HLkz: .55 });
    base.FRx *= 1.25;
    // (the spot is the best of a few along the back of the jaw: breeds differ in how far the leg gets)
    let bestScratch = null;
    for (const sp of [[.6, -.55, -.1], [.55, -.62, -.05], [.65, -.5, -.18], [.5, -.65, -.2]]) {
      const Q = { ...base };
      touchBest(S, Q, { a: 'HL', b: ['head'], zone: { bone: 'Head', off: headSurf(V(sp[0] * sx, sp[1], sp[2]).multiplyScalar(HS)), r: .06 }, keys: ['HLk1', 'HLk2', 'HLk3', 'HLkz', 'hdRoll', 'hdPitch', 'nkRoll', 'nkPitch', 'hipRoll'], bounds: { ...HB, hipRoll: [-.6, 0], HLk1: [-3, -1], HLkz: [-.2, 1.2] }, gap: .0005, iters: 40,
        guard: [{ a: 'HL', b: ['head', 'torso'] }, { a: 'HLb', b: ['head', 'torso'] }, { a: 'head', b: ['HL', 'FL', 'FR'] }, { a: 'FL', b: ['head'] }] }, [{ HLk1: -2.6, HLk2: .8, hdRoll: .7 }, { HLk1: -2.0, HLk2: 1.4, HLkz: .8, hdPitch: .6 }],
        { HLk1: [-.4, 0, .4], HLk2: [-.4, 0, .4], HLkz: [-.2, .2], hdRoll: [-.2, .15], hdPitch: [-.2, .2] });
      const e = Math.abs(touch.last - .0005);
      if (!bestScratch || e < bestScratch.e) bestScratch = { P: Q, err: touch.last, e };
      if (e < .002) break;
    }
    Object.assign(base, bestScratch.P); touch.last = bestScratch.err;
    report.scratch = Math.abs(touch.last - .0005);
    settle(S, base, { fk: ['HL'] });                                             // tail off the floor, other paws out of the body
    // getting the foot up: first carried (by IK) up round the outside of the haunch to where the raised foot
    // will be, then the leg takes the raised joint angles; the reverse on the way down
    applyPose(rig, base); const W = worldOf(rig, 'Toes_L');
    // the raking stroke runs from the contact pose outwards only (raking inwards would push the toes into the
    // head): try both ways on the mesh and keep the one that opens the gap
    const rakeGap = sg => { const C = contactOf(S), Q = { ...base, HLk2: base.HLk2 + .04 * sg, HLk3: base.HLk3 - .1 * sg }; applyPose(S, Q); C.update({ face: false }); return C.gap(C.sets.HL, p => p === 'head', .12).d; };
    const rakeDir = rakeGap(1) > rakeGap(-1) ? 1 : -1;
    add('ScratchEar', 3, true, withSettle(S, t => {
      const k = mj(seg(t, 0, .45)) * (1 - mj(seg(t, 2.5, 3)));
      const P = mix(SITG, base, k);
      // (the raised leg's joint angles are the scratching ones from the start: the IK -> angles crossfade does
      //  the moving, an angle halfway from the sitting leg would fold the leg into the haunch)
      for (const key of ['HLk1', 'HLk2', 'HLk3', 'HLk4', 'HLkz']) P[key] = base[key];
      const ik = mj(seg(t, 0, .3)) * (1 - mj(seg(t, 2.65, 3))), fk = mj(seg(t, .2, .35)) * (1 - mj(seg(t, 2.5, 2.7)));
      const g = V(SITG.HLx, SITG.HLy, SITG.HLz).lerp(W, ik);
      const sc = rakeDir * .5 * (1 - Math.cos(TAU * 7 * (t - .45))) * ss(seg(t, .45, .6)) * (1 - ss(seg(t, 2.3, 2.5)));
      // (held out wide while the leg changes between IK and joint angles too: in between it would cut the haunch)
      const wide = Math.max(Math.sin(Math.PI * ik), ik * Math.sin(Math.PI * fk));
      P.HLx = g.x + .9 * rig.hipH * wide; P.HLy = g.y + .25 * rig.hipH * wide; P.HLz = g.z; P.HLfk = fk;
      P.HLa = lerp(SITG.HLa, -.6, ik); P.HLt = lerp(0, .6, ik);
      P.HLk2 += .04 * sc * k; P.HLk3 -= .1 * sc * k;                               // the scratching stroke (toes rake the fur)
      P.hdRoll += .012 * Math.sin(TAU * 7 * t) * k; P.mouth = .15 * k;            // head jiggles with it
      return P;
    }, 3, {}, 30), { contacts: [{ a: 'HL', b: ['head'], when: (P, t) => t > .45 && t < 2.5 }] });
  }

  // 4. NibbleClaws: sitting, a front paw lifted a little with toes spread, head bent down and turned sideways
  //    chewing at the claws with the side of the mouth
  {
    const base = over(lickBase, { FLt: -.6, mouth: .35, hdRoll: .4 * sx, tongue: 0 });
    touchBest(S, base, { a: 'FL', b: ['head'], zone: { bone: 'Head', off: headSurf(V(.3 * sx, -.5, .75).multiplyScalar(HS)), r: .035 }, keys: [...PAWK, ...HEADK], bounds: HB, gap: -.003, iters: 45,
      guard: GUARD }, [{ hdRoll: .7 * sx, hdYaw: .3 * sx }, { hdPitch: 1, hdRoll: .55 * sx }, { FLz: lickBase.FLz + .02, hdRoll: .5 * sx, hdYaw: .2 * sx }],
      { FLz: [-.02, 0, .03], FLy: [0, .02], hdPitch: [-.2, 0, .2], hdRoll: [-.2, 0, .2], hdYaw: [-.2, .2] });
    report.nibble = Math.abs(touch.last + .003);
    add('NibbleClaws', 3.4, true, withSettle(S, t => {
      const P = { ...SITG }, up = mj(seg(t, 0, .45)) * (1 - mj(seg(t, 2.9, 3.4)));
      const upP = mj(seg(t, 0, .3)) * (1 - mj(seg(t, 3.1, 3.4))), upH = mj(seg(t, .12, .45)) * (1 - mj(seg(t, 2.9, 3.22)));   // (paw first, head after: see GroomFace)
      for (const [key, v] of Object.entries(UPP)) P[key] = (P[key] || 0) * (1 - upP) + v * upP;
      for (const key of [...FACEK, 'mouth']) P[key] = lerp(P[key], base[key], HEADK.includes(key) ? upH : upP);
      const chew = (t > .5 && t < 2.8) ? .5 + .5 * Math.sin(TAU * 4 * t) : 0;
      P.FLy += .02 * Math.sin(Math.PI * upP);                                 // the paw lifts clear of the floor on the way
      P.mouth = (.15 + .4 * chew) * up; P.blink = .7 * up;
      return P;
    }, 3.4, { fk: ['FL'] }, 14), { contacts: [{ a: 'FL', b: ['head'], when: (P, t) => t > .5 && t < 2.8 }] });
  }
  }
  // stretch: play-bow with a big yawn, then each hind leg stretched out behind
  add('Stretch', 4.2, false, withSettle(S, t => {
    const bow = ss(seg(t, .2, .9)) * (1 - ss(seg(t, 2.1, 2.6)));
    const P = { ...P0 };
    P.hipPitch = .38 * bow; P.spPitch = .22 * bow; P.hipY = -.04 * h * bow; P.hipZ = -.05 * h * bow;
    P.nkPitch = -.25 * bow; P.hdPitch = -.3 * bow;
    for (const f of ['FL', 'FR']) { P[f + 'z'] += .55 * h * bow; P[f + 'a'] = lerp(.3, 1.25, bow); P[f + 't'] = -.25 * bow; }
    for (const f of ['HL', 'HR']) { P[f + 'a'] = lerp(.2, .45, bow); }
    const yawn = bump(t, 1.35, .32);
    P.mouth = yawn; P.tongue = .38 * yawn; P.tongueBend = .15 * yawn; P.tongueCurl = -1.1 * yawn; P.tongueSpread = .5 * yawn; P.blink = .2 + .75 * yawn; P.hdPitch -= .25 * yawn;   // tongue curls up in the yawn
    P.earLp = P.earRp = -.35 * yawn;
    P.tailBase = lerp(-.3, 1.0, bow); P.tailBend = .06; P.tailTip = .15 * bow; P.tailWave = .1; P.tailWph = t / 2;
    // hind-leg stretches: body shifts forward, one leg extends back and up, toes spread
    for (const [f, c] of [['HL', 3.05], ['HR', 3.7]]) {
      const e = bump(t, c, .2);
      P[f + 'z'] -= .55 * h * e; P[f + 'y'] += .14 * h * e; P[f + 'a'] = lerp(P[f + 'a'], -1.3, e); P[f + 't'] = -.35 * e;
      P.hipZ += .04 * h * e; P.hipRoll += (f === 'HL' ? -.06 : .06) * e;
    }
    P.blink = Math.max(P.blink, blinkAt(t, [2.5]));
    return P;
  }, 4.2, { floor: -.001 }, 32));
  // drinking: crouched over the bowl, lapping ~3.5 times a second. Like real cats (Reis et al. 2010, Science):
  // the tongue tip curls under into a J so only its top touches the surface, then whips back up, pulling a
  // column of liquid that the jaw snaps shut on. Every 8 laps a short pause to swallow.
  {
    const f = 3.5, laps = 8, lapT = 1 / f, dur = laps * lapT + .95;
    // the bowls in the game are one size (items.js: liquid / kibble surface at BOWL_SURF): how far each breed
    // crouches and bends its neck is solved so the curled tongue tip just meets that surface at the bottom of
    // a lap; where the tongue meets it is where the game puts the bowl (clip.drink.bowl, root space)
    const crouch = (P, s) => { P.hipY = -.07 * h * s; P.hipPitch = .06 * s; P.spPitch = .18 * s; P.nkPitch = .4 * s; P.hdPitch = .5 * s; return P; };
    const C = contactOf(S), T = C.sets.tongue ||= C.points(p => p === 'tongue', 'face', 2), Hd = C.sets.headAll ||= C.points(p => p === 'head', 'face', 1);
    const lapPose = (s, u, st) => { const Q = crouch({ ...P0 }, s); for (const ff of ['FL', 'FR']) { Q[ff + 'z'] += .02 * h; Q[ff + 'a'] = .55; } Q.scapL = Q.scapR = .15; addTongue(Q, lapCycle(u)); Q.tongueStretch = st * Q.tongue; return settle(S, Q, { iters: 4 }); };
    // 1. crouch until the chin (lowest point of the head, face mesh: lips and jaw) is 3 mm above the surface at
    //    every point of the lap - it never dips into the drink
    const chinAt = s => { let y = Infinity; for (let u = 0; u < 1; u += .05) { applyPose(S, lapPose(s, u, 0)); C.update(); y = Math.min(y, C.lowest(Hd), C.lowest(C.sets.head)); } return y; };
    let lo = .3, hi = 2.4;
    for (let it = 0; it < 12; it++) { const m = (lo + hi) / 2; if (chinAt(m) > BOWL_SURF + .004) lo = m; else hi = m; }
    const sDrink = lo;
    // 2. the tongue reaches on down to the surface (lengthened like the grooming tongue, as far as needed)
    const tipAt = st => { let y = Infinity; for (const u of [.3, .33, .36, .4, .44, .48]) { applyPose(S, lapPose(sDrink, u, st)); C.update(); y = Math.min(y, C.lowest(T)); } return y; };   // (lowest over the down-stroke)
    let a0 = 0, a1 = 1.5;
    if (tipAt(0) <= BOWL_SURF - .0015) a1 = 0;
    else for (let it = 0; it < 12; it++) { const m = (a0 + a1) / 2; if (tipAt(m) > BOWL_SURF - .0015) a0 = m; else a1 = m; }
    const stDrink = a1, tipY = tipAt(stDrink), tp = C.M.face.pos; let tx = 0, tz = 0;
    { let y = Infinity; for (const u of [.3, .36, .44]) { applyPose(S, lapPose(sDrink, u, stDrink)); C.update(); for (const i of T.ids) if (tp[3 * i + 1] < y) { y = tp[3 * i + 1]; tx = tp[3 * i]; tz = tp[3 * i + 2]; } } }
    report.drink = Math.max(0, tipY - BOWL_SURF);
    add('Drink', dur, true, withSettle(S, t => {
      const P = crouch({ ...P0 }, sDrink);
      for (const ff of ['FL', 'FR']) { P[ff + 'z'] += .02 * h; P[ff + 'a'] = .55; }
      for (const ff of ['HL', 'HR']) P[ff + 'a'] = -.1;
      P.scapL = P.scapR = .15; P.blink = .45 + .55 * blinkAt(t, [dur - .3]);
      P.tailBase = -.4; P.tailBend = .06; P.tailWave = .08; P.tailWph = t / 2;
      P.earLp = P.earRp = .1;
      if (t < laps * lapT) {
        const u = frac(t * f);
        addTongue(P, lapCycle(u)); P.tongueStretch = stDrink * P.tongue;
      } else {                                                     // swallow, lift the head a little, lick the lips
        const v = seg(t, laps * lapT, dur), lift = Math.sin(Math.PI * v);
        P.hdPitch -= .2 * lift; P.nkPitch -= .08 * lift;
        addTongue(P, lipLick(seg(t, laps * lapT + .05, dur - .02)));
      }
      return P;
    }, dur, {}, 12), { drink: { laps, rate: f, surf: BOWL_SURF, bowl: { x: +tx.toFixed(4), z: +(tz + .012).toFixed(4) }, crouch: +sDrink.toFixed(3), tongue: +stDrink.toFixed(3) } });
  }
  add('LickLips', 1.4, false, t => {
    const P = over(SIT, { breath: Math.sin(TAU * t / 2), blink: .3 + .5 * Math.sin(Math.PI * seg(t, .1, 1.2)) });
    return addTongue(P, lipLick(seg(t, .15, 1.15)));
  });
  // flop: rolls onto its side and relaxes (Ragdoll); then breathing with lazy tail flicks
  {
    const FLOP = over(LOAF, { hipRoll: 1.38, hipY: -.62 * rig.hipY, spRoll: .1, hdRoll: .9, hdPitch: .05, nkPitch: .05, blink: .55, tailBase: -1.2, tailBend: .03, tailCurl: 0, tailYaw: 0, earLp: -.2, earRp: -.2 });
    for (const f of FOOT_KEYS) {
      const fr = f[0] === 'F', top = f[1] === 'L';
      Object.assign(FLOP, { [f + 'fk']: 1, [f + 'k1']: fr ? .7 : top ? -.25 : -.6, [f + 'k2']: fr ? -.45 : .7, [f + 'k3']: fr ? .5 : -.35, [f + 'k4']: .3, [f + 'kz']: fr ? (top ? .1 : -.05) : (top ? .35 : -.35) });   // forelegs reach forward clear of the head, hind legs splay off the belly
    }
    settle(S, FLOP, { fk: FOOT_KEYS, iters: 30, dense: true });
    add('Flop', 1.6, false, withSettle(S, t => {
      const k = seg(t, 0, 1.4), P = transfer(P0, FLOP, k);
      P.hipY -= .06 * h * Math.sin(Math.PI * ss(k));     // sinks before rolling over
      return P;
    }, 1.6, { fk: FOOT_KEYS }, 16));
    add('FlopIdle', 4, true, t => over(FLOP, { breath: 1.2 * Math.sin(TAU * t / 2.4), tailTip: .3 * Math.sin(TAU * t / 2 - 1), tailWave: .1, tailWph: t / 2, blink: .55 + .45 * blinkAt(t, [2.6]) }));
  }
  // paw batting: lift, tap down, return (Turkish Van at the water bowl)
  let tapLow = .5;
  const pawBat = t => {
    const P = { ...P0 }, u = t / 1.4;
    // the paw is raised well over the toy whatever the leg length (a Munchkin's half-leg is lower than the toy),
    // the tap comes down onto the toy's top (TOY_TOP), not through it to the floor; and back: first lifted off
    // the toy, then drawn back and set down behind it
    const lift = Math.max(.5 * h, TOY_TOP + .06);
    // (tapLow: where the tap stops, solved below on the mesh with this very pose)

    const v = clamp((u - .5) / .5, 0, 1);
    const up = u < .35 ? mj(u / .35) : u < .5 ? 1 - (1 - tapLow) * mj((u - .35) / .15) : tapLow * (1 - mj(seg(v, .5, 1))) + .32 * mj(seg(v, 0, .3)) * (1 - mj(seg(v, .55, 1)));
    const fwd = u < .5 ? mj(Math.min(1, u / .4)) : 1 - mj(seg(v, .3, 1));
    // the paw reaches far enough to clear a toy lying in front of it, whatever the leg length
    const reach = Math.max(.36 * h, .11);
    P.FLy += up * lift; P.FLz += fwd * reach; P.FLx *= .85; P.FLt = 1.0 * up;
    // (the wrist curls the toes down for the tap only; drawn back over the toy the paw is held level, toes up)
    P.FLa = u < .5 ? lerp(.3, -1.5, up) : lerp(lerp(.3, -1.5, tapLow), .1, mj(seg(v, .2, .45))) * (1 - mj(seg(v, .7, 1))) + .3 * mj(seg(v, .7, 1));
    P.scapL = -.35 * Math.min(1, up); P.scapLy = .012 * Math.min(1, up) * h; P.hipZ = -.025 * h * up; P.hipPitch = .06 * up; P.hipRoll = -.05 * up;
    P.hdPitch = .35 * Math.max(up, .8 * fwd) - .25 * Math.sin(Math.PI * v); P.hdYaw = .1 * fwd;   // (the head comes up as the paw is drawn back past the chin)
    P.tailBase = -.2; P.tailWave = .2; P.tailWph = u * 2; P.earLp = P.earRp = .12;
    return P;
  };
  {
    // the lowest point of the paw (curled toes, beans) at the bottom of the tap stops on the toy top
    const C = contactOf(S), at = k => { tapLow = k; let y = Infinity; for (let t = .56; t <= .705; t += .02) { applyPose(S, pawBat(t)); C.update(); y = Math.min(y, C.lowest(C.sets.FL), C.lowest(C.sets.FLb)); } return y; };   // (over the whole way down: the curled toes swing lowest just before the stop)
    let lo = .05, hi = 1;
    for (let it = 0; it < 14; it++) { const m = (lo + hi) / 2; if (at(m) < TOY_TOP + .001) lo = m; else hi = m; }
    tapLow = hi;
  }
  add('PawBat', 1.4, true, withSettle(S, pawBat, 1.4, { fk: ['FL'] }, 14));   // (the batting paw is left where it was aimed: the toy is placed under it)
  clips.groomReport = report;
  // a grooming clip whose contact this breed's body cannot make (legs too short to reach, ...) is not shipped:
  // it is left out of the clip set and listed with the reason, so the game simply never plays it for that cat
  const reachErr = { GroomFace: Math.max(report.pawLick ?? 0, report.wash ?? 0), ScratchEar: report.scratch, NibbleClaws: report.nibble };
  const out = clips.filter(c => !(reachErr[c.name] > GROOM_REACH_LIMIT));
  out.skipped = clips.filter(c => reachErr[c.name] > GROOM_REACH_LIMIT).map(c => ({ clip: c.name, reason: `contact out of reach for this body (${(reachErr[c.name] * 1000).toFixed(0)} mm short)` }));
  out.groomReport = report;
  return out;
}

// ------------------------------------------------------------------ bake to AnimationClips
const TAIL_LAG = .035, EAR_LAG = .06;
export function poseAt(clip, t) {
  const T = clip.loop ? ((t % clip.dur) + clip.dur) % clip.dur : clamp(t, 0, clip.dur);
  const P = clip.pose(T);
  if (clip.loop && clip.rootMotion && t >= clip.dur) { /* root motion continues over the loop */ }
  return P;
}
export function solveAt(rig, clip, t) {
  const tail = Array.from({ length: 10 }, (_, i) => clip.pose(clip.loop ? ((t - i * TAIL_LAG) % clip.dur + clip.dur) % clip.dur : clamp(t - i * TAIL_LAG, 0, clip.dur)));
  const ear = clip.pose(clip.loop ? ((t - EAR_LAG) % clip.dur + clip.dur) % clip.dur : clamp(t - EAR_LAG, 0, clip.dur));
  const P = clip.pose(clamp(t, 0, clip.dur));
  applyPose(rig, P, { tail, ear });
  return P;
}
export function bakeClip(rig, clip) {
  const n = Math.round(clip.dur * FPS), times = [], names = Object.keys(rig.B);
  const q = Object.fromEntries(names.map(k => [k, []])), pos = { Root: [], Hips: [], Scapula_L: [], Scapula_R: [], Tongue1: [], Tongue2: [], Tongue3: [], Tongue4: [], Tongue5: [] }, belly = [], morph = [];
  for (let i = 0; i <= n; i++) {
    const t = i / FPS;
    times.push(t);
    solveAt(rig, clip, t);
    for (const k of names) q[k].push(...rig.B[k].quaternion.toArray());
    for (const k in pos) pos[k].push(...rig.B[k].position.toArray());
    belly.push(...rig.B.Belly.scale.toArray());
    morph.push(...rig.face.morphTargetInfluences);
  }
  const tracks = [];
  for (const k of names) {
    const v = q[k];
    // drop bones that never move (keeps the files small)
    // keep every bone that leaves its rest rotation (identity) at any time - a bone HELD in a pose (the bent spine
    // while sitting) does not move but still needs its track, or a game engine snaps it back to the rest pose
    let posed = false; for (let i = 0; i < v.length && !posed; i += 4) if (Math.abs(v[i]) + Math.abs(v[i + 1]) + Math.abs(v[i + 2]) > 1e-5) posed = true;
    if (posed || k === 'Hips') tracks.push(new THREE.QuaternionKeyframeTrack(`${k}.quaternion`, times, v));
  }
  for (const k in pos) tracks.push(new THREE.VectorKeyframeTrack(`${k}.position`, times, pos[k]));
  tracks.push(new THREE.VectorKeyframeTrack('Belly.scale', times, belly));
  tracks.push(new THREE.NumberKeyframeTrack('Face.morphTargetInfluences', times, morph));
  const ac = new THREE.AnimationClip(clip.name, clip.dur, tracks);
  ac.userData = { loop: clip.loop, rootMotion: !!clip.rootMotion, speed: clip.speed, stride: clip.stride, jump: clip.jump };
  return ac;
}
